using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using Lumina.Data.Parsing.Layer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Reads a territory's placed objects out of its <c>.lgb</c> level files. A missing or unreadable file yields an empty result.<br/>
/// A level file states what could stand in a territory. Use <see cref="LayoutHelper.IsInstancePlaced"/> to ask whether a placement is really there.
/// </summary>
public static class LevelFileHelper
{
    private const string LevelSegment = "/level/";

    /// <summary>The level files a territory is laid out across.</summary>
    public static class Files
    {
        /// <summary>The map layout: aetheryte crystals, shared groups, and the zone-boundary ExitRanges.</summary>
        public const string PlanMap = "planmap.lgb";

        /// <summary>The event layout: the arrival PopRanges a transition or a warp lands the character in.</summary>
        public const string PlanEvent = "planevent.lgb";

        /// <summary>The planner layout: the placed NPCs, including the trigger NPCs of a lift or a ferry.</summary>
        public const string Planner = "planner.lgb";

        /// <summary>
        /// The live layout: the arrival volumes of the places whose contents the game switches on and off, such as a
        /// Grand Company barracks or a story tower's floors. It holds arrival volumes that appear in no other level
        /// file. A warp landing in one of those places resolves to no position without it.
        /// </summary>
        public const string PlanLive = "planlive.lgb";

        /// <summary>The static scenery layout. Large and holding nothing interactable. Rarely worth reading.</summary>
        public const string Background = "bg.lgb";

        /// <summary>
        /// The four files that between them hold everything a character can walk through, stand on, or talk to.
        /// This is what <see cref="ReadPlacements(string, LevelObjectFilter)"/> reads.
        /// </summary>
        public static readonly IReadOnlyList<string> Interactable = [PlanMap, PlanEvent, Planner, PlanLive];
    }

    /// <summary>Resolves the "bg/.../level/" directory a territory's level files sit in.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The level directory ending in a slash, or null when the territory names no level files.</returns>
    public static string? ResolveLevelDirectory(uint territoryId) => ResolveLevelDirectory(TerritoryHelper.Bg(territoryId));

    /// <inheritdoc cref="ResolveLevelDirectory(uint)"/>
    /// <param name="territoryBg">The TerritoryType.Bg value, e.g. "ffxiv/fst_f1/fld/f1f1/level/f1f1".</param>
    /// <returns>The level directory ending in a slash, or null when the input has no level segment.</returns>
    public static string? ResolveLevelDirectory(string territoryBg)
    {
        if (string.IsNullOrEmpty(territoryBg))
            return null;

        var cut = territoryBg.IndexOf(LevelSegment, StringComparison.Ordinal);
        if (cut < 0)
            return null;

        return "bg/" + territoryBg[..cut] + LevelSegment;
    }

    /// <summary>
    /// Resolves the territory's own asset root: the directory its <c>level/</c> and <c>collision/</c> folders sit
    /// in, as in <c>bg/ffxiv/fst_f1/fld/f1f1/</c>. This is what everything built from a territory's files is keyed
    /// on, since several territories can share one.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The asset root ending in a slash, or null when the territory names no level files.</returns>
    public static string? ResolveLevelRoot(uint territoryId) => ResolveLevelRoot(TerritoryHelper.Bg(territoryId));

    /// <inheritdoc cref="ResolveLevelRoot(uint)"/>
    /// <param name="territoryBg">The TerritoryType.Bg value, e.g. "ffxiv/fst_f1/fld/f1f1/level/f1f1".</param>
    /// <returns>The asset root ending in a slash, or null when the value has no level segment.</returns>
    public static string? ResolveLevelRoot(string territoryBg)
    {
        if (string.IsNullOrEmpty(territoryBg))
            return null;

        var cut = territoryBg.IndexOf(LevelSegment, StringComparison.Ordinal);
        return cut < 0 ? null : "bg/" + territoryBg[..cut] + "/";
    }

    /// <summary>
    /// Resolves the region root a territory's level files sit under: the first two segments of its <c>Bg</c> string
    /// (e.g. "ffxiv/wil_w1" for the Ul'dah region). Every territory a place owns is authored under one root, pairing
    /// a residential district with its own interiors without naming either.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The region root, or an empty string when the territory names no level files.</returns>
    public static string ResolveRegionRoot(uint territoryId) => ResolveRegionRoot(TerritoryHelper.Bg(territoryId));

    /// <inheritdoc cref="ResolveRegionRoot(uint)"/>
    /// <param name="territoryBg">The TerritoryType.Bg value.</param>
    /// <returns>The region root, or an empty string when the value has no two segments.</returns>
    public static string ResolveRegionRoot(string territoryBg)
    {
        if (string.IsNullOrEmpty(territoryBg))
            return string.Empty;

        var first = territoryBg.IndexOf('/');
        if (first < 0)
            return string.Empty;

        var second = territoryBg.IndexOf('/', first + 1);
        return second < 0 ? string.Empty : territoryBg[..second];
    }

    /// <summary>Reads a territory's placed objects from one of its level files.</summary>
    /// <param name="territoryBg">The TerritoryType.Bg string.</param>
    /// <param name="fileName">The level file name, one of <see cref="Files"/>.</param>
    /// <param name="filter">What to keep. The default keeps every mapped kind.</param>
    /// <returns>The placed objects, or an empty list when the file is missing or unreadable.</returns>
    public static IReadOnlyList<LevelObject> ReadObjects(
        string territoryBg,
        string fileName,
        LevelObjectFilter filter = default)
    {
        var directory = ResolveLevelDirectory(territoryBg);
        if (directory == null)
            return [];

        return SafeExecutor.ExecuteSafely(() =>
        {
            var data = NoireService.DataManager.GetFile(directory + fileName)?.Data;
            if (data == null)
                return (IReadOnlyList<LevelObject>)[];

            var layers = LayerGroupHelper.ReadLevel(data);
            if (layers.Count == 0)
                return (IReadOnlyList<LevelObject>)[];

            var territories = LayerSetHelper.ResolveLayerTerritories(territoryBg, layers);

            var list = new List<LevelObject>();
            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                var layerTerritories = territories[layerIndex];
                foreach (var entry in layer.Entries)
                {
                    // The layer carries the seasonal condition and the layer sets.
                    var mapped = Map(entry, layer.FestivalId, layer.FestivalPhase, layerTerritories, layer.Name);

                    // Filtered here to parse the whole world within memory.
                    if (filter.Keeps(mapped))
                        list.Add(mapped);
                }
            }

            return list;
        }, []) ?? [];
    }

    /// <summary>Reads a territory's placed objects from one of its level files, resolving the level path from the sheet.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="fileName">The level file name, one of <see cref="Files"/>.</param>
    /// <param name="filter">What to keep. The default keeps every mapped kind.</param>
    /// <returns>The placed objects, or an empty list when the territory or the file could not be read.</returns>
    public static IReadOnlyList<LevelObject> ReadObjects(
        uint territoryId,
        string fileName,
        LevelObjectFilter filter = default)
        => ReadObjects(TerritoryHelper.Bg(territoryId), fileName, filter);

    /// <summary>
    /// Reads everything interactable a territory places from the four <see cref="Files.Interactable"/> files.<br/>
    /// Crystals and zone lines are in the map file, arrival volumes in the event file, lift NPCs in the planner file and switchable places in the live file.
    /// </summary>
    /// <param name="territoryBg">The TerritoryType.Bg string.</param>
    /// <param name="filter">What to keep. The default keeps every mapped kind.</param>
    /// <returns>The placed objects across the four files.</returns>
    public static IReadOnlyList<LevelObject> ReadPlacements(string territoryBg, LevelObjectFilter filter = default)
    {
        var combined = new List<LevelObject>();
        foreach (var file in Files.Interactable)
            combined.AddRange(ReadObjects(territoryBg, file, filter));

        return combined;
    }

    /// <inheritdoc cref="ReadPlacements(string, LevelObjectFilter)"/>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="filter">What to keep. The default keeps every mapped kind.</param>
    public static IReadOnlyList<LevelObject> ReadPlacements(uint territoryId, LevelObjectFilter filter = default)
        => ReadPlacements(TerritoryHelper.Bg(territoryId), filter);

    /// <summary>Keeps only the objects of one kind, without allocating a query.</summary>
    /// <param name="objects">The objects to filter.</param>
    /// <param name="kind">The kind to keep.</param>
    /// <returns>The matching objects.</returns>
    public static IReadOnlyList<LevelObject> OfKind(IReadOnlyList<LevelObject> objects, LevelObjectKind kind)
    {
        var list = new List<LevelObject>();
        foreach (var levelObject in objects)
        {
            if (levelObject.Kind == kind)
                list.Add(levelObject);
        }

        return list;
    }

    /// <summary>Keeps the objects read out of a layer whose name contains a fragment, case-insensitive.</summary>
    /// <param name="objects">The objects to filter.</param>
    /// <param name="layerFragment">The fragment a layer name must contain.</param>
    /// <returns>The matching objects.</returns>
    public static IReadOnlyList<LevelObject> InLayer(IReadOnlyList<LevelObject> objects, string layerFragment)
    {
        var list = new List<LevelObject>();
        if (string.IsNullOrEmpty(layerFragment))
            return list;

        foreach (var levelObject in objects)
        {
            if (levelObject.Layer.Contains(layerFragment, StringComparison.OrdinalIgnoreCase))
                list.Add(levelObject);
        }

        return list;
    }

    /// <summary>Picks the object standing nearest a point.</summary>
    /// <param name="objects">The objects to search.</param>
    /// <param name="point">The point to measure from.</param>
    /// <param name="nearest">The nearest object when the list held one.</param>
    /// <returns>True when the list was not empty.</returns>
    public static bool TryGetNearest(IReadOnlyList<LevelObject> objects, Vector3 point, out LevelObject nearest)
    {
        nearest = default;
        var best = float.MaxValue;
        var found = false;

        foreach (var levelObject in objects)
        {
            var distance = Vector3.DistanceSquared(levelObject.Position, point);
            if (distance >= best)
                continue;

            best = distance;
            nearest = levelObject;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Indexes every spawn volume by the territory it stands in and its own instance id: a zone transition or a warp
    /// names a destination territory and a PopRange instance within it, and nothing else ties those two numbers to a
    /// position. An <see cref="LevelExitKind.IntraZoneTeleport"/> names no territory. It is looked up under the
    /// one it departs from.
    /// </summary>
    /// <param name="objectsByTerritory">Each territory's placed objects.</param>
    /// <returns>The arrival position for each (territory, PopRange instance id) pair.</returns>
    public static IReadOnlyDictionary<(uint Territory, uint InstanceId), Vector3> BuildPopRangeIndex(
        IReadOnlyDictionary<uint, IReadOnlyList<LevelObject>> objectsByTerritory)
    {
        var index = new Dictionary<(uint, uint), Vector3>();
        foreach (var (territoryId, objects) in objectsByTerritory)
        {
            foreach (var levelObject in objects)
            {
                if (levelObject.Kind == LevelObjectKind.PopRange)
                    index[(territoryId, levelObject.InstanceId)] = levelObject.Position;
            }
        }

        return index;
    }

    private static LevelExitKind MapExitKind(ExitRangeType exitType)
    {
        return exitType switch
        {
            ExitRangeType.ZoneLine => LevelExitKind.ZoneLine,
            ExitRangeType.Invisible => LevelExitKind.IntraZoneTeleport,
            _ => LevelExitKind.None,
        };
    }

    private static LevelObject Map(
        LayerGroupEntry entry,
        ushort festivalId,
        ushort festivalPhase,
        IReadOnlyList<uint>? layerTerritories,
        string layerName)
    {
        var position = entry.Translation;
        var yaw = entry.Rotation.Y;

        switch (entry.Type)
        {
            case LayerEntryType.ExitRange:
                // An ExitRange is a box the character steps through.
                return new LevelObject(LevelObjectKind.ExitRange, entry.InstanceId, position,
                    DestTerritoryId: entry.DestTerritoryId, DestInstanceId: entry.DestInstanceId,
                    Yaw: yaw, Scale: entry.Scale,
                    FestivalId: festivalId, FestivalPhase: festivalPhase,
                    ExitKind: MapExitKind(entry.ExitType), ReturnInstanceId: entry.ReturnInstanceId, Layer: layerName);

            case LayerEntryType.PopRange:
                return new LevelObject(LevelObjectKind.PopRange, entry.InstanceId, position,
                    FestivalId: festivalId, FestivalPhase: festivalPhase, LayerTerritories: layerTerritories,
                    Layer: layerName);

            case LayerEntryType.Aetheryte:
                // The placed crystal carries its Aetheryte row id.
                return new LevelObject(LevelObjectKind.Aetheryte, entry.InstanceId, position,
                    BaseId: entry.BaseId, Yaw: yaw,
                    FestivalId: festivalId, FestivalPhase: festivalPhase, LayerTerritories: layerTerritories,
                    Layer: layerName);

            case LayerEntryType.SharedGroup:
                return new LevelObject(LevelObjectKind.SharedGroup, entry.InstanceId, position,
                    AssetPath: entry.AssetPath, Yaw: yaw,
                    FestivalId: festivalId, FestivalPhase: festivalPhase, LayerTerritories: layerTerritories,
                    Layer: layerName);

            case LayerEntryType.EventNPC:
                return new LevelObject(LevelObjectKind.EventNpc, entry.InstanceId, position,
                    BaseId: entry.BaseId, Yaw: yaw,
                    FestivalId: festivalId, FestivalPhase: festivalPhase, LayerTerritories: layerTerritories,
                    Layer: layerName);

            case LayerEntryType.EventObject:
                // The object carries its EObj row id. One whose handler is a Warp row is an instance's exit.
                return new LevelObject(LevelObjectKind.EventObject, entry.InstanceId, position,
                    BaseId: entry.BaseId, Yaw: yaw,
                    FestivalId: festivalId, FestivalPhase: festivalPhase, LayerTerritories: layerTerritories,
                    Layer: layerName);

            default:
                return new LevelObject(LevelObjectKind.Other, entry.InstanceId, position,
                    LayerTerritories: layerTerritories, Layer: layerName);
        }
    }
}
