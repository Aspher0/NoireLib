using Newtonsoft.Json.Linq;
using NoireLib.Websocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// Calls one published surface, holding a transport default and resolving its target at call time.<br/>
/// The constructor sets the default transport. <see cref="NoireRemoteView.Over"/> overrides it for one call. The
/// client holds at most one socket connection.
/// </summary>
public sealed partial class NoireRemoteClient : IDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<NoireRemoteFrame>> pending = new();
    private readonly ConcurrentDictionary<string, NoireRemoteJob> following = new();
    private readonly List<Subscription> subscriptions = [];
    private readonly SemaphoreSlim socketGate = new(1, 1);
    private readonly object warnGate = new();

    private NoireWebsocketClient? socket;
    private NoireRemoteInstance? connectedTo;
    private NoireRemoteInstance? lastTarget;
    private NoireRemoteTarget lastAim = NoireRemoteTarget.Any;
    private volatile bool rebuilding;
    private string warnedAbout = string.Empty;
    private long nextId;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteClient"/> class, picking the transport per call.
    /// </summary>
    /// <param name="api">The published surface's name, as the plugin declared it.</param>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    public NoireRemoteClient(string api) : this(api, NoireRemoteTransport.Inherit)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteClient"/> class with a transport default.
    /// </summary>
    /// <param name="api">The published surface's name.</param>
    /// <param name="transport">
    /// The wire calls take unless one asks otherwise. <see cref="NoireRemoteView.Over"/> reaches the other wire
    /// from any client.
    /// </param>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    public NoireRemoteClient(string api, NoireRemoteTransport transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(api);

        Api = api;
        Transport = transport;
    }

    /// <summary>
    /// Gets the surface this client calls.
    /// </summary>
    public string Api { get; }

    /// <summary>
    /// Gets the wire calls take unless one asks otherwise.
    /// </summary>
    public NoireRemoteTransport Transport { get; }

    /// <summary>
    /// Gets or sets the target used when a call names none. Null aims at whichever instance answers.
    /// </summary>
    public static NoireRemoteTarget? DefaultTarget { get; set; }

    /// <summary>
    /// Gets or sets how long a call waits for an absent target when it names no window of its own. Zero fails at
    /// once and is the default.
    /// </summary>
    public static TimeSpan DefaultWait { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets how long a socket opened for one call stays open with nothing to do. A client constructed with
    /// <see cref="NoireRemoteTransport.Websocket"/> keeps its connection whatever this says.
    /// </summary>
    public static TimeSpan SocketIdle { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Raised when an instance publishing anything appears. Watching starts with the first handler and stops
    /// with the last. A program that never listens pays nothing for it.
    /// </summary>
    public static event Action<NoireRemoteInstance>? InstanceConnected
    {
        add
        {
            RemoteInstanceWatch.Connected += value;
            RemoteInstanceWatch.Start();
        }

        remove => RemoteInstanceWatch.Connected -= value;
    }

    /// <summary>
    /// Raised when an instance goes away, by closing or by dying.
    /// </summary>
    public static event Action<NoireRemoteInstance>? InstanceDisconnected
    {
        add
        {
            RemoteInstanceWatch.Disconnected += value;
            RemoteInstanceWatch.Start();
        }

        remove => RemoteInstanceWatch.Disconnected -= value;
    }

    internal bool HasOpenSocket => socket != null && socket.State == NoireSocketState.Connected;

    // Read through NoireRemote.Sender to tell several watched game clients apart.
    internal NoireRemoteInstance? LastSender => connectedTo;

    /// <summary>
    /// Aims at a character name, a character and world as <c>Name @ World</c>, or a plugin name.
    /// </summary>
    /// <param name="identity">The text to match.</param>
    /// <returns>A view of this client aimed at that instance.</returns>
    public NoireRemoteView On(string identity) => new(this, NoireRemoteTarget.Identity(identity), NoireRemoteTransport.Inherit, null);

    /// <summary>
    /// Aims at a tag the instance set on itself.
    /// </summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The value it has to hold.</param>
    /// <returns>A view of this client aimed at the instances carrying that tag.</returns>
    public NoireRemoteView On(string key, string value) => new(this, NoireRemoteTarget.Meta(key, value), NoireRemoteTransport.Inherit, null);

    /// <summary>
    /// Aims at a process id.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <returns>A view of this client aimed at that process.</returns>
    public NoireRemoteView On(int processId) => new(this, NoireRemoteTarget.Process(processId), NoireRemoteTransport.Inherit, null);

    /// <summary>
    /// Aims at every live instance publishing the surface.
    /// </summary>
    /// <returns>A view that answers once per instance.</returns>
    public NoireRemoteView OnAll() => new(this, NoireRemoteTarget.All, NoireRemoteTransport.Inherit, null);

    /// <summary>
    /// Aims at every live instance carrying a tag.
    /// </summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The value it has to hold.</param>
    /// <returns>A view that answers once per matching instance.</returns>
    public NoireRemoteView OnAll(string key, string value) => new(this, NoireRemoteTarget.EveryMeta(key, value), NoireRemoteTransport.Inherit, null);

    /// <summary>
    /// Takes one call over a wire other than this client's default.
    /// </summary>
    /// <param name="transport">The wire to use.</param>
    /// <returns>A view of this client on that wire.</returns>
    public NoireRemoteView Over(NoireRemoteTransport transport) => new(this, null, transport, null);

    /// <summary>
    /// Waits for an absent target to appear before failing.
    /// </summary>
    /// <param name="window">How long to wait.</param>
    /// <returns>A view of this client that waits.</returns>
    public NoireRemoteView WaitUpTo(TimeSpan window) => new(this, null, NoireRemoteTransport.Inherit, window);

    /// <summary>
    /// Calls a member and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The type the return value converts into.</typeparam>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, keyed by parameter name. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The converted result.</returns>
    public Task<TResult> InvokeAsync<TResult>(string member, object? args = null, CancellationToken cancellationToken = default)
        => new NoireRemoteView(this, null, NoireRemoteTransport.Inherit, null).InvokeAsync<TResult>(member, args, cancellationToken);

    /// <summary>
    /// Calls a member and discards its result.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, keyed by parameter name. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the member has run.</returns>
    public Task InvokeAsync(string member, object? args = null, CancellationToken cancellationToken = default)
        => new NoireRemoteView(this, null, NoireRemoteTransport.Inherit, null).InvokeAsync(member, args, cancellationToken);

    /// <summary>
    /// Starts a member that runs as a job and hands back a handle to follow it.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, keyed by parameter name. Null means none.</param>
    /// <param name="cancellationToken">Cancels the request that starts the job. The job itself keeps running.</param>
    /// <returns>A handle that reads the job's state and its result.</returns>
    public Task<NoireRemoteJob> StartAsync(string member, object? args = null, CancellationToken cancellationToken = default)
        => new NoireRemoteView(this, null, NoireRemoteTransport.Inherit, null).StartAsync(member, args, cancellationToken);

    /// <summary>
    /// Picks up a job this client did not start, or one it started before a reconnection, by its id.
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// A handle to the job. A job this listener does not know answers <see cref="NoireRemoteJobState.Unknown"/>.
    /// </returns>
    public async Task<NoireRemoteJob> AttachAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var instance = Resolve(lastAim, TimeSpan.Zero);
        var client = await EnsureSocketAsync(instance, cancellationToken).ConfigureAwait(false);
        var id = Interlocked.Increment(ref nextId);
        var answer = new TaskCompletionSource<NoireRemoteFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        pending[id] = answer;

        try
        {
            await client.SendAsync(
                new NoireRemoteFrame { Kind = NoireRemoteFrameKind.Progress, Id = id, Job = jobId }.Write(),
                cancellationToken).ConfigureAwait(false);

            using var registration = cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
            var frame = await answer.Task.ConfigureAwait(false);

            var status = frame.Payload?.ToObject<NoireRemoteJobStatus>()
                ?? new NoireRemoteJobStatus { Id = jobId, State = NoireRemoteJobState.Unknown };

            var job = new NoireRemoteJob(instance, status);

            if (status.State == NoireRemoteJobState.Running)
                Follow(job);

            return job;
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Follows a job's progress as it is reported, without polling. The handler is called with the fraction done
    /// and whatever the member said it was doing.
    /// </summary>
    /// <param name="job">The job to follow.</param>
    /// <param name="handler">Called on every report.</param>
    /// <returns>A handle that stops the delivery when disposed.</returns>
    public IDisposable Follow(NoireRemoteJob job, Action<double, string?> handler)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(handler);

        return Subscribe(NoireRemotePaths.ProgressTopic(job.Id), (_, payload) =>
        {
            var report = payload?.ToObject<NoireRemoteProgressReport>();

            if (report != null)
                handler(report.Value ?? 0, report.Route);
        });
    }

    /// <summary>
    /// Receives every event whose topic starts with a prefix, over a socket that opens on the first subscription.
    /// </summary>
    /// <param name="topicPrefix">The topic prefix, such as <c>nav.</c>. An empty string takes everything.</param>
    /// <param name="handler">Called with the topic and the event's arguments.</param>
    /// <returns>A handle that stops the delivery when disposed.</returns>
    public IDisposable Subscribe(string topicPrefix, Action<string, JToken?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(disposed, this);

        var subscription = new Subscription(this, topicPrefix ?? string.Empty, handler);

        lock (subscriptions)
            subscriptions.Add(subscription);

        // A subscription opens the socket immediately.
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureSocketAsync(NoireRemoteTarget.Any, TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);
                await SendSubscribeAsync(subscription.Prefix, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A subscription made while nothing listens stays registered until a connection opens.
            }
        });

        return subscription;
    }

    /// <summary>
    /// Receives every event whose topic starts with a prefix, converted to one type.
    /// </summary>
    /// <typeparam name="T">The type the event's arguments convert into.</typeparam>
    /// <param name="topicPrefix">The topic prefix.</param>
    /// <param name="handler">Called with the converted arguments.</param>
    /// <returns>A handle that stops the delivery when disposed.</returns>
    public IDisposable Subscribe<T>(string topicPrefix, Action<T?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Subscribe(topicPrefix, (_, payload) => handler(payload == null ? default : payload.ToObject<T>()));
    }

    /// <summary>
    /// Closes whatever this client holds open.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        lock (subscriptions)
            subscriptions.Clear();

        var held = Interlocked.Exchange(ref socket, null);
        held?.Dispose();
        socketGate.Dispose();
    }

    // Resolved at call time. A cached address is never reused.
    internal NoireRemoteInstance Resolve(NoireRemoteTarget target, TimeSpan wait)
    {
        // A reconnection re-resolves against this aim.
        lastAim = target;

        var deadline = DateTime.UtcNow + (wait > TimeSpan.Zero ? wait : TimeSpan.Zero);

        while (true)
        {
            var online = Discover(Api);
            var matching = online.Where(target.Matches).ToArray();

            if (matching.Length == 1)
                return matching[0];

            if (matching.Length > 1)
                return PickAndWarn(matching, target);

            if (DateTime.UtcNow >= deadline)
                throw new NoireRemoteNotFoundException(target.Describe(online));

            Resolver.Invalidate();
            Thread.Sleep(200);
        }
    }

    internal IReadOnlyList<NoireRemoteInstance> ResolveAll(NoireRemoteTarget target)
    {
        var online = Discover(Api);
        var matching = online.Where(target.Matches).ToArray();

        if (matching.Length == 0)
            throw new NoireRemoteNotFoundException(target.Describe(online));

        return matching;
    }

    // The caller is warned once per change in what is online.
    private NoireRemoteInstance PickAndWarn(IReadOnlyList<NoireRemoteInstance> matching, NoireRemoteTarget target)
    {
        var chosen = matching.OrderByDescending(instance => instance.StartedUtc).First();

        if (!target.IsAny)
            return chosen;

        var fingerprint = string.Join(",", matching.Select(instance => instance.Id).OrderBy(id => id));

        lock (warnGate)
        {
            if (warnedAbout == fingerprint)
                return chosen;

            warnedAbout = fingerprint;
        }

        Warn(matching.Count + " instances publish '" + Api + "', calling " + Describe(chosen)
            + ". Name one with .On(...) or NoireRemoteClient.DefaultTarget.");

        return chosen;
    }

    private static string Describe(NoireRemoteInstance instance)
        => string.IsNullOrEmpty(instance.Label) ? "pid " + instance.ProcessId : instance.Label;

    private sealed class Subscription(NoireRemoteClient client, string prefix, Action<string, JToken?> handler) : IDisposable
    {
        internal string Prefix { get; } = prefix;

        internal void Deliver(string topic, JToken? payload)
        {
            if (Prefix.Length == 0 || topic.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                handler(topic, payload);
        }

        public void Dispose()
        {
            lock (client.subscriptions)
                client.subscriptions.Remove(this);

            _ = client.SendUnsubscribeAsync(Prefix, CancellationToken.None);
        }
    }
}
