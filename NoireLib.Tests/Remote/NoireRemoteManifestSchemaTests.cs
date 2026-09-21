using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The document every generated artefact reads. One test per row of the type mapping, driven off real types, plus the
/// two things the older type table got wrong and the schema had to fix.
/// </summary>
public sealed class NoireRemoteManifestSchemaTests
{
    public enum Facing
    {
        North,
        South,
    }

    [Flags]
    public enum Marks
    {
        None = 0,
        Alpha = 1,
        Beta = 2,
    }

    public sealed class Waypoint
    {
        public string Name { get; set; } = string.Empty;

        public string? Note { get; set; }

        public Vector3 Position { get; set; }

        public int? Order { get; set; }
    }

    public static class Outer
    {
        public sealed class Report
        {
            public int Value { get; set; }
        }
    }

    public static class Other
    {
        public sealed class Report
        {
            public string Text { get; set; } = string.Empty;
        }
    }

    [NoireRemoteClass("Types", Description = "Every type the mapping table names.")]
    public static class TypeProbe
    {
        [NoireRemote]
        public static string Text(string value) => value;

        [NoireRemote]
        public static bool Flag(bool value) => value;

        [NoireRemote]
        public static int Whole(int value) => value;

        [NoireRemote]
        public static long Bigger(long value) => value;

        [NoireRemote]
        public static float Single(float value) => value;

        [NoireRemote]
        public static double Double(double value) => value;

        [NoireRemote]
        public static Guid Id(Guid value) => value;

        [NoireRemote]
        public static DateTime At(DateTime value) => value;

        [NoireRemote]
        public static TimeSpan Span(TimeSpan value) => value;

        [NoireRemote]
        public static Uri Link(Uri value) => value;

        [NoireRemote]
        public static byte[] Bytes(byte[] value) => value;

        [NoireRemote]
        public static Facing Turn(Facing value) => value;

        [NoireRemote]
        public static Marks Mark(Marks value) => value;

        [NoireRemote]
        public static Vector3 Move(Vector3 value) => value;

        [NoireRemote]
        public static IReadOnlyList<int> List(IReadOnlyList<int> value) => value;

        [NoireRemote]
        public static Dictionary<string, int> Map(Dictionary<string, int> value) => value;

        [NoireRemote]
        public static Waypoint Shape(Waypoint value) => value;

        [NoireRemote(Description = "Puts a marker down.", Confirm = true, Group = "world", Order = 3, Deprecated = true)]
        public static void Place([NoireRemoteParam("Where it goes.", Minimum = 0, Maximum = 10, Step = 0.5, Example = "2.5")] double radius)
            => _ = radius;
    }

    [NoireRemoteClass("Collide")]
    public static class CollisionProbe
    {
        [NoireRemote]
        public static Outer.Report First() => new();

        [NoireRemote]
        public static Other.Report Second() => new();
    }

    private static NoireRemoteManifest ManifestOf(params Type[] types)
    {
        using var server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
        });

        foreach (var type in types)
            server.PublishType(type);

        return NoireRemoteJson.Read<NoireRemoteManifest>(NoireRemoteJson.Write(server.Manifest()))!;
    }

    private static JObject SchemaOf(NoireRemoteManifest manifest, string member)
        => manifest.GetEndpoint("Types")!.GetMember(member)!.Parameters[0].Schema!;

    [Theory]
    [InlineData("Text", "string", null)]
    [InlineData("Flag", "boolean", null)]
    [InlineData("Whole", "integer", "int32")]
    [InlineData("Bigger", "integer", "int64")]
    [InlineData("Single", "number", "float")]
    [InlineData("Double", "number", null)]
    [InlineData("Id", "string", "uuid")]
    [InlineData("At", "string", "date-time")]
    [InlineData("Link", "string", "uri")]
    public void AScalarParameter_CarriesItsTypeAndItsFormat(string member, string type, string? format)
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), member);

        schema["type"]!.Value<string>().Should().Be(type);

        if (format == null)
            schema["format"].Should().BeNull();
        else
            schema["format"]!.Value<string>().Should().Be(format);
    }

    [Fact]
    public void AnIntegerParameter_CarriesTheBoundsItsTypeHas()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "Whole");

        schema["minimum"]!.Value<long>().Should().Be(int.MinValue);
        schema["maximum"]!.Value<long>().Should().Be(int.MaxValue);
    }

    [Fact]
    public void ATimeSpanParameter_SaysHowNewtonsoftWritesIt()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "Span");

        schema["type"]!.Value<string>().Should().Be("string");
        schema["description"]!.Value<string>().Should().Contain("hh:mm:ss");
        schema["format"].Should().BeNull("no format keyword describes what Newtonsoft writes");
    }

    [Fact]
    public void AByteArrayParameter_IsBase64()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "Bytes");

        schema["type"]!.Value<string>().Should().Be("string");
        schema["contentEncoding"]!.Value<string>().Should().Be("base64");
    }

    [Fact]
    public void AnEnumParameter_ListsItsNames()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "Turn");

        schema["type"]!.Value<string>().Should().Be("string");
        schema["enum"]!.Values<string>().Should().BeEquivalentTo("North", "South");
        schema["x-noire-flags"].Should().BeNull();
    }

    [Fact]
    public void AFlagsEnumParameter_SaysSoAndKeepsItsNames()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "Mark");

        schema["x-noire-flags"]!.Value<bool>().Should().BeTrue();
        schema["enum"]!.Values<string>().Should().Contain("Alpha");
        schema["description"]!.Value<string>().Should().Contain("comma");
    }

    [Fact]
    public void AVectorParameter_AcceptsTheObjectFormAndTheArrayForm()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "Move");
        var union = schema["oneOf"] as JArray;

        union.Should().NotBeNull().And.HaveCount(2);

        var asObject = union![0];
        asObject["type"]!.Value<string>().Should().Be("object");
        (asObject["properties"] as JObject)!.Properties().Select(p => p.Name).Should().BeEquivalentTo("x", "y", "z");

        var asArray = union[1];
        asArray["type"]!.Value<string>().Should().Be("array");
        asArray["minItems"]!.Value<int>().Should().Be(3);
        asArray["maxItems"]!.Value<int>().Should().Be(3);
    }

    [Fact]
    public void AListParameter_CarriesItsElementType()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "List");

        schema["type"]!.Value<string>().Should().Be("array");
        schema["items"]!["type"]!.Value<string>().Should().Be("integer");
    }

    [Fact]
    public void AMapParameter_CarriesItsValueType()
    {
        var schema = SchemaOf(ManifestOf(typeof(TypeProbe)), "Map");

        schema["type"]!.Value<string>().Should().Be("object");
        schema["additionalProperties"]!["type"]!.Value<string>().Should().Be("integer");
    }

    [Fact]
    public void AClassParameter_ReferencesADefinitionThatCarriesRequiredAndNullability()
    {
        var manifest = ManifestOf(typeof(TypeProbe));
        var schema = SchemaOf(manifest, "Shape");

        schema["$ref"]!.Value<string>().Should().Be("#/$defs/Waypoint");

        var shape = manifest.Defs!["Waypoint"];

        shape["required"]!.Values<string>().Should().Contain("name").And.Contain("position");
        shape["required"]!.Values<string>().Should().NotContain("note", "a nullable reference is not required");
        shape["required"]!.Values<string>().Should().NotContain("order", "a nullable value type is not required");

        shape["properties"]!["note"]!["type"]!.Values<string>().Should().BeEquivalentTo("string", "null");
        shape["properties"]!["order"]!["type"]!.Values<string>().Should().BeEquivalentTo("integer", "null");
    }

    [Fact]
    public void TwoTypesWithOneShortName_GetTwoDefinitions()
    {
        var manifest = ManifestOf(typeof(CollisionProbe));

        manifest.Defs.Should().NotBeNull();
        manifest.Defs!.Keys.Where(key => key.EndsWith("Report", StringComparison.Ordinal)).Should().HaveCount(2,
            "keying the table on a short name collapses two types and the second answers as the first");

        var first = manifest.GetEndpoint("Collide")!.GetMember("First")!.Returns!.Schema!["$ref"]!.Value<string>();
        var second = manifest.GetEndpoint("Collide")!.GetMember("Second")!.Returns!.Schema!["$ref"]!.Value<string>();

        first.Should().NotBe(second);
        manifest.Defs.Should().ContainKey(first!.Substring("#/$defs/".Length));
        manifest.Defs.Should().ContainKey(second!.Substring("#/$defs/".Length));
    }

    [Fact]
    public void AUniqueShortName_StaysShort()
    {
        var manifest = ManifestOf(typeof(TypeProbe));

        manifest.Defs.Should().ContainKey("Waypoint");
    }

    [Fact]
    public void TheOlderTypeTable_StillWritesWhatItAlwaysWrote()
    {
        var manifest = ManifestOf(typeof(TypeProbe));

        manifest.Types.Should().NotBeNull();
        manifest.Types!.Should().ContainKey("Waypoint");

        var shape = manifest.Types["Waypoint"];

        shape["name"]!.Value<string>().Should().Be("string");
        shape["position"]!.Value<string>().Should().Be("vector3");
        shape["order"]!.Value<string>().Should().Be("integer");
    }

    [Fact]
    public void TheAttributes_ReachTheMemberAndItsParameter()
    {
        var manifest = ManifestOf(typeof(TypeProbe));
        var member = manifest.GetEndpoint("Types")!.GetMember("Place")!;

        member.Summary.Should().Be("Puts a marker down.");
        member.Confirm.Should().BeTrue();
        member.Group.Should().Be("world");
        member.Order.Should().Be(3);
        member.Deprecated.Should().BeTrue();
        member.Access.Should().Be("local");

        var parameter = member.Parameters[0];

        parameter.Summary.Should().Be("Where it goes.");
        parameter.Schema!["description"]!.Value<string>().Should().Be("Where it goes.");
        parameter.Schema["minimum"]!.Value<double>().Should().Be(0);
        parameter.Schema["maximum"]!.Value<double>().Should().Be(10);
        parameter.Schema["x-noire-step"]!.Value<double>().Should().Be(0.5);
        parameter.Schema["examples"]!.Values<string>().Should().ContainSingle().Which.Should().Be("2.5");
    }

    [Fact]
    public void TheEndpointDescription_ReachesTheEndpointAndNotItsMembers()
    {
        var manifest = ManifestOf(typeof(TypeProbe));

        manifest.GetEndpoint("Types")!.Summary.Should().Be("Every type the mapping table names.");
        manifest.GetEndpoint("Types")!.GetMember("Text")!.Summary.Should().BeNull();
    }

    [Fact]
    public void TheRevision_ChangesWhenAPublicationIsAdded()
    {
        using var server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
        });

        server.PublishType(typeof(TypeProbe));
        var first = server.Manifest().Revision;

        server.PublishType(typeof(CollisionProbe));
        var second = server.Manifest().Revision;

        second.Should().BeGreaterThan(first);
    }

    [Fact]
    public void TheChannelList_NamesTheFourThatShip()
    {
        var manifest = ManifestOf(typeof(TypeProbe));

        manifest.Channels.Should().NotBeNull();
        manifest.Channels!.Select(channel => channel.Name).Should().Contain(
            [NoireRemoteChannels.Events, NoireRemoteChannels.Progress, NoireRemoteChannels.Log, NoireRemoteChannels.Metrics]);
    }

    [Fact]
    public void TheManifestMarksAPublishedPropertyReadOnly()
    {
        using var publication = NoireRemote.PublishType(typeof(ReadOnlyProbe));

        var manifest = NoireRemote.Manifest();
        var member = manifest.Endpoints.Single(endpoint => endpoint.Name == "ReadOnly").Members.Single();

        member.ReadOnly.Should().BeTrue();
        member.Transports.Should().BeEquivalentTo(["http", "ws"]);
    }

    [Fact]
    public void PublishingASocketMovesTheManifestRevision()
    {
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(ReadOnlyProbe));
        var before = NoireRemote.Manifest().Revision;

        using var socket = NoireLib.Websocket.NoireWebsocket.Publish("extra");

        NoireRemote.Manifest().Revision.Should().NotBe(before,
            "a cached manifest would otherwise hide a socket that was published after it was read");
    }

    [NoireRemoteClass("ReadOnly")]
    private static class ReadOnlyProbe
    {
        [NoireRemote]
        public static int MajorVersion => 7;
    }
}
