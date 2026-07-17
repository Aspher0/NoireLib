using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker.Internal;

/// <summary>
/// The hub role: owns the TCP listener (ephemeral port), the rendezvous publication, local client sessions,
/// hub-to-hub LAN links, and all routing. Routing runs on socket threads so a frozen framework thread never
/// stalls relaying for the rest of the network.
/// </summary>
internal sealed class HubServer : IDisposable
{
    private readonly NoireNetworker owner;
    private readonly NetworkerOptions options;
    private readonly string networkName;
    private readonly CancellationTokenSource lifetime;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly object sessionGate = new();
    private readonly Dictionary<Guid, Session> clientSessions = new();      // local peer id -> session
    private readonly Dictionary<Guid, Session> hubLinks = new();            // remote hub id -> session
    private readonly Dictionary<Guid, Guid> remotePeerOwners = new();       // remote peer id -> remote hub id
    private readonly HashSet<Guid> dialsInProgress = new();

    private TcpListener? listener;
    private IDisposable? rendezvousHolder;
    private LanDiscovery? lanDiscovery;
    private int disposed;

    public HubServer(NoireNetworker owner, CancellationToken parentToken)
    {
        this.owner = owner;
        options = owner.ActiveOptions;
        networkName = owner.NetworkName!;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
    }

    /// <summary>
    /// A unique id for this hub incarnation, used for LAN link dial direction and deduplication.
    /// </summary>
    public Guid HubId { get; } = Guid.NewGuid();

    public int Port { get; private set; }

    /// <summary>
    /// Completes (faulted or not) when the hub can no longer operate and the supervision loop must react.
    /// </summary>
    public Task Completion => completion.Task;

    public void Start()
    {
        var bindAddress = options.EnableLan ? IPAddress.Any : IPAddress.Loopback;

        listener = new TcpListener(bindAddress, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;

        rendezvousHolder = RendezvousFile.Publish(NetworkerNames.MapName(networkName), networkName, Port);

        _ = AcceptLoopAsync();
        _ = LinkWatchdogLoopAsync();

        if (options.EnableLan)
        {
            if (options.LanSecret == null)
                owner.InternalLogWarning("LAN is enabled without a secret - this network is open to any LAN peer.");

            lanDiscovery = new LanDiscovery(this, owner, lifetime.Token);
            lanDiscovery.Start();
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var tcpClient = await listener!.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
                _ = HandleIncomingConnectionAsync(tcpClient);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
            {
                owner.InternalLogError(ex, "Hub accept loop failed; abandoning hub role.");
                completion.TrySetResult();
            }
        }
    }

    private async Task HandleIncomingConnectionAsync(TcpClient tcpClient)
    {
        FramedConnection? connection = null;

        try
        {
            connection = new FramedConnection(tcpClient, options.MaxFrameBytes);
            var nonce = NetworkerNames.CreateNonce();

            await connection.SendAsync(new Envelope
            {
                Kind = EnvelopeKind.Challenge,
                Network = networkName,
                Payload = Wire.ToPayload(new ChallengePayload { Nonce = nonce }),
            }, lifetime.Token).ConfigureAwait(false);

            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(5));

            var hello = await connection.ReceiveAsync(handshakeTimeout.Token).ConfigureAwait(false);

            if (hello == null)
            {
                connection.Dispose();
                return;
            }

            if (hello.Network != networkName)
            {
                await RejectAsync(connection, "Network name mismatch.").ConfigureAwait(false);
                return;
            }

            if (hello.Version != Wire.ProtocolVersion)
            {
                await RejectAsync(connection, $"Protocol version mismatch (hub: {Wire.ProtocolVersion}, peer: {hello.Version}).").ConfigureAwait(false);
                return;
            }

            switch (hello.Kind)
            {
                case EnvelopeKind.Hello:
                    await HandleClientHelloAsync(connection, hello).ConfigureAwait(false);
                    break;

                case EnvelopeKind.HelloHub:
                    await HandleHubHelloAsync(connection, hello, nonce).ConfigureAwait(false);
                    break;

                default:
                    await RejectAsync(connection, "Unexpected handshake frame.").ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                owner.InternalLog($"Incoming connection failed during handshake: {ex.Message}");

            connection?.Dispose();
        }
    }

    private async Task HandleClientHelloAsync(FramedConnection connection, Envelope hello)
    {
        // Local clients always connect over loopback; LAN peers join through their own hub.
        if (!connection.IsLoopback)
        {
            await RejectAsync(connection, "Clients must join through their machine's local hub.").ConfigureAwait(false);
            return;
        }

        var payload = Wire.FromPayload<HelloPayload>(hello.Payload);

        if (payload == null || payload.Self.Id == Guid.Empty)
        {
            await RejectAsync(connection, "Malformed hello.").ConfigureAwait(false);
            return;
        }

        var session = new Session(this, connection, payload.Self.Id, isHubLink: false);

        lock (sessionGate)
            clientSessions[payload.Self.Id] = session;

        await connection.SendAsync(new Envelope
        {
            Kind = EnvelopeKind.Welcome,
            Origin = owner.SelfId,
            Payload = Wire.ToPayload(new WelcomePayload
            {
                HubPeerId = owner.SelfId,
                Peers = owner.CaptureKnownPeerStates(excludePeerId: payload.Self.Id),
            }),
        }, lifetime.Token).ConfigureAwait(false);

        owner.ApplyPeerStateModel(payload.Self, changedKey: null);
        BroadcastPeerState(payload.Self, fromLocalOrigin: true, excludeSession: session);

        session.RunReadLoop();
    }

    private async Task HandleHubHelloAsync(FramedConnection connection, Envelope hello, string nonce)
    {
        if (!options.EnableLan)
        {
            await RejectAsync(connection, "LAN is not enabled on this network.").ConfigureAwait(false);
            return;
        }

        var payload = Wire.FromPayload<HelloHubPayload>(hello.Payload);

        if (payload == null || payload.HubId == Guid.Empty)
        {
            await RejectAsync(connection, "Malformed hub hello.").ConfigureAwait(false);
            return;
        }

        if (options.LanSecret != null && payload.Proof != NetworkerNames.ComputeProof(options.LanSecret, nonce))
        {
            await RejectAsync(connection, "Authentication failed.").ConfigureAwait(false);
            return;
        }

        lock (sessionGate)
        {
            if (hubLinks.ContainsKey(payload.HubId))
            {
                _ = RejectAsync(connection, "Duplicate hub link.");
                return;
            }
        }

        var session = new Session(this, connection, payload.HubId, isHubLink: true);

        lock (sessionGate)
            hubLinks[payload.HubId] = session;

        await connection.SendAsync(new Envelope
        {
            Kind = EnvelopeKind.WelcomeHub,
            Origin = owner.SelfId,
            Payload = Wire.ToPayload(new WelcomeHubPayload
            {
                HubId = HubId,
                Peers = CaptureLocalPeerStatesForRemote(),
            }),
        }, lifetime.Token).ConfigureAwait(false);

        RegisterRemotePeers(payload.Peers, payload.HubId);
        owner.InternalLog($"Hub link established with {payload.HubId} ({connection.RemoteEndPoint}).");

        session.RunReadLoop();
    }

    /// <summary>
    /// Dials another machine's hub discovered through LAN beacons. Only the lower hub id dials, so links are unique.
    /// </summary>
    public async Task DialHubLinkAsync(IPEndPoint remoteEndPoint, Guid remoteHubId)
    {
        lock (sessionGate)
        {
            if (hubLinks.ContainsKey(remoteHubId) || !dialsInProgress.Add(remoteHubId))
                return;
        }

        FramedConnection? connection = null;

        try
        {
            var tcpClient = new TcpClient(remoteEndPoint.AddressFamily);

            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));

            await tcpClient.ConnectAsync(remoteEndPoint, connectTimeout.Token).ConfigureAwait(false);
            connection = new FramedConnection(tcpClient, options.MaxFrameBytes);

            var challenge = await connection.ReceiveAsync(connectTimeout.Token).ConfigureAwait(false);

            if (challenge is not { Kind: EnvelopeKind.Challenge } || challenge.Network != networkName)
            {
                connection.Dispose();
                return;
            }

            var noncePayload = Wire.FromPayload<ChallengePayload>(challenge.Payload);

            await connection.SendAsync(new Envelope
            {
                Kind = EnvelopeKind.HelloHub,
                Network = networkName,
                Payload = Wire.ToPayload(new HelloHubPayload
                {
                    HubId = HubId,
                    Proof = options.LanSecret != null && noncePayload != null
                        ? NetworkerNames.ComputeProof(options.LanSecret, noncePayload.Nonce)
                        : null,
                    Peers = CaptureLocalPeerStatesForRemote(),
                }),
            }, connectTimeout.Token).ConfigureAwait(false);

            var welcome = await connection.ReceiveAsync(connectTimeout.Token).ConfigureAwait(false);

            if (welcome is { Kind: EnvelopeKind.WelcomeHub })
            {
                var payload = Wire.FromPayload<WelcomeHubPayload>(welcome.Payload);

                if (payload == null)
                {
                    connection.Dispose();
                    return;
                }

                var session = new Session(this, connection, payload.HubId, isHubLink: true);

                lock (sessionGate)
                {
                    if (hubLinks.ContainsKey(payload.HubId))
                    {
                        connection.Dispose();
                        return;
                    }

                    hubLinks[payload.HubId] = session;
                }

                RegisterRemotePeers(payload.Peers, payload.HubId);
                owner.InternalLog($"Hub link established with {payload.HubId} ({remoteEndPoint}).");
                session.RunReadLoop();
            }
            else
            {
                if (welcome is { Kind: EnvelopeKind.Reject })
                    owner.InternalLogWarning($"Hub link to {remoteEndPoint} rejected: {Wire.FromPayload<RejectPayload>(welcome.Payload)?.Reason}");

                connection.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                owner.InternalLog($"Hub link dial to {remoteEndPoint} failed: {ex.Message}");

            connection?.Dispose();
        }
        finally
        {
            lock (sessionGate)
                dialsInProgress.Remove(remoteHubId);
        }
    }

    /// <summary>
    /// Routes an envelope originating from this machine (the hub's own module or a local client session).
    /// </summary>
    public void RouteFromLocal(Envelope envelope, Session? originSession)
    {
        var target = envelope.Target;

        if (originSession != null && (target == null || target == owner.SelfId))
            owner.HandleInboundEnvelope(envelope);

        List<Session> locals, links;

        lock (sessionGate)
        {
            locals = clientSessions.Values.ToList();
            links = hubLinks.Values.ToList();
        }

        foreach (var session in locals)
        {
            if (ReferenceEquals(session, originSession))
                continue;

            if (target == null || target == session.RemoteId)
                session.Post(envelope);
        }

        if (target == null)
        {
            foreach (var link in links)
                link.Post(envelope);
        }
        else
        {
            Guid ownerHub;

            lock (sessionGate)
            {
                if (!remotePeerOwners.TryGetValue(target.Value, out ownerHub))
                    return;
            }

            Session? link;

            lock (sessionGate)
                hubLinks.TryGetValue(ownerHub, out link);

            link?.Post(envelope);
        }
    }

    /// <summary>
    /// Routes an envelope received from a hub link: local delivery only - never re-forwarded to other links (no multi-hop).
    /// </summary>
    public void RouteFromRemote(Envelope envelope)
    {
        var target = envelope.Target;

        if (target == null || target == owner.SelfId)
            owner.HandleInboundEnvelope(envelope);

        List<Session> locals;

        lock (sessionGate)
            locals = clientSessions.Values.ToList();

        foreach (var session in locals)
        {
            if (target == null || target == session.RemoteId)
                session.Post(envelope);
        }
    }

    public void BroadcastPeerState(PeerStateModel state, bool fromLocalOrigin, Session? excludeSession, string? changedKey = null)
    {
        var localEnvelope = new Envelope
        {
            Kind = EnvelopeKind.PeerState,
            Origin = state.Id,
            TypeName = changedKey,
            Payload = Wire.ToPayload(state),
        };

        List<Session> locals, links;

        lock (sessionGate)
        {
            locals = clientSessions.Values.ToList();
            links = fromLocalOrigin ? hubLinks.Values.ToList() : new List<Session>();
        }

        foreach (var session in locals)
        {
            if (!ReferenceEquals(session, excludeSession))
                session.Post(localEnvelope);
        }

        if (links.Count > 0)
        {
            // Peers crossing a hub link are remote from the receiver's point of view.
            var remoteState = new PeerStateModel { Id = state.Id, Metadata = state.Metadata, Flags = state.Flags, Remote = true };

            var remoteEnvelope = new Envelope
            {
                Kind = EnvelopeKind.PeerState,
                Origin = state.Id,
                TypeName = changedKey,
                Payload = Wire.ToPayload(remoteState),
            };

            foreach (var link in links)
                link.Post(remoteEnvelope);
        }
    }

    public void BroadcastPeerLeft(Guid peerId, bool fromLocalOrigin, Session? excludeSession)
    {
        var envelope = new Envelope { Kind = EnvelopeKind.PeerLeft, Origin = peerId };

        List<Session> locals, links;

        lock (sessionGate)
        {
            locals = clientSessions.Values.ToList();
            links = fromLocalOrigin ? hubLinks.Values.ToList() : new List<Session>();
        }

        foreach (var session in locals)
        {
            if (!ReferenceEquals(session, excludeSession))
                session.Post(envelope);
        }

        foreach (var link in links)
            link.Post(envelope);
    }

    private void RegisterRemotePeers(PeerStateModel[] peers, Guid remoteHubId)
    {
        foreach (var peer in peers)
        {
            lock (sessionGate)
                remotePeerOwners[peer.Id] = remoteHubId;

            var remoteState = new PeerStateModel { Id = peer.Id, Metadata = peer.Metadata, Flags = peer.Flags, Remote = true };
            owner.ApplyPeerStateModel(remoteState, changedKey: null);
            BroadcastPeerState(remoteState, fromLocalOrigin: false, excludeSession: null);
        }
    }

    private PeerStateModel[] CaptureLocalPeerStatesForRemote()
    {
        var states = owner.CaptureKnownPeerStates(excludePeerId: null)
            .Where(state => !state.Remote)
            .ToArray();

        foreach (var state in states)
            state.Remote = true;

        return states;
    }

    /// <summary>
    /// Handles a session ending, whether through a goodbye, an EOF, or a transport error.
    /// </summary>
    internal void OnSessionClosed(Session session)
    {
        if (session.IsHubLink)
        {
            List<Guid> orphanedPeers;

            lock (sessionGate)
            {
                if (!hubLinks.Remove(session.RemoteId, out var registered) || !ReferenceEquals(registered, session))
                    return;

                orphanedPeers = remotePeerOwners
                    .Where(pair => pair.Value == session.RemoteId)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var peerId in orphanedPeers)
                    remotePeerOwners.Remove(peerId);
            }

            owner.InternalLog($"Hub link {session.RemoteId} closed; removing {orphanedPeers.Count} remote peer(s).");

            foreach (var peerId in orphanedPeers)
            {
                owner.RemovePeerById(peerId);
                BroadcastPeerLeft(peerId, fromLocalOrigin: false, excludeSession: null);
            }
        }
        else
        {
            lock (sessionGate)
            {
                if (!clientSessions.Remove(session.RemoteId, out var registered) || !ReferenceEquals(registered, session))
                    return;
            }

            owner.RemovePeerById(session.RemoteId);
            BroadcastPeerLeft(session.RemoteId, fromLocalOrigin: true, excludeSession: session);
        }
    }

    internal void HandleSessionEnvelope(Session session, Envelope envelope)
    {
        switch (envelope.Kind)
        {
            case EnvelopeKind.Message:
            case EnvelopeKind.Request:
            case EnvelopeKind.Response:
            case EnvelopeKind.Event:
                if (session.IsHubLink)
                    RouteFromRemote(envelope);
                else
                    RouteFromLocal(envelope, session);
                break;

            case EnvelopeKind.PeerState:
                {
                    var state = Wire.FromPayload<PeerStateModel>(envelope.Payload);

                    if (state == null)
                        break;

                    if (session.IsHubLink)
                    {
                        lock (sessionGate)
                            remotePeerOwners[state.Id] = session.RemoteId;

                        state.Remote = true;
                        owner.ApplyPeerStateModel(state, envelope.TypeName);
                        BroadcastPeerState(state, fromLocalOrigin: false, excludeSession: null, changedKey: envelope.TypeName);
                    }
                    else
                    {
                        owner.ApplyPeerStateModel(state, envelope.TypeName);
                        BroadcastPeerState(state, fromLocalOrigin: true, excludeSession: session, changedKey: envelope.TypeName);
                    }

                    break;
                }

            case EnvelopeKind.PeerLeft:
                {
                    if (!session.IsHubLink || envelope.Origin == null)
                        break;

                    lock (sessionGate)
                        remotePeerOwners.Remove(envelope.Origin.Value);

                    owner.RemovePeerById(envelope.Origin.Value);
                    BroadcastPeerLeft(envelope.Origin.Value, fromLocalOrigin: false, excludeSession: null);
                    break;
                }

            case EnvelopeKind.Goodbye:
                session.Dispose();
                OnSessionClosed(session);
                break;

            case EnvelopeKind.HubGoodbye:
                if (session.IsHubLink)
                {
                    session.Dispose();
                    OnSessionClosed(session);
                }

                break;

            case EnvelopeKind.Ping:
                session.Post(new Envelope { Kind = EnvelopeKind.Pong });
                break;

            case EnvelopeKind.Pong:
                break;
        }
    }

    private async Task LinkWatchdogLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(options.PingInterval, lifetime.Token).ConfigureAwait(false);

                List<Session> links;

                lock (sessionGate)
                    links = hubLinks.Values.ToList();

                var timeoutMs = (long)options.LanLinkTimeout.TotalMilliseconds;

                foreach (var link in links)
                {
                    if (Environment.TickCount64 - link.LastReceivedTick > timeoutMs)
                    {
                        owner.InternalLogWarning($"Hub link {link.RemoteId} timed out; dropping it.");
                        link.Dispose();
                        OnSessionClosed(link);
                    }
                    else
                    {
                        link.Post(new Envelope { Kind = EnvelopeKind.Ping });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RejectAsync(FramedConnection connection, string reason)
    {
        try
        {
            await connection.SendAsync(new Envelope
            {
                Kind = EnvelopeKind.Reject,
                Network = networkName,
                Payload = Wire.ToPayload(new RejectPayload { Reason = reason }),
            }, lifetime.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            connection.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        List<Session> allSessions;

        lock (sessionGate)
        {
            allSessions = clientSessions.Values.Concat(hubLinks.Values).ToList();
            clientSessions.Clear();
            hubLinks.Clear();
            remotePeerOwners.Clear();
        }

        // Announce the shutdown so clients re-elect immediately instead of waiting for disconnect detection. Each
        // session closes itself once its goodbye has been written, rather than being closed here after a fixed pause:
        // this runs during a plugin unload or a SetActive(false), both of which reach it from the framework thread,
        // where pausing to watch the frames flush would stall the game's frame.
        var goodbye = new Envelope { Kind = EnvelopeKind.HubGoodbye, Origin = owner.SelfId };

        foreach (var session in allSessions)
            session.CloseAfterSending(goodbye);

        lanDiscovery?.Dispose();
        lifetime.Cancel();

        try
        {
            listener?.Stop();
        }
        catch
        {
            // Best effort.
        }

        rendezvousHolder?.Dispose();
        lifetime.Dispose();
        completion.TrySetResult();
    }

    /// <summary>
    /// One connected session: a local client (RemoteId = peer id) or a hub link (RemoteId = remote hub id).
    /// </summary>
    internal sealed class Session : IDisposable
    {
        private readonly HubServer hub;
        private readonly FramedConnection connection;

        public Session(HubServer hub, FramedConnection connection, Guid remoteId, bool isHubLink)
        {
            this.hub = hub;
            this.connection = connection;
            RemoteId = remoteId;
            IsHubLink = isHubLink;
        }

        public Guid RemoteId { get; }

        public bool IsHubLink { get; }

        public long LastReceivedTick => connection.LastReceivedTick;

        public void Post(Envelope envelope)
            => connection.Post(envelope, ex => hub.owner.InternalLog($"Send to {RemoteId} failed: {ex.Message}"));

        /// <summary>
        /// Sends one last frame and closes the session once it has been written, without blocking the caller.
        /// </summary>
        /// <param name="envelope">The farewell frame to send.</param>
        public void CloseAfterSending(Envelope envelope)
            => connection.CloseAfterSending(envelope, ex => hub.owner.InternalLog($"Goodbye to {RemoteId} failed: {ex.Message}"));

        public void RunReadLoop()
            => _ = ReadLoopAsync();

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!hub.lifetime.IsCancellationRequested)
                {
                    var envelope = await connection.ReceiveAsync(hub.lifetime.Token).ConfigureAwait(false);

                    if (envelope == null)
                        break;

                    hub.HandleSessionEnvelope(this, envelope);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Transport failure - treated exactly like a departure below.
            }

            Dispose();
            hub.OnSessionClosed(this);
        }

        public void Dispose()
            => connection.Dispose();
    }
}
