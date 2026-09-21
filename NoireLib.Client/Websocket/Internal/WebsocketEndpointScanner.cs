using NoireLib.Remote;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// Every method is bound to a typed delegate once. The dispatch path never reflects.
internal static class WebsocketEndpointScanner
{
    public static string EndpointNameOf(Type type, NoireWebsocketEndpointAttribute? attribute, string? explicitName)
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

    public static NoireWebsocketEndpointOptions OptionsOf(NoireWebsocketEndpointAttribute? attribute)
    {
        var options = new NoireWebsocketEndpointOptions();

        if (attribute == null)
            return options;

        options.RequireSubProtocol = attribute.RequireSubProtocol;
        options.Access = attribute.Access;
        options.Requires = attribute.Requires;
        options.Thread = attribute.Thread;

        if (attribute.SubProtocols != null)
        {
            foreach (var protocol in attribute.SubProtocols)
            {
                if (!string.IsNullOrWhiteSpace(protocol))
                    options.SubProtocols.Add(protocol);
            }
        }

        return options;
    }

    // The owner is the instance, or the type for a static surface. One call detaches everything it attached.
    public static int Attach(NoireWebsocketEndpoint endpoint, Type type, object? target, INoireRemoteHost host)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            | (target == null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);

        var owner = target ?? type;
        var attached = 0;

        foreach (var method in type.GetMethods(flags))
        {
            var attribute = method.GetCustomAttribute<NoireWebsocketOnAttribute>();

            if (attribute == null)
                continue;

            if (method.IsGenericMethodDefinition)
            {
                Warn(host, type, method, "a generic method cannot be a socket handler");
                continue;
            }

            if (!method.IsStatic && target == null)
            {
                Warn(host, type, method, "an instance method needs an instance to publish from");
                continue;
            }

            var options = new NoireSocketSubscribeOptions { Owner = owner, Key = attribute.Key };

            if (!TryAttach(endpoint, method, target, attribute, options, out var reason))
            {
                Warn(host, type, method, reason);
                continue;
            }

            attached++;
        }

        return attached;
    }

    private static bool TryAttach(
        NoireWebsocketEndpoint endpoint,
        MethodInfo method,
        object? target,
        NoireWebsocketOnAttribute attribute,
        NoireSocketSubscribeOptions options,
        out string reason)
    {
        reason = string.Empty;

        var awaited = typeof(Task).IsAssignableFrom(method.ReturnType);

        if (!awaited && method.ReturnType != typeof(void))
        {
            reason = "a socket handler must return void or a Task; this one returns " + method.ReturnType.Name;
            return false;
        }

        var parameters = method.GetParameters();
        var instance = method.IsStatic ? null : target;

        if (parameters.Length == 2
            && parameters[0].ParameterType == typeof(NoireWebsocketConnection)
            && parameters[1].ParameterType == typeof(NoireWebsocketMessage))
        {
            var requires = attribute.Requires;
            var access = attribute.Access;

            if (awaited)
            {
                var handler = (Func<NoireWebsocketConnection, NoireWebsocketMessage, Task>)method.CreateDelegate(
                    typeof(Func<NoireWebsocketConnection, NoireWebsocketMessage, Task>), instance);

                endpoint.AddMessageHandler(handler, requires, access, options);
            }
            else
            {
                var handler = (Action<NoireWebsocketConnection, NoireWebsocketMessage>)method.CreateDelegate(
                    typeof(Action<NoireWebsocketConnection, NoireWebsocketMessage>), instance);

                endpoint.AddMessageHandler((connection, message) => { handler(connection, message); return Task.CompletedTask; }, requires, access, options);
            }

            return true;
        }

        if (parameters.Length == 2
            && parameters[0].ParameterType == typeof(NoireWebsocketConnection)
            && parameters[1].ParameterType == typeof(NoireWebsocketClose))
        {
            if (awaited)
            {
                var handler = (Func<NoireWebsocketConnection, NoireWebsocketClose, Task>)method.CreateDelegate(
                    typeof(Func<NoireWebsocketConnection, NoireWebsocketClose, Task>), instance);

                endpoint.AddCloseHandler(handler, options);
            }
            else
            {
                var handler = (Action<NoireWebsocketConnection, NoireWebsocketClose>)method.CreateDelegate(
                    typeof(Action<NoireWebsocketConnection, NoireWebsocketClose>), instance);

                endpoint.AddCloseHandler((connection, close) => { handler(connection, close); return Task.CompletedTask; }, options);
            }

            return true;
        }

        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(NoireWebsocketConnection))
        {
            if (awaited)
            {
                var handler = (Func<NoireWebsocketConnection, Task>)method.CreateDelegate(typeof(Func<NoireWebsocketConnection, Task>), instance);
                endpoint.AddOpenHandler(handler, options);
            }
            else
            {
                var handler = (Action<NoireWebsocketConnection>)method.CreateDelegate(typeof(Action<NoireWebsocketConnection>), instance);
                endpoint.AddOpenHandler(connection => { handler(connection); return Task.CompletedTask; }, options);
            }

            return true;
        }

        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Exception))
        {
            if (awaited)
            {
                var handler = (Func<Exception, Task>)method.CreateDelegate(typeof(Func<Exception, Task>), instance);
                endpoint.AddErrorHandler(handler, options);
            }
            else
            {
                var handler = (Action<Exception>)method.CreateDelegate(typeof(Action<Exception>), instance);
                endpoint.AddErrorHandler(exception => { handler(exception); return Task.CompletedTask; }, options);
            }

            return true;
        }

        reason = "no socket callback takes this signature";
        return false;
    }

    private static void Warn(INoireRemoteHost host, Type type, MethodInfo method, string reason)
        => host.Log(NoireRemoteLogLevel.Warning, "[NoireWebsocket] " + type.FullName + "." + method.Name + " is not attached: " + reason, null);
}
