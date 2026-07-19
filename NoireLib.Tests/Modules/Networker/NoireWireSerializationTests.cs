using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Networker.Internal;
using System;
using System.Runtime.Versioning;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the wire-format invariants of the Networker's envelope serializer. The Networker deserializes bytes arriving
/// from a LAN socket, so these hold regardless of what any other code loaded into the same process does:<br/>
/// the protocol is defined by the serializer settings declared in Wire alone, type information is never resolved out
/// of payload content, and a malformed frame is rejected rather than thrown.
/// </summary>
[Collection(JsonDefaultSettingsCollection.Name)]
[SupportedOSPlatform("windows")]
public class NoireWireSerializationTests
{
    private static Envelope CreateEnvelope()
        => new()
        {
            Version = Wire.ProtocolVersion,
            Kind = EnvelopeKind.Request,
            Network = "NoireLib.Tests.Network",
            Origin = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Target = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            TypeName = "NoireLib.Tests.SomeMessage",
            Payload = new JObject { ["text"] = "payload", ["value"] = 42 },
            RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Error = "some error",
        };

    [Fact]
    public void Serializer_Pins_TypeNameHandling_To_None()
    {
        Wire.Serializer.TypeNameHandling.Should().Be(TypeNameHandling.None,
            "resolving types named by payload content is a deserialization-gadget risk on an untrusted socket");
    }

    [Fact]
    public void Encode_Produces_Identical_Bytes_Whatever_JsonConvert_DefaultSettings_Says()
    {
        var envelope = CreateEnvelope();

        var baseline = Encoding.UTF8.GetString(Wire.Encode(envelope));
        var underHostileDefaults = Encoding.UTF8.GetString(HostileJsonDefaults.While(() => Wire.Encode(envelope)));

        underHostileDefaults.Should().Be(baseline,
            "the wire format is defined by the serializer settings in Wire alone, never by a process-global");
    }

    [Fact]
    public void Encode_Never_Writes_Type_Or_Reference_Metadata_Under_Hostile_DefaultSettings()
    {
        var json = Encoding.UTF8.GetString(HostileJsonDefaults.While(() => Wire.Encode(CreateEnvelope())));

        json.Should().NotContain("$type", "type information must never reach the wire");
        json.Should().NotContain("$id", "reference metadata must never reach the wire");
        json.Should().NotContain("$ref", "reference metadata must never reach the wire");
    }

    [Fact]
    public void Round_Trip_Under_Hostile_DefaultSettings_Still_Yields_The_Original_Envelope()
    {
        var envelope = CreateEnvelope();

        var decoded = HostileJsonDefaults.While(() => Wire.Decode(Wire.Encode(envelope)));

        decoded.Should().NotBeNull();
        decoded!.Kind.Should().Be(envelope.Kind);
        decoded.Origin.Should().Be(envelope.Origin);
        JToken.DeepEquals(decoded.Payload, envelope.Payload).Should().BeTrue();
    }

    [Fact]
    public void Round_Trip_Preserves_Every_Envelope_Field()
    {
        var envelope = CreateEnvelope();

        var decoded = Wire.Decode(Wire.Encode(envelope));

        decoded.Should().NotBeNull();
        decoded!.Version.Should().Be(envelope.Version);
        decoded.Kind.Should().Be(envelope.Kind);
        decoded.Network.Should().Be(envelope.Network);
        decoded.Origin.Should().Be(envelope.Origin);
        decoded.Target.Should().Be(envelope.Target);
        decoded.TypeName.Should().Be(envelope.TypeName);
        decoded.RequestId.Should().Be(envelope.RequestId);
        decoded.Error.Should().Be(envelope.Error);
        JToken.DeepEquals(decoded.Payload, envelope.Payload).Should().BeTrue("the payload must survive the round trip verbatim");
    }

    [Fact]
    public void Round_Trip_Omits_Absent_Optional_Fields()
    {
        var envelope = new Envelope { Kind = EnvelopeKind.Ping };

        var frame = Wire.Encode(envelope);
        var decoded = Wire.Decode(frame);

        Encoding.UTF8.GetString(frame).Should().NotContain("null", "unset optional fields are dropped rather than written as null");
        decoded.Should().NotBeNull();
        decoded!.Kind.Should().Be(EnvelopeKind.Ping);
        decoded.Network.Should().BeNull();
        decoded.Origin.Should().BeNull();
        decoded.Payload.Should().BeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{\"v\":1,\"k\":")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"v\":1,\"k\":\"not-a-kind\"}")]
    [InlineData("{\"v\":1,\"k\":10}trailing garbage")]
    [InlineData("{\"v\":1,\"k\":10}{\"v\":1,\"k\":11}")]
    public void Decode_Returns_Null_On_Malformed_Input(string text)
    {
        Wire.Decode(Encoding.UTF8.GetBytes(text)).Should().BeNull(
            "a frame off the socket is untrusted input, so Decode reports failure rather than throwing");
    }

    [Fact]
    public void Decode_Returns_Null_On_Arbitrary_Bytes()
    {
        var garbage = new byte[] { 0xFF, 0xFE, 0x00, 0x13, 0x37, 0xDE, 0xAD, 0xBE, 0xEF };

        Wire.Decode(garbage).Should().BeNull();
    }

    [Fact]
    public void Decode_Leaves_A_Payload_Type_Hint_As_Inert_Data()
    {
        var frame = Encoding.UTF8.GetBytes(
            "{\"v\":1,\"k\":10,\"t\":\"Some.Message\",\"p\":{\"$type\":\"System.Diagnostics.Process, System.Diagnostics.Process\",\"text\":\"x\"}}");

        var decoded = Wire.Decode(frame);

        decoded.Should().NotBeNull();

        var payload = decoded!.Payload as JObject;

        payload.Should().NotBeNull("a payload stays an unmaterialized token until a locally known type asks for it");
        payload!.ContainsKey("$type").Should().BeTrue("a type hint from a peer is carried as ordinary data, never acted on");
    }
}
