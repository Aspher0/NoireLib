using NoireLib.Remote;
using NoireLib.Remote.Internal;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

/// <summary>
/// One published socket, reachable at <c>/noire/ws/{name}</c> on the listener NoireRemote already binds. Handlers are
/// registered on it before or after it is published, and every callback runs on the endpoint's thread in the order
/// the peer sent them.
/// </summary>
public sealed class NoireWebsocketEndpoint : IDisposable
{
    private readonly ConcurrentDictionary<Guid, NoireWebsocketConnection> connections = new();
    private readonly SocketHandlerSet<NoireWebsocketConnection> openHandlers;
    private readonly SocketHandlerSet<SocketInbound> messageHandlers;
    private readonly SocketHandlerSet<SocketClosed> closeHandlers;
    private readonly SocketHandlerSet<Exception> errorHandlers;
    private readonly SocketPump pump;

    private int disposed;

    internal NoireWebsocketEndpoint(NoireWebsocketServer server, string name, NoireWebsocketEndpointOptions options)
    {
        Server = server;
        Name = name;
        Options = options;
        Path = NoireWebsocketServer.RoutePrefix + name;

        openHandlers = new SocketHandlerSet<NoireWebsocketConnection>(Report);
        messageHandlers = new SocketHandlerSet<SocketInbound>(Report);
        closeHandlers = new SocketHandlerSet<SocketClosed>(Report);
        errorHandlers = new SocketHandlerSet<Exception>(LogHandlerFault);
        pump = new SocketPump(() => Server.Http.Host, () => Thread, Report);
    }

    /// <summary>
    /// Gets the name the socket is published under.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the path a peer upgrades on, such as <c>/noire/ws/chat</c>.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the server the socket is published on.
    /// </summary>
    public NoireWebsocketServer Server { get; }

    /// <summary>
    /// Gets the settings this socket runs under. They are read when a peer upgrades. A change reaches only the
    /// next connection. Connections already open keep their existing settings.
    /// </summary>
    public NoireWebsocketEndpointOptions Options { get; }

    /// <summary>
    /// Gets the thread handlers actually run on, after the endpoint, the server options and the host have each had
    /// their say.
    /// </summary>
    public NoireRemoteThread Thread
        => RemoteTypeScanner.Resolve(NoireRemoteThread.Inherit, Options.Thread, Server.Options.Thread, Server.Http.Host);

    /// <summary>
    /// Gets whether an upgrade from another machine is accepted, after the endpoint and the listener have each had
    /// their say.
    /// </summary>
    public NoireRemoteAccess Access
        => RemoteTypeScanner.ResolveAccess(NoireRemoteAccess.Inherit, Options.Access);

    /// <summary>
    /// Gets the character state a message needs before it is delivered, after the endpoint and the thread have each
    /// had their say.
    /// </summary>
    public NoireRemoteReadiness Requires
        => RemoteTypeScanner.ResolveReadiness(NoireRemoteReadiness.Inherit, Options.Requires, Thread);

    /// <summary>
    /// Gets how many peers are connected.
    /// </summary>
    public int ClientCount => connections.Count;

    /// <summary>Gets the connected peers as a snapshot. Safe to enumerate while connections change.</summary>
    public IReadOnlyList<NoireWebsocketConnection> Clients => [.. connections.Values];

    /// <summary>
    /// Gets the rooms that have a member, as a snapshot taken now.
    /// </summary>
    public IReadOnlyList<NoireWebsocketRoom> Rooms => RoomTable.Snapshot();

    /// <summary>
    /// Gets whether the endpoint has been disposed and unpublished.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    internal RoomTable RoomTable { get; } = new();

    internal SocketPump Pump => pump;

    /// <summary>
    /// Finds a room by name.
    /// </summary>
    /// <param name="name">The room name, matched ignoring case.</param>
    /// <returns>The room, or null when nothing is in it.</returns>
    public NoireWebsocketRoom? GetRoom(string name)
        => RoomTable.Get(name);

    /// <summary>
    /// Finds a connected peer by id.
    /// </summary>
    /// <param name="id">The connection id.</param>
    /// <returns>The connection, or null when it is no longer open.</returns>
    public NoireWebsocketConnection? GetClient(Guid id)
        => connections.TryGetValue(id, out var connection) ? connection : null;

    /// <summary>
    /// Registers a handler run when a peer connects, before the first message of that connection reaches anything.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnOpen(Action<NoireWebsocketConnection> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return openHandlers.Add(connection => { handler(connection); return Task.CompletedTask; }, options);
    }

    /// <summary>
    /// Registers a handler run when a peer connects, awaited before the first message is delivered.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnOpen(Func<NoireWebsocketConnection, Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return openHandlers.Add(handler, options);
    }

    /// <summary>
    /// Registers a handler run for every message a peer sends.
    /// </summary>
    /// <param name="handler">What to run. The message's bytes stop being valid when it returns.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnMessage(Action<NoireWebsocketConnection, NoireWebsocketMessage> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddMessageHandler((connection, message) => { handler(connection, message); return Task.CompletedTask; },
            NoireRemoteReadiness.Inherit, NoireRemoteAccess.Inherit, options);
    }

    /// <summary>
    /// Registers a handler run for every message a peer sends, awaited before the next one is read.
    /// </summary>
    /// <param name="handler">What to run. The message's bytes stop being valid when the returned task completes.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnMessage(Func<NoireWebsocketConnection, NoireWebsocketMessage, Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddMessageHandler(handler, NoireRemoteReadiness.Inherit, NoireRemoteAccess.Inherit, options);
    }

    /// <summary>
    /// Registers a handler run when a connection ends, whichever side ended it.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnClose(Action<NoireWebsocketConnection, NoireWebsocketClose> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return closeHandlers.Add(context => { handler(context.Connection, context.Close); return Task.CompletedTask; }, options);
    }

    /// <summary>
    /// Registers a handler run when a connection ends, awaited before the next callback is delivered.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnClose(Func<NoireWebsocketConnection, NoireWebsocketClose, Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return closeHandlers.Add(context => handler(context.Connection, context.Close), options);
    }

    /// <summary>
    /// Registers a handler run for every failure on this socket, including one a single connection recovers from.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnError(Action<Exception> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return errorHandlers.Add(exception => { handler(exception); return Task.CompletedTask; }, options);
    }

    /// <summary>
    /// Registers a handler run for every failure on this socket, awaited before the next one is reported.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnError(Func<Exception, Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return errorHandlers.Add(handler, options);
    }

    /// <summary>
    /// Removes every subscription registered under an owner.
    /// </summary>
    /// <param name="owner">The owner named in <see cref="NoireSocketSubscribeOptions.Owner"/>.</param>
    public void UnsubscribeAll(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        openHandlers.RemoveOwner(owner);
        messageHandlers.RemoveOwner(owner);
        closeHandlers.RemoveOwner(owner);
        errorHandlers.RemoveOwner(owner);
    }

    /// <summary>
    /// Queues a text message to the peers that pass the filters and returns without waiting for it to be written.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="room">A room to stay inside, or null for every connected peer.</param>
    /// <param name="except">A connection to leave out, such as the sender.</param>
    /// <param name="where">A further test a peer has to pass, or null for all of them.</param>
    public void Broadcast(string text, string? room = null, NoireWebsocketConnection? except = null, Func<NoireWebsocketConnection, bool>? where = null)
        => Deliver(TargetsOf(room), NoireWebsocketMessageKind.Text, Encoding.UTF8.GetBytes(text ?? string.Empty), except, where);

    /// <summary>
    /// Queues a binary message to the peers that pass the filters and returns without waiting for it to be written.
    /// </summary>
    /// <param name="bytes">The payload.</param>
    /// <param name="room">A room to stay inside, or null for every connected peer.</param>
    /// <param name="except">A connection to leave out.</param>
    /// <param name="where">A further test a peer has to pass, or null for all of them.</param>
    public void Broadcast(byte[] bytes, string? room = null, NoireWebsocketConnection? except = null, Func<NoireWebsocketConnection, bool>? where = null)
        => Deliver(TargetsOf(room), NoireWebsocketMessageKind.Binary, bytes ?? [], except, where);

    /// <summary>
    /// Queues a value serialized as JSON to the peers that pass the filters and returns without waiting.
    /// </summary>
    /// <param name="value">The value. It goes through the shared serializer, never a per-message one.</param>
    /// <param name="room">A room to stay inside, or null for every connected peer.</param>
    /// <param name="except">A connection to leave out.</param>
    /// <param name="where">A further test a peer has to pass, or null for all of them.</param>
    public void BroadcastJson(object? value, string? room = null, NoireWebsocketConnection? except = null, Func<NoireWebsocketConnection, bool>? where = null)
        => Deliver(TargetsOf(room), NoireWebsocketMessageKind.Text, NoireRemoteJson.WriteBytes(value), except, where);

    /// <summary>
    /// Sends a text message to the peers that pass the filters and waits until each has been written or has failed.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="room">A room to stay inside, or null for every connected peer.</param>
    /// <param name="except">A connection to leave out.</param>
    /// <param name="where">A further test a peer has to pass, or null for all of them.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing when every peer has been written to.</returns>
    public Task BroadcastAsync(string text, string? room = null, NoireWebsocketConnection? except = null,
        Func<NoireWebsocketConnection, bool>? where = null, CancellationToken cancellationToken = default)
        => DeliverAsync(TargetsOf(room), NoireWebsocketMessageKind.Text, Encoding.UTF8.GetBytes(text ?? string.Empty), except, where, cancellationToken);

    /// <summary>
    /// Sends a binary message to the peers that pass the filters and waits until each has been written or has failed.
    /// </summary>
    /// <param name="bytes">The payload.</param>
    /// <param name="room">A room to stay inside, or null for every connected peer.</param>
    /// <param name="except">A connection to leave out.</param>
    /// <param name="where">A further test a peer has to pass, or null for all of them.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing when every peer has been written to.</returns>
    public Task BroadcastAsync(byte[] bytes, string? room = null, NoireWebsocketConnection? except = null,
        Func<NoireWebsocketConnection, bool>? where = null, CancellationToken cancellationToken = default)
        => DeliverAsync(TargetsOf(room), NoireWebsocketMessageKind.Binary, bytes ?? [], except, where, cancellationToken);

    /// <summary>
    /// Closes every connection on this socket with the close handshake.
    /// </summary>
    /// <param name="code">The code sent to each peer.</param>
    /// <param name="reason">The reason text sent with it, or null for none.</param>
    /// <returns>A task completing once every connection is down.</returns>
    public async Task CloseAllAsync(NoireWebsocketCloseCode code = NoireWebsocketCloseCode.GoingAway, string? reason = null)
    {
        var open = Clients;

        if (open.Count == 0)
            return;

        var closing = new List<Task>(open.Count);

        foreach (var connection in open)
            closing.Add(connection.CloseAsync(code, reason));

        await Task.WhenAll(closing).ConfigureAwait(false);
    }

    /// <summary>
    /// Unpublishes the socket, drops every connection on it and removes every handler. A consumer that never calls it
    /// loses nothing: the server frees every endpoint it owns when it is disposed.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        Server.Unpublish(this);

        foreach (var connection in Clients)
            connection.Abort();

        pump.Dispose();
        openHandlers.Clear();
        messageHandlers.Clear();
        closeHandlers.Clear();
        errorHandlers.Clear();
    }

    // Keeps the method attribute's settings beside the handler for the per-message fallback.
    internal NoireSocketSubscription AddMessageHandler(
        Func<NoireWebsocketConnection, NoireWebsocketMessage, Task> handler,
        NoireRemoteReadiness requires,
        NoireRemoteAccess access,
        NoireSocketSubscribeOptions? options)
        => messageHandlers.Add(
            inbound => Allows(inbound.Connection, requires, access, inbound.Generation)
                ? handler(inbound.Connection, inbound.Message)
                : Task.CompletedTask,
            options);

    internal NoireSocketSubscription AddOpenHandler(Func<NoireWebsocketConnection, Task> handler, NoireSocketSubscribeOptions? options)
        => openHandlers.Add(handler, options);

    internal NoireSocketSubscription AddCloseHandler(Func<NoireWebsocketConnection, NoireWebsocketClose, Task> handler, NoireSocketSubscribeOptions? options)
        => closeHandlers.Add(context => handler(context.Connection, context.Close), options);

    internal NoireSocketSubscription AddErrorHandler(Func<Exception, Task> handler, NoireSocketSubscribeOptions? options)
        => errorHandlers.Add(handler, options);

    // The budget is claimed and the connection registered first. A refusal stays an ordinary HTTP answer.
    internal async Task AdoptAsync(HttpConnectionAdoption adoption, string accept, string? subProtocol)
    {
        var connection = Register(adoption.Stream, adoption.Client, adoption.RemoteAddress, adoption.IsLoopback, subProtocol, out var claim);

        if (connection == null)
        {
            await HttpResponseWriter.WriteFailureAsync(adoption.Stream, RefusalOf(claim), Server.Http.InstanceId, null, CancellationToken.None).ConfigureAwait(false);
            adoption.Client.Dispose();

            return;
        }

        try
        {
            await HttpResponseWriter.WriteUpgradeAsync(adoption.Stream, accept, subProtocol, Server.Http.InstanceId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            connections.TryRemove(connection.Id, out _);
            Server.Slots.Release(adoption.RemoteAddress);
            adoption.Client.Dispose();

            throw;
        }

        connection.Start(adoption.Carried);
    }

    // Null when the socket budget refused it. claim says which limit.
    internal NoireWebsocketConnection? Register(
        Stream stream, IDisposable? owner, string remoteAddress, bool isLoopback, string? subProtocol, out SocketSlotResult claim)
    {
        var options = Server.Options.Clone();

        claim = Server.Slots.TryClaim(remoteAddress, options.MaxSockets, options.MaxSocketsPerAddress);

        if (claim != SocketSlotResult.Claimed)
            return null;

        var connection = new NoireWebsocketConnection(this, stream, owner, remoteAddress, isLoopback, subProtocol, options);
        connections[connection.Id] = connection;

        return connection;
    }

    // Totals since the socket was published, for the console.
    private long messagesIn;
    private long messagesOut;
    private long bytesIn;
    private long bytesOut;

    internal long MessagesIn => Interlocked.Read(ref messagesIn);

    internal long MessagesOut => Interlocked.Read(ref messagesOut);

    internal long BytesIn => Interlocked.Read(ref bytesIn);

    internal long BytesOut => Interlocked.Read(ref bytesOut);

    internal void CountIn(int bytes)
    {
        Interlocked.Increment(ref messagesIn);
        Interlocked.Add(ref bytesIn, bytes);
    }

    internal void CountOut(int bytes)
    {
        Interlocked.Increment(ref messagesOut);
        Interlocked.Add(ref bytesOut, bytes);
    }

    // An open console page is told. With no page open this is two field reads.
    internal void Touched()
    {
        if (Name.Length > 0 && Name[0] != '_')
            Server.Http.InvalidateConsole(NoireRemoteServer.ConsoleScopes.Sockets);
    }

    internal void EnqueueOpen(NoireWebsocketConnection connection)
    {
        Touched();

        RemoteTraffic.Socket(
            Server.Http.ChannelsIfAny, Server.Http.Options, Name, "open", connection.Id, connection.RemoteAddress, null, null);

        pump.Enqueue(() => openHandlers.DispatchAsync(connection));
    }

    // The payload stops being valid when the read loop reads the next frame.
    internal Task DeliverMessageAsync(NoireWebsocketConnection connection, NoireWebsocketMessage message, long generation)
    {
        connection.CountIn(message.Bytes.Length);

        if (RemoteTraffic.WantsFrames(Server.Http.ChannelsIfAny, Server.Http.Options, Name))
        {
            RemoteTraffic.Frame(
                Server.Http.ChannelsIfAny, Server.Http.Options, Name, "in", connection.Id,
                message.Bytes.Length, message.Kind == NoireWebsocketMessageKind.Text ? message.Text : null);
        }

        if (!messageHandlers.HasHandlers)
            return Task.CompletedTask;

        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        pump.Enqueue(async () =>
        {
            try
            {
                if (Allows(connection, NoireRemoteReadiness.Inherit, NoireRemoteAccess.Inherit, generation))
                    await messageHandlers.DispatchAsync(new SocketInbound(connection, message, generation)).ConfigureAwait(false);
            }
            finally
            {
                delivered.TrySetResult();
            }
        });

        return WaitForDeliveryAsync(delivered.Task, connection.Life);
    }

    internal void Unregister(NoireWebsocketConnection connection, NoireWebsocketClose close)
    {
        connections.TryRemove(connection.Id, out _);
        RoomTable.Drop(connection);
        Server.Slots.Release(connection.RemoteAddress);

        RemoteTraffic.Socket(
            Server.Http.ChannelsIfAny, Server.Http.Options, Name, "closed", connection.Id, connection.RemoteAddress,
            (int)close.RawCode, close.Reason);

        Touched();

        pump.Enqueue(() => closeHandlers.DispatchAsync(new SocketClosed(connection, close)));
    }

    internal void Deliver(
        IReadOnlyList<NoireWebsocketConnection> targets,
        NoireWebsocketMessageKind kind,
        byte[] payload,
        NoireWebsocketConnection? except,
        Func<NoireWebsocketConnection, bool>? where)
    {
        if (targets.Count == 0)
            return;

        // Encoded once for every peer.
        var frame = WebsocketFrameWriter.Message(kind, payload);

        foreach (var connection in targets)
        {
            if (!Passes(connection, except, where))
                continue;

            connection.Post(frame);
        }
    }

    internal Task DeliverAsync(
        IReadOnlyList<NoireWebsocketConnection> targets,
        NoireWebsocketMessageKind kind,
        byte[] payload,
        NoireWebsocketConnection? except,
        Func<NoireWebsocketConnection, bool>? where,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
            return Task.CompletedTask;

        var frame = WebsocketFrameWriter.Message(kind, payload);
        List<Task>? writes = null;

        foreach (var connection in targets)
        {
            if (!Passes(connection, except, where))
                continue;

            writes ??= new List<Task>(targets.Count);
            writes.Add(connection.SendFrameQuietlyAsync(frame, cancellationToken));
        }

        return writes == null ? Task.CompletedTask : Task.WhenAll(writes);
    }

    internal void Report(Exception exception)
    {
        if (errorHandlers.HasHandlers)
        {
            pump.Enqueue(() => errorHandlers.DispatchAsync(exception));
            return;
        }

        Server.Http.Host.Log(NoireRemoteLogLevel.Error, "[NoireWebsocket] " + Path + " reported a failure with no error handler attached.", exception);
    }

    private void LogHandlerFault(Exception exception)
        => Server.Http.Host.Log(NoireRemoteLogLevel.Error, "[NoireWebsocket] an error handler of " + Path + " threw.", exception);

    // A disposed pump drops the work. The wait is bounded by the connection's lifetime.
    private static async Task WaitForDeliveryAsync(Task delivered, CancellationToken life)
    {
        try
        {
            await delivered.WaitAsync(life).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private IReadOnlyList<NoireWebsocketConnection> TargetsOf(string? room)
        => room == null ? Clients : RoomTable.Get(room)?.Clients ?? [];

    private static bool Passes(NoireWebsocketConnection connection, NoireWebsocketConnection? except, Func<NoireWebsocketConnection, bool>? where)
    {
        if (except != null && ReferenceEquals(connection, except))
            return false;

        return where == null || where(connection);
    }

    private bool Allows(NoireWebsocketConnection connection, NoireRemoteReadiness requires, NoireRemoteAccess access, long generation)
    {
        if (RemoteTypeScanner.ResolveAccess(access, Options.Access) == NoireRemoteAccess.Local && !connection.IsLoopback)
            return false;

        var wanted = RemoteTypeScanner.ResolveReadiness(requires, Options.Requires, Thread);

        if (connection.IsReady(wanted, generation, out var reason))
            return true;

        // Once per drain.
        if (connection.ShouldReportNotReady(generation))
            Report(new NoireSocketException("A message on " + Path + " was not delivered: " + reason));

        return false;
    }

    private static HttpFailure RefusalOf(SocketSlotResult claim)
        => claim == SocketSlotResult.TooManyForAddress
            ? new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener holds as many sockets from one address as it accepts.") { RetryAfterSeconds = 5 }
            : new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener holds as many sockets as it accepts.") { RetryAfterSeconds = 5 };
}
