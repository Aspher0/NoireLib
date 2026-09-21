using FluentAssertions;
using Newtonsoft.Json;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins the wire's exact JSON text. A Python or C++ caller is written against these shapes. A change here is
/// always a protocol break.
/// </summary>
public sealed class NoireRemoteJsonTests
{
    public enum Facing
    {
        North,
        South,
    }

    public sealed record PickReport(bool Hit, int Polygon, float Height);

    public sealed class Waypoint
    {
        public Vector3 Position { get; set; }

        public string? Label { get; set; }

        public Facing Facing { get; set; }

        public int? Weight { get; set; }
    }

    [Fact]
    public void Vector3_WritesLowercaseComponentsInOrder()
    {
        NoireRemoteJson.Write(new Vector3(12.5f, 0f, -3.25f)).Should().Be("{\"x\":12.5,\"y\":0.0,\"z\":-3.25}");
    }

    [Fact]
    public void Vector2AndVector4AndQuaternion_WriteTheSameShape()
    {
        NoireRemoteJson.Write(new Vector2(1f, 2f)).Should().Be("{\"x\":1.0,\"y\":2.0}");
        NoireRemoteJson.Write(new Vector4(1f, 2f, 3f, 4f)).Should().Be("{\"x\":1.0,\"y\":2.0,\"z\":3.0,\"w\":4.0}");
        NoireRemoteJson.Write(Quaternion.Identity).Should().Be("{\"x\":0.0,\"y\":0.0,\"z\":0.0,\"w\":1.0}");
    }

    [Fact]
    public void Vector3_ReadsAnObjectABareArrayAndAPartialObject()
    {
        NoireRemoteJson.FromToken<Vector3>(Newtonsoft.Json.Linq.JToken.Parse("{\"x\":1,\"y\":2,\"z\":3}"))
            .Should().Be(new Vector3(1, 2, 3));

        NoireRemoteJson.FromToken<Vector3>(Newtonsoft.Json.Linq.JToken.Parse("[1,2,3]"))
            .Should().Be(new Vector3(1, 2, 3));

        NoireRemoteJson.FromToken<Vector3>(Newtonsoft.Json.Linq.JToken.Parse("{\"X\":4}"))
            .Should().Be(new Vector3(4, 0, 0), "component names bind ignoring case and a missing one is zero");
    }

    [Fact]
    public void Vector3_ReadsAnArrayOfTheWrongLengthAsAFailure()
    {
        var act = () => NoireRemoteJson.FromToken<Vector3>(Newtonsoft.Json.Linq.JToken.Parse("[1,2]"));
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void AnEnum_CrossesAsItsMemberName()
    {
        NoireRemoteJson.Write(Facing.South).Should().Be("\"South\"");
        NoireRemoteJson.FromToken<Facing>(Newtonsoft.Json.Linq.JToken.Parse("\"south\"")).Should().Be(Facing.South);
        NoireRemoteJson.FromToken<Facing>(Newtonsoft.Json.Linq.JToken.Parse("1")).Should().Be(Facing.South,
            "an integer is still accepted inbound");
    }

    [Fact]
    public void AClass_WritesCamelCaseAndDropsNulls()
    {
        var waypoint = new Waypoint { Position = new Vector3(1, 2, 3), Facing = Facing.North };

        NoireRemoteJson.Write(waypoint).Should().Be("{\"position\":{\"x\":1.0,\"y\":2.0,\"z\":3.0},\"facing\":\"North\"}");
    }

    [Fact]
    public void ARecord_RoundTrips()
    {
        var json = NoireRemoteJson.Write(new PickReport(true, 1841, 0.125f));

        json.Should().Be("{\"hit\":true,\"polygon\":1841,\"height\":0.125}");
        NoireRemoteJson.Read<PickReport>(json).Should().Be(new PickReport(true, 1841, 0.125f));
    }

    [Fact]
    public void ADictionary_KeepsItsKeysVerbatim()
    {
        var map = new Dictionary<string, int> { ["FirstKey"] = 1, ["second_key"] = 2 };

        NoireRemoteJson.Write(map).Should().Be("{\"FirstKey\":1,\"second_key\":2}",
            "the camelCase resolver renames members, never dictionary keys");
    }

    [Fact]
    public void ANullable_WritesOnlyWhenItHasAValue()
    {
        NoireRemoteJson.Write(new Waypoint { Weight = 4 }).Should().Contain("\"weight\":4");
        NoireRemoteJson.Write(new Waypoint()).Should().NotContain("weight");
    }

    [Fact]
    public void ADocument_RefusesTrailingContent()
    {
        var act = () => NoireRemoteJson.Read<PickReport>("{\"hit\":true} {\"hit\":false}");
        act.Should().Throw<JsonException>("a request body holds exactly one JSON document");
    }

    [Fact]
    public void ReassigningTheProcessWideDefaultSettings_DoesNotReshapeTheWire()
    {
        var previous = JsonConvert.DefaultSettings;

        try
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                NullValueHandling = NullValueHandling.Include,
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver(),
            };

            NoireRemoteJson.Write(new PickReport(true, 1841, 0.125f))
                .Should().Be("{\"hit\":true,\"polygon\":1841,\"height\":0.125}",
                    "the serializer is built from its own settings, immune to the process-wide defaults any other plugin can assign");
        }
        finally
        {
            JsonConvert.DefaultSettings = previous;
        }
    }

    [Fact]
    public void TheSettings_LeaveTypeNameHandlingOff()
    {
        NoireRemoteJson.CreateSettings().TypeNameHandling.Should().Be(TypeNameHandling.None);
    }

    [Fact]
    public void CreateSettings_HandsBackAFreshInstanceEveryTime()
    {
        var first = NoireRemoteJson.CreateSettings();
        var second = NoireRemoteJson.CreateSettings();

        first.Should().NotBeSameAs(second);

        first.NullValueHandling = NullValueHandling.Include;
        NoireRemoteJson.CreateSettings().NullValueHandling.Should().Be(NullValueHandling.Ignore);
    }
}
