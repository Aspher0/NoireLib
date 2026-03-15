using System;
using System.Net;

namespace NoireLib.NetworkRelay;

internal sealed class RelayPeerRegistration(string peerId, string displayName, IPEndPoint endPoint, DateTimeOffset lastSeenUtc, bool isDynamic)
{
    public string PeerId { get; } = peerId;
    public string DisplayName { get; set; } = displayName;
    public IPEndPoint EndPoint { get; set; } = endPoint;
    public IPEndPoint? ReliableEndPoint { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; } = lastSeenUtc;
    public bool IsDynamic { get; set; } = isDynamic;

    public NetworkRelayPeer ToModel()
        => new(PeerId, DisplayName, EndPoint, ReliableEndPoint, LastSeenUtc, IsDynamic);
}
