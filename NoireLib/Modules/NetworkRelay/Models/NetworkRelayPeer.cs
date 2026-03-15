using System;
using System.Net;

namespace NoireLib.NetworkRelay;

/// <summary>
/// Represents a peer known by the relay.
/// </summary>
/// <param name="PeerId">The unique identifier of the peer.</param>
/// <param name="DisplayName">The friendly display name of the peer.</param>
/// <param name="EndPoint">The network endpoint associated with the peer.</param>
/// <param name="ReliableEndPoint">The optional reliable TCP endpoint associated with the peer.</param>
/// <param name="LastSeenUtc">The UTC timestamp at which the peer was last seen.</param>
/// <param name="IsDynamic"><see langword="true"/> if the peer was discovered dynamically; otherwise, <see langword="false"/>.</param>
public sealed record NetworkRelayPeer(
    string PeerId,
    string DisplayName,
    IPEndPoint EndPoint,
    IPEndPoint? ReliableEndPoint,
    DateTimeOffset LastSeenUtc,
    bool IsDynamic);
