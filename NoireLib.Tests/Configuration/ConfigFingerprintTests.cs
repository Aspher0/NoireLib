using FluentAssertions;
using Newtonsoft.Json;
using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the content fingerprint: every mutation kind moves it, members the serializer ignores do
/// not, reverting a change restores it, and a warm fingerprint allocates nothing.
/// </summary>
public sealed class ConfigFingerprintTests
{
    public enum ProbeMode
    {
        First,
        Second,
    }

    public sealed class ProbeLeaf
    {
        public string Name { get; set; } = "leaf";

        public int Depth { get; set; }
    }

    public sealed class ProbeNested
    {
        public bool Locked { get; set; }

        public ProbeLeaf Leaf { get; set; } = new();
    }

    public sealed class Probe
    {
        public bool Flag { get; set; } = true;

        public int Count { get; set; } = 3;

        public float Ratio { get; set; } = 1.5f;

        public string Name { get; set; } = "name";

        public string? Optional { get; set; } = "present";

        public int? MaybeCount { get; set; } = 5;

        public ProbeMode Mode { get; set; } = ProbeMode.First;

        public List<string> Tags { get; set; } = ["a", "b"];

        public List<ProbeLeaf> Items { get; set; } = [new ProbeLeaf { Name = "first" }, new ProbeLeaf { Name = "second" }];

        public Dictionary<uint, ProbeNested> Zones { get; set; } = new() { [1] = new ProbeNested() };

        public HashSet<int> Set { get; set; } = [1, 2, 3];

        public int[] Numbers { get; set; } = [10, 20];

        public ProbeNested Ui { get; set; } = new();

        public int PublicField = 9;

        [JsonIgnore]
        public int IgnoredCounter { get; set; }
    }

    private static long FingerprintOf(Probe probe) => ConfigFingerprint.ForType(typeof(Probe))(probe);

    private static void AssertMutationMoves(Action<Probe> mutate, string because)
    {
        var probe = new Probe();
        var before = FingerprintOf(probe);

        mutate(probe);

        FingerprintOf(probe).Should().NotBe(before, because);
    }

    [Fact]
    public void TheSameState_FingerprintsIdentically()
    {
        var probe = new Probe();

        FingerprintOf(probe).Should().Be(FingerprintOf(probe));
    }

    [Fact]
    public void EveryMutationKind_MovesTheFingerprint()
    {
        AssertMutationMoves(p => p.Flag = false, "a bool set is a change");
        AssertMutationMoves(p => p.Count = 4, "an int set is a change");
        AssertMutationMoves(p => p.Ratio = 2f, "a float set is a change");
        AssertMutationMoves(p => p.Name = "other", "a string set is a change");
        AssertMutationMoves(p => p.Optional = null, "a string going null is a change");
        AssertMutationMoves(p => p.MaybeCount = null, "a nullable emptying is a change");
        AssertMutationMoves(p => p.Mode = ProbeMode.Second, "an enum set is a change");
        AssertMutationMoves(p => p.Tags.Add("c"), "a list add is a change");
        AssertMutationMoves(p => p.Tags.RemoveAt(0), "a list remove is a change");
        AssertMutationMoves(p => p.Tags[0] = "changed", "a list element replacement is a change");
        AssertMutationMoves(p => p.Items[1].Name = "renamed", "mutating an object inside a list is a change");
        AssertMutationMoves(p => p.Zones[2] = new ProbeNested(), "a dictionary add is a change");
        AssertMutationMoves(p => p.Zones[1].Locked = true, "mutating an object inside a dictionary is a change");
        AssertMutationMoves(p => p.Zones[1].Leaf.Depth = 3, "a change two objects deep inside a dictionary is a change");
        AssertMutationMoves(p => p.Set.Add(4), "a set add is a change");
        AssertMutationMoves(p => p.Numbers[0] = 11, "an array element write is a change");
        AssertMutationMoves(p => p.Ui.Locked = true, "a nested property set is a change");
        AssertMutationMoves(p => p.Ui.Leaf.Name = "deep", "a nested-nested property set is a change");
        AssertMutationMoves(p => p.Ui = new ProbeNested { Locked = true }, "replacing a nested object is a change");
        AssertMutationMoves(p => p.PublicField = 10, "a public field write is serialized, so it is a change");
    }

    [Fact]
    public void AMemberTheSerializerIgnores_DoesNotMoveTheFingerprint()
    {
        var probe = new Probe();
        var before = FingerprintOf(probe);

        probe.IgnoredCounter = 999;

        FingerprintOf(probe).Should().Be(before,
            "the fingerprint mirrors exactly what the serializer writes, and [JsonIgnore] members are not written");
    }

    [Fact]
    public void RevertingAChange_RestoresTheFingerprint()
    {
        var probe = new Probe();
        var before = FingerprintOf(probe);

        probe.Tags.Add("transient");
        probe.Ui.Locked = true;
        probe.Ui.Locked = false;
        probe.Tags.RemoveAt(probe.Tags.Count - 1);

        FingerprintOf(probe).Should().Be(before,
            "a change that fully reverts before the next check is not a change and must not cause a write");
    }

    [Fact]
    public void NullingANestedObject_IsAChange_AndDistinctFromAnEmptyOne()
    {
        var withNested = new Probe();
        var withNull = new Probe { Optional = null };

        FingerprintOf(withNested).Should().NotBe(FingerprintOf(withNull));
    }

    [Fact]
    public void AWarmFingerprint_AllocatesNothing()
    {
        var probe = new Probe();

        // Warm the walkers and any lazily built mixers.
        for (var i = 0; i < 3; i++)
            FingerprintOf(probe);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 100; i++)
            FingerprintOf(probe);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0,
            "the fingerprint runs on frames where a config was merely read, so it must not produce garbage");
    }
}
