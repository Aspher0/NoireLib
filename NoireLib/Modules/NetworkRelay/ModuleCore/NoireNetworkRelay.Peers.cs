using Dalamud.Utility;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace NoireLib.NetworkRelay;

public partial class NoireNetworkRelay
{
    /// <summary>
    /// Registers or updates the local relay instance using the current relay configuration.
    /// </summary>
    /// <param name="peerId">Optional peer identifier for the local relay instance. If not provided, the existing instance ID will be used, otherwise <see cref="InstanceId"/> will be updated.</param>
    /// <param name="displayName">Optional friendly display name for the local relay instance. If provided, <see cref="DisplayName"/> will be updated.</param>
    /// <param name="activateSelf">Whether the local relay instance should also be marked active for self announcements. Null won't change the current state.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay RegisterSelf(string? peerId = null, string? displayName = null, bool? activateSelf = null)
    {
        var previousInstanceId = InstanceId;

        if (!displayName.IsNullOrWhitespace())
            SetDisplayName(displayName);

        var endPoint = new IPEndPoint(BindAddress, Port);
        var reliableEndPoint = EnableReliableTransport
            ? new IPEndPoint(BindAddress, ReliablePort)
            : null;

        if (!peerId.IsNullOrWhitespace())
            SetInstanceId(peerId);

        Volatile.Write(ref selfRegistrationEnabled, 1);

        if (activateSelf != null)
            Volatile.Write(ref selfActivityEnabled, activateSelf.Value ? 1 : 0);

        if (!string.Equals(previousInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase))
            UnregisterPeer(previousInstanceId);

        UpsertPeer(InstanceId, DisplayName, endPoint, reliableEndPoint, isDynamic: false);

        if (activateSelf == true && IsActive && EnablePeerDiscovery)
            SendPresenceAnnouncement(CreateHelloEnvelope());

        return this;
    }

    /// <summary>
    /// Marks the local relay instance active for self announcements.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay ActivateSelf()
    {
        if (!IsSelfRegistered)
            throw new InvalidOperationException("RegisterSelf() must be called before activating self.");

        Volatile.Write(ref selfActivityEnabled, 1);

        if (IsActive)
        {
            RegisterSelf();

            if (EnablePeerDiscovery)
                SendPresenceAnnouncement(CreateHelloEnvelope());
        }

        return this;
    }

    /// <summary>
    /// Marks the local relay instance inactive for self announcements.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay DeactivateSelf()
    {
        Volatile.Write(ref selfActivityEnabled, 0);
        return this;
    }

    /// <summary>
    /// Registers or updates a known peer using a hostname or IP address.
    /// </summary>
    /// <param name="peerId">The peer identifier.</param>
    /// <param name="hostOrAddress">The hostname or IP address of the peer.</param>
    /// <param name="port">The peer UDP port.</param>
    /// <param name="displayName">Optional friendly display name for the peer.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay RegisterPeer(string peerId, string hostOrAddress, int port, string? displayName = null)
    {
        var reliablePort = EnableReliableTransport ? port : (int?)null;
        return RegisterPeer(peerId, hostOrAddress, port, reliablePort, displayName);
    }

    /// <summary>
    /// Registers or updates a known peer using a hostname or IP address with an explicit reliable TCP port.
    /// </summary>
    /// <param name="peerId">The peer identifier.</param>
    /// <param name="hostOrAddress">The hostname or IP address of the peer.</param>
    /// <param name="port">The peer UDP port.</param>
    /// <param name="reliablePort">The peer TCP port used for reliable delivery.</param>
    /// <param name="displayName">Optional friendly display name for the peer.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay RegisterPeer(string peerId, string hostOrAddress, int port, int? reliablePort, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(peerId))
            throw new ArgumentException("Peer ID cannot be empty.", nameof(peerId));

        if (string.IsNullOrWhiteSpace(hostOrAddress))
            throw new ArgumentException("Host cannot be empty.", nameof(hostOrAddress));

        var address = ResolveAddress(hostOrAddress.Trim(), BindAddress.AddressFamily);
        var endPoint = new IPEndPoint(address, ValidatePort(port));
        var reliableEndPoint = reliablePort.HasValue ? new IPEndPoint(address, ValidatePort(reliablePort.Value)) : null;
        return RegisterPeer(peerId, endPoint, reliableEndPoint, displayName);
    }

    /// <summary>
    /// Registers or updates a known peer using an explicit endpoint.
    /// </summary>
    /// <param name="peerId">The peer identifier.</param>
    /// <param name="endPoint">The peer endpoint to register.</param>
    /// <param name="displayName">Optional friendly display name for the peer.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay RegisterPeer(string peerId, IPEndPoint endPoint, string? displayName = null)
    {
        var reliableEndPoint = EnableReliableTransport
            ? new IPEndPoint(endPoint.Address, endPoint.Port)
            : null;

        return RegisterPeer(peerId, endPoint, reliableEndPoint, displayName);
    }

    /// <summary>
    /// Registers or updates a known peer using explicit UDP and TCP endpoints.
    /// </summary>
    /// <param name="peerId">The peer identifier.</param>
    /// <param name="endPoint">The peer UDP endpoint to register.</param>
    /// <param name="reliableEndPoint">The optional peer TCP endpoint used for reliable delivery.</param>
    /// <param name="displayName">Optional friendly display name for the peer.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay RegisterPeer(string peerId, IPEndPoint endPoint, IPEndPoint? reliableEndPoint, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(peerId))
            throw new ArgumentException("Peer ID cannot be empty.", nameof(peerId));

        if (endPoint == null)
            throw new ArgumentNullException(nameof(endPoint));

        UpsertPeer(peerId.Trim(), displayName?.Trim(), endPoint, reliableEndPoint, isDynamic: false);
        return this;
    }

    /// <summary>
    /// Removes the local relay instance from the peer list.
    /// </summary>
    /// <returns><see langword="true"/> if the local relay instance was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnregisterSelf()
    {
        Volatile.Write(ref selfActivityEnabled, 0);
        Volatile.Write(ref selfRegistrationEnabled, 0);
        return UnregisterPeer(InstanceId);
    }

    /// <summary>
    /// Removes a registered peer.
    /// </summary>
    /// <param name="peerId">The peer identifier to remove.</param>
    /// <returns><see langword="true"/> if the peer was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnregisterPeer(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId))
            return false;

        NetworkRelayPeer? removedPeer = null;

        lock (peerLock)
        {
            if (!peers.Remove(peerId.Trim(), out var peer))
                return false;

            removedPeer = peer.ToModel();
        }

        Interlocked.Increment(ref totalPeersRemoved);
        OnPeerRemoved(removedPeer, false);
        return true;
    }

    /// <summary>
    /// Removes all tracked peers.
    /// </summary>
    /// <param name="includeStaticPeers">Whether manually registered peers should also be removed.</param>
    /// <returns>The number of peers removed.</returns>
    public int ClearPeers(bool includeStaticPeers = true)
    {
        List<NetworkRelayPeer> removedPeers = [];

        lock (peerLock)
        {
            foreach (var peer in peers.Values.Where(peer => includeStaticPeers || peer.IsDynamic).ToList())
            {
                removedPeers.Add(peer.ToModel());
                peers.Remove(peer.PeerId);
            }
        }

        if (removedPeers.Count == 0)
            return 0;

        Interlocked.Add(ref totalPeersRemoved, removedPeers.Count);

        foreach (var peer in removedPeers)
            OnPeerRemoved(peer, peer.IsDynamic);

        return removedPeers.Count;
    }

    /// <summary>
    /// Gets all currently tracked peers.
    /// </summary>
    /// <returns>A snapshot of all currently tracked peers.</returns>
    public IReadOnlyList<NetworkRelayPeer> GetPeers()
    {
        SweepExpiredPeers();

        lock (peerLock)
            return peers.Values
                .OrderBy(peer => peer.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(peer => peer.PeerId, StringComparer.OrdinalIgnoreCase)
                .Select(peer => peer.ToModel())
                .ToList();
    }

    /// <summary>
    /// Tries to get a tracked peer.
    /// </summary>
    /// <param name="peerId">The peer identifier to look up.</param>
    /// <param name="peer">When this method returns, contains the peer if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the peer was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetPeer(string peerId, out NetworkRelayPeer? peer)
    {
        peer = null;

        if (string.IsNullOrWhiteSpace(peerId))
            return false;

        lock (peerLock)
        {
            if (!peers.TryGetValue(peerId.Trim(), out var registration))
                return false;

            peer = registration.ToModel();
            return true;
        }
    }

    /// <summary>
    /// Broadcasts a presence announcement for discovery.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay AnnouncePresence()
    {
        if (!IsSelfRegistered || !IsSelfActive)
            return this;

        EnsureCanSend();

        SendPresenceAnnouncement(CreateHelloEnvelope());

        RegisterSelf();

        return this;
    }

    private void HandlePeerActivity(RelayEnvelope envelope, IPEndPoint remoteEndPoint, NetworkRelayTransportKind transportKind)
    {
        if (string.Equals(envelope.SenderId, InstanceId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!AutoRegisterPeers && !TryGetPeerEndpoint(envelope.SenderId, out _))
            return;

        TryGetPeer(envelope.SenderId, out var existingPeer);

        var udpPort = transportKind == NetworkRelayTransportKind.Udp
            ? remoteEndPoint.Port
            : existingPeer?.EndPoint.Port ?? Port;
        int? reliablePort = envelope.SenderReliablePort ?? existingPeer?.ReliableEndPoint?.Port;

        if (envelope.Payload is { Type: not JTokenType.Null and not JTokenType.None })
        {
            try
            {
                var hello = envelope.Payload.ToObject<RelayHelloPayload>(CreateJsonSerializer());
                if (hello is { Port: > 0 })
                    udpPort = hello.Port;

                if (hello is { ReliableTransportEnabled: true, ReliablePort: > 0 })
                    reliablePort = hello.ReliablePort;
                else if (hello is { ReliableTransportEnabled: false })
                    reliablePort = null;
            }
            catch
            {
            }
        }

        var peer = UpsertPeer(
            envelope.SenderId,
            string.IsNullOrWhiteSpace(envelope.SenderDisplayName) ? envelope.SenderId : envelope.SenderDisplayName,
            new IPEndPoint(remoteEndPoint.Address, udpPort),
            reliablePort.HasValue ? new IPEndPoint(remoteEndPoint.Address, reliablePort.Value) : existingPeer?.ReliableEndPoint,
            isDynamic: true);

        if (EnableLogging)
            NoireLogger.LogVerbose(this, $"Peer seen: {peer.DisplayName} ({peer.EndPoint}) Reliable: {peer.ReliableEndPoint}.");
    }

    private NetworkRelayPeer UpsertPeer(string peerId, string? displayName, IPEndPoint endPoint, IPEndPoint? reliableEndPoint, bool isDynamic)
    {
        var isNewPeer = false;
        NetworkRelayPeer peerModel;

        lock (peerLock)
        {
            if (!peers.TryGetValue(peerId, out var registration))
            {
                registration = new RelayPeerRegistration(peerId, displayName ?? peerId, endPoint, DateTimeOffset.UtcNow, isDynamic)
                {
                    ReliableEndPoint = reliableEndPoint,
                };
                peers[peerId] = registration;
                isNewPeer = true;
            }
            else
            {
                registration.DisplayName = string.IsNullOrWhiteSpace(displayName) ? registration.DisplayName : displayName!;
                registration.EndPoint = endPoint;
                registration.ReliableEndPoint = reliableEndPoint ?? registration.ReliableEndPoint;
                registration.LastSeenUtc = DateTimeOffset.UtcNow;
                registration.IsDynamic = isDynamic && registration.IsDynamic;
            }

            peerModel = registration.ToModel();
        }

        if (isNewPeer)
            Interlocked.Increment(ref totalPeersRegistered);

        OnPeerSeen(peerModel, isNewPeer);
        return peerModel;
    }

    private void SweepExpiredPeers()
    {
        if (PeerExpiration == TimeSpan.Zero)
            return;

        List<NetworkRelayPeer> expiredPeers = [];
        var cutoff = DateTimeOffset.UtcNow - PeerExpiration;

        lock (peerLock)
        {
            foreach (var peer in peers.Values.Where(peer => peer.IsDynamic && peer.LastSeenUtc < cutoff).ToList())
            {
                expiredPeers.Add(peer.ToModel());
                peers.Remove(peer.PeerId);
            }
        }

        if (expiredPeers.Count == 0)
            return;

        Interlocked.Add(ref totalPeersRemoved, expiredPeers.Count);

        foreach (var peer in expiredPeers)
            OnPeerRemoved(peer, true);
    }

    private bool IsPeerAllowed(string peerId)
    {
        lock (peerLock)
            return allowedPeerIds.Count == 0 || allowedPeerIds.Contains(peerId);
    }

    private bool TryGetPeerEndpoint(string peerId, out IPEndPoint endPoint)
    {
        lock (peerLock)
        {
            if (peers.TryGetValue(peerId, out var peer))
            {
                endPoint = peer.EndPoint;
                return true;
            }
        }

        endPoint = null!;
        return false;
    }

    private bool TryGetReliablePeerEndpoint(string peerId, out IPEndPoint endPoint)
    {
        lock (peerLock)
        {
            if (peers.TryGetValue(peerId, out var peer) && peer.ReliableEndPoint != null)
            {
                endPoint = peer.ReliableEndPoint;
                return true;
            }
        }

        endPoint = null!;
        return false;
    }

    private IReadOnlyList<IPEndPoint> GetKnownPeerAnnouncementEndpoints()
    {
        lock (peerLock)
            return peers.Values
                .Where(peer => !string.Equals(peer.PeerId, InstanceId, StringComparison.OrdinalIgnoreCase))
                .Select(peer => new IPEndPoint(peer.EndPoint.Address, peer.EndPoint.Port))
                .DistinctBy(endPoint => endPoint.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private void SendPresenceAnnouncement(RelayEnvelope envelope)
    {
        if (EnableBroadcast)
            SendHelloEnvelope(envelope, GetBroadcastEndPoint());

        foreach (var endPoint in GetKnownPeerAnnouncementEndpoints())
            SendHelloEnvelope(envelope, endPoint);
    }

    private IPEndPoint GetBroadcastEndPoint()
    {
        if (!EnableBroadcast)
            throw new InvalidOperationException("UDP broadcast is disabled for this relay.");

        if (BindAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new NotSupportedException("UDP broadcast is only supported with IPv4 bind addresses.");

        return new IPEndPoint(IPAddress.Broadcast, Port);
    }

    private RelayEnvelope CreateHelloEnvelope()
        => new()
        {
            Kind = EnvelopeKindHello,
            Channel = SystemChannel,
            MessageId = Guid.NewGuid().ToString("N"),
            SenderId = InstanceId,
            SenderDisplayName = DisplayName,
            SenderReliablePort = EnableReliableTransport ? ReliablePort : null,
            MessageType = typeof(RelayHelloPayload).FullName ?? nameof(RelayHelloPayload),
            SentAtUtc = DateTimeOffset.UtcNow,
            Payload = CreatePayloadToken(new RelayHelloPayload(Port, EnableReliableTransport ? ReliablePort : null, EnableReliableTransport)),
        };

    private void OnPeerSeen(NetworkRelayPeer peer, bool isNewPeer)
    {
        try
        {
            PeerSeen?.Invoke(peer);
            PublishIntegrationEvent(new NetworkRelayPeerSeenEvent(peer, isNewPeer));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalDispatchExceptionsCaught);
            HandleException(ex, "raising PeerSeen event");
        }
    }

    private void OnPeerRemoved(NetworkRelayPeer peer, bool expired)
    {
        try
        {
            PeerRemoved?.Invoke(peer);
            PublishIntegrationEvent(new NetworkRelayPeerRemovedEvent(peer, expired));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalDispatchExceptionsCaught);
            HandleException(ex, "raising PeerRemoved event");
        }
    }
}
