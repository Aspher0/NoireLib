using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One layer set a level defines, and the territory it belongs to.</summary>
/// <param name="LayerSetId">The layer set's id, in the level editor's own id space.</param>
/// <param name="TerritoryId">The TerritoryType row the layer set belongs to.</param>
public readonly record struct LevelLayerSet(uint LayerSetId, uint TerritoryId);

/// <summary>
/// Answers which territory a placed object belongs to when several territories share one level directory.
/// <br/>
/// A level file's layers each list the layer sets they belong to, and the level-base (<c>.lvb</c>) file beside them
/// maps every layer set to a TerritoryType row. So a layer naming only another territory's sets is not part of this
/// territory at all, which is how a story-scene warp NPC stops reading as a permanent fixture of the zone it shares
/// a level file with.
/// <br/>
/// Both structures are read from the file bytes rather than through Lumina, whose
/// <c>Layer.LayerSetReferences</c> resolves its list against the layer instead of against the list and returns
/// unrelated numbers.
/// </summary>
public static class LayerSetHelper
{
    // The layer-set record: id at 0x00, TerritoryType row at 0x0C, the rest internal offsets.
    private const int LayerSetRecordSize = 0x1C;
    private const int LayerSetTerritoryOffset = 0x0C;

    // Both containers put their chunk data at 0x14, and every offset inside a chunk is relative to it.
    private const int ChunkDataOffset = 0x14;

    /// <summary>Reads a level's layer sets and the territory each belongs to.</summary>
    /// <param name="territoryId">Any TerritoryType row that uses the level.</param>
    /// <returns>The layer sets, empty when the level names none or the file could not be read.</returns>
    public static IReadOnlyList<LevelLayerSet> ReadLayerSets(uint territoryId)
        => ReadLayerSets(TerritoryHelper.Bg(territoryId));

    /// <inheritdoc cref="ReadLayerSets(uint)"/>
    /// <param name="territoryBg">The TerritoryType.Bg value.</param>
    /// <returns>The layer sets, empty when the level names none or the file could not be read.</returns>
    public static IReadOnlyList<LevelLayerSet> ReadLayerSets(string territoryBg)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var path = LevelBasePath(territoryBg);
            if (path == null)
                return (IReadOnlyList<LevelLayerSet>)[];

            return ParseLayerSets(NoireService.DataManager.GetFile(path)?.Data);
        }, []) ?? [];
    }

    /// <summary>
    /// The territories each layer of a level file belongs to, in the file's own layer order so it lines up with
    /// what <see cref="LevelFileHelper"/> reads. An empty entry means the layer is unconditional and belongs to
    /// every territory sharing the level.
    /// </summary>
    /// <param name="territoryBg">The TerritoryType.Bg value.</param>
    /// <param name="fileName">The level file name, one of <see cref="LevelFileHelper.Files"/>.</param>
    /// <returns>One entry per layer, empty when the file could not be read.</returns>
    public static IReadOnlyList<IReadOnlyList<uint>> ReadLayerTerritories(string territoryBg, string fileName)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var directory = LevelFileHelper.ResolveLevelDirectory(territoryBg);
            if (directory == null)
                return (IReadOnlyList<IReadOnlyList<uint>>)[];

            var owners = OwnerIndex(ReadLayerSets(territoryBg));
            var layers = ParseLayerSetReferences(NoireService.DataManager.GetFile(directory + fileName)?.Data);
            var result = new IReadOnlyList<uint>[layers.Count];
            for (var i = 0; i < layers.Count; i++)
                result[i] = Resolve(layers[i], owners);

            return (IReadOnlyList<IReadOnlyList<uint>>)result;
        }, []) ?? [];
    }

    /// <summary>
    /// Whether a layer belonging to these territories is part of one of them. A layer that names none is
    /// unconditional, so it belongs to every territory sharing the level.
    /// </summary>
    /// <param name="layerTerritories">The layer's territories, from <see cref="ReadLayerTerritories"/>.</param>
    /// <param name="territoryId">The territory being read.</param>
    /// <returns>True when the layer is part of that territory.</returns>
    public static bool Belongs(IReadOnlyList<uint>? layerTerritories, uint territoryId)
    {
        if (layerTerritories == null || layerTerritories.Count == 0)
            return true;

        for (var i = 0; i < layerTerritories.Count; i++)
        {
            if (layerTerritories[i] == territoryId)
                return true;
        }

        return false;
    }

    /// <summary>Parses the layer-set table out of a level-base file's bytes.</summary>
    /// <param name="data">The <c>.lvb</c> file bytes.</param>
    /// <returns>The layer sets, empty when the bytes are not a parseable level-base file.</returns>
    public static IReadOnlyList<LevelLayerSet> ParseLayerSets(byte[]? data)
    {
        if (data is not { Length: >= 0x24 } || !Magic(data, 0, "LVB1") || !Magic(data, 0x0C, "SCN1"))
            return [];

        var folder = ChunkDataOffset + BitConverter.ToInt32(data, 0x20);
        if (folder < 0 || folder + 12 > data.Length)
            return [];

        var count = BitConverter.ToInt32(data, folder + 4);
        var table = folder + 0x0C;
        if (count is < 0 or > 4096 || table < 0 || table + (count * LayerSetRecordSize) > data.Length)
            return [];

        var sets = new LevelLayerSet[count];
        for (var i = 0; i < count; i++)
        {
            var at = table + (i * LayerSetRecordSize);
            sets[i] = new LevelLayerSet(
                BitConverter.ToUInt32(data, at),
                BitConverter.ToUInt32(data, at + LayerSetTerritoryOffset));
        }

        return sets;
    }

    /// <summary>Parses each layer's own layer-set list out of a level file's bytes, in the file's layer order.</summary>
    /// <param name="data">The <c>.lgb</c> file bytes.</param>
    /// <returns>One list of layer-set ids per layer, empty when the bytes are not a parseable level file.</returns>
    public static IReadOnlyList<IReadOnlyList<uint>> ParseLayerSetReferences(byte[]? data)
    {
        if (data is not { Length: >= 0x24 } || !Magic(data, 0, "LGB1") || !Magic(data, 0x0C, "LGP1"))
            return [];

        var table = ChunkDataOffset + BitConverter.ToInt32(data, 0x1C);
        var layerCount = BitConverter.ToInt32(data, 0x20);
        if (layerCount is < 0 or > 100000 || table < 0 || table + (layerCount * 4) > data.Length)
            return [];

        var layers = new IReadOnlyList<uint>[layerCount];
        for (var i = 0; i < layerCount; i++)
        {
            layers[i] = [];
            var layerAt = table + BitConverter.ToInt32(data, table + (i * 4));
            if (layerAt < 0 || layerAt + 0x24 > data.Length)
                continue;

            var listAt = layerAt + BitConverter.ToInt32(data, layerAt + 0x14);
            if (listAt <= layerAt || listAt + 12 > data.Length)
                continue;

            // The list's own offset is relative to the list, not to the layer. Reading it against the layer lands
            // in the instance-object table and yields numbers that are not layer sets at all.
            var setsAt = listAt + BitConverter.ToInt32(data, listAt + 4);
            var setCount = BitConverter.ToInt32(data, listAt + 8);
            if (setCount is < 1 or > 4096 || setsAt < 0 || setsAt + (setCount * 4) > data.Length)
                continue;

            var sets = new uint[setCount];
            for (var s = 0; s < setCount; s++)
                sets[s] = BitConverter.ToUInt32(data, setsAt + (s * 4));

            layers[i] = sets;
        }

        return layers;
    }

    private static Dictionary<uint, List<uint>> OwnerIndex(IReadOnlyList<LevelLayerSet> sets)
    {
        var index = new Dictionary<uint, List<uint>>(sets.Count);
        foreach (var set in sets)
        {
            if (set.TerritoryId == 0)
                continue;

            if (!index.TryGetValue(set.LayerSetId, out var owners))
            {
                owners = [];
                index[set.LayerSetId] = owners;
            }

            owners.Add(set.TerritoryId);
        }

        return index;
    }

    // The territories a layer's sets name. A set the table does not describe rules nothing out, so the layer is
    // reported unconditional rather than attributed to a territory the level never claimed it for.
    private static IReadOnlyList<uint> Resolve(IReadOnlyList<uint> layerSets, Dictionary<uint, List<uint>> owners)
    {
        if (layerSets.Count == 0)
            return [];

        List<uint>? territories = null;
        foreach (var set in layerSets)
        {
            if (!owners.TryGetValue(set, out var found))
                return [];

            foreach (var territory in found)
            {
                territories ??= [];
                if (!territories.Contains(territory))
                    territories.Add(territory);
            }
        }

        return (IReadOnlyList<uint>?)territories ?? [];
    }

    // The level-base file is named for the level directory it sits in, which is the last segment of the Bg value.
    private static string? LevelBasePath(string territoryBg)
    {
        var directory = LevelFileHelper.ResolveLevelDirectory(territoryBg);
        if (directory == null)
            return null;

        var stem = territoryBg[(territoryBg.LastIndexOf('/') + 1)..];
        return stem.Length == 0 ? null : directory + stem + ".lvb";
    }

    private static bool Magic(byte[] data, int at, string magic)
    {
        for (var i = 0; i < magic.Length; i++)
        {
            if (data[at + i] != (byte)magic[i])
                return false;
        }

        return true;
    }
}
