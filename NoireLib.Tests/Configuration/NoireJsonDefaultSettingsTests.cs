using FluentAssertions;
using Newtonsoft.Json;
using NoireLib.Helpers.ObjectExtensions;
using NoireLib.Networker.Internal;
using NoireLib.UpdateTracker;
using System;
using System.Runtime.Versioning;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Groups every test that assigns <see cref="JsonConvert.DefaultSettings"/>. That property is process-global, so a
/// test touching it must not run beside any other test that serializes JSON. Parallelization is disabled for the whole
/// collection to guarantee that, and any future test class that installs default settings must join this collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class JsonDefaultSettingsCollection
{
    /// <summary>
    /// The collection name shared by the definition and its member classes.
    /// </summary>
    public const string Name = "JsonConvert.DefaultSettings";
}

/// <summary>
/// Runs code with a hostile <see cref="JsonConvert.DefaultSettings"/> installed, so tests can prove that NoireLib's
/// serialization is decided by its own settings objects rather than by a global any code in the process can assign.
/// </summary>
internal static class HostileJsonDefaults
{
    /// <summary>
    /// Invokes <paramref name="action"/> with default settings that turn on the type resolution and reference
    /// metadata NoireLib must never emit or honour, always restoring the previous value.
    /// </summary>
    /// <typeparam name="T">The result type of <paramref name="action"/>.</typeparam>
    /// <param name="action">The code to run inside the hostile window.</param>
    /// <returns>Whatever <paramref name="action"/> returned.</returns>
    public static T While<T>(Func<T> action)
    {
        var previousDefaultSettings = JsonConvert.DefaultSettings;

        try
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                PreserveReferencesHandling = PreserveReferencesHandling.All,
                MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
                Formatting = Formatting.Indented,
            };

            return action();
        }
        finally
        {
            // This is a process-global; leaking it would corrupt every later JSON operation in the test run.
            JsonConvert.DefaultSettings = previousDefaultSettings;
        }
    }
}

/// <summary>
/// Locks the invariant that no subsystem's serialization can be reshaped by <see cref="JsonConvert.DefaultSettings"/>.
/// NoireLib is loaded next to unrelated plugins in one process, so any of them can assign that global at any time.<br/>
/// The paths covered here read bytes that arrive from outside the plugin: an unauthenticated LAN broadcast, a mapped
/// file other local processes can write, and an HTTP response body.
/// </summary>
[Collection(JsonDefaultSettingsCollection.Name)]
[SupportedOSPlatform("windows")]
public class NoireJsonDefaultSettingsTests
{
    private sealed class ClonablePayload
    {
        public string Text { get; set; } = string.Empty;
        public int Value { get; set; }
        public string? Absent { get; set; }
    }

    private static BeaconModel CreateBeacon()
        => new()
        {
            NetworkHash = "0123456789abcdef",
            HubId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Port = 51234,
        };

    [Fact]
    public void Discovery_Beacon_Encodes_Identically_Under_Hostile_DefaultSettings()
    {
        var beacon = CreateBeacon();

        var baseline = Encoding.UTF8.GetString(Wire.EncodeModel(beacon));
        var underHostileDefaults = Encoding.UTF8.GetString(HostileJsonDefaults.While(() => Wire.EncodeModel(beacon)));

        underHostileDefaults.Should().Be(baseline,
            "a beacon is broadcast to the LAN, so its bytes must be decided by the Networker alone");
        underHostileDefaults.Should().NotContain("$type", "type information must never reach the LAN");
        underHostileDefaults.Should().NotContain("$id", "reference metadata must never reach the LAN");
    }

    [Fact]
    public void Discovery_Beacon_Round_Trips_Under_Hostile_DefaultSettings()
    {
        var beacon = CreateBeacon();

        var decoded = HostileJsonDefaults.While(() => Wire.DecodeModel<BeaconModel>(Wire.EncodeModel(beacon)));

        decoded.Should().NotBeNull();
        decoded!.NetworkHash.Should().Be(beacon.NetworkHash);
        decoded.HubId.Should().Be(beacon.HubId);
        decoded.Port.Should().Be(beacon.Port);
    }

    [Theory]
    [InlineData("not a beacon")]
    [InlineData("{\"nh\":")]
    [InlineData("{\"nh\":\"x\",\"p\":1}garbage")]
    public void Discovery_Beacon_Decode_Returns_Null_On_A_Malformed_Datagram(string text)
    {
        Wire.DecodeModel<BeaconModel>(Encoding.UTF8.GetBytes(text)).Should().BeNull(
            "a datagram can arrive from any host on the LAN, so a malformed one is skipped rather than thrown");
    }

    [Fact]
    public void Discovery_Beacon_Decode_Returns_Null_On_Arbitrary_Bytes()
    {
        Wire.DecodeModel<BeaconModel>(new byte[] { 0xFF, 0xFE, 0x00, 0x42, 0xDE, 0xAD }).Should().BeNull();
    }

    [Fact]
    public void Rendezvous_Record_Round_Trips_Under_Hostile_DefaultSettings()
    {
        var data = new RendezvousData
        {
            Network = "NoireLib.Tests.Network",
            Port = 51234,
            Generation = 7,
            ProcessId = 4242,
        };

        var baseline = Encoding.UTF8.GetString(Wire.EncodeModel(data));
        var decoded = HostileJsonDefaults.While(() =>
        {
            var bytes = Wire.EncodeModel(data);

            Encoding.UTF8.GetString(bytes).Should().Be(baseline,
                "the mapped file is read by other processes running this same library version");

            return Wire.DecodeModel<RendezvousData>(bytes);
        });

        decoded.Should().NotBeNull();
        decoded!.Network.Should().Be(data.Network);
        decoded.Port.Should().Be(data.Port);
        decoded.Generation.Should().Be(data.Generation);
        decoded.ProcessId.Should().Be(data.ProcessId);
    }

    [Fact]
    public void Rendezvous_Record_Decode_Returns_Null_On_Corrupt_Content()
    {
        Wire.DecodeModel<RendezvousData>(Encoding.UTF8.GetBytes("{\"n\":\"x\",\"p\":")).Should().BeNull(
            "a torn or corrupt mapped file means no usable rendezvous, not a crash");
    }

    [Fact]
    public void Repository_Response_Parses_Identically_Under_Hostile_DefaultSettings()
    {
        const string json = """
            [
              { "Author": "Someone", "Name": "Plugin One", "InternalName": "PluginOne", "AssemblyVersion": "1.2.3.4" },
              { "Author": "Someone", "Name": "Plugin Two", "InternalName": "PluginTwo", "AssemblyVersion": "0.1.0.0" }
            ]
            """;

        var entries = HostileJsonDefaults.While(() => NoireUpdateTracker.ParseRepositoryResponse(json));

        entries.Should().NotBeNull();
        entries!.Should().HaveCount(2);
        entries[0].InternalName.Should().Be("PluginOne");
        entries[0].AssemblyVersion.Should().Be("1.2.3.4");
        entries[1].InternalName.Should().Be("PluginTwo");
    }

    [Fact]
    public void Repository_Response_Never_Resolves_A_Type_Named_By_The_Response()
    {
        const string json = """
            [{ "$type": "System.Diagnostics.Process, System.Diagnostics.Process", "InternalName": "PluginOne" }]
            """;

        var entries = HostileJsonDefaults.While(() => NoireUpdateTracker.ParseRepositoryResponse(json));

        entries.Should().NotBeNull();
        entries!.Should().HaveCount(1);
        entries[0].Should().BeOfType<RepoEntry>("a response body must never name the type it is read into");
        entries[0].InternalName.Should().Be("PluginOne");
    }

    [Fact]
    public void Repository_Response_Throws_On_A_Malformed_Body()
    {
        var act = () => NoireUpdateTracker.ParseRepositoryResponse("not json at all");

        act.Should().Throw<JsonException>("the update check reports a bad response through its own error handling");
    }

    [Fact]
    public void Clone_Is_Unaffected_By_Hostile_DefaultSettings()
    {
        var original = new ClonablePayload { Text = "value", Value = 42 };

        var clone = HostileJsonDefaults.While(() => original.Clone());

        clone.Should().NotBeNull();
        clone.Should().NotBeSameAs(original);
        clone!.Text.Should().Be("value");
        clone.Value.Should().Be(42);
        clone.Absent.Should().BeNull();
    }
}
