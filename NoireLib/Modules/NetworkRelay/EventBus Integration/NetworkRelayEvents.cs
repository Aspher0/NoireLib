using System;

namespace NoireLib.NetworkRelay;

/// <summary>
/// Represents an error raised by the relay.
/// </summary>
/// <param name="Operation">The operation during which the error occurred.</param>
/// <param name="Exception">The exception that was observed.</param>
/// <param name="TimestampUtc">The UTC timestamp at which the error was captured.</param>
public sealed record NetworkRelayError(string Operation, Exception Exception, DateTimeOffset TimestampUtc);

/// <summary>
/// EventBus integration event raised when a relay message is received.
/// </summary>
/// <param name="Message">The received relay message.</param>
public sealed record NetworkRelayMessageReceivedEvent(NetworkRelayMessage Message);

/// <summary>
/// EventBus integration event raised when a peer is seen or refreshed.
/// </summary>
/// <param name="Peer">The peer that was seen or refreshed.</param>
/// <param name="IsNewPeer"><see langword="true"/> if the peer was newly registered; otherwise, <see langword="false"/>.</param>
public sealed record NetworkRelayPeerSeenEvent(NetworkRelayPeer Peer, bool IsNewPeer);

/// <summary>
/// EventBus integration event raised when a peer is removed.
/// </summary>
/// <param name="Peer">The peer that was removed.</param>
/// <param name="Expired"><see langword="true"/> if the peer was removed because it expired; otherwise, <see langword="false"/>.</param>
public sealed record NetworkRelayPeerRemovedEvent(NetworkRelayPeer Peer, bool Expired);

/// <summary>
/// EventBus integration event raised when the relay observes an error.
/// </summary>
/// <param name="Error">The relay error that was observed.</param>
public sealed record NetworkRelayErrorEvent(NetworkRelayError Error);
