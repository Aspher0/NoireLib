using NoireLib.Remote;
using NoireLib.Remote.Internal;
using NoireLib.Websocket.Internal;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

/// <summary>
/// One socket a peer opened against a published endpoint. It reaches
/// <see cref="NoireSocketState.Connected"/>, <see cref="NoireSocketState.Closing"/> and
/// <see cref="NoireSocketState.Disconnected"/>. An accepted connection never reconnects itself. The peer opens a
/// new one instead.
/// </summary>
public sealed class NoireWebsocketConnection : IDisposable
{
    private const int ReadChunk = 8192;

    private static readonly byte[] HeartbeatPing = WebsocketFrameWriter.Ping(ReadOnlySpan<byte>.Empty);

    private readonly Stream stream;
    private readonly IDisposable? owner;
    private readonly NoireWebsocketServerOptions options;
    private readonly WebsocketCodec codec;
    private readonly PeerSendQueue queue;
    private readonly CancellationTokenSource life = new();
    private readonly CancellationToken lifeToken;
    private readonly ConcurrentDictionary<string, object?> items = new(StringComparer.Ordinal);

    // One answer per readiness value per drain, indexed by enum value.
    private readonly NoireRemoteReadinessResult[] readiness = new NoireRemoteReadinessResult[4];
    private readonly long[] readinessStamp = [-1, -1, -1, -1];

    private Task? readLoop;
    private Task? writeLoop;
    private NoireWebsocketClose? localClose;
    private long drainGeneration;
    private long reportedNotReady = -1;
    private int stateValue;
    private int closeSent;
    private int finished;

    internal NoireWebsocketConnection(
        NoireWebsocketEndpoint endpoint,
        Stream stream,
        IDisposable? owner,
        string remoteAddress,
        bool isLoopback,
        string? subProtocol,
        NoireWebsocketServerOptions options)
    {
        Endpoint = endpoint;
        RemoteAddress = remoteAddress;
        IsLoopback = isLoopback;
        SubProtocol = subProtocol;

        this.stream = stream;
        this.owner = owner;
        this.options = options;

        // Reading Token after teardown throws.
        lifeToken = life.Token;

        codec = new WebsocketCodec(new WebsocketCodecLimits(options.MaxFrameSize, options.MaxMessageSize, options.MaxMessageFragments));
        queue = new PeerSendQueue(options.PeerQueueCapacity, options.Overflow);
    }

    /// <summary>
    /// Gets the id identifying this connection for as long as it is open.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets the endpoint the peer connected to.
    /// </summary>
    public NoireWebsocketEndpoint Endpoint { get; }

    /// <summary>
    /// Gets the peer's address. Every connection from this machine reads as 127.0.0.1.
    /// </summary>
    public string RemoteAddress { get; }

    /// <summary>
    /// Gets whether the peer is on this machine.
    /// </summary>
    public bool IsLoopback { get; }

    /// <summary>
    /// Gets the sub-protocol the handshake settled on, or null when none was negotiated.
    /// </summary>
    public string? SubProtocol { get; }

    /// <summary>
    /// Gets when the connection was accepted.
    /// </summary>
    public DateTime OpenedUtc { get; } = DateTime.UtcNow;

    /// <summary>
    /// Gets where the connection is in its lifecycle.
    /// </summary>
    public NoireSocketState State => (NoireSocketState)Volatile.Read(ref stateValue);

    /// <summary>
    /// Gets whether the connection is open and carrying messages.
    /// </summary>
    public bool IsConnected => State == NoireSocketState.Connected;

    /// <summary>
    /// Gets how the connection ended, or null while it is still open.
    /// </summary>
    public NoireWebsocketClose? Close { get; private set; }

    /// <summary>
    /// Gets whatever the application wants to keep beside this connection, such as who authenticated on it. Nothing
    /// in the library reads it.
    /// </summary>
    public ConcurrentDictionary<string, object?> Items => items;

    /// <summary>
    /// Gets how many messages are waiting to be written to this peer. A plugin can throttle itself with this before
    /// <see cref="NoireWebsocketServerOptions.Overflow"/> fires.
    /// </summary>
    public int QueuedMessages => queue.Count;

    /// <summary>
    /// Gets the rooms this connection is in, as a snapshot taken now.
    /// </summary>
    public IReadOnlyList<string> Rooms => Endpoint.RoomTable.RoomsOf(this);

    /// <summary>
    /// Joins a room, creating it when it is the first member.
    /// </summary>
    /// <param name="name">The room name, matched ignoring case.</param>
    /// <returns>The room, whether it already existed or was created.</returns>
    /// <exception cref="ArgumentException">If the name is blank or longer than <see cref="NoireWebsocketServerOptions.MaxRoomNameLength"/>.</exception>
    /// <exception cref="NoireSocketException">If the endpoint is at <see cref="NoireWebsocketServerOptions.MaxRooms"/> or the connection is at <see cref="NoireWebsocketServerOptions.MaxRoomsPerConnection"/>.</exception>
    public NoireWebsocketRoom Join(string name)
    {
        var room = Endpoint.RoomTable.Join(Endpoint, this, name, options);

        Endpoint.Touched();

        return room;
    }

    /// <summary>
    /// Leaves a room, removing it when this was its last member.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <returns>True when the connection was in it.</returns>
    public bool Leave(string name)
    {
        var left = Endpoint.RoomTable.Leave(this, name);

        if (left)
            Endpoint.Touched();

        return left;
    }

    /// <summary>
    /// Checks whether the connection is in a room.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <returns>True when it is a member.</returns>
    public bool IsIn(string name)
        => Endpoint.RoomTable.IsIn(this, name);

    /// <summary>
    /// Queues a text message and returns without waiting for it to be written. A connection that has already closed
    /// swallows it without throwing. A broadcast over a snapshot is not a race.
    /// </summary>
    /// <param name="text">The message.</param>
    public void Send(string text)
        => Post(Outbound(NoireWebsocketMessageKind.Text, Encoding.UTF8.GetBytes(text ?? string.Empty)));

    /// <summary>
    /// Queues a binary message and returns without waiting for it to be written.
    /// </summary>
    /// <param name="bytes">The payload.</param>
    public void Send(byte[] bytes)
        => Post(Outbound(NoireWebsocketMessageKind.Binary, bytes ?? []));

    /// <summary>
    /// Queues a value serialized as JSON and returns without waiting for it to be written.
    /// </summary>
    /// <param name="value">The value. It goes through the shared serializer, never a per-message one.</param>
    public void SendJson(object? value)
        => Post(Outbound(NoireWebsocketMessageKind.Text, NoireRemoteJson.WriteBytes(value)));

    /// <summary>
    /// Sends a text message and waits until it has been written.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing when the message has left.</returns>
    /// <exception cref="NoireSocketClosedException">If the connection is not open.</exception>
    /// <exception cref="NoireSocketQueueFullException">If the queue was full under <see cref="NoireWebsocketPeerOverflow.CloseConnection"/>. A drop policy completes instead. A caller watches <see cref="QueuedMessages"/> to avoid one.</exception>
    public Task SendAsync(string text, CancellationToken cancellationToken = default)
        => SendFrameAsync(Outbound(NoireWebsocketMessageKind.Text, Encoding.UTF8.GetBytes(text ?? string.Empty)), cancellationToken);

    /// <summary>
    /// Sends a binary message and waits until it has been written.
    /// </summary>
    /// <param name="bytes">The payload.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing when the message has left.</returns>
    /// <exception cref="NoireSocketClosedException">If the connection is not open.</exception>
    public Task SendAsync(byte[] bytes, CancellationToken cancellationToken = default)
        => SendFrameAsync(Outbound(NoireWebsocketMessageKind.Binary, bytes ?? []), cancellationToken);

    /// <summary>
    /// Sends a value serialized as JSON and waits until it has been written.
    /// </summary>
    /// <param name="value">The value. It goes through the shared serializer, never a per-message one.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing when the message has left.</returns>
    /// <exception cref="NoireSocketClosedException">If the connection is not open.</exception>
    public Task SendJsonAsync(object? value, CancellationToken cancellationToken = default)
        => SendFrameAsync(Outbound(NoireWebsocketMessageKind.Text, NoireRemoteJson.WriteBytes(value)), cancellationToken);

    /// <summary>
    /// Sends a ping and returns without waiting for the answer.
    /// </summary>
    public void Ping()
        => Post(HeartbeatPing);

    /// <summary>
    /// Closes the connection with the close handshake: the code goes out, nothing further is sent, and the peer's
    /// answer is waited for up to <see cref="NoireWebsocketServerOptions.CloseTimeout"/> before the socket is dropped.
    /// </summary>
    /// <param name="code">The code sent to the peer. 1005 and 1006 describe a connection. Neither travels on one. Either sends an empty close.</param>
    /// <param name="reason">The reason text, cut at 123 bytes on a character boundary, or null for none.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing once the connection is down.</returns>
    public async Task CloseAsync(NoireWebsocketCloseCode code = NoireWebsocketCloseCode.Normal, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref closeSent, 1) == 0)
        {
            localClose = new NoireWebsocketClose((int)code, reason, NoireWebsocketCloseInitiator.Local);
            SetState(NoireSocketState.Closing);

            var frame = new PeerSendItem(WebsocketFrameWriter.Close((int)code, reason), true);
            var written = frame.Awaited;
            queue.EnqueueFirst(frame);

            await AwaitQuietlyAsync(written, options.CloseTimeout, cancellationToken).ConfigureAwait(false);
        }

        var loop = readLoop;

        if (loop != null && !await AwaitQuietlyAsync(loop, options.CloseTimeout, cancellationToken).ConfigureAwait(false))
        {
            Abort();
            await AwaitQuietlyAsync(loop, options.CloseTimeout, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Drops the connection without the close handshake. Calling it is optional: the endpoint frees every connection
    /// it owns when it is disposed.
    /// </summary>
    public void Dispose()
        => Abort();

    // The read loop disposes the stream and client, whichever side ended.
    internal void Start(ReadOnlyMemory<byte> carried)
    {
        Volatile.Write(ref stateValue, (int)NoireSocketState.Connected);

        // A first frame that came with the handshake is dispatched after the open handler.
        Endpoint.EnqueueOpen(this);

        var pipelined = carried.IsEmpty ? [] : carried.ToArray();

        writeLoop = Task.Run(WriteLoopAsync, CancellationToken.None);
        readLoop = Task.Run(() => ReadLoopAsync(pipelined), CancellationToken.None);
    }

    internal CancellationToken Life => lifeToken;

    internal void Abort()
    {
        try
        {
            life.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        // Cancelling alone does not always unblock a read already inside the socket.
        try
        {
            stream.Dispose();
        }
        catch (Exception)
        {
        }
    }

    // For the console's socket view. A broadcast counts once per peer. Control frames do not count.
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
        Endpoint.CountIn(bytes);
        Endpoint.Touched();
    }

    private void CountOut(int bytes)
    {
        Interlocked.Increment(ref messagesOut);
        Interlocked.Add(ref bytesOut, bytes);
        Endpoint.CountOut(bytes);
        Endpoint.Touched();
    }

    // Every text and binary send passes here. The gate is read before decoding the payload.
    private byte[] Outbound(NoireWebsocketMessageKind kind, byte[] payload)
    {
        var server = Endpoint.Server.Http;

        if (RemoteTraffic.WantsFrames(server.ChannelsIfAny, server.Options, Endpoint.Name))
        {
            RemoteTraffic.Frame(
                server.ChannelsIfAny, server.Options, Endpoint.Name, "out", Id, payload.Length,
                kind == NoireWebsocketMessageKind.Text ? Encoding.UTF8.GetString(payload) : null);
        }

        return WebsocketFrameWriter.Message(kind, payload);
    }

    // A connection closed between the snapshot and the write is expected.
    internal void Post(byte[] frame)
    {
        if (!IsConnected)
            return;

        CountOut(frame.Length);

        var item = new PeerSendItem(frame);

        switch (queue.Enqueue(item))
        {
            case PeerEnqueueResult.Queued:
            case PeerEnqueueResult.DroppedNewest:
            case PeerEnqueueResult.DroppedOldest:
            case PeerEnqueueResult.Closed:
                return;

            default:
                OverflowClose();
                return;
        }
    }

    internal Task SendFrameQuietlyAsync(byte[] frame, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            return Task.CompletedTask;

        try
        {
            return SendFrameAsync(frame, cancellationToken);
        }
        catch (NoireSocketException)
        {
            return Task.CompletedTask;
        }
    }

    // Once per drain per readiness value.
    internal bool IsReady(NoireRemoteReadiness requires, long generation, out string reason)
    {
        reason = string.Empty;

        if (requires is NoireRemoteReadiness.None or NoireRemoteReadiness.Inherit)
            return true;

        var slot = (int)requires;

        if (readinessStamp[slot] != generation)
        {
            readiness[slot] = Endpoint.Server.Http.Host.CheckReadiness(requires, Endpoint.Path);
            readinessStamp[slot] = generation;
        }

        var answer = readiness[slot];

        if (answer.IsReady)
            return true;

        reason = answer.Reason;
        return false;
    }

    internal bool ShouldReportNotReady(long generation)
        => Interlocked.Exchange(ref reportedNotReady, generation) != generation;

    private Task SendFrameAsync(byte[] frame, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new NoireSocketClosedException("The connection to " + RemoteAddress + " is " + State + ".");

        CountOut(frame.Length);

        var item = new PeerSendItem(frame);
        var written = item.Awaited;

        switch (queue.Enqueue(item))
        {
            case PeerEnqueueResult.Queued:
                break;

            case PeerEnqueueResult.DroppedNewest:
            case PeerEnqueueResult.DroppedOldest:
                return Task.CompletedTask;

            case PeerEnqueueResult.Closed:
                throw new NoireSocketClosedException("The connection to " + RemoteAddress + " stopped before the message was queued.");

            default:
                OverflowClose();
                throw new NoireSocketQueueFullException(queue.Capacity);
        }

        return written.WaitAsync(cancellationToken);
    }

    private void OverflowClose()
        => _ = CloseAsync(NoireWebsocketCloseCode.TryAgainLater, "This connection is not reading the messages sent to it.");

    private async Task ReadLoopAsync(byte[] pipelined)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadChunk);
        NoireWebsocketClose? close = null;

        try
        {
            if (pipelined.Length > 0)
            {
                codec.Deliver(pipelined);
                close = await DrainAsync().ConfigureAwait(false);
            }

            while (close == null && !lifeToken.IsCancellationRequested)
            {
                int count;

                try
                {
                    count = await ReadAsync(buffer).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!lifeToken.IsCancellationRequested)
                        close = new NoireWebsocketClose((int)NoireWebsocketCloseCode.AbnormalClosure,
                            "Nothing arrived within the idle timeout.", NoireWebsocketCloseInitiator.Transport);

                    break;
                }

                if (count <= 0)
                    break;

                codec.Deliver(buffer.AsSpan(0, count));
                close = await DrainAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            Endpoint.Report(exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await FinishAsync(close).ConfigureAwait(false);
    }

    private async Task<int> ReadAsync(byte[] buffer)
    {
        if (options.IdleTimeout <= TimeSpan.Zero)
            return await stream.ReadAsync(buffer, lifeToken).ConfigureAwait(false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifeToken);
        deadline.CancelAfter(options.IdleTimeout);

        return await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
    }

    // A payload stops being valid when the loop reads the next frame. Each dispatch is awaited.
    private async Task<NoireWebsocketClose?> DrainAsync()
    {
        var generation = Interlocked.Increment(ref drainGeneration);

        while (codec.TryRead(out var result))
        {
            switch (result.Event)
            {
                case WebsocketCodecEvent.Message:
                    await Endpoint.DeliverMessageAsync(this, new NoireWebsocketMessage(result.MessageKind, result.Payload), generation).ConfigureAwait(false);
                    break;

                case WebsocketCodecEvent.Ping:
                    queue.EnqueueFirst(new PeerSendItem(WebsocketFrameWriter.Pong(result.Payload.Span)));
                    break;

                case WebsocketCodecEvent.Pong:
                    break;

                case WebsocketCodecEvent.Close:
                    return await EchoCloseAsync(result.Code, result.Reason).ConfigureAwait(false);

                case WebsocketCodecEvent.Failure:
                    return await FailAsync(result.Code, result.Reason).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task<NoireWebsocketClose> EchoCloseAsync(int code, string? reason)
    {
        if (Interlocked.Exchange(ref closeSent, 1) != 0)
        {
            // The peer answers a close this side started.
            return localClose ?? new NoireWebsocketClose(code, reason, NoireWebsocketCloseInitiator.Local);
        }

        SetState(NoireSocketState.Closing);

        var frame = new PeerSendItem(WebsocketFrameWriter.Close(code, reason), true);
        var written = frame.Awaited;
        queue.EnqueueFirst(frame);

        await AwaitQuietlyAsync(written, options.CloseTimeout, CancellationToken.None).ConfigureAwait(false);

        return new NoireWebsocketClose(code, reason, NoireWebsocketCloseInitiator.Remote);
    }

    private async Task<NoireWebsocketClose> FailAsync(int code, string? reason)
    {
        if (Interlocked.Exchange(ref closeSent, 1) == 0)
        {
            SetState(NoireSocketState.Closing);

            var frame = new PeerSendItem(WebsocketFrameWriter.Close(code, reason), true);
            var written = frame.Awaited;
            queue.EnqueueFirst(frame);

            await AwaitQuietlyAsync(written, options.CloseTimeout, CancellationToken.None).ConfigureAwait(false);
        }

        return new NoireWebsocketClose(code, reason, NoireWebsocketCloseInitiator.Local);
    }

    private async Task FinishAsync(NoireWebsocketClose? close)
    {
        if (Interlocked.Exchange(ref finished, 1) != 0)
            return;

        Close = close ?? localClose ?? new NoireWebsocketClose((int)NoireWebsocketCloseCode.AbnormalClosure, null, NoireWebsocketCloseInitiator.Transport);
        Volatile.Write(ref stateValue, (int)NoireSocketState.Disconnected);

        queue.Close(new NoireSocketClosedException("The connection to " + RemoteAddress + " ended before the message was written."));

        var writer = writeLoop;

        if (writer != null)
            await AwaitQuietlyAsync(writer, options.CloseTimeout, CancellationToken.None).ConfigureAwait(false);

        Abort();

        codec.Dispose();
        life.Dispose();

        try
        {
            owner?.Dispose();
        }
        catch (Exception)
        {
        }

        Endpoint.Unregister(this, Close);
    }

    private async Task WriteLoopAsync()
    {
        var wait = options.HeartbeatInterval > TimeSpan.Zero ? options.HeartbeatInterval : Timeout.InfiniteTimeSpan;

        try
        {
            while (!lifeToken.IsCancellationRequested)
            {
                var item = await queue.DequeueAsync(wait, lifeToken).ConfigureAwait(false);

                if (item == null)
                {
                    if (queue.IsClosed)
                        break;

                    await stream.WriteAsync(HeartbeatPing, lifeToken).ConfigureAwait(false);
                    await stream.FlushAsync(lifeToken).ConfigureAwait(false);

                    continue;
                }

                try
                {
                    await stream.WriteAsync(item.Frame, lifeToken).ConfigureAwait(false);
                    await stream.FlushAsync(lifeToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    item.Fail(exception);
                    throw;
                }

                item.Complete();

                // Nothing goes out after a close frame.
                if (item.IsClose)
                    break;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            Endpoint.Report(exception);
        }
    }

    private void SetState(NoireSocketState next)
        => Interlocked.Exchange(ref stateValue, (int)next);

    private static async Task<bool> AwaitQuietlyAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return task.IsCompleted;
        }
    }
}
