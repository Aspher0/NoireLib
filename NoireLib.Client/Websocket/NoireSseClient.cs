using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Buffers;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

/// <summary>
/// A server-sent-events connection: one long-lived HTTP response that the server writes events into, reopened with
/// backoff whenever it drops.<br/>
/// It reaches <see cref="NoireSocketState.Disconnected"/>, <see cref="NoireSocketState.Connecting"/>,
/// <see cref="NoireSocketState.Connected"/>, <see cref="NoireSocketState.Reconnecting"/> and
/// <see cref="NoireSocketState.Faulted"/>.
/// </summary>
public sealed class NoireSseClient : IDisposable
{
    private const string EventStreamContentType = "text/event-stream";

    private readonly SocketHandlerSet<NoireSseEvent> events;
    private readonly SocketHandlerSet<string> comments;
    private readonly SocketHandlerSet<object?> opens;
    private readonly SocketHandlerSet<object?> closes;
    private readonly SocketHandlerSet<Exception> errors;
    private readonly SocketHandlerSet<NoireSocketState> states;
    private readonly SocketPump pump;

    private INoireRemoteHost? host;
    private SocketsHttpHandler? handler;
    private HttpClient? transport;
    private CancellationTokenSource? lifetime;
    private TaskCompletionSource? firstAttempt;
    private Task? loop;
    private BackoffClock? backoff;
    private NoireRetryPolicy? backoffPolicy;
    private TimeSpan? serverRetry;
    private volatile NoireSocketState state = NoireSocketState.Disconnected;
    private bool streamDelivered;
    private int running;
    private bool disposed;

    /// <summary>
    /// Creates a client. Nothing is requested until <see cref="ConnectAsync"/> is called. Every handler can be
    /// attached before the first byte moves.
    /// </summary>
    /// <param name="url">The stream's address.</param>
    /// <param name="options">The settings, or null for the defaults.</param>
    /// <exception cref="ArgumentException">If the address is null or blank.</exception>
    public NoireSseClient(string url, NoireSseOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Url = url;
        Options = options ?? new NoireSseOptions();

        events = new SocketHandlerSet<NoireSseEvent>(ReportHandlerFault);
        comments = new SocketHandlerSet<string>(ReportHandlerFault);
        opens = new SocketHandlerSet<object?>(ReportHandlerFault);
        closes = new SocketHandlerSet<object?>(ReportHandlerFault);
        errors = new SocketHandlerSet<Exception>(ReportHandlerFault);
        states = new SocketHandlerSet<NoireSocketState>(ReportHandlerFault);
        pump = new SocketPump(() => ResolvedHost, () => Options.Thread, ReportHandlerFault);
    }

    /// <summary>
    /// Gets the stream's address.
    /// </summary>
    public string Url { get; }

    /// <summary>Gets the client's settings. Changes apply on the next attempt. <see cref="NoireSseOptions.Http"/> and <see cref="NoireSseOptions.ConfigureHandler"/> build the handler once.</summary>
    public NoireSseOptions Options { get; }

    /// <summary>
    /// Gets where the connection is in its lifecycle.
    /// </summary>
    public NoireSocketState State => state;

    /// <summary>
    /// Gets or sets the last id the stream sent. This is what <c>Last-Event-ID</c> carries on every reconnect. Set
    /// it before connecting to resume the stream a previous run had reached.
    /// </summary>
    public string? LastEventId { get; set; }

    /// <summary>
    /// Gets the last failure, or null when nothing has failed yet.
    /// </summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Opens the stream and keeps it open, reconnecting with backoff for as long as the retry policy allows.<br/>
    /// The returned task completes when the stream first opens. A first attempt that fails throws right away, with
    /// no retry of its own, surfacing a wrong address immediately. The failure also reaches
    /// <see cref="OnError(Action{Exception}, NoireSocketSubscribeOptions?)"/>, reaching a caller that started the
    /// client through the facade too.
    /// </summary>
    /// <param name="cancellationToken">Stops the connection and every later reconnect.</param>
    /// <returns>A task completing when the stream is open.</returns>
    /// <exception cref="NoireSocketConnectException">If the server refused the first request.</exception>
    /// <exception cref="ObjectDisposedException">If the client has been disposed.</exception>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            return firstAttempt?.Task ?? Task.CompletedTask;

        host = Options.Host ?? NoireWebsocketHost.Current;
        streamDelivered = false;
        serverRetry = null;
        backoff = null;
        backoffPolicy = null;

        EnsureTransport();

        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        SetState(NoireSocketState.Connecting);

        var token = lifetime.Token;
        loop = Task.Run(() => RunAsync(token), CancellationToken.None);

        return firstAttempt.Task;
    }

    /// <summary>
    /// Closes the stream and stops reconnecting.
    /// </summary>
    /// <returns>A task completing once the read loop has stopped.</returns>
    public async Task CloseAsync()
    {
        var source = lifetime;
        var task = loop;

        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (task != null)
            await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to every event the stream dispatches.
    /// </summary>
    /// <param name="handler">What runs for each event.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnEvent(Action<NoireSseEvent> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return events.Add(dispatched => { handler(dispatched); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnEvent(Action{NoireSseEvent}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnEvent(Func<NoireSseEvent, Task> handler, NoireSocketSubscribeOptions? options = null)
        => events.Add(handler, options);

    /// <summary>Subscribes to the events of one type.</summary>
    /// <param name="type">The event type to match, compared exactly.</param>
    /// <param name="handler">What runs for each matching event.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnEvent(string type, Action<NoireSseEvent> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnEvent(type, dispatched => { handler(dispatched); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnEvent(string, Action{NoireSseEvent}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnEvent(string type, Func<NoireSseEvent, Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(handler);

        return events.Add(
            dispatched => string.Equals(dispatched.Type, type, StringComparison.Ordinal) ? handler(dispatched) : Task.CompletedTask,
            options);
    }

    /// <summary>Subscribes to the stream's comment lines, the keep-alives a server sends through proxies.</summary>
    /// <param name="handler">What runs for each comment, taking the text after the colon.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnComment(Action<string> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return comments.Add(comment => { handler(comment); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnComment(Action{string}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnComment(Func<string, Task> handler, NoireSocketSubscribeOptions? options = null)
        => comments.Add(handler, options);

    /// <summary>Subscribes to the stream opening, again after every reconnect.</summary>
    /// <param name="handler">What runs each time the stream opens.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnOpen(Action handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return opens.Add(_ => { handler(); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnOpen(Action, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnOpen(Func<Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return opens.Add(_ => handler(), options);
    }

    /// <summary>
    /// Subscribes to the client stopping for good, whether it was closed, cancelled or left
    /// <see cref="NoireSocketState.Faulted"/>. A reconnect is not a close.
    /// </summary>
    /// <param name="handler">What runs when the client stops.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnClose(Action handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return closes.Add(_ => { handler(); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnClose(Action, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnClose(Func<Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return closes.Add(_ => handler(), options);
    }

    /// <summary>
    /// Subscribes to every failure, including the one a failed first attempt throws from
    /// <see cref="ConnectAsync"/>.
    /// </summary>
    /// <param name="handler">What runs for each failure.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnError(Action<Exception> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return errors.Add(exception => { handler(exception); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnError(Action{Exception}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnError(Func<Exception, Task> handler, NoireSocketSubscribeOptions? options = null)
        => errors.Add(handler, options);

    /// <summary>
    /// Subscribes to every change of <see cref="State"/>.
    /// </summary>
    /// <param name="handler">What runs for each change, taking the new state.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnStateChanged(Action<NoireSocketState> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return states.Add(changed => { handler(changed); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnStateChanged(Action{NoireSocketState}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnStateChanged(Func<NoireSocketState, Task> handler, NoireSocketSubscribeOptions? options = null)
        => states.Add(handler, options);

    /// <summary>
    /// Removes every subscription registered with one owner. A consumer can drop what it registered without
    /// keeping the handles.
    /// </summary>
    /// <param name="owner">The owner named on <see cref="NoireSocketSubscribeOptions.Owner"/>.</param>
    public void UnsubscribeAll(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        events.RemoveOwner(owner);
        comments.RemoveOwner(owner);
        opens.RemoveOwner(owner);
        closes.RemoveOwner(owner);
        errors.RemoveOwner(owner);
        states.RemoveOwner(owner);
    }

    /// <summary>
    /// Stops the stream and releases the handler it was requested through.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        try
        {
            lifetime?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        transport?.Dispose();
        handler?.Dispose();
        transport = null;
        handler = null;
    }

    private INoireRemoteHost ResolvedHost => host ?? NoireWebsocketHost.Current;

    private void EnsureTransport()
    {
        if (transport != null)
            return;

        handler = SocketHttpFactory.CreateHandler(Options.Http, Options.ConfigureHandler);

        // The handler carries the opening deadline.
        transport = new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var opened = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            streamDelivered = false;

            try
            {
                await ReadStreamAsync(() => opened = true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Report(exception);

                if (!opened)
                {
                    // A wrong address throws to the ConnectAsync caller and never retries.
                    firstAttempt?.TrySetException(exception);
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            // An empty stream answered over and over is backed off like any stall.
            if (streamDelivered)
                backoff?.Reset();

            EnsureBackoff();

            if (backoff?.ShouldRetry != true)
            {
                SetState(NoireSocketState.Faulted);
                Finish();
                return;
            }

            SetState(NoireSocketState.Reconnecting);

            try
            {
                await Task.Delay(backoff.Next(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetState(NoireSocketState.Disconnected);
        firstAttempt?.TrySetCanceled();
        Finish();
    }

    private async Task ReadStreamAsync(Action opened, CancellationToken cancellationToken)
    {
        var parser = new SseParser(Options.MaxEventSize) { LastEventId = LastEventId };

        using var request = BuildRequest();
        using var response = await transport!
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new NoireSocketConnectException(
                "The stream at " + Url + " was refused with status " + (int)response.StatusCode + ".",
                response.StatusCode,
                SocketHttpFactory.ReadHeaders(response));

        opened();
        SetState(NoireSocketState.Connected);
        firstAttempt?.TrySetResult();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

                if (read == 0)
                    return;

                var items = parser.Feed(buffer.AsSpan(0, read));

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];

                    if (item.Event != null)
                        Publish(events, item.Event);
                    else if (item.Comment != null)
                        Publish(comments, item.Comment);
                }

                if (items.Count > 0)
                    streamDelivered = true;

                LastEventId = parser.LastEventId;

                if (parser.ServerRetry != null)
                    serverRetry = parser.ServerRetry;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private HttpRequestMessage BuildRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url);
        var http = Options.Http;

        SocketHttpFactory.ApplyHeaders(request, http, Url);

        if (!http.Headers.ContainsKey("Accept"))
            request.Headers.TryAddWithoutValidation("Accept", EventStreamContentType);

        if (!http.Headers.ContainsKey("Cache-Control"))
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        if (LastEventId != null)
            request.Headers.TryAddWithoutValidation("Last-Event-ID", LastEventId);

        return request;
    }

    private void EnsureBackoff()
    {
        // The server's retry replaces the first delay only.
        var effective = serverRetry == null ? Options.Retry : Options.Retry with { InitialDelay = serverRetry.Value };

        if (backoff != null && effective.Equals(backoffPolicy))
            return;

        backoffPolicy = effective;
        backoff = new BackoffClock(effective);
    }

    private void SetState(NoireSocketState next)
    {
        if (state == next)
            return;

        state = next;
        Publish(states, next);

        if (next == NoireSocketState.Connected)
            Publish(opens, null);
    }

    private void Finish()
    {
        Interlocked.Exchange(ref running, 0);
        Publish(closes, null);
    }

    private void Report(Exception exception)
    {
        LastError = exception;
        Publish(errors, exception);
    }

    private void Publish<TContext>(SocketHandlerSet<TContext> set, TContext context)
    {
        if (set.HasHandlers)
            pump.Enqueue(() => set.DispatchAsync(context));
    }

    // Publishing a handler fault onto the error surface would loop if an error handler throws.
    private void ReportHandlerFault(Exception exception)
        => ResolvedHost.Log(NoireRemoteLogLevel.Error, "A " + nameof(NoireSseClient) + " handler faulted.", exception);
}
