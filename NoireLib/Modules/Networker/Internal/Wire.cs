using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// (<see cref="TypeNameHandling.None"/>); payloads only materialize into locally registered types.<br/>
/// Every operation here goes through a serializer built from <see cref="SerializerSettings"/>, so the protocol
/// is defined entirely by this file.
/// </summary>
internal static class Wire
{
    public const int ProtocolVersion = 1;

    /// <summary>
    /// The single source of truth for how anything on the wire is serialized. TypeNameHandling stays None - never change this.
    /// </summary>
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>
    /// The serializer used for payload conversion. TypeNameHandling stays None - never change this.<br/>
    /// It is built with <see cref="JsonSerializer.Create(JsonSerializerSettings)"/>, which resolves every setting from
    /// <see cref="SerializerSettings"/> alone. The parameterless <see cref="JsonConvert"/> overloads and
    /// <see cref="JsonSerializer.CreateDefault(JsonSerializerSettings)"/> instead merge in
    /// <see cref="JsonConvert.DefaultSettings"/>, a process-global that any other code loaded into this process can
    /// assign, which would let unrelated code reshape the protocol at runtime. Nothing here may use those overloads.
    /// </summary>
    public static readonly JsonSerializer Serializer = JsonSerializer.Create(SerializerSettings);

    /// <summary>
    /// The serializer used for whole documents: a framed envelope, a discovery beacon, a rendezvous record. It matches
    /// <see cref="Serializer"/> and additionally rejects input carrying anything after its top-level value, since each
    /// of those holds exactly one JSON document.<br/>
    /// It is a separate instance because <see cref="JToken.ToObject(Type, JsonSerializer)"/> toggles
    /// <see cref="JsonSerializer.CheckAdditionalContent"/> on the instance it is handed, which would let a payload
    /// conversion transiently relax the check for a concurrent <see cref="DecodeModel{T}"/>.
    /// </summary>
    private static readonly JsonSerializer DocumentSerializer = CreateDocumentSerializer();

    private static JsonSerializer CreateDocumentSerializer()
    {
        var serializer = JsonSerializer.Create(SerializerSettings);
        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    /// <summary>
    /// Serializes a standalone wire model to compact UTF-8 JSON.
    /// </summary>
    public static byte[] EncodeModel(object model)
    {
        var builder = new StringBuilder(256);

        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            DocumentSerializer.Serialize(jsonWriter, model);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Reads a standalone wire model from UTF-8 JSON, returning null when the bytes are malformed.<br/>
    /// Callers are handed bytes they do not control (a socket, a broadcast datagram, a shared mapped file), so a
    /// failure is reported rather than thrown.
    /// </summary>
    public static T? DecodeModel<T>(byte[] bytes) where T : class
    {
        try
        {
            using var stringReader = new StringReader(Encoding.UTF8.GetString(bytes));
            using var jsonReader = new JsonTextReader(stringReader);

            return DocumentSerializer.Deserialize<T>(jsonReader);
        }
        catch
        {
            return null;
        }
    }

    public static byte[] Encode(Envelope envelope)
        => EncodeModel(envelope);

    public static Envelope? Decode(byte[] frame)
        => DecodeModel<Envelope>(frame);

    public static JToken ToPayload(object value)
        => JToken.FromObject(value, Serializer);

    public static T? FromPayload<T>(JToken? payload) where T : class
        => payload?.ToObject<T>(Serializer);

    public static object? FromPayload(JToken? payload, Type type)
        => payload?.ToObject(type, Serializer);
}
