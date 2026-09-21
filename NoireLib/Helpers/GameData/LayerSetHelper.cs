using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Answers which territory a placed object belongs to when several territories share one level directory.<br/>
/// Layers list their layer sets and the <c>.lvb</c> file maps each set to a TerritoryType row. Both are read from the bytes: Lumina's <c>Layer.LayerSetReferences</c> resolves against the wrong base.
/// </summary>
public static class LayerSetHelper
{
    // Id at 0x00, TerritoryType row at 0x0C, the rest internal offsets.
    private const int LayerSetRecordSize = 0x1C;
    private const int LayerSetTerritoryOffset = 0x0C;

    // Both containers put their chunk data at 0x14. Every offset inside a chunk is relative to it.
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

            var data = NoireService.DataManager.GetFile(directory + fileName)?.Data;
            return data == null
                ? (IReadOnlyList<IReadOnlyList<uint>>)[]
                : ResolveLayerTerritories(territoryBg, LayerGroupHelper.ReadLevel(data));
        }, []) ?? [];
    }

    internal static IReadOnlyList<IReadOnlyList<uint>> ResolveLayerTerritories(
        string territoryBg,
        IReadOnlyList<LayerGroupLayer> layers)
    {
        if (layers.Count == 0)
            return [];

        var owners = OwnerIndex(ReadLayerSets(territoryBg));
        var result = new IReadOnlyList<uint>[layers.Count];
        for (var i = 0; i < layers.Count; i++)
            result[i] = Resolve(layers[i].LayerSetIds, owners);

        return result;
    }

    /// <summary>
    /// Whether a layer belonging to these territories is part of one of them. A layer that names none is
    /// unconditional. It belongs to every territory sharing the level.
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

    // A set the table does not describe leaves the layer unconditional.
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

    // Named for its level directory, the last segment of the Bg value.
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
