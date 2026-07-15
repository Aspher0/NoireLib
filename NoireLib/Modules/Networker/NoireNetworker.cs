using NoireLib.Core.Modules;
using NoireLib.Core.Subscriptions;
using NoireLib.Networker.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker;

/// <summary>
/// Zero-configuration communication between game instances on the same PC and, optionally, the LAN.<br/>
/// One instance per machine is elected hub through a named kernel mutex (no well-known ports at all);
/// everyone else connects to it over loopback TCP. LAN machines bridge hub-to-hub via UDP beacons.<br/>
/// Handlers, request continuations, and peer events are all delivered on the framework thread.<br/>
/// <b>Never sync-block (<c>.Wait()</c> / <c>.Result</c>) on a networker task from the framework thread - always await.</b>
/// </summary>
public partial class NoireNetworker : NoireModuleBase<NoireNetworker>
{
    private readonly object sendGate = new();
    private readonly object peersGate = new();
    private readonly Dictionary<Guid, NetworkerPeer> peers = new();
    private readonly Queue<Envelope> outbox = new();

    private NetworkerOptions options = new();
    private NetworkerOptions? activeOptions;
    private string? networkName;
    private NetworkerSelf? self;
    private long peerGeneration;

    private NoireSubscriptionRegistry<string, MessageContext> messageRegistry = null!;
    private NoireSubscriptionRegistry<int, NetworkerPeer> peerJoinedRegistry = null!;
    private NoireSubscriptionRegistry<int, NetworkerPeer> peerLeftRegistry = null!;
    private NoireSubscriptionRegistry<int, (NetworkerPeer Peer, string Key)> peerUpdatedRegistry = null!;
    private NoireSubscriptionRegistry<int, NetworkerState> stateRegistry = null!;

    private DeliveryPump? pump;
    private RequestBroker? broker;
    private ElectionMutex? election;
    private CancellationTokenSource? supervisionCts;
    private Task? supervisionTask;
    private HubServer? hubServer;
    private ClientConnection? clientConnection;
    private volatile NetworkerState state = NetworkerState.Stopped;

    internal readonly record struct MessageContext(NetworkerPeer Peer, object Message);

    /// <summary>
    /// The default constructor needed for internal purposes.<br/>
    /// Configure through <see cref="SetNetworkName(string)"/> and <see cref="Options"/> before activating.
    /// </summary>
    public NoireNetworker() : base((string?)null, false, true) { }

    /// <summary>
    /// Creates a new networker for the given network name.
    /// </summary>
    /// <param name="networkName">The name identifying the network (e.g. "MyPlugin.Sync"). Instances only see peers using the same name.</param>
    /// <param name="options">Optional settings; same-PC operation needs none.</param>
    /// <param name="moduleId">Optional module ID for multiple networker instances.</param>
    /// <param name="active">Whether to activate (join the network) on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
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

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireNetworker(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

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

    /// <summary>
    /// The unique, session-scoped identifier of the local instance on the network.
    /// </summary>
    public Guid SelfId { get; } = Guid.NewGuid();

    /// <summary>
    /// The name of the network this instance belongs to.
    /// </summary>
    public string? NetworkName => networkName;

    /// <summary>
    /// The options of this networker. Changes made while the networker is active require a restart (deactivate/activate) to apply.
    /// </summary>
    public NetworkerOptions Options => options;

    /// <summary>
    /// The current connection state.
    /// </summary>
    public NetworkerState State => state;

    /// <summary>
    /// Whether this instance currently is the machine's hub. Informational only - usage never differs.
    /// </summary>
    public bool IsHub => hubServer != null;

    /// <summary>
    /// The local instance's own presence. Metadata set here is automatically synchronized to every peer.
    /// </summary>
    public NetworkerSelf Self => self ??= new NetworkerSelf(SelfId, OnSelfMetadataChanged);

    /// <summary>
    /// Every other peer on the network - same-PC and LAN alike, one list. Excludes <see cref="Self"/>.
    /// </summary>
    public IReadOnlyList<NetworkerPeer> OtherPeers
    {
        get
        {
            lock (peersGate)
                return peers.Values.ToArray();
        }
    }

    /// <summary>
    /// Sets the network name. When the networker is active, it leaves the current network and joins the new one.
    /// </summary>
    /// <param name="newNetworkName">The new network name.</param>
    /// <returns>The module instance for chaining.</returns>
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
        pump = new DeliveryPump(activeOptions.DeliveryQueueCapacity, (ex, message) => InternalLogError(ex, message));
        broker = new RequestBroker(pump);
        election = new ElectionMutex(NetworkerNames.MutexName(networkName));
        supervisionCts = new CancellationTokenSource();
        supervisionTask = Task.Run(() => RunAsync(supervisionCts.Token));

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
        var cts = supervisionCts;

        if (cts == null)
            return;

        supervisionCts = null;
        cts.Cancel();

        try
        {
            supervisionTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Cancellation exceptions are expected here.
        }

        supervisionTask = null;
        broker?.FailAll(new OperationCanceledException("The networker was stopped."));
        FailAllBarriers();
        ClearAllPeersWithEvents();
        SetState(NetworkerState.Stopped);

        lock (sendGate)
            outbox.Clear();

        election?.Dispose();
        election = null;
        broker = null;

        var pumpToDispose = pump;
        pump = null;
        pumpToDispose?.Dispose();
        cts.Dispose();

        InternalLog($"Networker stopped for network '{networkName}'.");
    }

    #endregion

    #region Supervision (election / connection loop)

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var firstAttempt = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetState(firstAttempt ? NetworkerState.Starting : NetworkerState.Reelecting);
                firstAttempt = false;

                if (election!.TryAcquire())
                    await RunAsHubAsync(cancellationToken).ConfigureAwait(false);
                else
                    await RunAsClientAsync(cancellationToken).ConfigureAwait(false);
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
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunAsHubAsync(CancellationToken cancellationToken)
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

        // The hub faulted (it only completes on its own when something went wrong) - brief pause, then re-elect.
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsClientAsync(CancellationToken cancellationToken)
    {
        var rendezvous = RendezvousFile.TryRead(NetworkerNames.MapName(networkName!));

        if (rendezvous == null || rendezvous.Network != networkName)
        {
            // The hub is mid-startup or mid-failover; retry shortly.
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            return;
        }

        var client = new ClientConnection(this, cancellationToken);

        try
        {
            if (!await client.ConnectAsync(rendezvous.Port, cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                return;
            }

            lock (sendGate)
                clientConnection = client;

            SetState(NetworkerState.Ready);
            InternalLog($"Joined '{networkName}' as client (hub port {rendezvous.Port}).");

            await client.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (sendGate)
                clientConnection = null;

            client.Dispose();
        }
    }

    /// <summary>
    /// After becoming hub, peers inherited from the previous role re-confirm themselves by reconnecting.
    /// Anything not seen again within the grace period is treated as departed.
    /// </summary>
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
                    // Buffer during (re-)election; flushed the moment the network is Ready again.
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

        // A message can outrun its sender's presence during joins; hand out a transient peer rather than dropping it.
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

    internal void InternalLog(string message)
    {
        if (EnableLogging)
            NoireLogger.LogDebug(this, message);
    }

    internal void InternalLogWarning(string message)
        => NoireLogger.LogWarning(this, message);

    internal void InternalLogError(Exception? ex, string message)
        => NoireLogger.LogError(this, ex, message);

    private void ReportHandlerException(Exception ex, string description)
        => NoireLogger.LogError(this, ex, $"Unhandled exception in {description}.");

    #endregion
}
