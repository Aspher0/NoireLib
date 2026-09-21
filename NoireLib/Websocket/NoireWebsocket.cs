using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

/// <summary>
/// Opens outbound connections and publishes sockets on the listener NoireRemote already binds. Four transports are
/// reachable from here, each with its own client and its own settings: raw RFC 6455, server-sent events,
/// long-polling and Socket.IO.<br/>
/// Every connect overload taking a <c>configure</c> callback runs it before the first byte moves. A message
/// arriving immediately still reaches a handler. Everything opened through this class is closed when NoireLib is
/// disposed.
/// </summary>
public static partial class NoireWebsocket
{
    internal const string DisposeKey = "NoireLib.Internal.NoireWebsocket.Dispose";

    private static readonly List<WeakReference<IDisposable>> Connections = new();

    private static readonly object ConnectionGate = new();

    private static bool disposeHookRegistered;

    /// <summary>
    /// Opens a raw WebSocket connection.
    /// </summary>
    /// <param name="url">The absolute URL. An <c>http</c> or <c>https</c> scheme is read as <c>ws</c> or <c>wss</c>.</param>
    /// <param name="options">The settings the connection runs under. Null takes the defaults.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentException">If the URL is not absolute or carries a scheme that is not a socket.</exception>
    public static NoireWebsocketClient Connect(string url, NoireWebsocketClientOptions? options = null,
        Action<NoireWebsocketClient>? configure = null)
        => Start(new NoireWebsocketClient(url, options), configure, static client => client.ConnectAsync());

    /// <summary>
    /// Opens a raw WebSocket connection, attaching handlers first.
    /// </summary>
    /// <param name="url">The absolute URL. An <c>http</c> or <c>https</c> scheme is read as <c>ws</c> or <c>wss</c>.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the callback is null.</exception>
    public static NoireWebsocketClient Connect(string url, Action<NoireWebsocketClient> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Connect(url, null, configure);
    }

    /// <summary>
    /// Opens a raw WebSocket connection.
    /// </summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="options">The settings the connection runs under. Null takes the defaults.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    public static NoireWebsocketClient Connect(Uri url, NoireWebsocketClientOptions? options = null,
        Action<NoireWebsocketClient>? configure = null)
        => Start(new NoireWebsocketClient(url, options), configure, static client => client.ConnectAsync());

    /// <summary>
    /// Opens a raw WebSocket connection, attaching handlers first.
    /// </summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the callback is null.</exception>
    public static NoireWebsocketClient Connect(Uri url, Action<NoireWebsocketClient> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Connect(url, null, configure);
    }

    /// <summary>
    /// Opens a raw WebSocket connection to a socket published by another NoireRemote listener, taking the address and
    /// the credential from its directory record.
    /// </summary>
    /// <param name="record">The record, as <see cref="NoireRemoteDirectory"/> reads it.</param>
    /// <param name="socketName">The socket name, as the publisher spelled it.</param>
    /// <param name="options">The settings the connection runs under. They are copied, and the record's credential is
    /// used only when none is set on them.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the record is null.</exception>
    /// <exception cref="ArgumentException">If the socket name is not usable on the wire.</exception>
    public static NoireWebsocketClient ConnectTo(NoireRemoteInstanceRecord record, string socketName,
        NoireWebsocketClientOptions? options = null, Action<NoireWebsocketClient>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Connect(
            WebsocketDiscovery.SocketUrl(record.Address, record.Port, socketName),
            WithCredential(options, WebsocketDiscovery.CredentialFor(record)),
            configure);
    }

    /// <summary>
    /// Opens a raw WebSocket connection to a socket published by another NoireRemote listener, attaching handlers first.
    /// </summary>
    /// <param name="record">The record, as <see cref="NoireRemoteDirectory"/> reads it.</param>
    /// <param name="socketName">The socket name, as the publisher spelled it.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the record or the callback is null.</exception>
    public static NoireWebsocketClient ConnectTo(NoireRemoteInstanceRecord record, string socketName,
        Action<NoireWebsocketClient> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return ConnectTo(record, socketName, null, configure);
    }

    /// <summary>
    /// Opens a raw WebSocket connection to a socket published by an instance discovery found, signing with the shared
    /// secret when the instance carries one and sending the loopback token otherwise.
    /// </summary>
    /// <param name="instance">The instance, as <see cref="NoireRemoteClient"/> lists or addresses it.</param>
    /// <param name="socketName">The socket name, as the publisher spelled it.</param>
    /// <param name="options">The settings the connection runs under. They are copied, and the instance's credential
    /// is used only when none is set on them.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the instance is null.</exception>
    /// <exception cref="ArgumentException">If the socket name is not usable on the wire.</exception>
    public static NoireWebsocketClient ConnectTo(NoireRemoteInstance instance, string socketName,
        NoireWebsocketClientOptions? options = null, Action<NoireWebsocketClient>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Connect(
            WebsocketDiscovery.SocketUrl(instance.Address, instance.Port, socketName),
            WithCredential(options, WebsocketDiscovery.CredentialFor(instance)),
            configure);
    }

    /// <summary>
    /// Opens a raw WebSocket connection to a socket published by an instance discovery found, attaching handlers first.
    /// </summary>
    /// <param name="instance">The instance, as <see cref="NoireRemoteClient"/> lists or addresses it.</param>
    /// <param name="socketName">The socket name, as the publisher spelled it.</param>
    /// <param name="configure">Attaches handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the instance or the callback is null.</exception>
    public static NoireWebsocketClient ConnectTo(NoireRemoteInstance instance, string socketName,
        Action<NoireWebsocketClient> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return ConnectTo(instance, socketName, null, configure);
    }

    /// <summary>
    /// Opens a server-sent events stream.
    /// </summary>
    /// <param name="url">The absolute URL of the event stream.</param>
    /// <param name="options">The settings the stream runs under. Null takes the defaults.</param>
    /// <param name="configure">Attaches handlers before the stream opens.</param>
    /// <returns>The client, already connecting.</returns>
    public static NoireSseClient ConnectSse(string url, NoireSseOptions? options = null,
        Action<NoireSseClient>? configure = null)
        => Start(new NoireSseClient(url, options), configure, static client => client.ConnectAsync());

    /// <summary>
    /// Opens a server-sent events stream, attaching handlers first.
    /// </summary>
    /// <param name="url">The absolute URL of the event stream.</param>
    /// <param name="configure">Attaches handlers before the stream opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the callback is null.</exception>
    public static NoireSseClient ConnectSse(string url, Action<NoireSseClient> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return ConnectSse(url, null, configure);
    }

    /// <summary>
    /// Starts a long-polling loop.
    /// </summary>
    /// <param name="url">The absolute URL to poll.</param>
    /// <param name="options">The settings the loop runs under. Null takes the defaults.</param>
    /// <param name="configure">Attaches handlers before the first poll is sent.</param>
    /// <returns>The client, already polling.</returns>
    public static NoireLongPollClient ConnectLongPoll(string url, NoireLongPollOptions? options = null,
        Action<NoireLongPollClient>? configure = null)
        => Start(new NoireLongPollClient(url, options), configure, static client => client.ConnectAsync());

    /// <summary>
    /// Starts a long-polling loop, attaching handlers first.
    /// </summary>
    /// <param name="url">The absolute URL to poll.</param>
    /// <param name="configure">Attaches handlers before the first poll is sent.</param>
    /// <returns>The client, already polling.</returns>
    /// <exception cref="ArgumentNullException">If the callback is null.</exception>
    public static NoireLongPollClient ConnectLongPoll(string url, Action<NoireLongPollClient> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return ConnectLongPoll(url, null, configure);
    }

    /// <summary>
    /// Opens a Socket.IO connection. A server behind a proxy that rewraps WebSocket frames needs
    /// <see cref="NoireSocketIOTransport.PollingOnly"/> on the options.
    /// </summary>
    /// <param name="url">The absolute server URL. The namespace is the path on it.</param>
    /// <param name="options">The settings the connection runs under. Null takes the defaults.</param>
    /// <param name="configure">Attaches event handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    public static NoireSocketIOClient ConnectSocketIO(string url, NoireSocketIOOptions? options = null,
        Action<NoireSocketIOClient>? configure = null)
        => Start(new NoireSocketIOClient(url, options), configure, static client => client.ConnectAsync());

    /// <summary>
    /// Opens a Socket.IO connection, attaching event handlers first.
    /// </summary>
    /// <param name="url">The absolute server URL. The namespace is the path on it.</param>
    /// <param name="configure">Attaches event handlers before the connection opens.</param>
    /// <returns>The client, already connecting.</returns>
    /// <exception cref="ArgumentNullException">If the callback is null.</exception>
    public static NoireSocketIOClient ConnectSocketIO(string url, Action<NoireSocketIOClient> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return ConnectSocketIO(url, null, configure);
    }

    // One dispose key for everything the facade opened.
    internal static void DisposeAll()
    {
        foreach (var connection in TakeConnections())
        {
            try
            {
                connection.Dispose();
            }
            catch (Exception exception)
            {
                NoireLogger.LogError(exception, "[NoireWebsocket] a connection did not close cleanly.");
            }
        }

        // The listener's own teardown never tells the socket half.
        NoireWebsocketServer.Find(NoireRemote.InstanceId)?.Dispose();

        // The same key registered twice is refused. It is handed back here.
        disposeHookRegistered = false;
        NoireLibMain.UnregisterOnDispose(DisposeKey);
    }

    private static NoireWebsocketClientOptions WithCredential(NoireWebsocketClientOptions? options,
        NoireSocketCredential? credential)
    {
        // Copied. A reused options object would carry the first instance's credential.
        var settings = options?.Clone() ?? new NoireWebsocketClientOptions();
        settings.Http.Credential ??= credential;

        return settings;
    }

    private static T Start<T>(T client, Action<T>? configure, Func<T, Task> connect)
        where T : class, IDisposable
    {
        RegisterDisposeHook();

        lock (ConnectionGate)
        {
            // Collected connections are compacted out.
            Connections.RemoveAll(static entry => !entry.TryGetTarget(out _));
            Connections.Add(new WeakReference<IDisposable>(client));
        }

        // Handlers attach before anything is read or written.
        configure?.Invoke(client);

        Observe(connect(client));

        return client;
    }

    // A caller that never awaited the connect must not meet an unobserved exception.
    private static void Observe(Task connect)
    {
        if (connect.IsCompleted)
        {
            _ = connect.Exception;
            return;
        }

        _ = connect.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static IReadOnlyList<IDisposable> TakeConnections()
    {
        lock (ConnectionGate)
        {
            var live = new List<IDisposable>(Connections.Count);

            foreach (var entry in Connections)
            {
                if (entry.TryGetTarget(out var connection))
                    live.Add(connection);
            }

            Connections.Clear();

            return live;
        }
    }

    private static void RegisterDisposeHook()
    {
        if (disposeHookRegistered)
            return;

        disposeHookRegistered = NoireLibMain.RegisterOnDispose(DisposeKey, DisposeAll);
    }
}
