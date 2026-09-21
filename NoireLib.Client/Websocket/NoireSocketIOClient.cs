using NoireLib.Remote;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PackageDisconnectReason = SocketIOClient.DisconnectReason;
using PackageSocket = SocketIOClient.SocketIO;

namespace NoireLib.Websocket;

/// <summary>
/// A Socket.IO connection: named events in both directions, acknowledgements, binary attachments and the protocol
/// package's own reconnection.<br/>
/// It reaches <see cref="NoireSocketState.Disconnected"/>, <see cref="NoireSocketState.Connecting"/>,
/// <see cref="NoireSocketState.Connected"/>, <see cref="NoireSocketState.Reconnecting"/>,
/// <see cref="NoireSocketState.Closing"/> and <see cref="NoireSocketState.Faulted"/>.
/// </summary>
public sealed class NoireSocketIOClient : IDisposable
{
    private readonly SocketIOBridge bridge;
    private readonly Dictionary<string, SocketHandlerSet<NoireSocketIOEvent>> eventSets = new(StringComparer.Ordinal);
    private readonly object eventGate = new();
    private readonly SocketHandlerSet<object?> opens;
    private readonly SocketHandlerSet<string> closes;
    private readonly SocketHandlerSet<Exception> errors;
    private readonly SocketHandlerSet<NoireSocketState> states;
    private readonly SocketPump pump;

    private INoireRemoteHost? host;
    private volatile NoireSocketState state = NoireSocketState.Disconnected;
    private bool warningLogged;
    private bool disposed;

    /// <summary>Creates a client. Nothing is requested until <see cref="ConnectAsync"/>.</summary>
    /// <param name="url">The server's address. The namespace is read from it when the options name none.</param>
    /// <param name="options">The settings, or null for the defaults.</param>
    /// <exception cref="ArgumentException">The address is null, blank, relative or not an HTTP or socket scheme.</exception>
    public NoireSocketIOClient(string url, NoireSocketIOOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Options = options ?? new NoireSocketIOOptions();
        Url = BuildServerUri(url, Options.Namespace, out var warning);
        UrlWarning = warning;

        opens = new SocketHandlerSet<object?>(ReportHandlerFault);
        closes = new SocketHandlerSet<string>(ReportHandlerFault);
        errors = new SocketHandlerSet<Exception>(ReportHandlerFault);
        states = new SocketHandlerSet<NoireSocketState>(ReportHandlerFault);
        pump = new SocketPump(() => ResolvedHost, () => Options.Thread, ReportHandlerFault);

        bridge = new SocketIOBridge(Url, Options);
        AttachPackageEvents();
    }

    /// <summary>
    /// Gets the address the connection was built for, with the namespace as its path.
    /// </summary>
    public Uri Url { get; }

    /// <summary>
    /// Gets the settings this client was built with. They were read when the client was constructed. Change
    /// <see cref="Underlying"/>'s own options to reconfigure it afterwards.
    /// </summary>
    public NoireSocketIOOptions Options { get; }

    /// <summary>
    /// Gets the protocol package's own client. Nothing it offers is fenced off by this one.
    /// </summary>
    public PackageSocket Underlying => bridge.Client;

    /// <summary>
    /// Gets where the connection is in its lifecycle.
    /// </summary>
    public NoireSocketState State => state;

    /// <summary>
    /// Gets whether the connection is open and carrying events.
    /// </summary>
    public bool IsConnected => bridge.Client.Connected;

    /// <summary>
    /// Gets the session id the server assigned, or null while nothing is connected.
    /// </summary>
    public string? SocketId => bridge.Client.Id;

    /// <summary>
    /// Gets the last failure, or null when nothing has failed yet.
    /// </summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Gets what is wrong with the address the client was given, or null when nothing is. It is readable here as
    /// well as logged, because a standalone host discards log lines when it has nowhere to write them.
    /// </summary>
    public string? UrlWarning { get; }

    /// <summary>
    /// Opens the connection.<br/>
    /// The protocol package retries the first attempt on its own schedule. With the default attempt count this
    /// does not return until the server answers. Pass a token to put a bound on it.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the connection.</param>
    /// <returns>A task completing when the connection is open.</returns>
    /// <exception cref="NoireSocketConnectException">If the protocol package gave up.</exception>
    /// <exception cref="ObjectDisposedException">If the client has been disposed.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        host = Options.Host ?? NoireWebsocketHost.Current;

        if (UrlWarning != null && !warningLogged)
        {
            warningLogged = true;
            ResolvedHost.Log(NoireRemoteLogLevel.Warning, UrlWarning, null);
        }

        SetState(NoireSocketState.Connecting);

        try
        {
            await bridge.Client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetState(NoireSocketState.Disconnected);
            throw;
        }
        catch (Exception exception)
        {
            Report(exception);
            SetState(NoireSocketState.Faulted);

            throw new NoireSocketConnectException("The Socket.IO server at " + Url + " could not be reached.",
                null, null, exception);
        }
    }

    /// <summary>
    /// Closes the connection. The protocol package treats a close asked for here as final and does not reconnect
    /// after it.
    /// </summary>
    /// <returns>A task completing once the close has been sent.</returns>
    public async Task CloseAsync()
    {
        // The package ignores a close while nothing is open. No disconnect callback would ever run.
        if (!bridge.Client.Connected)
        {
            SetState(NoireSocketState.Disconnected);
            return;
        }

        SetState(NoireSocketState.Closing);
        await bridge.Client.DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Emits an event, without waiting for an acknowledgement.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="data">The arguments. A byte array among them crosses as a binary attachment.</param>
    /// <returns>A task completing once the event has been written.</returns>
    /// <exception cref="NoireSocketClosedException">If the connection is not open.</exception>
    public Task EmitAsync(string name, params object?[] data)
    {
        RequireOpen(name);
        return bridge.Client.EmitAsync(name, data);
    }

    /// <summary>
    /// Emits an event and waits for the acknowledgement the server answers it with.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="data">The arguments. A byte array among them crosses as a binary attachment.</param>
    /// <param name="timeout">How long to wait, or null to wait until the token is cancelled.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The acknowledgement, carrying whatever the server sent back.</returns>
    /// <exception cref="NoireSocketClosedException">If the connection is not open.</exception>
    /// <exception cref="NoireSocketTimeoutException">If the acknowledgement did not arrive in time.</exception>
    public async Task<NoireSocketIOEvent> EmitAndWaitAsync(string name, object?[] data, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        RequireOpen(name);

        var answer = new TaskCompletionSource<NoireSocketIOEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        await bridge.Client
            .EmitAsync(name, response => answer.TrySetResult(new NoireSocketIOEvent(name, response)), data ?? [])
            .ConfigureAwait(false);

        try
        {
            return timeout == null
                ? await answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await answer.Task.WaitAsync(timeout.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new NoireSocketTimeoutException("The event " + name + " was not acknowledged in time.", timeout!.Value);
        }
    }

    /// <summary>
    /// Subscribes to one event name. Every subscriber to a name is invoked, and disposing one handle leaves the
    /// others in place.
    /// </summary>
    /// <param name="name">The event name, matched exactly.</param>
    /// <param name="handler">What runs for each event.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription On(string name, Action<NoireSocketIOEvent> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return On(name, received => { handler(received); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="On(string, Action{NoireSocketIOEvent}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription On(string name, Func<NoireSocketIOEvent, Task> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(handler);

        return ResolveEventSet(name).Add(handler, options);
    }

    /// <summary>Subscribes to the connection opening, again after every reconnect.</summary>
    /// <param name="handler">What runs each time the connection opens.</param>
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
    /// Subscribes to the connection dropping, taking the protocol package's own reason. A reconnect follows it
    /// unless the close was asked for by either side.
    /// </summary>
    /// <param name="handler">What runs when the connection drops.</param>
    /// <param name="options">How the subscription behaves, or null for the defaults.</param>
    /// <returns>The handle that removes this one handler.</returns>
    public NoireSocketSubscription OnClose(Action<string> handler, NoireSocketSubscribeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return closes.Add(reason => { handler(reason); return Task.CompletedTask; }, options);
    }

    /// <inheritdoc cref="OnClose(Action{string}, NoireSocketSubscribeOptions?)"/>
    public NoireSocketSubscription OnClose(Func<string, Task> handler, NoireSocketSubscribeOptions? options = null)
        => closes.Add(handler, options);

    /// <summary>
    /// Subscribes to every failure, including the errors the server sends and the ones a reconnect attempt raises.
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

        SocketHandlerSet<NoireSocketIOEvent>[] sets;

        lock (eventGate)
        {
            sets = new SocketHandlerSet<NoireSocketIOEvent>[eventSets.Count];
            eventSets.Values.CopyTo(sets, 0);
        }

        foreach (var set in sets)
            set.RemoveOwner(owner);

        opens.RemoveOwner(owner);
        closes.RemoveOwner(owner);
        errors.RemoveOwner(owner);
        states.RemoveOwner(owner);
    }

    /// <summary>
    /// Closes the connection and releases the transport it was opened through.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        bridge.Dispose();
        pump.Dispose();

        lock (eventGate)
            eventSets.Clear();

        opens.Clear();
        closes.Clear();
        errors.Clear();
        states.Clear();
    }

    private INoireRemoteHost ResolvedHost => host ?? NoireWebsocketHost.Current;

    // For the package the namespace is the address's path.
    private static Uri BuildServerUri(string url, string? space, out string? warning)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            throw new ArgumentException("A Socket.IO address has to be absolute.", nameof(url));

        var scheme = parsed.Scheme;

        if (scheme is not ("http" or "https" or "ws" or "wss"))
            throw new ArgumentException("A Socket.IO address has to be http, https, ws or wss.", nameof(url));

        warning = scheme is "ws" or "wss"
            ? "The Socket.IO address " + url + " uses the " + scheme + " scheme. The options choose the transport "
              + "regardless of the scheme in the address. The connection still starts over HTTP. Use http or "
              + "https to say what is meant."
            : null;

        if (string.IsNullOrEmpty(space))
            return parsed;

        var builder = new UriBuilder(parsed) { Path = space.StartsWith('/') ? space : "/" + space };
        return builder.Uri;
    }

    private void AttachPackageEvents()
    {
        var client = bridge.Client;

        client.OnConnected += (_, _) => SetState(NoireSocketState.Connected);
        client.OnDisconnected += (_, reason) => HandleDisconnected(reason);
        client.OnReconnectAttempt += (_, _) => SetState(NoireSocketState.Reconnecting);
        client.OnReconnectError += (_, exception) => Report(exception);
        client.OnReconnectFailed += (_, _) => SetState(NoireSocketState.Faulted);
        client.OnError += (_, message) => Report(new NoireSocketException(message));
    }

    private void HandleDisconnected(string reason)
    {
        Publish(closes, reason);

        var final = !Options.Reconnect.Enabled
            || reason == PackageDisconnectReason.IOClientDisconnect
            || reason == PackageDisconnectReason.IOServerDisconnect;

        SetState(final ? NoireSocketState.Disconnected : NoireSocketState.Reconnecting);
    }

    // One package handler per event name, fanned out here. Per-subscriber registration would let one disposal silence the others.
    private SocketHandlerSet<NoireSocketIOEvent> ResolveEventSet(string name)
    {
        lock (eventGate)
        {
            if (eventSets.TryGetValue(name, out var existing))
                return existing;

            var created = new SocketHandlerSet<NoireSocketIOEvent>(ReportHandlerFault);
            eventSets[name] = created;

            bridge.Client.On(name, response => Publish(created, new NoireSocketIOEvent(name, response)));
            return created;
        }
    }

    private void RequireOpen(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!bridge.Client.Connected)
            throw new NoireSocketClosedException("The event " + name + " cannot be emitted while the connection is not open.");
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
        => ResolvedHost.Log(NoireRemoteLogLevel.Error, "A " + nameof(NoireSocketIOClient) + " handler faulted.", exception);
}
