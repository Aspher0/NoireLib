using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

/// <summary>
/// A long-polling client: it issues a request, waits for the server to answer it, dispatches the answer and issues
/// the next one, carrying an opaque cursor forward so the server knows where the last answer left off.<br/>
/// It reaches <see cref="NoireSocketState.Disconnected"/>, <see cref="NoireSocketState.Connecting"/>,
/// <see cref="NoireSocketState.Connected"/>, <see cref="NoireSocketState.Reconnecting"/> and
/// <see cref="NoireSocketState.Faulted"/>.
/// </summary>
public sealed class NoireLongPollClient : IDisposable
{
    private readonly SocketHandlerSet<NoireLongPollResponse> responses;
    private readonly SocketHandlerSet<object?> opens;
    private readonly SocketHandlerSet<object?> closes;
    private readonly SocketHandlerSet<Exception> errors;
    private readonly SocketHandlerSet<NoireSocketState> states;
    private readonly SocketPump pump;

    private INoireRemoteHost? host;
    private SocketsHttpHandler? handler;
    private HttpClient? transport;
    private CancellationTokenSource? lifetime;
    private LongPollLoop? driver;
    private Task? loop;
    private volatile NoireSocketState state = NoireSocketState.Disconnected;
    private int running;
    private bool disposed;

    /// <summary>
    /// Creates a client. Nothing is requested until <see cref="ConnectAsync"/> is called. Every handler can be
    /// attached before the first poll goes out.
    /// </summary>
    /// <param name="url">The address polled.</param>
    /// <param name="options">The settings, or null for the defaults.</param>
    /// <exception cref="ArgumentException">If the address is null or blank.</exception>
    public NoireLongPollClient(string url, NoireLongPollOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Url = url;
        Options = options ?? new NoireLongPollOptions();

        responses = new SocketHandlerSet<NoireLongPollResponse>(ReportHandlerFault);
        opens = new SocketHandlerSet<object?>(ReportHandlerFault);
        closes = new SocketHandlerSet<object?>(ReportHandlerFault);
        errors = new SocketHandlerSet<Exception>(ReportHandlerFault);
        states = new SocketHandlerSet<NoireSocketState>(ReportHandlerFault);
        pump = new SocketPump(() => ResolvedHost, () => Options.Thread, ReportHandlerFault);
    }

    /// <summary>
    /// Gets the address polled.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Gets the client's settings. Changes apply on the next poll.<br/>
    /// <see cref="NoireLongPollOptions.Retry"/>, <see cref="NoireLongPollOptions.EmptyPollDelay"/> and <see cref="NoireLongPollOptions.InitialCursor"/> are read when the run starts. <see cref="NoireLongPollOptions.Http"/> and <see cref="NoireLongPollOptions.ConfigureHandler"/> build the handler once.
    /// </summary>
    public NoireLongPollOptions Options { get; }

    /// <summary>
    /// Gets where the run is in its lifecycle.
    /// </summary>
    public NoireSocketState State => state;

    /// <summary>
    /// Gets the cursor the next poll will carry, or null when no answer has named one yet.
    /// </summary>
    public string? Cursor => driver?.Cursor ?? Options.InitialCursor;

    /// <summary>
    /// Gets the last failure, or null when nothing has failed yet.
    /// </summary>
    public Exception? LastError { get; private set; }

    /// <summary>Starts the polling loop and returns once it is running. A server holding a poll for its full timeout is normal.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A completed task once the loop is running.</returns>
    /// <exception cref="ArgumentException"><see cref="NoireLongPollOptions.Body"/> is set while <see cref="NoireLongPollOptions.Method"/> is not <see cref="NoireLongPollMethod.Post"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        GuardBody();

        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            return Task.CompletedTask;

        host = Options.Host ?? NoireWebsocketHost.Current;
        LastError = null;

        EnsureTransport();

        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        driver = new LongPollLoop(
            Options.Retry,
            Options.EmptyPollDelay,
            Options.InitialCursor,
            PollAsync,
            Options.ReadCursor ?? ReadCursorFromAnswer,
            answer => Publish(responses, answer),
            Report,
            SetState,
            (delay, token) => Task.Delay(delay, token));

        SetState(NoireSocketState.Connecting);

        var token = lifetime.Token;
        loop = Task.Run(() => RunAsync(token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the loop after the poll in flight ends.
    /// </summary>
    /// <returns>A task completing once the loop has stopped.</returns>
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
    /// Issues one poll with the current cursor and returns the answer. It is the request without the loop around it:
    /// nothing is dispatched to the handlers and the cursor is left where it was.
    /// </summary>
    /// <param name="cancellationToken">Abandons the poll.</param>
    /// <returns>The answer, whatever status it carries.</returns>
    /// <exception cref="NoireSocketTimeoutException">If the server did not answer within
    /// <see cref="NoireLongPollOptions.PollTimeout"/>.</exception>
    /// <exception cref="ObjectDisposedException">If the client has been disposed.</exception>
    public Task<NoireLongPollResponse> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        GuardBody();

        return PollAsync(Cursor, cancellationToken);
    }

    /// <summary>
    /// Subscribes to every answer the server gives, empty ones included, exactly as it arrived.
    /// </summary>
    /// <param name="handler">What runs for each answer.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnResponse(Action<NoireLongPollResponse> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return responses.Add(answer => { handler(answer); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnResponse(Action{NoireLongPollResponse}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnResponse(Func<NoireLongPollResponse, Task> handler, NoireSocketSubscribeOptions? options = null)
        => responses.Add(handler, options);

    /// <summary>Subscribes to the server answering a poll for the first time after the loop starts or recovers.</summary>
    /// <param name="handler">What runs each time the loop starts answering again.</param>
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
    /// Subscribes to the loop stopping for good, whether it was closed, cancelled or left
    /// <see cref="NoireSocketState.Faulted"/>. A retry is not a close.
    /// </summary>
    /// <param name="handler">What runs when the loop stops.</param>
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
    /// Subscribes to every failure, including the refusal that stops the loop.
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

        responses.RemoveOwner(owner);
        opens.RemoveOwner(owner);
        closes.RemoveOwner(owner);
        errors.RemoveOwner(owner);
        states.RemoveOwner(owner);
    }

    /// <summary>
    /// Stops the loop and releases the handler the polls were issued through.
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

    private void GuardBody()
    {
        if (Options.Body == null || Options.Method == NoireLongPollMethod.Post)
            return;

        throw new ArgumentException(
            nameof(NoireLongPollOptions.Body) + " is set. " + nameof(NoireLongPollOptions.Method) + " has to be "
            + nameof(NoireLongPollMethod.Post) + ". A GET carries no body and would send it nowhere.",
            nameof(NoireLongPollOptions.Body));
    }

    private void EnsureTransport()
    {
        if (transport != null)
            return;

        handler = SocketHttpFactory.CreateHandler(Options.Http, Options.ConfigureHandler);

        // The server holds a poll as long as it likes. The deadline is applied per call.
        transport = new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await driver!.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Report(exception);
        }
        catch (Exception)
        {
        }

        if (state != NoireSocketState.Faulted)
            SetState(NoireSocketState.Disconnected);

        Interlocked.Exchange(ref running, 0);
        Publish(closes, null);
    }

    private async Task<NoireLongPollResponse> PollAsync(string? cursor, CancellationToken cancellationToken)
    {
        EnsureTransport();

        var method = Options.Method == NoireLongPollMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        var address = BuildUrl(cursor);
        var http = Options.Http;

        using var request = new HttpRequestMessage(method, address);

        byte[]? payload = null;

        // A signed credential covers the body. The bytes must be the same.
        if (Options.Method == NoireLongPollMethod.Post)
        {
            payload = NoireRemoteJson.WriteBytes(Options.Body);
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(NoireRemoteHeaders.JsonContentType) { CharSet = "utf-8" };
        }

        SocketHttpFactory.ApplyHeaders(request, http, address, payload);

        if (cursor != null && !string.IsNullOrEmpty(Options.CursorHeader))
            request.Headers.TryAddWithoutValidation(Options.CursorHeader, cursor);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (Options.PollTimeout > TimeSpan.Zero)
            deadline.CancelAfter(Options.PollTimeout);

        try
        {
            using var response = await transport!
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return new NoireLongPollResponse((int)response.StatusCode, SocketHttpFactory.ReadHeaders(response), body, cursor);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NoireSocketTimeoutException(
                "The poll of " + Url + " was not answered in time. The timeout has to be longer than the server's own hold.",
                Options.PollTimeout);
        }
    }

    private string BuildUrl(string? cursor)
    {
        var parameter = Options.CursorParameter;

        if (cursor == null || string.IsNullOrEmpty(parameter))
            return Url;

        var separator = Url.Contains('?') ? '&' : '?';
        return Url + separator + Uri.EscapeDataString(parameter) + "=" + Uri.EscapeDataString(cursor);
    }

    private string? ReadCursorFromAnswer(NoireLongPollResponse response)
    {
        var header = Options.CursorHeader;

        if (!string.IsNullOrEmpty(header)
            && response.Headers.TryGetValue(header, out var carried)
            && !string.IsNullOrWhiteSpace(carried))
            return carried;

        var field = Options.CursorField;

        if (string.IsNullOrEmpty(field) || string.IsNullOrWhiteSpace(response.Body))
            return null;

        try
        {
            var token = JToken.Parse(response.Body).SelectToken(field);
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
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
        => ResolvedHost.Log(NoireRemoteLogLevel.Error, "A " + nameof(NoireLongPollClient) + " handler faulted.", exception);
}
