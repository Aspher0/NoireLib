using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

/// <summary>
/// One outbound RFC 6455 connection, with reconnection, a bounded send queue and callbacks delivered in order on the
/// host's thread. Construction opens nothing. Set <see cref="Options"/> and attach handlers first, then call
/// <see cref="ConnectAsync"/>. No message can arrive before anything is listening for it.<br/>
/// It reaches every value of <see cref="NoireSocketState"/>.
/// </summary>
public sealed partial class NoireWebsocketClient : IDisposable
{
    private readonly SocketHandlerSet<NoireWebsocketClient> openHandlers;
    private readonly SocketHandlerSet<NoireWebsocketMessage> messageHandlers;
    private readonly SocketHandlerSet<NoireWebsocketClose> closeHandlers;
    private readonly SocketHandlerSet<Exception> errorHandlers;
    private readonly SocketHandlerSet<NoireSocketState> stateHandlers;
    private readonly SocketPump pump;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly object lifecycleGate = new();

    private NoireWebsocketClientOptions active;
    private INoireRemoteHost? host;
    private SocketsHttpHandler? handler;
    private HttpMessageInvoker? invoker;
    private SendQueue? queue;
    private CancellationTokenSource? life;
    private Task? runLoop;
    private Task? writeLoop;
    private TaskCompletionSource openSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<Connection> connectionSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Connection? connection;
    private NoireWebsocketCloseCode localCloseCode = NoireWebsocketCloseCode.Normal;
    private string? localCloseReason;
    private int closedLocally;
    private int stateValue;
    private int disposed;

    /// <summary>
    /// Creates a client for a URL, without connecting.
    /// </summary>
    /// <param name="url">The absolute URL. An <c>http</c> or <c>https</c> scheme is read as <c>ws</c> or <c>wss</c>.</param>
    /// <param name="options">The settings to start from, or null for the defaults.</param>
    /// <exception cref="ArgumentException">If the URL is not absolute or carries a scheme that is not a socket.</exception>
    public NoireWebsocketClient(string url, NoireWebsocketClientOptions? options = null)
        : this(ParseUrl(url), options)
    {
    }

    /// <summary>
    /// Creates a client for a URL, without connecting.
    /// </summary>
    /// <param name="url">The absolute URL. An <c>http</c> or <c>https</c> scheme is read as <c>ws</c> or <c>wss</c>.</param>
    /// <param name="options">The settings to start from, or null for the defaults.</param>
    /// <exception cref="ArgumentException">If the URL is not absolute or carries a scheme that is not a socket.</exception>
    public NoireWebsocketClient(Uri url, NoireWebsocketClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(url);

        Url = NormalizeUrl(url);
        Options = options ?? new NoireWebsocketClientOptions();
        active = Options;

        openHandlers = new SocketHandlerSet<NoireWebsocketClient>(Report);
        messageHandlers = new SocketHandlerSet<NoireWebsocketMessage>(Report);
        closeHandlers = new SocketHandlerSet<NoireWebsocketClose>(Report);
        stateHandlers = new SocketHandlerSet<NoireSocketState>(Report);
        errorHandlers = new SocketHandlerSet<Exception>(LogHandlerFault);
        pump = new SocketPump(ResolveHost, () => active.Thread, Report);
    }

    /// <summary>
    /// Gets the URL the connection is opened to.
    /// </summary>
    public Uri Url { get; }

    /// <summary>
    /// Gets the settings the next connect runs under. Mutating them while a connection is open changes nothing until
    /// it is closed and opened again. The HTTP handler is built once at the first connect. The
    /// <see cref="NoireSocketHttpOptions"/> values that shape it are fixed for the client's lifetime.
    /// </summary>
    public NoireWebsocketClientOptions Options { get; }

    /// <summary>
    /// Gets where the connection is in its lifecycle.
    /// </summary>
    public NoireSocketState State => (NoireSocketState)Volatile.Read(ref stateValue);

    /// <summary>
    /// Gets whether the connection is open and carrying messages.
    /// </summary>
    public bool IsConnected => State == NoireSocketState.Connected;

    /// <summary>
    /// Gets the sub-protocol the server accepted, or null when none was negotiated.
    /// </summary>
    public string? SubProtocol { get; private set; }

    /// <summary>
    /// Gets the last failure reported, whether or not an error handler was attached.
    /// </summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Gets how the last connection ended, or null while the first one has never closed.
    /// </summary>
    public NoireWebsocketClose? LastClose { get; private set; }

    /// <summary>
    /// Gets how many messages are waiting to be written. A caller can throttle itself with this before the overflow
    /// policy fires.
    /// </summary>
    public int QueuedMessages => queue?.Count ?? 0;

    /// <summary>
    /// Opens the connection and keeps it open, reconnecting on the retry policy's schedule.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the client entirely, beyond just the wait.</param>
    /// <returns>A task completing when the connection is first open.</returns>
    /// <exception cref="NoireSocketConnectException">If the handshake fails and the retry policy allows no attempt.</exception>
    /// <exception cref="ObjectDisposedException">If the client has been disposed.</exception>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        lock (lifecycleGate)
        {
            if (runLoop is { IsCompleted: false })
                return openSignal.Task;

            active = Options.Clone();
            host = active.Host ?? NoireWebsocketHost.Current;
            queue = new SendQueue(active.SendQueueCapacity, active.Overflow);
            openSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connectionSignal = new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref closedLocally, 0);

            handler ??= SocketHttpFactory.CreateHandler(active.Http, active.ConfigureHandler);
            invoker ??= new HttpMessageInvoker(handler, disposeHandler: false);

            // The previous source is not disposed. Unwinding loops would see ObjectDisposedException in place of the cancellation.
            life = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Set before the loop starts. A send on the next line must not see a disconnected client.
            SetState(NoireSocketState.Connecting);

            var token = life.Token;
            runLoop = Task.Run(() => RunAsync(token), CancellationToken.None);
            writeLoop = Task.Run(() => WriteLoopAsync(token), CancellationToken.None);

            return openSignal.Task;
        }
    }

    /// <summary>
    /// Closes the connection with the close handshake and stops reconnecting.
    /// </summary>
    /// <param name="code">The code sent to the peer.</param>
    /// <param name="reason">The reason text sent with it, or null for none.</param>
    /// <param name="cancellationToken">A token abandoning the wait for the peer's answer.</param>
    /// <returns>A task completing once the connection is down.</returns>
    public async Task CloseAsync(NoireWebsocketCloseCode code = NoireWebsocketCloseCode.Normal, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        localCloseCode = code;
        localCloseReason = reason;
        Volatile.Write(ref closedLocally, 1);

        var live = Volatile.Read(ref connection);
        var loop = runLoop;

        if (live != null)
        {
            SetState(NoireSocketState.Closing);

            try
            {
                await SendCloseFrameAsync(live.Socket, code, reason, cancellationToken).ConfigureAwait(false);

                // A peer that never answers is dropped after the connection timeout.
                if (loop != null)
                    await loop.WaitAsync(active.Http.ConnectionTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                live.Socket.Abort();
            }
        }

        try
        {
            life?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        await AwaitQuietlyAsync(loop).ConfigureAwait(false);
        await AwaitQuietlyAsync(writeLoop).ConfigureAwait(false);

        SetState(NoireSocketState.Disconnected);
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
        stateHandlers.RemoveOwner(owner);
    }

    /// <summary>
    /// Aborts the connection, drops every subscription and disposes the HTTP handler this client owns.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        Volatile.Write(ref closedLocally, 1);

        try
        {
            life?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Volatile.Read(ref connection)?.Socket.Abort();
        queue?.Close(new NoireSocketClosedException("The client was disposed before the message was written."));

        if (openSignal.TrySetCanceled())
            _ = openSignal.Task.Exception;

        Volatile.Write(ref stateValue, (int)NoireSocketState.Disconnected);

        pump.Dispose();
        openHandlers.Clear();
        messageHandlers.Clear();
        closeHandlers.Clear();
        errorHandlers.Clear();
        stateHandlers.Clear();

        invoker?.Dispose();
        handler?.Dispose();
        life?.Dispose();
        sendGate.Dispose();
    }

    /// <summary>
    /// Registers a handler run once the connection is open, before the first message of that connection reaches
    /// anything.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnOpen(Action handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return openHandlers.Add(_ => { handler(); return Task.CompletedTask; }, options);
    }

    /// <summary>
    /// Registers a handler run once the connection is open, awaited before the first message is delivered.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnOpen(Func<Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return openHandlers.Add(_ => handler(), options);
    }

    /// <summary>
    /// Registers a handler run for every reassembled message.
    /// </summary>
    /// <param name="handler">What to run. The message's bytes stop being valid when it returns.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnMessage(Action<NoireWebsocketMessage> handler,
        NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return messageHandlers.Add(message => { handler(message); return Task.CompletedTask; }, options);
    }

    /// <summary>
    /// Registers a handler run for every reassembled message and awaited before the next one is read.
    /// </summary>
    /// <param name="handler">What to run. The message's bytes stop being valid when the returned task completes.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnMessage(Func<NoireWebsocketMessage, Task> handler,
        NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return messageHandlers.Add(handler, options);
    }

    /// <summary>
    /// Registers a handler run each time a connection ends, including one that is about to be retried.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnClose(Action<NoireWebsocketClose> handler,
        NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return closeHandlers.Add(close => { handler(close); return Task.CompletedTask; }, options);
    }

    /// <summary>
    /// Registers a handler run each time a connection ends, awaited before the retry delay starts.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnClose(Func<NoireWebsocketClose, Task> handler,
        NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return closeHandlers.Add(handler, options);
    }

    /// <summary>
    /// Registers a handler run for every failure, including one the client recovers from by reconnecting.
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
    /// Registers a handler run for every failure and awaited before the next one is reported.
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
    /// Registers a handler run every time the lifecycle state changes.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnStateChanged(Action<NoireSocketState> handler,
        NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return stateHandlers.Add(state => { handler(state); return Task.CompletedTask; }, options);
    }

    /// <summary>
    /// Registers a handler run every time the lifecycle state changes, awaited before the next change is delivered.
    /// </summary>
    /// <param name="handler">What to run.</param>
    /// <param name="options">What the subscription does beyond running the handler.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnStateChanged(Func<NoireSocketState, Task> handler,
        NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return stateHandlers.Add(handler, options);
    }

    private async Task RunAsync(CancellationToken token)
    {
        var backoff = new BackoffClock(active.Retry);
        Exception? terminal = null;

        while (!token.IsCancellationRequested && Volatile.Read(ref closedLocally) == 0)
        {
            var socket = CreateSocket();

            try
            {
                await socket.ConnectAsync(Url, invoker!, token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var failure = BuildConnectException(socket, exception);
                socket.Dispose();

                if (token.IsCancellationRequested || Volatile.Read(ref closedLocally) != 0)
                    break;

                Report(failure);

                if (!backoff.ShouldRetry)
                {
                    terminal = failure;
                    break;
                }

                SetState(NoireSocketState.Reconnecting);

                if (!await DelayAsync(backoff.Next(), token).ConfigureAwait(false))
                    break;

                continue;
            }

            backoff.Reset();
            SubProtocol = socket.SubProtocol;

            var live = new Connection(socket);
            Volatile.Write(ref connection, live);
            SetState(NoireSocketState.Connected);
            pump.Enqueue(() => openHandlers.DispatchAsync(this));
            openSignal.TrySetResult();
            connectionSignal.TrySetResult(live);

            NoireWebsocketClose close;

            try
            {
                close = await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Cancellation means the client is stopping.
                if (!token.IsCancellationRequested)
                    Report(exception);

                close = new NoireWebsocketClose((int)NoireWebsocketCloseCode.AbnormalClosure, exception.Message,
                    NoireWebsocketCloseInitiator.Transport);
            }
            finally
            {
                // The writer waits on the replacement signal.
                Volatile.Write(ref connectionSignal, new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously));
                Volatile.Write(ref connection, null);
                live.Ended.TrySetResult();
                socket.Dispose();
            }

            if (Volatile.Read(ref closedLocally) != 0)
                close = new NoireWebsocketClose((int)localCloseCode, localCloseReason,
                    NoireWebsocketCloseInitiator.Local);

            LastClose = close;
            pump.Enqueue(() => closeHandlers.DispatchAsync(close));

            if (token.IsCancellationRequested || Volatile.Read(ref closedLocally) != 0)
                break;

            if (close.WasClean && !active.Retry.ReconnectOnCleanClose)
                break;

            if (!backoff.ShouldRetry)
            {
                terminal = new NoireSocketException("The connection dropped and the retry policy allows no further attempt.");
                break;
            }

            SetState(NoireSocketState.Reconnecting);

            if (!await DelayAsync(backoff.Next(), token).ConfigureAwait(false))
                break;
        }

        if (terminal != null)
            Fail(terminal);
        else
            SetState(NoireSocketState.Disconnected);

        queue?.Close(terminal ?? new NoireSocketClosedException("The connection stopped before the message was written."));

        if (openSignal.TrySetCanceled())
            _ = openSignal.Task.Exception;

        // The writer outlives one connection. Its own loop ends it.
        try
        {
            life?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task AwaitQuietlyAsync(Task? loop)
    {
        if (loop == null)
            return;

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            // Dispose raced the delay.
            return false;
        }
    }

    private ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        var buffer = Math.Max(NoireWebsocketClientOptions.MinimumReceiveBufferSize, active.ReceiveBufferSize);

        socket.Options.KeepAliveInterval = active.KeepAliveInterval;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetBuffer(buffer, buffer);

        foreach (var protocol in active.SubProtocols)
            socket.Options.AddSubProtocol(protocol);

        SocketHttpFactory.ApplyHeaders(socket.Options, active.Http, Url.PathAndQuery);

        active.ConfigureSocket?.Invoke(socket.Options);
        return socket;
    }

    private NoireSocketConnectException BuildConnectException(ClientWebSocket socket, Exception inner)
    {
        if (inner is OperationCanceledException)
            return new NoireSocketConnectException("The handshake to " + Url + " was abandoned.", null, null, inner);

        HttpStatusCode? status = socket.HttpStatusCode == 0 ? null : socket.HttpStatusCode;
        Dictionary<string, string>? headers = null;

        if (socket.HttpResponseHeaders != null)
        {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in socket.HttpResponseHeaders)
                headers[pair.Key] = string.Join(", ", pair.Value);
        }

        var message = status == null
            ? "The handshake to " + Url + " failed."
            : "The handshake to " + Url + " was answered with " + (int)status.Value + ".";

        return new NoireSocketConnectException(message, status, headers, inner);
    }

    private void Fail(Exception error)
    {
        LastError = error;
        SetState(NoireSocketState.Faulted);
        queue?.Close(error);

        // A caller that never awaited the connect must not leave the exception unobserved.
        if (openSignal.TrySetException(error))
            _ = openSignal.Task.Exception;
    }

    private void SetState(NoireSocketState next)
    {
        if (Interlocked.Exchange(ref stateValue, (int)next) == (int)next)
            return;

        pump.Enqueue(() => stateHandlers.DispatchAsync(next));
    }

    private void Report(Exception exception)
    {
        LastError = exception;

        if (errorHandlers.HasHandlers)
        {
            pump.Enqueue(() => errorHandlers.DispatchAsync(exception));
            return;
        }

        ResolveHost().Log(NoireRemoteLogLevel.Error, "The socket connection to " + Url + " reported a failure with no error handler attached.", exception);
    }

    private void LogHandlerFault(Exception exception)
        => ResolveHost().Log(NoireRemoteLogLevel.Error, "An error handler of the socket connection to " + Url + " threw.", exception);

    private INoireRemoteHost ResolveHost()
        => host ?? NoireWebsocketHost.Current;

    private static Uri ParseUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            throw new ArgumentException("A socket URL has to be absolute.", nameof(url));

        return parsed;
    }

    private static Uri NormalizeUrl(Uri url)
    {
        if (!url.IsAbsoluteUri)
            throw new ArgumentException("A socket URL has to be absolute.", nameof(url));

        return url.Scheme switch
        {
            "ws" or "wss" => url,
            "http" => new UriBuilder(url) { Scheme = "ws" }.Uri,
            "https" => new UriBuilder(url) { Scheme = "wss" }.Uri,
            _ => throw new ArgumentException("A socket URL carries ws, wss, http or https; '" + url.Scheme + "' is none of them.", nameof(url)),
        };
    }

    private sealed class Connection
    {
        public Connection(ClientWebSocket socket)
            => Socket = socket;

        public ClientWebSocket Socket { get; }

        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
