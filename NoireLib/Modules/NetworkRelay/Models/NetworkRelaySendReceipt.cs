using System;
using System.Net;

namespace NoireLib.NetworkRelay;

/// <summary>
/// Represents the successful completion of an awaitable reliable relay send.
/// </summary>
/// <param name="MessageId">The unique relay message identifier that was sent.</param>
/// <param name="Channel">The logical relay channel used for delivery.</param>
/// <param name="TargetPeerId">The optional target peer identifier associated with the send.</param>
/// <param name="RemoteEndPoint">The remote endpoint the reliable payload was sent to.</param>
/// <param name="SentAtUtc">The UTC timestamp at which the relay message was created.</param>
/// <param name="AcknowledgedAtUtc">The UTC timestamp at which the acknowledgement was produced by the receiver.</param>
public sealed record NetworkRelaySendReceipt(
    string MessageId,
    string Channel,
    string? TargetPeerId,
    IPEndPoint RemoteEndPoint,
    DateTimeOffset SentAtUtc,
    DateTimeOffset AcknowledgedAtUtc);
