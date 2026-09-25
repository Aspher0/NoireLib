using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using FluentAssertions;
using Lumina.Data.Parsing.Layer;
using NoireLib.Helpers;
using NoireLib.Movement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins the public level and shared group reader on hand-built bytes: both containers, every entry kept whatever its
/// type, the type-specific fields at their offsets, and nested groups composed child first, then parent.
/// </summary>
public class LayerGroupHelperTests
{
    private const float Tol = 1e-4f;

    [Fact]
    public void Read_TellsTheTwoContainersApart()
    {
        var layer = new NavFileBuilder.Layer { Id = 7, Name = "only", Instances = { NavFileBuilder.Background(1, "a.pcb") } };

        LayerGroupHelper.Read(NavFileBuilder.Lgb(layer)).Should().ContainSingle().Which.Name.Should().Be("only");
        LayerGroupHelper.Read(NavFileBuilder.Sgb(layer)).Should().ContainSingle().Which.Id.Should().Be(7);
        LayerGroupHelper.Read(Encoding.UTF8.GetBytes("NOPE, and then a good deal of padding")).Should().BeEmpty();
    }

    [Fact]
    public void Read_KeepsEveryEntry_WhateverItsType()
    {
        var file = NavFileBuilder.Lgb(new NavFileBuilder.Layer
        {
            Id = 1,
            Name = "mixed",
            Instances =
            {
                new NavFileBuilder.Instance { Type = 8, Id = 1, Words = { [0x30] = 1003339 } },
                new NavFileBuilder.Instance { Type = 40, Id = 2 },
                NavFileBuilder.DoorRange(3),
                new NavFileBuilder.Instance { Type = 1, Id = 4, Words = { [0x38] = 0, [0x48] = 1 }, Strings = { [0x30] = "prop.mdl" } },
            },
        });

        var entries = LayerGroupHelper.Read(file).Single().Entries;

        entries.Select(e => e.Type).Should().Equal(
            LayerEntryType.EventNPC, LayerEntryType.PopRange, LayerEntryType.DoorRange, LayerEntryType.BG);
        entries[0].BaseId.Should().Be(1003339);
        entries[3].CollisionType.Should().Be(ModelCollisionType.None, "a model with no collision is still a placed model");
        entries[3].AssetPath.Should().Be("prop.mdl");
    }

    [Fact]
    public void Read_BackgroundCarriesItsModelAndItsCollisionMesh()
    {
        var file = NavFileBuilder.Lgb(new NavFileBuilder.Layer
        {
            Id = 1,
            Name = "bg",
            Instances =
            {
                new NavFileBuilder.Instance
                {
                    Type = 1,
                    Id = 9,
                    Words = { [0x38] = 1, [0x3C] = 0x200000, [0x40] = 0x207004, [0x48] = 1 },
                    Strings = { [0x30] = "bg/ffxiv/fst_f1/fld/f1f1/bgparts/f1f1_a1_yagu7.mdl", [0x34] = "bg/ffxiv/fst_f1/fld/f1f1/collision/f1f1_a1_yagu7.pcb" },
                },
            },
        });

        var entry = LayerGroupHelper.Read(file).Single().Entries.Single();

        entry.AssetPath.Should().EndWith(".mdl");
        entry.CollisionPath.Should().EndWith(".pcb");
        entry.CollisionType.Should().Be(ModelCollisionType.Replace);
        entry.Attribute.Should().Be(0x207004);
        entry.AttributeMask.Should().Be(0x200000);
        entry.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Read_ExitRangeCarriesItsDestination()
    {
        var file = NavFileBuilder.Lgb(new NavFileBuilder.Layer
        {
            Id = 1,
            Name = "exits",
            Instances =
            {
                new NavFileBuilder.Instance
                {
                    Type = 41,
                    Id = 5,
                    Words = { [0x30] = 1, [0x3C] = 2, [0x40] = 135 << 16, [0x48] = 2443384, [0x4C] = 2210362 },
                },
            },
        });

        var exit = LayerGroupHelper.Read(file).Single().Entries.Single();

        exit.Shape.Should().Be(TriggerBoxShape.TriggerBoxShapeBox);
        exit.ExitType.Should().Be(ExitRangeType.Invisible);
        exit.DestTerritoryId.Should().Be(135);
        exit.DestInstanceId.Should().Be(2443384);
        exit.ReturnInstanceId.Should().Be(2210362);
    }

    [Fact]
    public void Read_PopRangeCarriesItsSpawnOffsets()
    {
        var file = NavFileBuilder.Lgb(new NavFileBuilder.Layer
        {
            Id = 1,
            Name = "arrivals",
            Instances =
            {
                new NavFileBuilder.Instance
                {
                    Type = 40,
                    Id = 6,
                    Body = 0x40 + 2 * 12,
                    Words =
                    {
                        [0x30] = 1, [0x34] = 0x40 - 0x34, [0x38] = 2,
                        [0x40] = BitConverter.SingleToInt32Bits(1.5f), [0x44] = BitConverter.SingleToInt32Bits(-0.25f), [0x48] = BitConverter.SingleToInt32Bits(2f),
                        [0x4C] = BitConverter.SingleToInt32Bits(-3f), [0x50] = 0, [0x54] = BitConverter.SingleToInt32Bits(0.5f),
                    },
                },
                new NavFileBuilder.Instance { Type = 40, Id = 7, Words = { [0x30] = 1, [0x34] = 0x7FFF, [0x38] = 20 } },
            },
        });

        var entries = LayerGroupHelper.Read(file).Single().Entries;

        entries[0].SpawnOffsets.Should().Equal(new Vector3(1.5f, -0.25f, 2f), new Vector3(-3f, 0f, 0.5f));
        entries[1].SpawnOffsets.Should().BeEmpty("a list running past the end of the file is not read");
    }

    [Fact]
    public void Read_SharedGroupCarriesItsDoorFields()
    {
        var entry = LayerGroupHelper.Read(NavFileBuilder.Lgb(new NavFileBuilder.Layer
            {
                Id = 1,
                Name = "doors",
                Instances = { NavFileBuilder.SharedGroup(1, "door.sgb", keepDoorSolid: true, doorState: NavDoorState.Closed) },
            }))
            .Single().Entries.Single();

        entry.AssetPath.Should().Be("door.sgb");
        entry.InitialDoorState.Should().Be(DoorState.Closed);
        entry.NotCreateNavimeshDoor.Should().BeTrue();
    }

    [Fact]
    public void Read_OnATruncatedFile_YieldsWhatItCanRatherThanThrowing()
    {
        var file = NavFileBuilder.Lgb(new NavFileBuilder.Layer
        {
            Id = 1,
            Name = "cut",
            Instances = { NavFileBuilder.Background(1, "a.pcb"), new NavFileBuilder.Instance { Type = 41, Id = 2 } },
        });

        for (var cut = 4; cut < file.Length; cut += 3)
        {
            var act = () => LayerGroupHelper.Read(file.AsSpan(0, cut));
            act.Should().NotThrow();
        }
    }

    /// <summary>
    /// A nested group's entries stand at their own placement composed onto the group's, child first, then parent.
    /// A group that places itself is listed but not walked into again.
    /// </summary>
    [Fact]
    public void Flatten_ComposesNestedGroupsOntoTheirParent_AndStopsAtACycle()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["level.lgb"] = NavFileBuilder.Lgb(new NavFileBuilder.Layer
            {
                Id = 1,
                Name = "groups",
                Instances =
                {
                    new NavFileBuilder.Instance
                    {
                        Type = 6,
                        Id = 1,
                        Translation = new Vector3(10, 0, 0),
                        Rotation = new Vector3(0, MathF.PI / 2f, 0),
                        Strings = { [0x30] = "a.sgb" },
                    },
                },
            }),
            ["a.sgb"] = NavFileBuilder.Sgb(new NavFileBuilder.Layer
            {
                Id = 1,
                Name = "inside",
                Instances =
                {
                    new NavFileBuilder.Instance { Type = 1, Id = 2, Translation = new Vector3(1, 0, 0), Strings = { [0x30] = "part.mdl" } },
                    new NavFileBuilder.Instance { Type = 6, Id = 3, Strings = { [0x30] = "a.sgb" } },
                },
            }),
        };

        var flat = LayerGroupHelper.Flatten("level.lgb", Read(files), null, LayerGroupHelper.DefaultMaxDepth);

        flat.Select(e => e.InstanceId).Should().Equal(1u, 2u, 3u);
        var part = Vector3.Transform(Vector3.Zero, flat[1].World);
        part.X.Should().BeApproximately(10, Tol);
        part.Z.Should().BeApproximately(-1, Tol);
    }

    [Fact]
    public void Flatten_FiltersOnlyTheFilesOwnLayers()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["level.lgb"] = NavFileBuilder.Lgb(
                new NavFileBuilder.Layer { Id = 1, Name = "keep", Instances = { NavFileBuilder.SharedGroup(1, "a.sgb") } },
                new NavFileBuilder.Layer { Id = 2, Name = "drop", Instances = { NavFileBuilder.Background(2, "b.pcb") } }),
            ["a.sgb"] = NavFileBuilder.Sgb(
                new NavFileBuilder.Layer { Id = 1, Name = "nested", Instances = { NavFileBuilder.Background(3, "c.pcb") } }),
        };

        var flat = LayerGroupHelper.Flatten("level.lgb", Read(files), layer => layer.Name == "keep", LayerGroupHelper.DefaultMaxDepth);

        flat.Select(e => e.InstanceId).Should().Equal(1u, 3u);
    }

    [Fact]
    public void Read_WaterRangeAndMapRangeCarryTheirPriorityShapeAndFlags()
    {
        var entries = LayerGroupHelper.Read(NavFileBuilder.Lgb(new NavFileBuilder.Layer
            {
                Id = 1,
                Name = "ranges",
                Instances =
                {
                    new NavFileBuilder.Instance { Type = 86, Id = 1, Body = 0x40, Words = { [0x30] = 2, [0x34] = 150 | (1 << 16), [0x3C] = 0x100 } },
                    new NavFileBuilder.Instance { Type = 43, Id = 2, Body = 0x6C, Words = { [0x30] = 3, [0x34] = unchecked((ushort)-5) | (1 << 16), [0x64] = 1 << 24, [0x68] = 1 | (1 << 8) } },
                    new NavFileBuilder.Instance { Type = 43, Id = 3, Body = 0x6C, Words = { [0x30] = 1, [0x34] = 100, [0x64] = 1 << 16 } },
                },
            }))
            .Single().Entries;

        entries[0].Type.Should().Be(LayerGroupHelper.WaterRangeEntryType);
        entries[0].Shape.Should().Be(TriggerBoxShape.TriggerBoxShapeSphere);
        entries[0].Priority.Should().Be(150);
        entries[0].WaterRangeFlags.Should().Be(0x100u);

        entries[1].Type.Should().Be(LayerEntryType.MapRange);
        entries[1].Shape.Should().Be(TriggerBoxShape.TriggerBoxShapeCylinder);
        entries[1].Priority.Should().Be(-5);
        entries[1].FlyingDisabled.Should().BeTrue();
        entries[1].MountsAndOrnamentsDisabled.Should().BeTrue();
        entries[1].LalafellOnly.Should().BeTrue();

        entries[2].FlyingDisabled.Should().BeFalse("0x66 is the flight height message's slab, not a ban");
        entries[2].MountsAndOrnamentsDisabled.Should().BeFalse();
        entries[2].LalafellOnly.Should().BeFalse();
        entries[2].WaterRangeFlags.Should().Be(0u);
    }

    [Fact]
    public void Read_ARangeCutShortOfItsBody_ReadsNothing()
    {
        var file = NavFileBuilder.Lgb(new NavFileBuilder.Layer
        {
            Id = 1,
            Name = "ranges",
            Instances = { new NavFileBuilder.Instance { Type = 43, Id = 2, Body = 0x6C, Words = { [0x64] = 1 << 24 } } },
        });

        var withBody = LayerGroupHelper.Read(file).Single().Entries.Should().ContainSingle().Subject;
        withBody.FlyingDisabled.Should().BeTrue();

        // The layer's name follows the last body: cutting it and four more bytes ends the file inside the flags.
        var cut = "ranges".Length + 1 + 4;
        LayerGroupHelper.Read(file.AsSpan(0, file.Length - cut)).Single().Entries.Should().BeEmpty();

        var water = NavFileBuilder.Lgb(new NavFileBuilder.Layer
        {
            Id = 1,
            Name = "ranges",
            Instances = { new NavFileBuilder.Instance { Type = 86, Id = 1, Body = 0x40, Words = { [0x3C] = 1 } } },
        });
        LayerGroupHelper.Read(water.AsSpan(0, water.Length - cut)).Single().Entries.Should().BeEmpty("a water range needs its flags");
    }

    private static Func<string, IReadOnlyList<LayerGroupLayer>> Read(Dictionary<string, byte[]> files)
        => path => files.TryGetValue(path, out var bytes) ? LayerGroupHelper.Read(bytes) : [];
}
