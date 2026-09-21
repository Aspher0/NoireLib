using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace NoireLib.Remote;

/// <summary>
/// What a frame on the API socket carries.
/// </summary>
public enum NoireRemoteFrameKind
{
    /// <summary>Caller to host: invoke a member.</summary>
    Call,

    /// <summary>Host to caller: the answer to one call.</summary>
    Result,

    /// <summary>Caller to host: start receiving a topic.</summary>
    Subscribe,

    /// <summary>Caller to host: stop receiving a topic.</summary>
    Unsubscribe,

    /// <summary>Host to caller: a published event.</summary>
    Event,

    /// <summary>Host to caller: a job's progress.</summary>
    Progress,

    /// <summary>Either way: a liveness probe.</summary>
    Ping,

    /// <summary>Either way: the answer to a liveness probe.</summary>
    Pong,
}

/// <summary>
/// One frame on the API socket at <c>/noire/ws/_api</c>. It is the HTTP envelope plus a correlation id and a kind,
/// so a program that has read the HTTP contract already knows this one.
/// </summary>
public sealed class NoireRemoteFrame
{
    /// <summary>Gets or sets what the frame is.</summary>
    // Written lowercase, as the contract spells it.
    [JsonProperty("kind")]
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter), typeof(Newtonsoft.Json.Serialization.CamelCaseNamingStrategy))]
    public NoireRemoteFrameKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the id correlating a call with its result. Zero on anything the host sends unasked.
    /// </summary>
    [JsonProperty("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the published surface the frame addresses.
    /// </summary>
    [JsonProperty("api", NullValueHandling = NullValueHandling.Ignore)]
    public string? Api { get; set; }

    /// <summary>
    /// Gets or sets the member being called, or the topic on an event, a subscription or an unsubscription.
    /// </summary>
    [JsonProperty("member", NullValueHandling = NullValueHandling.Ignore)]
    public string? Member { get; set; }

    /// <summary>
    /// Gets or sets the arguments of a call, the value of a result, or the arguments of an event.
    /// </summary>
    [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? Payload { get; set; }

    /// <summary>
    /// Gets or sets why a call failed, in the shape the HTTP envelope uses.
    /// </summary>
    [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
    public NoireRemoteError? Error { get; set; }

    /// <summary>
    /// Gets or sets the job id, on the result that starts a job and on every progress frame that follows.
    /// </summary>
    [JsonProperty("job", NullValueHandling = NullValueHandling.Ignore)]
    public string? Job { get; set; }

    /// <summary>
    /// Gets or sets how far a job has got, from zero to one, or null on a frame that is not about progress.
    /// </summary>
    [JsonProperty("percent", NullValueHandling = NullValueHandling.Ignore)]
    public double? Percent { get; set; }

    /// <summary>
    /// Gets or sets what a job is doing, when it says.
    /// </summary>
    [JsonProperty("stage", NullValueHandling = NullValueHandling.Ignore)]
    public string? Stage { get; set; }

    /// <summary>
    /// Reads a frame off the wire.
    /// </summary>
    /// <param name="utf8">The frame's bytes.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="NoireRemoteProtocolException">If the bytes are not a frame this contract describes.</exception>
    public static NoireRemoteFrame Parse(ReadOnlySpan<byte> utf8)
        => Parse(Encoding.UTF8.GetString(utf8));

    /// <summary>
    /// Reads a frame off the wire.
    /// </summary>
    /// <param name="text">The frame's text.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="NoireRemoteProtocolException">If the text is not a frame this contract describes.</exception>
    public static NoireRemoteFrame Parse(string text)
    {
        JObject document;

        try
        {
            document = JObject.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new NoireRemoteProtocolException(NoireRemoteErrorCodes.FrameMalformed, "A socket frame has to be one JSON object.", exception);
        }

        var kindToken = document["kind"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(kindToken) || !Enum.TryParse<NoireRemoteFrameKind>(kindToken, ignoreCase: true, out var kind))
            throw new NoireRemoteProtocolException(NoireRemoteErrorCodes.UnknownFrameKind, "'" + kindToken + "' is not a frame kind this listener serves.");

        return new NoireRemoteFrame
        {
            Kind = kind,
            Id = document["id"]?.Value<long>() ?? 0,
            Api = document["api"]?.Value<string>(),
            Member = document["member"]?.Value<string>(),
            Payload = document["payload"],
            Error = document["error"]?.ToObject<NoireRemoteError>(),
            Job = document["job"]?.Value<string>(),
            Percent = document["percent"]?.Value<double>(),
            Stage = document["stage"]?.Value<string>(),
        };
    }

    /// <summary>
    /// Writes the frame the way the wire carries it.
    /// </summary>
    /// <returns>The frame's text.</returns>
    public string Write() => NoireRemoteJson.Write(this);

    /// <summary>
    /// Writes the frame the way the wire carries it.
    /// </summary>
    /// <returns>The frame's bytes.</returns>
    public byte[] ToUtf8() => Encoding.UTF8.GetBytes(Write());
}
