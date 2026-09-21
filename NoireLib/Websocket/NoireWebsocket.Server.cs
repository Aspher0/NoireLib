using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Websocket;

public static partial class NoireWebsocket
{
    /// <summary>
    /// Gets the socket half of this plugin's listener. Anything a published socket can do that this class does not
    /// forward is on it.
    /// </summary>
    public static NoireWebsocketServer Instance
    {
        get
        {
            RegisterDisposeHook();
            return NoireWebsocketServer.For(NoireRemote.Instance);
        }
    }

    /// <summary>
    /// Gets the settings every socket accepted here runs under. Change them before a peer connects. A connection
    /// already open keeps the values it was accepted with.
    /// </summary>
    public static NoireWebsocketServerOptions Options => Instance.Options;

    /// <summary>
    /// Gets the published sockets, as a snapshot taken now.
    /// </summary>
    public static IReadOnlyList<NoireWebsocketEndpoint> Endpoints => Instance.Endpoints;

    /// <summary>
    /// Gets how many sockets are open across every published endpoint.
    /// </summary>
    public static int ConnectionCount => Instance.ConnectionCount;

    /// <summary>
    /// Publishes a socket under a name, with no handler yet. This is the primitive the attributes are sugar over.
    /// </summary>
    /// <param name="name">The name the socket is reachable under, at <c>/noire/ws/{name}</c>.</param>
    /// <param name="options">The sub-protocols, access, readiness and thread. Null takes the defaults.</param>
    /// <returns>The endpoint, to register handlers on.</returns>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    /// <exception cref="InvalidOperationException">If the name is not usable on the wire, or another socket already claims it.</exception>
    public static NoireWebsocketEndpoint Publish(string name, NoireWebsocketEndpointOptions? options = null)
        => Instance.Publish(name, options);

    /// <summary>
    /// Publishes a live object's <see cref="NoireWebsocketOnAttribute"/> methods as one socket.
    /// </summary>
    /// <param name="instance">The object whose handlers are attached.</param>
    /// <param name="name">The socket name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>The endpoint the handlers were attached to.</returns>
    /// <exception cref="ArgumentNullException">If the object is null.</exception>
    /// <exception cref="InvalidOperationException">If another socket already claims the name.</exception>
    public static NoireWebsocketEndpoint Publish(object instance, string? name = null)
        => Instance.Publish(instance, name);

    /// <summary>
    /// Publishes a type's static <see cref="NoireWebsocketOnAttribute"/> methods as one socket.
    /// </summary>
    /// <param name="type">The type whose handlers are attached.</param>
    /// <param name="name">The socket name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>The endpoint the handlers were attached to.</returns>
    /// <exception cref="ArgumentNullException">If the type is null.</exception>
    /// <exception cref="InvalidOperationException">If another socket already claims the name.</exception>
    public static NoireWebsocketEndpoint PublishType(Type type, string? name = null)
        => Instance.PublishType(type, name);

    /// <summary>
    /// Publishes a type's static <see cref="NoireWebsocketOnAttribute"/> methods as one socket.
    /// </summary>
    /// <typeparam name="T">The type whose handlers are attached.</typeparam>
    /// <param name="name">The socket name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>The endpoint the handlers were attached to.</returns>
    public static NoireWebsocketEndpoint PublishType<T>(string? name = null)
        => Instance.PublishType<T>(name);

    /// <summary>
    /// Publishes every type in an assembly carrying <see cref="NoireWebsocketEndpointAttribute"/>. This is what
    /// <see cref="NoireLibMain.Initialize"/> calls on the plugin's own assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The endpoints published, in the order the types were found.</returns>
    /// <exception cref="ArgumentNullException">If the assembly is null.</exception>
    public static IReadOnlyList<NoireWebsocketEndpoint> PublishAttributedTypes(Assembly assembly)
        => Instance.PublishAttributedTypes(assembly);

    /// <summary>
    /// Finds a published socket by name, ignoring case.
    /// </summary>
    /// <param name="name">The socket name.</param>
    /// <returns>The endpoint, or null when nothing publishes it.</returns>
    public static NoireWebsocketEndpoint? GetEndpoint(string name)
        => Instance.GetEndpoint(name);

    /// <summary>
    /// Builds the loopback URL of a socket this plugin publishes. It reads the port the listener is bound to. The
    /// listener has to be running.
    /// </summary>
    /// <param name="socketName">The socket name.</param>
    /// <returns>The <c>ws</c> URL of that socket.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the listener is not bound.</exception>
    /// <exception cref="ArgumentException">If the socket name is not usable on the wire.</exception>
    public static Uri LocalUrl(string socketName)
        => WebsocketDiscovery.SocketUrl("127.0.0.1", NoireRemote.Port, socketName);
}
