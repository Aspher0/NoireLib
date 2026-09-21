using System;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Remote.Internal;

// One raise becomes one published event. The publication removes the handler.
internal static class RemoteEventScanner
{
    public static List<NoireRemoteEventInfo> Scan(
        Type type,
        object? target,
        string endpoint,
        Action<string, string, object?> publish,
        INoireRemoteHost host)
    {
        var declared = new List<NoireRemoteEventInfo>();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | (target == null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);

        foreach (var member in type.GetEvents(flags))
        {
            var attribute = member.GetCustomAttribute<NoireRemoteAttribute>();

            if (attribute == null)
                continue;

            var handlerType = member.EventHandlerType;

            if (handlerType == null || member.AddMethod == null || member.RemoveMethod == null)
                continue;

            var topic = string.IsNullOrWhiteSpace(attribute.Name)
                ? (endpoint + "." + member.Name).ToLowerInvariant()
                : attribute.Name!;

            var channel = string.IsNullOrWhiteSpace(attribute.Channel) ? NoireRemoteChannels.Events : attribute.Channel!;

            if (!TryDescribe(handlerType, out var payloadType, out var names, out var reason))
            {
                host.Log(NoireRemoteLogLevel.Warning,
                    "[NoireRemote] " + type.FullName + "." + member.Name + " is not published: " + reason, null);

                continue;
            }

            if (payloadType != null && !RemoteJsonContract.CanCross(payloadType, out var why))
            {
                host.Log(NoireRemoteLogLevel.Warning,
                    "[NoireRemote] " + type.FullName + "." + member.Name + " is not published: its payload is " + why, null);

                continue;
            }

            var handler = BuildHandler(handlerType, names, payloadType, value => publish(channel, topic, value));

            if (handler == null)
                continue;

            var instance = member.AddMethod.IsStatic ? null : target;

            try
            {
                member.AddMethod.Invoke(instance, [handler]);
            }
            catch (Exception exception)
            {
                host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] " + type.FullName + "." + member.Name + " could not be attached", exception);
                continue;
            }

            var remove = member.RemoveMethod;

            declared.Add(new NoireRemoteEventInfo(member.Name, topic, channel, payloadType, () =>
            {
                try
                {
                    remove.Invoke(instance, [handler]);
                }
                catch (Exception)
                {
                }
            }));
        }

        return declared;
    }

    // An EventHandler's sender is a live CLR object and is dropped.
    private static bool TryDescribe(Type handlerType, out Type? payloadType, out string[] names, out string reason)
    {
        payloadType = null;
        names = [];
        reason = string.Empty;

        if (!typeof(Delegate).IsAssignableFrom(handlerType))
        {
            reason = "its handler is not a delegate";
            return false;
        }

        var invoke = handlerType.GetMethod("Invoke");

        if (invoke == null || invoke.ReturnType != typeof(void))
        {
            reason = "its handler returns a value";
            return false;
        }

        var parameters = invoke.GetParameters();

        if (handlerType == typeof(EventHandler)
            || (handlerType.IsGenericType && handlerType.GetGenericTypeDefinition() == typeof(EventHandler<>)))
        {
            payloadType = parameters.Length == 2 ? parameters[1].ParameterType : null;
            return true;
        }

        switch (parameters.Length)
        {
            case 0:
                return true;

            case 1:
                payloadType = parameters[0].ParameterType;
                return true;

            case 2:
            case 3:
            case 4:
                // Keyed by the delegate's own parameter names.
                names = new string[parameters.Length];

                for (var index = 0; index < parameters.Length; index++)
                    names[index] = parameters[index].Name ?? ("arg" + index);

                return true;

            default:
                reason = "its handler takes more than four arguments";
                return false;
        }
    }

    private static Delegate? BuildHandler(Type handlerType, string[] names, Type? payloadType, Action<object?> publish)
    {
        var invoke = handlerType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters();
        var isEventHandler = handlerType == typeof(EventHandler)
            || (handlerType.IsGenericType && handlerType.GetGenericTypeDefinition() == typeof(EventHandler<>));

        object? Read(object?[] arguments)
        {
            if (isEventHandler)
                return arguments.Length == 2 ? arguments[1] : null;

            if (parameters.Length == 0)
                return null;

            if (parameters.Length == 1)
                return arguments[0];

            var payload = new Dictionary<string, object?>(names.Length);

            for (var index = 0; index < names.Length && index < arguments.Length; index++)
                payload[names[index]] = arguments[index];

            return payload;
        }

        _ = payloadType;

        return RemoteDelegateFactory.Create(handlerType, arguments => publish(Read(arguments)));
    }
}
