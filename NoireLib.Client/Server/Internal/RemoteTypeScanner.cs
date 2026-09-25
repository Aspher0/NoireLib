using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

internal static class RemoteTypeScanner
{
    public static string EndpointNameOf(Type type, NoireRemoteClassAttribute? attribute, string? explicitName)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName!;

        if (!string.IsNullOrWhiteSpace(attribute?.Name))
            return attribute!.Name!;

        var name = type.Name;

        if (type.IsInterface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name.Substring(1);

        return name;
    }

    public static List<NoireRemoteMemberInfo> Scan(Type type, object? target, string endpoint, NoireRemoteClassAttribute? attribute, NoireRemoteOptions options, INoireRemoteHost host)
    {
        var members = new List<NoireRemoteMemberInfo>();
        var flags = Flags(target);

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName || method.IsGenericMethodDefinition || method.DeclaringType == typeof(object))
                continue;

            var memberAttribute = method.GetCustomAttribute<NoireRemoteAttribute>();

            if (memberAttribute == null)
                continue;

            if (!method.IsStatic && target == null)
                continue;

            var name = string.IsNullOrWhiteSpace(memberAttribute?.Name) ? method.Name : memberAttribute!.Name!;

            if (!NoireRemotePaths.IsValidName(name))
            {
                Warn(host, type, method.Name, "'" + name + "' is not a usable member name");
                continue;
            }

            var member = Build(method, target, endpoint, name, memberAttribute, attribute, options, host, out var reason);

            if (member == null)
            {
                Warn(host, type, method.Name, reason);
                continue;
            }

            members.Add(member);
        }

        foreach (var property in type.GetProperties(Flags(target)))
        {
            var memberAttribute = property.GetCustomAttribute<NoireRemoteAttribute>();

            if (memberAttribute == null)
                continue;

            if (IsConsumerPropertyType(property.PropertyType))
                continue;

            var name = string.IsNullOrWhiteSpace(memberAttribute.Name) ? property.Name : memberAttribute.Name!;

            if (!NoireRemotePaths.IsValidName(name))
            {
                Warn(host, type, property.Name, "'" + name + "' is not a usable member name");
                continue;
            }

            var getter = property.GetGetMethod(nonPublic: true) ?? property.GetGetMethod(nonPublic: false);

            if (getter == null)
            {
                Warn(host, type, property.Name, "a published property reads its value and has no getter");
                continue;
            }

            if (!getter.IsStatic && target == null)
                continue;

            var propertyMember = BuildProperty(property, getter, target, endpoint, name, memberAttribute, attribute, options, host, out var propertyReason);

            if (propertyMember == null)
            {
                Warn(host, type, property.Name, propertyReason);
                continue;
            }

            members.Add(propertyMember);
        }

        return members;
    }

    internal static bool IsConsumerPropertyType(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;

        if (typeof(Delegate).IsAssignableFrom(bare))
            return true;

        // Must match what NoireRemoteBinding recognises as a consumer wrapper.
        return bare.IsGenericType && bare.GetGenericTypeDefinition() == typeof(NoireRemoteConsumer<>);
    }

    private static BindingFlags Flags(object? target)
        => BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
           | (target == null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);

    public static NoireRemoteMemberInfo? Build(
        MethodInfo method,
        object? target,
        string endpoint,
        string name,
        NoireRemoteAttribute? memberAttribute,
        NoireRemoteClassAttribute? endpointAttribute,
        NoireRemoteOptions options,
        INoireRemoteHost host,
        out string reason)
    {
        reason = string.Empty;

        var parameters = new List<NoireRemoteParameterInfo>();

        foreach (var parameter in method.GetParameters())
        {
            if (parameter.IsOut || parameter.ParameterType.IsByRef)
            {
                reason = "parameter '" + parameter.Name + "' is passed by reference";
                return null;
            }

            if (RemoteJsonContract.IsCancellationToken(parameter.ParameterType))
            {
                parameters.Add(new NoireRemoteParameterInfo(parameter.Name ?? "cancellationToken", parameter.ParameterType, NoireRemoteJsonTypes.Null, false, null, null, true));
                continue;
            }

            if (RemoteProgressSink.PayloadTypeOf(parameter.ParameterType) is { } progressType)
            {
                if (!RemoteJsonContract.CanCross(progressType, out var progressWhy))
                {
                    reason = "the progress reporter carries a " + progressType.Name + ", which is " + progressWhy;
                    return null;
                }

                parameters.Add(new NoireRemoteParameterInfo(
                    parameter.Name ?? "progress",
                    parameter.ParameterType,
                    NoireRemoteJsonTypes.Null,
                    false,
                    null,
                    null,
                    false)
                {
                    IsProgress = true,
                    ProgressType = progressType,
                    ProgressFactory = RemoteProgressSink.FactoryFor(progressType),
                });

                continue;
            }

            if (!RemoteJsonContract.CanCross(parameter.ParameterType, out var why))
            {
                reason = "parameter '" + parameter.Name + "' is " + why;
                return null;
            }

            var parameterAttribute = parameter.GetCustomAttribute<NoireRemoteParamAttribute>();

            parameters.Add(new NoireRemoteParameterInfo(
                parameter.Name ?? ("arg" + parameters.Count),
                parameter.ParameterType,
                RemoteJsonContract.JsonTypeOf(parameter.ParameterType, null),
                !parameter.HasDefaultValue,
                parameter.HasDefaultValue ? parameter.DefaultValue : null,
                RemoteJsonContract.EnumValuesOf(parameter.ParameterType),
                false)
            {
                Source = parameter,
                Summary = parameterAttribute?.Description,
                Minimum = parameterAttribute?.Minimum ?? double.NaN,
                Maximum = parameterAttribute?.Maximum ?? double.NaN,
                Step = parameterAttribute?.Step ?? double.NaN,
                Example = parameterAttribute?.Example,
                Control = parameterAttribute?.Control ?? NoireRemoteControl.Auto,
            });
        }

        var returnType = RemoteJsonContract.UnwrapReturn(method.ReturnType);

        if (!RemoteJsonContract.CanCross(returnType, out var returnWhy))
        {
            reason = "the return type is " + returnWhy;
            return null;
        }

        var thread = Resolve(memberAttribute?.Thread ?? NoireRemoteThread.Inherit, endpointAttribute?.Thread ?? NoireRemoteThread.Inherit, options.DefaultThread, host);
        var requires = ResolveReadiness(memberAttribute?.Requires ?? NoireRemoteReadiness.Inherit, endpointAttribute?.Requires ?? NoireRemoteReadiness.Inherit, thread);
        var access = ResolveAccess(memberAttribute?.Access ?? NoireRemoteAccess.Inherit, endpointAttribute?.Access ?? NoireRemoteAccess.Inherit);
        var mode = (memberAttribute?.Mode ?? NoireRemoteCallMode.Inherit) == NoireRemoteCallMode.Inherit ? NoireRemoteCallMode.Sync : memberAttribute!.Mode;
        var timeout = ResolveTimeout(memberAttribute?.TimeoutSeconds ?? 0, endpointAttribute?.TimeoutSeconds ?? 0, options.DefaultCallTimeout);

        var progress = parameters.Find(item => item.IsProgress);

        return new NoireRemoteMemberInfo(
            endpoint,
            name,
            BuildInvoker(method, target),
            parameters,
            returnType,
            RemoteJsonContract.JsonTypeOf(returnType, null),
            thread,
            requires,
            mode,
            timeout,
            access)
        {
            Source = method,
            Summary = memberAttribute?.Description,
            Confirm = memberAttribute?.Confirm ?? false,
            Group = memberAttribute?.Group,
            Order = memberAttribute?.Order ?? 0,
            Deprecated = memberAttribute?.Deprecated ?? false,
            ProgressClrType = progress?.ProgressType,
            Transports = ResolveTransports(memberAttribute?.Transports ?? NoireRemoteTransport.Inherit, endpointAttribute?.Transports ?? NoireRemoteTransport.Inherit),
        };
    }

    // The setter is ignored. Writing a plugin's state from another process belongs in a method.
    public static NoireRemoteMemberInfo? BuildProperty(
        PropertyInfo property,
        MethodInfo getter,
        object? target,
        string endpoint,
        string name,
        NoireRemoteAttribute memberAttribute,
        NoireRemoteClassAttribute? endpointAttribute,
        NoireRemoteOptions options,
        INoireRemoteHost host,
        out string reason)
    {
        reason = string.Empty;

        if (!RemoteJsonContract.CanCross(property.PropertyType, out var why))
        {
            reason = "its value is " + why;
            return null;
        }

        var thread = Resolve(memberAttribute.Thread, endpointAttribute?.Thread ?? NoireRemoteThread.Inherit, options.DefaultThread, host);
        var requires = ResolveReadiness(memberAttribute.Requires, endpointAttribute?.Requires ?? NoireRemoteReadiness.Inherit, thread);
        var access = ResolveAccess(memberAttribute.Access, endpointAttribute?.Access ?? NoireRemoteAccess.Inherit);
        var timeout = ResolveTimeout(memberAttribute.TimeoutSeconds, endpointAttribute?.TimeoutSeconds ?? 0, options.DefaultCallTimeout);

        return new NoireRemoteMemberInfo(
            endpoint,
            name,
            BuildInvoker(getter, target),
            [],
            property.PropertyType,
            RemoteJsonContract.JsonTypeOf(property.PropertyType, null),
            thread,
            requires,
            NoireRemoteCallMode.Sync,
            timeout,
            access)
        {
            Source = getter,
            Summary = memberAttribute.Description,
            Confirm = memberAttribute.Confirm,
            Group = memberAttribute.Group,
            Order = memberAttribute.Order,
            Deprecated = memberAttribute.Deprecated,
            ReadOnly = true,
            Transports = ResolveTransports(memberAttribute.Transports, endpointAttribute?.Transports ?? NoireRemoteTransport.Inherit),
        };
    }

    public static Func<object?[], CancellationToken, Task<object?>> BuildInvoker(MethodInfo method, object? target)
    {
        return async (arguments, cancellationToken) =>
        {
            object? result;

            try
            {
                result = method.Invoke(method.IsStatic ? null : target, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }

            return await UnwrapAsync(result).ConfigureAwait(false);
        };
    }

    private static async Task<object?> UnwrapAsync(object? result)
    {
        switch (result)
        {
            case null:
                return null;

            case Task task:
                await task.ConfigureAwait(false);
                return ReadTaskResult(task);

            case ValueTask valueTask:
                await valueTask.ConfigureAwait(false);
                return null;
        }

        var type = result.GetType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTask = (Task)type.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null)!;
            await asTask.ConfigureAwait(false);
            return ReadTaskResult(asTask);
        }

        return result;
    }

    private static object? ReadTaskResult(Task task)
    {
        var type = task.GetType();

        if (!type.IsGenericType)
            return null;

        return type.GetProperty(nameof(Task<int>.Result))?.GetValue(task);
    }

    public static NoireRemoteThread Resolve(NoireRemoteThread member, NoireRemoteThread endpoint, NoireRemoteThread fallback, INoireRemoteHost host)
    {
        if (member != NoireRemoteThread.Inherit)
            return member;

        if (endpoint != NoireRemoteThread.Inherit)
            return endpoint;

        if (fallback != NoireRemoteThread.Inherit)
            return fallback;

        var hosted = host.DefaultThread;

        return hosted == NoireRemoteThread.Inherit ? NoireRemoteThread.Framework : hosted;
    }

    public static NoireRemoteReadiness ResolveReadiness(NoireRemoteReadiness member, NoireRemoteReadiness endpoint, NoireRemoteThread thread)
    {
        if (member != NoireRemoteReadiness.Inherit)
            return member;

        if (endpoint != NoireRemoteReadiness.Inherit)
            return endpoint;

        return thread == NoireRemoteThread.Framework ? NoireRemoteReadiness.StateReady : NoireRemoteReadiness.None;
    }

    // A member's own setting, then its class's, then both.
    public static NoireRemoteTransport ResolveTransports(NoireRemoteTransport member, NoireRemoteTransport endpoint)
    {
        if (member != NoireRemoteTransport.Inherit)
            return member;

        return endpoint == NoireRemoteTransport.Inherit ? NoireRemoteTransport.All : endpoint;
    }

    public static NoireRemoteAccess ResolveAccess(NoireRemoteAccess member, NoireRemoteAccess endpoint)
    {
        if (member != NoireRemoteAccess.Inherit)
            return member;

        return endpoint == NoireRemoteAccess.Inherit ? NoireRemoteAccess.Local : endpoint;
    }

    public static TimeSpan ResolveTimeout(double memberSeconds, double endpointSeconds, TimeSpan fallback)
    {
        if (memberSeconds > 0)
            return TimeSpan.FromSeconds(memberSeconds);

        if (endpointSeconds > 0)
            return TimeSpan.FromSeconds(endpointSeconds);

        return fallback;
    }

    private static void Warn(INoireRemoteHost host, Type type, string member, string reason)
        => host.Log(NoireRemoteLogLevel.Warning, "[NoireRemote] " + type.FullName + "." + member + " is not published: " + reason, null);
}
