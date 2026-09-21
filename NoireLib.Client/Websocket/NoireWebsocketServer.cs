using NoireLib.Core.Reflection;
using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace NoireLib.Websocket;

/// <summary>
/// The socket half of one NoireRemote listener. It binds nothing of its own: a peer upgrades on the port NoireRemote
/// already listens on, under <see cref="RoutePrefix"/>.<br/>
/// Publishing is independent of the listener's state. A socket published before <see cref="NoireRemoteServer.Start()"/>
/// or after <see cref="NoireRemoteServer.Stop"/> is served as soon as the listener is bound again. Stopping the listener
/// closes every open connection with <see cref="NoireWebsocketCloseCode.GoingAway"/>, because a socket cannot be
/// resumed. Peers connect again. <see cref="NoireRemoteServer.Reset"/> and <see cref="NoireRemoteServer.Dispose"/> change
/// the listener's state without announcing it. Dispose this alongside the listener. Do not rely on them.
/// </summary>
public sealed class NoireWebsocketServer : IDisposable
{
    /// <summary>The prefix every published socket sits under, with both slashes.</summary>
    public const string RoutePrefix = "/noire/ws/";

    private static readonly object AttachGate = new();

    private static readonly ConcurrentDictionary<Guid, NoireWebsocketServer> Attached = new();

    private readonly ConcurrentDictionary<string, NoireWebsocketEndpoint> endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<NoireRemoteListenerState> listenerStateChanged;

    private int disposed;

    private NoireWebsocketServer(NoireRemoteServer http)
    {
        Http = http;
        listenerStateChanged = OnListenerStateChanged;
        http.StateChanged += listenerStateChanged;
    }

    /// <summary>
    /// Gets the listener the sockets are served on.
    /// </summary>
    public NoireRemoteServer Http { get; }

    /// <summary>
    /// Gets the settings every socket accepted here runs under. Change them before a peer connects. A connection
    /// already open keeps the values it was accepted with.
    /// </summary>
    public NoireWebsocketServerOptions Options { get; } = new();

    /// <summary>
    /// Gets the published sockets, as a snapshot taken now.
    /// </summary>
    public IReadOnlyList<NoireWebsocketEndpoint> Endpoints => [.. endpoints.Values];

    /// <summary>
    /// Gets how many sockets are open across every endpoint.
    /// </summary>
    public int ConnectionCount => Slots.Count;

    /// <summary>
    /// Gets whether the server has been disposed.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    internal SocketSlots Slots { get; } = new();

    /// <summary>
    /// Gets the socket half of a listener, creating it the first time it is asked for.
    /// </summary>
    /// <param name="http">The listener to serve sockets on.</param>
    /// <returns>The one socket server attached to that listener.</returns>
    /// <exception cref="ArgumentNullException">If the listener is null.</exception>
    public static NoireWebsocketServer For(NoireRemoteServer http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (Attached.TryGetValue(http.InstanceId, out var existing))
            return existing;

        lock (AttachGate)
        {
            if (Attached.TryGetValue(http.InstanceId, out existing))
                return existing;

            var created = new NoireWebsocketServer(http);
            Attached[http.InstanceId] = created;

            return created;
        }
    }

    /// <summary>
    /// Publishes a socket under a name, with no handler yet. This is the primitive the attributes are sugar over.
    /// </summary>
    /// <param name="name">The name the socket is reachable under, at <c>/noire/ws/{name}</c>.</param>
    /// <param name="options">The sub-protocols, access, readiness and thread. Null takes the defaults.</param>
    /// <returns>The endpoint, to register handlers on.</returns>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    /// <exception cref="InvalidOperationException">If the name is not usable on the wire, or another socket already claims it.</exception>
    /// <exception cref="ObjectDisposedException">If the server has been disposed.</exception>
    public NoireWebsocketEndpoint Publish(string name, NoireWebsocketEndpointOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!NoireRemotePaths.IsValidName(name))
            throw new InvalidOperationException("'" + name + "' is not a usable socket name. It may not be blank, start with an underscore or carry a slash.");

        var endpoint = new NoireWebsocketEndpoint(this, name, options ?? new NoireWebsocketEndpointOptions());

        if (!endpoints.TryAdd(name, endpoint))
            throw new InvalidOperationException("This listener already publishes a socket named '" + name + "'.");

        // Publishing, withdrawing and the reserved path all announce the surface.
        Http.AnnounceSurface();
        Http.InvalidateConsole(NoireRemoteServer.ConsoleScopes.Sockets);

        return endpoint;
    }

    // Reserved names start with an underscore. Publish refuses them for callers.
    internal NoireWebsocketEndpoint PublishReserved(string name, NoireWebsocketEndpointOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var endpoint = new NoireWebsocketEndpoint(this, name, options ?? new NoireWebsocketEndpointOptions());

        if (!endpoints.TryAdd(name, endpoint))
            throw new InvalidOperationException("This listener already publishes a socket named '" + name + "'.");

        Http.AnnounceSurface();

        return endpoint;
    }

    /// <summary>
    /// Publishes a live object's <see cref="NoireWebsocketOnAttribute"/> methods as one socket.
    /// </summary>
    /// <param name="instance">The object whose handlers are attached.</param>
    /// <param name="name">The socket name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>The endpoint the handlers were attached to.</returns>
    /// <exception cref="ArgumentNullException">If the instance is null.</exception>
    /// <exception cref="InvalidOperationException">If another socket already claims the name.</exception>
    public NoireWebsocketEndpoint Publish(object instance, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return PublishCore(instance.GetType(), instance, name);
    }

    /// <summary>
    /// Publishes a type's static <see cref="NoireWebsocketOnAttribute"/> methods as one socket.
    /// </summary>
    /// <param name="type">The type whose handlers are attached.</param>
    /// <param name="name">The socket name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>The endpoint the handlers were attached to.</returns>
    /// <exception cref="ArgumentNullException">If the type is null.</exception>
    /// <exception cref="InvalidOperationException">If another socket already claims the name.</exception>
    public NoireWebsocketEndpoint PublishType(Type type, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        return PublishCore(type, null, name);
    }

    /// <summary>
    /// Publishes a type's static <see cref="NoireWebsocketOnAttribute"/> methods as one socket.
    /// </summary>
    /// <typeparam name="T">The type whose handlers are attached.</typeparam>
    /// <param name="name">The socket name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>The endpoint the handlers were attached to.</returns>
    public NoireWebsocketEndpoint PublishType<T>(string? name = null)
        => PublishCore(typeof(T), null, name);

    /// <summary>
    /// Publishes every type in an assembly carrying <see cref="NoireWebsocketEndpointAttribute"/>.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The endpoints published, in the order the types were found.</returns>
    /// <exception cref="ArgumentNullException">If the assembly is null.</exception>
    public IReadOnlyList<NoireWebsocketEndpoint> PublishAttributedTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var published = new List<NoireWebsocketEndpoint>();

        foreach (var type in AttributedTypes.WithAttribute<NoireWebsocketEndpointAttribute>(assembly))
        {
            if (type.IsInterface)
                continue;

            try
            {
                published.Add(PublishCore(type, null, null));
            }
            catch (Exception exception)
            {
                // One bad type does not cost the other sockets.
                Http.Host.Log(NoireRemoteLogLevel.Error, "[NoireWebsocket] " + type.FullName + " was not published", exception);
            }
        }

        return published;
    }

    /// <summary>
    /// Finds a published socket by name, ignoring case.
    /// </summary>
    /// <param name="name">The socket name.</param>
    /// <returns>The endpoint, or null when nothing publishes it.</returns>
    public NoireWebsocketEndpoint? GetEndpoint(string name)
        => string.IsNullOrEmpty(name) ? null : endpoints.TryGetValue(name, out var endpoint) ? endpoint : null;

    /// <summary>
    /// Unpublishes every socket, drops every connection and detaches from the listener.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        Http.StateChanged -= listenerStateChanged;
        Attached.TryRemove(Http.InstanceId, out _);

        foreach (var endpoint in Endpoints)
            endpoint.Dispose();

        endpoints.Clear();
    }

    internal static NoireWebsocketServer? Find(Guid instance)
        => Attached.TryGetValue(instance, out var server) ? server : null;

    internal IReadOnlyList<string> EndpointNames()
        => [.. endpoints.Keys];

    internal void Unpublish(NoireWebsocketEndpoint endpoint)
    {
        if (endpoints.TryRemove(new KeyValuePair<string, NoireWebsocketEndpoint>(endpoint.Name, endpoint)))
        {
            Http.AnnounceSurface();
            Http.InvalidateConsole(NoireRemoteServer.ConsoleScopes.Sockets);
        }
    }

    private NoireWebsocketEndpoint PublishCore(Type type, object? target, string? name)
    {
        var attribute = type.GetCustomAttribute<NoireWebsocketEndpointAttribute>();
        var endpoint = Publish(
            WebsocketEndpointScanner.EndpointNameOf(type, attribute, name),
            WebsocketEndpointScanner.OptionsOf(attribute));

        var attached = WebsocketEndpointScanner.Attach(endpoint, type, target, Http.Host);

        if (attached == 0)
            Http.Host.Log(NoireRemoteLogLevel.Warning, "[NoireWebsocket] " + type.FullName + " publishes the socket '" + endpoint.Name + "' with no handler.", null);

        return endpoint;
    }

    // A socket cannot be resumed across a rebind. Peers are told the endpoint is going away.
    private void OnListenerStateChanged(NoireRemoteListenerState state)
    {
        if (state is not (NoireRemoteListenerState.Stopped or NoireRemoteListenerState.Failed))
            return;

        foreach (var endpoint in Endpoints)
            _ = endpoint.CloseAllAsync(NoireWebsocketCloseCode.GoingAway, "The listener stopped.");
    }
}
