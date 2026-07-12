using System;

namespace NoireLib.Networker;

/// <summary>
/// Thrown when a pending request fails because the target peer left the network before answering.
/// </summary>
public sealed class PeerLeftException : Exception
{
    /// <summary>
    /// Creates a new <see cref="PeerLeftException"/>.
    /// </summary>
    /// <param name="peerId">The identifier of the peer that left.</param>
    public PeerLeftException(Guid peerId)
        : base($"Peer {peerId} left the network before answering the request.")
    {
        PeerId = peerId;
    }

    /// <summary>
    /// The identifier of the peer that left.
    /// </summary>
    public Guid PeerId { get; }
}
