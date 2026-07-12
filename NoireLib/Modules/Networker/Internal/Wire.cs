using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.Networker.Internal;

/// <summary>
/// The kind of a wire envelope.
/// </summary>
internal enum EnvelopeKind
{
    Challenge = 0,
    Hello = 1,
    HelloHub = 2,
    Welcome = 3,
    WelcomeHub = 4,
    Reject = 5,
    Goodbye = 6,
    HubGoodbye = 7,
    PeerState = 8,
    PeerLeft = 9,
    Message = 10,
    Request = 11,
    Response = 12,
    Ping = 13,
    Pong = 14,
    Event = 15,
}

/// <summary>
/// The single JSON wire model. Unknown fields are ignored on read, keeping minor protocol revisions forward-compatible.<br/>
/// For <see cref="EnvelopeKind.PeerState"/>, <see cref="TypeName"/> carries the changed key hint ("m:key" / "f:flag") when applicable.
/// </summary>
internal sealed class Envelope
{
    [JsonProperty("v")]
    public int Version { get; set; } = Wire.ProtocolVersion;

    [JsonProperty("k")]
    public EnvelopeKind Kind { get; set; }

    [JsonProperty("net", NullValueHandling = NullValueHandling.Ignore)]
    public string? Network { get; set; }

    [JsonProperty("org", NullValueHandling = NullValueHandling.Ignore)]
    public Guid? Origin { get; set; }

    [JsonProperty("tgt", NullValueHandling = NullValueHandling.Ignore)]
    public Guid? Target { get; set; }

    [JsonProperty("t", NullValueHandling = NullValueHandling.Ignore)]
    public string? TypeName { get; set; }

    [JsonProperty("p", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? Payload { get; set; }

    [JsonProperty("rq", NullValueHandling = NullValueHandling.Ignore)]
    public Guid? RequestId { get; set; }

    [JsonProperty("e", NullValueHandling = NullValueHandling.Ignore)]
    public string? Error { get; set; }
}

/// <summary>
/// The serialized state of one peer, exchanged in handshakes and presence updates.
/// </summary>
internal sealed class PeerStateModel
{
    [JsonProperty("id")]
    public Guid Id { get; set; }

    [JsonProperty("m")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonProperty("f")]
    public string[] Flags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// True when the peer lives on another machine, relative to the receiver. Rewritten by the forwarding hub.
    /// </summary>
    [JsonProperty("r")]
    public bool Remote { get; set; }
}

internal sealed class ChallengePayload
{
    [JsonProperty("n")]
    public string Nonce { get; set; } = string.Empty;
}

internal sealed class HelloPayload
{
    [JsonProperty("s")]
    public PeerStateModel Self { get; set; } = new();

    [JsonProperty("a", NullValueHandling = NullValueHandling.Ignore)]
    public string? Proof { get; set; }
}

internal sealed class HelloHubPayload
{
    [JsonProperty("h")]
    public Guid HubId { get; set; }

    [JsonProperty("a", NullValueHandling = NullValueHandling.Ignore)]
    public string? Proof { get; set; }

    [JsonProperty("ps")]
    public PeerStateModel[] Peers { get; set; } = Array.Empty<PeerStateModel>();
}

internal sealed class WelcomePayload
{
    [JsonProperty("h")]
    public Guid HubPeerId { get; set; }

    [JsonProperty("ps")]
    public PeerStateModel[] Peers { get; set; } = Array.Empty<PeerStateModel>();
}

internal sealed class WelcomeHubPayload
{
    [JsonProperty("h")]
    public Guid HubId { get; set; }

    [JsonProperty("ps")]
    public PeerStateModel[] Peers { get; set; } = Array.Empty<PeerStateModel>();
}

internal sealed class RejectPayload
{
    [JsonProperty("r")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class BeaconModel
{
    [JsonProperty("nh")]
    public string NetworkHash { get; set; } = string.Empty;

    [JsonProperty("h")]
    public Guid HubId { get; set; }

    [JsonProperty("p")]
    public int Port { get; set; }
}

/// <summary>
/// Wire-level serialization. Deserialization is never type-driven by payload content
/// (<see cref="TypeNameHandling.None"/>); payloads only materialize into locally registered types.
/// </summary>
internal static class Wire
{
    public const int ProtocolVersion = 1;

    /// <summary>
    /// The serializer used for envelopes and payload conversion. TypeNameHandling stays None — never change this.
    /// </summary>
    public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None,
        NullValueHandling = NullValueHandling.Ignore,
    });

    public static byte[] Encode(Envelope envelope)
        => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));

    public static Envelope? Decode(byte[] frame)
    {
        try
        {
            return JsonConvert.DeserializeObject<Envelope>(Encoding.UTF8.GetString(frame));
        }
        catch
        {
            return null;
        }
    }

    public static JToken ToPayload(object value)
        => JToken.FromObject(value, Serializer);

    public static T? FromPayload<T>(JToken? payload) where T : class
        => payload?.ToObject<T>(Serializer);

    public static object? FromPayload(JToken? payload, Type type)
        => payload?.ToObject(type, Serializer);
}
