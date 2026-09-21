using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the two byte layouts that say which territory a placement belongs to: the level-base file's layer-set table
/// and a level file's per-layer layer-set list. Both are read from the bytes, never through Lumina, whose layer
/// reading resolves the list against the layer and not against the list. These fixtures are what keeps the
/// offsets honest. Every case is built by hand. None of it needs a game.
/// </summary>
public sealed class LayerSetHelperTests
{
    [Fact]
    public void ParseLayerSets_ReadsEachLayerSetAndTheTerritoryItBelongsTo()
    {
        var data = LevelBase([(136767u, 735u), (218650u, 828u)]);

        var sets = LayerSetHelper.ParseLayerSets(data);

        sets.Should().Equal(
            new LevelLayerSet(136767, 735),
            new LevelLayerSet(218650, 828));
    }

    [Theory]
    [InlineData("XXXX", "SCN1")]
    [InlineData("LVB1", "XXXX")]
    public void ParseLayerSets_RejectsAnythingThatIsNotALevelBaseFile(string fileMagic, string chunkMagic)
    {
        var data = LevelBase([(1u, 2u)]);
        Write(data, 0, fileMagic);
        Write(data, 0x0C, chunkMagic);

        LayerSetHelper.ParseLayerSets(data).Should().BeEmpty();
    }

    [Fact]
    public void ParseLayerSets_RejectsACountThatRunsPastTheFile()
    {
        var data = LevelBase([(1u, 2u)]);
        BitConverter.GetBytes(4096).CopyTo(data, ChunkData + FolderOffset + 4);

        LayerSetHelper.ParseLayerSets(data).Should().BeEmpty();
    }

    [Fact]
    public void LayerSetIds_AreReadRelativeToTheList()
    {
        var data = LevelFile([[136767u, 218650u], [], [218650u]]);

        var layers = LayerGroupHelper.ReadLevel(data).Select(layer => layer.LayerSetIds).ToList();

        layers.Should().HaveCount(3);
        layers[0].Should().Equal(136767u, 218650u);
        layers[1].Should().BeEmpty("a layer naming no set is unconditional");
        layers[2].Should().Equal(218650u);
    }

    [Fact]
    public void LayerSetIds_AreNotReadFromAnythingThatIsNotALevelFile()
    {
        var data = LevelFile([[1u]]);
        Write(data, 0x0C, "XXXX");

        LayerGroupHelper.ReadLevel(data).Should().BeEmpty();
    }

    /// <summary>
    /// The rule the extraction reads: a layer naming no set at all is unconditional and belongs everywhere, and one
    /// naming sets belongs only to the territories they map to.
    /// </summary>
    [Theory]
    [InlineData(735u, true)]
    [InlineData(828u, false)]
    public void Belongs_AsksWhetherTheLayerIsPartOfTheTerritory(uint territory, bool expected)
        => LayerSetHelper.Belongs(new uint[] { 735 }, territory).Should().Be(expected);

    [Fact]
    public void Belongs_TreatsALayerWithNoSetsAsUnconditional()
    {
        LayerSetHelper.Belongs(null, 735).Should().BeTrue();
        LayerSetHelper.Belongs([], 735).Should().BeTrue();
    }

    // "LVB1" header, an "SCN1" chunk pointing at the layer-set folder, one 0x1C record per set.
    private const int ChunkData = 0x14;
    private const int FolderOffset = 0x100;
    private const int RecordSize = 0x1C;

    private static byte[] LevelBase(IReadOnlyList<(uint Set, uint Territory)> sets)
    {
        var folder = ChunkData + FolderOffset;
        var data = new byte[folder + 0x0C + (sets.Count * RecordSize)];
        Write(data, 0, "LVB1");
        Write(data, 0x0C, "SCN1");
        BitConverter.GetBytes(FolderOffset).CopyTo(data, 0x20);
        BitConverter.GetBytes(sets.Count).CopyTo(data, folder + 4);

        for (var i = 0; i < sets.Count; i++)
        {
            var at = folder + 0x0C + (i * RecordSize);
            BitConverter.GetBytes(sets[i].Set).CopyTo(data, at);
            BitConverter.GetBytes(sets[i].Territory).CopyTo(data, at + 0x0C);
        }

        return data;
    }

    // "LGB1" header, an "LGP1" chunk of layer offsets, one layer header per entry. The 0x14 list offset is relative to the list itself.
    private static byte[] LevelFile(IReadOnlyList<uint[]> layerSets)
    {
        const int layerSize = 0x40;
        var table = ChunkData + 0x20;
        var layersAt = table + (layerSets.Count * 4);
        var setsAt = layersAt + (layerSets.Count * layerSize);

        var total = setsAt;
        foreach (var sets in layerSets)
            total += sets.Length * 4;

        var data = new byte[total];
        Write(data, 0, "LGB1");
        Write(data, 0x0C, "LGP1");
        BitConverter.GetBytes(table - ChunkData).CopyTo(data, 0x1C);
        BitConverter.GetBytes(layerSets.Count).CopyTo(data, 0x20);

        var nextSet = setsAt;
        for (var i = 0; i < layerSets.Count; i++)
        {
            var layerAt = layersAt + (i * layerSize);
            BitConverter.GetBytes(layerAt - table).CopyTo(data, table + (i * 4));

            var listAt = layerAt + 0x30;
            BitConverter.GetBytes(listAt - layerAt).CopyTo(data, layerAt + 0x14);
            BitConverter.GetBytes(nextSet - listAt).CopyTo(data, listAt + 4);
            BitConverter.GetBytes(layerSets[i].Length).CopyTo(data, listAt + 8);

            foreach (var set in layerSets[i])
            {
                BitConverter.GetBytes(set).CopyTo(data, nextSet);
                nextSet += 4;
            }
        }

        return data;
    }

    private static void Write(byte[] data, int at, string magic)
    {
        for (var i = 0; i < magic.Length; i++)
            data[at + i] = (byte)magic[i];
    }
}
