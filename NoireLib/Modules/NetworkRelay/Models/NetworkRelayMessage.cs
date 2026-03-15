using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;

namespace NoireLib.NetworkRelay;

/// <summary>
/// Represents a relay message after transport metadata and payload have been parsed.
/// </summary>
/// <param name="MessageId">The unique relay message identifier.</param>
/// <param name="Channel">The logical relay channel used for delivery.</param>
/// <param name="SenderId">The unique identifier of the sender.</param>
/// <param name="SenderDisplayName">The optional friendly display name of the sender.</param>
/// <param name="MessageType">The serialized payload type name if available.</param>
/// <param name="Payload">The raw payload content.</param>
/// <param name="SentAtUtc">The UTC timestamp at which the message was sent.</param>
/// <param name="RemoteEndPoint">The remote endpoint from which the message was received.</param>
/// <param name="TargetPeerId">The optional single target peer identifier.</param>
/// <param name="TargetPeerIds">The optional collection of target peer identifiers.</param>
/// <param name="TransportKind">The transport used to deliver the message.</param>
public sealed record NetworkRelayMessage(
    string MessageId,
    string Channel,
    string SenderId,
    string? SenderDisplayName,
    string? MessageType,
    JToken Payload,
    DateTimeOffset SentAtUtc,
    IPEndPoint RemoteEndPoint,
    string? TargetPeerId,
    IReadOnlyCollection<string> TargetPeerIds,
    NetworkRelayTransportKind TransportKind)
{
    /// <summary>
    /// Gets a value indicating whether the message used the reliable transport path.
    /// </summary>
    public bool IsReliable => TransportKind == NetworkRelayTransportKind.Tcp;

    /// <summary>
    /// Deserializes the payload to the requested type.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize to.</typeparam>
    /// <param name="serializerSettings">Optional serializer settings to use during deserialization.</param>
    /// <returns>The deserialized payload.</returns>
    public TPayload GetPayload<TPayload>(JsonSerializerSettings? serializerSettings = null)
    {
        var payload = Payload.ToObject<TPayload>(JsonSerializer.CreateDefault(serializerSettings));
        if (payload == null)
            throw new InvalidOperationException($"Unable to deserialize relay payload to {typeof(TPayload).Name}.");

        return payload;
    }

    /// <summary>
    /// Converts the message to a strongly typed relay message.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize to.</typeparam>
    /// <param name="serializerSettings">Optional serializer settings to use during deserialization.</param>
    /// <returns>A strongly typed relay message instance.</returns>
    public NetworkRelayMessage<TPayload> ToTyped<TPayload>(JsonSerializerSettings? serializerSettings = null)
        => new(
            MessageId,
            Channel,
            SenderId,
            SenderDisplayName,
            MessageType,
            GetPayload<TPayload>(serializerSettings),
            SentAtUtc,
            RemoteEndPoint,
            TargetPeerId,
            TargetPeerIds,
            TransportKind);
}

/// <summary>
/// Represents a strongly typed relay message.
/// </summary>
/// <typeparam name="TPayload">The strongly typed payload type.</typeparam>
/// <param name="MessageId">The unique relay message identifier.</param>
/// <param name="Channel">The logical relay channel used for delivery.</param>
/// <param name="SenderId">The unique identifier of the sender.</param>
/// <param name="SenderDisplayName">The optional friendly display name of the sender.</param>
/// <param name="MessageType">The serialized payload type name if available.</param>
/// <param name="Payload">The strongly typed payload value.</param>
/// <param name="SentAtUtc">The UTC timestamp at which the message was sent.</param>
/// <param name="RemoteEndPoint">The remote endpoint from which the message was received.</param>
/// <param name="TargetPeerId">The optional single target peer identifier.</param>
/// <param name="TargetPeerIds">The optional collection of target peer identifiers.</param>
/// <param name="TransportKind">The transport used to deliver the message.</param>
public sealed record NetworkRelayMessage<TPayload>(
    string MessageId,
    string Channel,
    string SenderId,
    string? SenderDisplayName,
    string? MessageType,
    TPayload Payload,
    DateTimeOffset SentAtUtc,
    IPEndPoint RemoteEndPoint,
    string? TargetPeerId,
    IReadOnlyCollection<string> TargetPeerIds,
    NetworkRelayTransportKind TransportKind)
{
    /// <summary>
    /// Gets a value indicating whether the message used the reliable transport path.
    /// </summary>
    public bool IsReliable => TransportKind == NetworkRelayTransportKind.Tcp;
}
