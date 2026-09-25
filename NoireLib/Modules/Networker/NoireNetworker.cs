using NoireLib.Core.Modules;
using NoireLib.Core.Subscriptions;
using NoireLib.Networker.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker;

/// <summary>
/// Zero-configuration messaging between game instances on one PC and, optionally, the LAN. Everything is delivered
/// on the framework thread. <b>Never block on a networker task from the framework thread: await it.</b>
/// </summary>
public partial class NoireNetworker : NoireModuleBase<NoireNetworker>
{
    private static readonly TimeSpan ElectionRetryBaseDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ElectionRetryMaxDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SupervisionStopTimeout = TimeSpan.FromSeconds(2);

    private readonly object sendGate = new();
    private readonly object peersGate = new();
    private readonly Dictionary<Guid, NetworkerPeer> peers = new();
    private readonly Queue<Envelope> outbox = new();

    private NetworkerOptions options = new();
    private NetworkerOptions? activeOptions;
    private string? networkName;
    private NetworkerSelf? self;
    private long peerGeneration;
    private long electionAttempts;

    private NoireSubscriptionRegistry<string, MessageContext> messageRegistry = null!;
    private NoireSubscriptionRegistry<int, NetworkerPeer> peerJoinedRegistry = null!;
    private NoireSubscriptionRegistry<int, NetworkerPeer> peerLeftRegistry = null!;
    private NoireSubscriptionRegistry<int, (NetworkerPeer Peer, string Key)> peerUpdatedRegistry = null!;
    private NoireSubscriptionRegistry<int, NetworkerState> stateRegistry = null!;

    private volatile DeliveryPump? pump;
    private volatile RequestBroker? broker;
    private volatile ElectionMutex? election;
    private volatile CancellationTokenSource? supervisionCts;
    private volatile Task? supervisionTask;
    private HubServer? hubServer;
    private ClientConnection? clientConnection;
    private volatile NetworkerState state = NetworkerState.Stopped;

    internal readonly record struct MessageContext(NetworkerPeer Peer, object Message);

    /// <summary>
    /// The default constructor needed for internal purposes; configure through <see cref="SetNetworkName(string)"/> and <see cref="Options"/> before activating.
    /// </summary>
    public NoireNetworker() : base((string?)null, false, true) { }

    /// <summary>Creates a new networker for the given network name.</summary>
    /// <param name="networkName">The name identifying the network (e.g. "MyPlugin.Sync"). Instances only see peers using the same name.</param>
    /// <param name="moduleId">Optional module ID for multiple networker instances.</param>
    /// <param name="active">Whether to activate (join the network) on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="options">Optional settings; same-PC operation needs none.</param>
    public NoireNetworker(
        string networkName,
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        NetworkerOptions? options = null) : base(moduleId, false, enableLogging)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkName);

        this.networkName = networkName;

        if (options != null)
            this.options = options.Clone();

        if (active)
            SetActive(true);
    }

    // Always created inactive: no network name can be supplied here.
    internal NoireNetworker(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, false, enableLogging)
    {
        if (active)
            InternalLog($"Created without a network name, so activation was deferred. Call {nameof(SetNetworkName)} and then {nameof(SetActive)} to join a network.");
    }

    /// <inheritdoc/>
    protected override void InitializeModule(params object?[] args)
    {
        messageRegistry = new(ReportHandlerException);
        peerJoinedRegistry = new(ReportHandlerException);
        peerLeftRegistry = new(ReportHandlerException);
        peerUpdatedRegistry = new(ReportHandlerException);
        stateRegistry = new(ReportHandlerException);
    }

    #region Public state

    /// <summary>The unique, session-scoped identifier of the local instance on the network.</summary>
    public Guid SelfId { get; } = Guid.NewGuid();

    /// <summary>The name of the network this instance belongs to.</summary>
    public string? NetworkName => networkName;

    /// <summary>The options. A change applies once the networker restarts.</summary>
    public NetworkerOptions Options => options;

    /// <summary>The current connection state.</summary>
    public NetworkerState State => state;

    /// <summary>Whether this instance is the machine's hub. Informational: usage is the same either way.</summary>
    public bool IsHub => hubServer != null;

    /// <summary>This instance's own presence. Its metadata is synchronized to every peer.</summary>
    public NetworkerSelf Self => self ??= new NetworkerSelf(SelfId, OnSelfMetadataChanged);

    /// <summary>Every other peer, on this PC or the LAN. Excludes <see cref="Self"/>.</summary>
    public IReadOnlyList<NetworkerPeer> OtherPeers
    {
        get
        {
            lock (peersGate)
                return peers.Values.ToArray();
        }
    }

    /// <summary>Sets the network name. An active networker leaves its network and joins the new one.</summary>
    /// <param name="newNetworkName">The network name.</param>
    /// <returns>This module.</returns>
    public NoireNetworker SetNetworkName(string newNetworkName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newNetworkName);

        if (networkName == newNetworkName)
            return this;

        var wasActive = IsActive;

        if (wasActive)
            SetActive(false);

        networkName = newNetworkName;

        if (wasActive)
            SetActive(true);

        return this;
    }

    #endregion

    #region Module lifecycle

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        if (string.IsNullOrWhiteSpace(networkName))
        {
            NoireLogger.LogError(this, $"Cannot activate: no network name is set. Use {nameof(SetNetworkName)} first.");
            IsActive = false;
            return;
        }

        activeOptions = options.Clone();
        pump = new DeliveryPump(activeOptions.DeliveryQueueCapacity, (ex, message) => InternalLogError(ex, message))
        {
            ForceQueuedDelivery = ForceQueuedDelivery,
        };
        broker = new RequestBroker(pump);
        election = new ElectionMutex(NetworkerNames.MutexName(networkName));

        // Published last: everything teardown releases must already exist.
        var cts = new CancellationTokenSource();
        supervisionTask = Task.Run(() => RunAsync(cts.Token));
        supervisionCts = cts;

        InternalLog($"Networker activated for network '{networkName}'.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
        => StopInternal();

    /// <inheritdoc/>
    protected override void DisposeInternal()
    {
        IsActive = false;
        StopInternal();
    }

    private void StopInternal()
    {
        // Claimed atomically: a deactivation racing disposal tears down once.
        var cts = Interlocked.Exchange(ref supervisionCts, null);

        if (cts == null)
            return;

        cts.Cancel();

        var task = supervisionTask;
        supervisionTask = null;

        var supervisionEnded = WaitForSupervisionExit(task);

        broker?.FailAll(new OperationCanceledException("The networker was stopped."));
        FailAllBarriers();
        ClearAllPeersWithEvents();

        // Disposed even under a live loop: a held role would block every other election. The mutex refuses the role once disposed.
        election?.Dispose();
        election = null;
        broker = null;

        // Disposed unconditionally: posting to a disposed pump does nothing.
        var pumpToDispose = pump;
        pump = null;
        pumpToDispose?.Dispose();

        // Delivered after the pump is gone, on this thread: a disposed pump drops its backlog, and Stopped must be seen last.
        SetState(NetworkerState.Stopped);

        lock (sendGate)
            outbox.Clear();

        // Disposed only once the loop has finished: disposing it underneath the loop would throw.
        if (supervisionEnded)
            cts.Dispose();
        else
            InternalLogWarning("The supervision loop did not stop within the teardown timeout; its cancellation source is left to finalization.");

        InternalLog($"Networker stopped for network '{networkName}'.");
    }

    // Bounded: teardown runs during a plugin unload, which must never block on the network.
    private static bool WaitForSupervisionExit(Task? task)
    {
        if (task == null)
            return true;

        try
        {
            return task.Wait(SupervisionStopTimeout);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    #endregion

    // Test seam: queues deliveries without NoireLib. Only DrainDeliveries runs them. Read on activation.
    internal bool ForceQueuedDelivery { get; set; }

    internal void DrainDeliveries()
        => pump?.Drain();

    #region Supervision (election / connection loop)

    // Each attempt costs a dedicated thread: this measures the supervision loop's churn.
    internal long ElectionAttempts => Interlocked.Read(ref electionAttempts);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var firstAttempt = true;
        var idleAttempts = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetState(firstAttempt ? NetworkerState.Starting : NetworkerState.Reelecting);
                firstAttempt = false;

                var electionLocal = election;

                if (electionLocal == null)
                    break;

                Interlocked.Increment(ref electionAttempts);

                var reachedReady = electionLocal.TryAcquire()
                    ? await RunAsHubAsync(cancellationToken).ConfigureAwait(false)
                    : await RunAsClientAsync(cancellationToken).ConfigureAwait(false);

                // An attempt that served the network re-elects at once. One that established nothing backs off: each costs a thread.
                idleAttempts = reachedReady ? 0 : (idleAttempts + 1);

                if (idleAttempts > 0)
                    await Task.Delay(ComputeElectionBackoff(idleAttempts), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                InternalLogError(ex, "Supervision loop error; retrying.");

                try
                {
                    await Task.Delay(ComputeElectionBackoff(++idleAttempts), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    // Doubles from 100 ms to a 2 s ceiling: a slow hub still joins fast, and a network that cannot form stays quiet.
    internal static TimeSpan ComputeElectionBackoff(int idleAttempts)
    {
        if (idleAttempts <= 1)
            return ElectionRetryBaseDelay;

        // Capped: shifting further would overflow, and the ceiling is reached long before.
        var doublings = Math.Min(idleAttempts - 1, 16);
        var delayMs = ElectionRetryBaseDelay.TotalMilliseconds * (1L << doublings);

        return delayMs >= ElectionRetryMaxDelay.TotalMilliseconds
            ? ElectionRetryMaxDelay
            : TimeSpan.FromMilliseconds(delayMs);
    }

    private async Task<bool> RunAsHubAsync(CancellationToken cancellationToken)
    {
        var hub = new HubServer(this, cancellationToken);

        try
        {
            hub.Start();

            lock (sendGate)
                hubServer = hub;

            BeginPeerGenerationSweep(cancellationToken);
            SetState(NetworkerState.Ready);
            InternalLog($"Became hub for '{networkName}' on port {hub.Port}.");

            await hub.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (sendGate)
                hubServer = null;

            hub.Dispose();
            election?.Release();
        }

        // The hub only completes on its own after a fault.
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> RunAsClientAsync(CancellationToken cancellationToken)
    {
        var rendezvous = RendezvousFile.TryRead(NetworkerNames.MapName(networkName!));

        // The hub is mid-startup or mid-failover.
        if (rendezvous == null || rendezvous.Network != networkName)
            return false;

        var client = new ClientConnection(this, cancellationToken);

        try
        {
            if (!await client.ConnectAsync(rendezvous.Port, cancellationToken).ConfigureAwait(false))
                return false;

            lock (sendGate)
                clientConnection = client;

            SetState(NetworkerState.Ready);
            InternalLog($"Joined '{networkName}' as client (hub port {rendezvous.Port}).");

            // Held until the hub goes away, exactly when this instance must contend for the role.
            await client.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            lock (sendGate)
                clientConnection = null;

            client.Dispose();
        }
    }

    // Peers inherited from the previous role re-confirm by reconnecting. Unseen ones depart after the grace period.
    private void BeginPeerGenerationSweep(CancellationToken cancellationToken)
    {
        var sweepGeneration = Interlocked.Increment(ref peerGeneration);

        _ = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ContinueWith(task =>
        {
            if (task.IsCanceled)
                return;

            pump?.Post(() =>
            {
                List<NetworkerPeer> stale;

                lock (peersGate)
                {
                    stale = peers.Values.Where(peer => peer.SeenGeneration < sweepGeneration).ToList();

                    foreach (var peer in stale)
                        peers.Remove(peer.Id);
                }

                HubServer? hub;

                lock (sendGate)
                    hub = hubServer;

                foreach (var peer in stale)
                {
                    InternalLog($"Peer {peer} did not return after failover; removing it.");
                    broker?.FailPeer(peer.Id);
                    hub?.BroadcastPeerLeft(peer.Id, fromLocalOrigin: true, excludeSession: null);
                    peerLeftRegistry.Dispatch(0, peer);
                    PublishModuleEvent(new NetworkerPeerLeftEvent(this, peer));
                }

                if (stale.Count > 0)
                    EvaluateBarriers();
            });
        }, TaskScheduler.Default);
    }

    #endregion

    #region State & sending

    internal NetworkerOptions ActiveOptions => activeOptions ?? options;

    // Through the pump, ordered against peer events and messages. Without a pump, on the calling thread.
    private void SetState(NetworkerState newState)
    {
        NetworkerState oldState;

        lock (sendGate)
        {
            if (state == newState)
                return;

            oldState = state;
            state = newState;

            if (newState == NetworkerState.Ready)
            {
                while (outbox.Count > 0)
                    RouteEnvelopeUnderLock(outbox.Dequeue());
            }
        }

        var pumpLocal = pump;

        if (pumpLocal != null)
        {
            pumpLocal.Post(() =>
            {
                stateRegistry.Dispatch(0, newState);
                PublishModuleEvent(new NetworkerStateChangedEvent(this, oldState, newState));
                EvaluateBarriers();
            });
        }
        else
        {
            // No barrier to evaluate: without a pump the networker has stopped and failed every barrier.
            stateRegistry.Dispatch(0, newState);
            PublishModuleEvent(new NetworkerStateChangedEvent(this, oldState, newState));
        }
    }

    internal void SendEnvelope(Envelope envelope)
    {
        lock (sendGate)
        {
            switch (state)
            {
                case NetworkerState.Ready:
                    RouteEnvelopeUnderLock(envelope);
                    return;

                case NetworkerState.Starting:
                case NetworkerState.Reelecting:
                    // Buffered during an election, flushed once Ready.
                    if (outbox.Count < ActiveOptions.OutboundBufferCapacity)
                        outbox.Enqueue(envelope);
                    else
                        InternalLogWarning("Outbound buffer overflow; message dropped.");

                    return;
            }
        }

        InternalLogWarning("Cannot send: the networker is stopped.");
    }

    private void RouteEnvelopeUnderLock(Envelope envelope)
    {
        if (hubServer != null)
        {
            if (envelope.Kind == EnvelopeKind.PeerState)
            {
                var stateModel = Wire.FromPayload<PeerStateModel>(envelope.Payload);

                if (stateModel != null)
                    hubServer.BroadcastPeerState(stateModel, fromLocalOrigin: true, excludeSession: null, changedKey: envelope.TypeName);
            }
            else
            {
                hubServer.RouteFromLocal(envelope, originSession: null);
            }
        }
        else
        {
            clientConnection?.Post(envelope);
        }
    }

    #endregion

    #region Peer directory

    internal void HandleInboundEnvelope(Envelope envelope)
        => pump?.Post(() => HandleInboundOnPump(envelope));

    internal void ApplyPeerStateModel(PeerStateModel model, string? changedKey)
    {
        if (model.Id == SelfId)
            return;

        pump?.Post(() =>
        {
            bool isNew;
            NetworkerPeer peer;

            lock (peersGate)
            {
                isNew = !peers.TryGetValue(model.Id, out peer!);

                if (isNew)
                {
                    peer = new NetworkerPeer(model.Id);
                    peers[model.Id] = peer;
                }

                peer.IsSameMachine = !model.Remote;
                peer.SeenGeneration = Interlocked.Read(ref peerGeneration);
                peer.ReplaceState(model.Metadata, model.Flags);
            }

            if (isNew)
            {
                peerJoinedRegistry.Dispatch(0, peer);
                PublishModuleEvent(new NetworkerPeerJoinedEvent(this, peer));
            }
            else
            {
                var key = TranslateChangedKey(changedKey);
                peerUpdatedRegistry.Dispatch(0, (peer, key));
                PublishModuleEvent(new NetworkerPeerUpdatedEvent(this, peer, key));
            }

            EvaluateBarriers();
        });
    }

    internal void RemovePeerById(Guid peerId)
    {
        pump?.Post(() =>
        {
            NetworkerPeer? peer;

            lock (peersGate)
            {
                if (!peers.Remove(peerId, out peer))
                    return;
            }

            broker?.FailPeer(peerId);
            peerLeftRegistry.Dispatch(0, peer);
            PublishModuleEvent(new NetworkerPeerLeftEvent(this, peer));
            EvaluateBarriers();
        });
    }

    internal void ResyncPeersFromWelcome(PeerStateModel[] models)
    {
        pump?.Post(() =>
        {
            var joined = new List<NetworkerPeer>();
            var left = new List<NetworkerPeer>();

            lock (peersGate)
            {
                var incomingIds = models.Select(model => model.Id).ToHashSet();

                foreach (var peerId in peers.Keys.Where(id => !incomingIds.Contains(id)).ToList())
                {
                    if (peers.Remove(peerId, out var removed))
                        left.Add(removed);
                }

                foreach (var model in models)
                {
                    if (model.Id == SelfId)
                        continue;

                    if (!peers.TryGetValue(model.Id, out var peer))
                    {
                        peer = new NetworkerPeer(model.Id);
                        peers[model.Id] = peer;
                        joined.Add(peer);
                    }

                    peer.IsSameMachine = !model.Remote;
                    peer.SeenGeneration = Interlocked.Read(ref peerGeneration);
                    peer.ReplaceState(model.Metadata, model.Flags);
                }
            }

            foreach (var peer in left)
            {
                broker?.FailPeer(peer.Id);
                peerLeftRegistry.Dispatch(0, peer);
                PublishModuleEvent(new NetworkerPeerLeftEvent(this, peer));
            }

            foreach (var peer in joined)
            {
                peerJoinedRegistry.Dispatch(0, peer);
                PublishModuleEvent(new NetworkerPeerJoinedEvent(this, peer));
            }

            EvaluateBarriers();
        });
    }

    internal PeerStateModel[] CaptureKnownPeerStates(Guid? excludePeerId)
    {
        lock (peersGate)
        {
            var states = new List<PeerStateModel>(peers.Count + 1)
            {
                CaptureSelfState(),
            };

            foreach (var peer in peers.Values)
            {
                if (peer.Id == excludePeerId)
                    continue;

                var (metadata, flags) = peer.CaptureState();
                states.Add(new PeerStateModel { Id = peer.Id, Metadata = metadata, Flags = flags, Remote = !peer.IsSameMachine });
            }

            return states.ToArray();
        }
    }

    internal PeerStateModel CaptureSelfState()
    {
        var (metadata, flags) = Self.CaptureState();
        return new PeerStateModel { Id = SelfId, Metadata = metadata, Flags = flags, Remote = false };
    }

    private void ClearAllPeersWithEvents()
    {
        List<NetworkerPeer> removed;

        lock (peersGate)
        {
            removed = peers.Values.ToList();
            peers.Clear();
        }

        foreach (var peer in removed)
        {
            peerLeftRegistry.Dispatch(0, peer);
            PublishModuleEvent(new NetworkerPeerLeftEvent(this, peer));
        }
    }

    private NetworkerPeer ResolvePeer(Guid? peerId)
    {
        if (peerId == null)
            return new NetworkerPeer(Guid.Empty);

        lock (peersGate)
        {
            if (peers.TryGetValue(peerId.Value, out var peer))
                return peer;
        }

        // A message can outrun its sender's presence during a join.
        return new NetworkerPeer(peerId.Value);
    }

    private static string TranslateChangedKey(string? changedKey)
        => changedKey switch
        {
            null => "*",
            _ when changedKey.StartsWith("m:", StringComparison.Ordinal) => changedKey[2..],
            _ when changedKey.StartsWith("f:", StringComparison.Ordinal) => "flag:" + changedKey[2..],
            _ => changedKey,
        };

    #endregion

    #region Logging

    // For the Internal/ classes. The handler overload builds nothing while EnableLogging is off.
    internal void InternalLog([InterpolatedStringHandlerArgument("")] ref NoireLogHandler message)
    {
        if (message.IsEnabled)
            NoireLogger.LogDebug(this, message.ToStringAndClear());
    }

    internal void InternalLog(string message)
        => LogDebug(message);

    internal void InternalLogWarning(string message)
        => LogWarning(message);

    internal void InternalLogError(Exception? ex, string message)
        => LogError(ex, message);

    private void ReportHandlerException(Exception ex, string description)
        => LogError(ex, $"Unhandled exception in {description}.");

    #endregion
}
