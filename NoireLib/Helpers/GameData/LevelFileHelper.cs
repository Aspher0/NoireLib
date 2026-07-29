using System;
using System.Collections.Generic;
using System.Numerics;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;

namespace NoireLib.Helpers;

/// <summary>
/// Reads a territory's placed objects out of its level (<c>.lgb</c>) files: the crystals, zone lines, spawn volumes,
/// NPCs, and interactables the game lays a place out with. Resolving the level directory from a territory's <c>Bg</c>
/// string is a pure rule; the file read itself goes through the game's data archives. A missing or unreadable file
/// yields an empty result rather than throwing.
/// <br/>
/// A level file states what <i>could</i> stand in a territory, never what does: layers are switched on and off for
/// quest progress, instance, phase, and season. Use <see cref="LayoutHelper.IsInstancePlaced"/> to ask the loaded
/// game layout whether a particular placement is really standing there.
/// </summary>
public static class LevelFileHelper
{
    private const string LevelSegment = "/level/";

    /// <summary>
    /// The level files a territory is laid out across. A place's objects are spread over several of them rather than
    /// one, so anything wanting a complete picture reads more than a single file.
    /// </summary>
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
        /// file, so a warp landing in one of those places resolves to no position without it.
        /// </summary>
        public const string PlanLive = "planlive.lgb";

        /// <summary>The static scenery layout. Large and holding nothing interactable, so it is rarely worth reading.</summary>
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
    /// <param name="filter">What to keep; the default keeps every mapped kind.</param>
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
            // Read the raw bytes and validate the layer-group header BEFORE letting Lumina parse the file. A handful
            // of stub planner.lgb files are malformed: they carry the file magic but a blank chunk magic, so Lumina's
            // parser reads a garbage layer count and attempts a multi-gigabyte allocation that throws OutOfMemory. In
            // game, with the client already holding several gigabytes, each such attempt first triggers a long
            // blocking garbage collection, so seventeen of them across the world froze the client for tens of
            // seconds. Skipping them on the raw header, which never parses, avoids the bad allocation entirely.
            var raw = NoireService.DataManager.GetFile(directory + fileName);
            if (!IsParseable(raw?.Data))
                return (IReadOnlyList<LevelObject>)[];

            var lgb = NoireService.DataManager.GetFile<LgbFile>(directory + fileName);
            if (lgb == null)
                return (IReadOnlyList<LevelObject>)[];

            var list = new List<LevelObject>();
            foreach (var layer in lgb.Layers)
            {
                foreach (var instance in layer.InstanceObjects)
                {
                    // The layer, not the object, carries the seasonal condition, so it is stamped onto every object
                    // read out of that layer and travels with it.
                    var mapped = Map(instance, layer.FestivalID, layer.FestivalPhaseID);

                    // Filtering here, rather than accumulating everything first, keeps the retained set small enough
                    // to parse the whole world without running out of memory.
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
    /// <param name="filter">What to keep; the default keeps every mapped kind.</param>
    /// <returns>The placed objects, or an empty list when the territory or the file could not be read.</returns>
    public static IReadOnlyList<LevelObject> ReadObjects(
        uint territoryId,
        string fileName,
        LevelObjectFilter filter = default)
        => ReadObjects(TerritoryHelper.Bg(territoryId), fileName, filter);

    /// <summary>
    /// Reads everything interactable a territory places, merging <see cref="Files.Interactable"/> into one list.
    /// Crystals and zone lines live in the map file, arrival volumes in the event file, the trigger NPCs of a lift in
    /// the planner file, and the arrival volumes of the places the game switches on and off in the live file, so a
    /// complete picture needs all four.
    /// </summary>
    /// <param name="territoryBg">The TerritoryType.Bg string.</param>
    /// <param name="filter">What to keep; the default keeps every mapped kind.</param>
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
    /// <param name="filter">What to keep; the default keeps every mapped kind.</param>
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

    /// <summary>
    /// Indexes every spawn volume by the territory it stands in and its own instance id: a zone transition or a warp
    /// names a destination territory and a PopRange instance within it, and nothing else ties those two numbers to a
    /// position. An <see cref="LevelExitKind.IntraZoneTeleport"/> names no territory, so it is looked up under the
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

    // A well-formed layer group starts with file magic "LGB1" and, at offset 0x0C, chunk magic "LGP1". The
    // malformed stub files carry the file magic but a blank chunk magic, so the chunk magic is the reliable check.
    private static bool IsParseable(byte[]? data)
    {
        return data is { Length: >= 0x10 }
            && data[0] == (byte)'L' && data[1] == (byte)'G' && data[2] == (byte)'B' && data[3] == (byte)'1'
            && data[0x0C] == (byte)'L' && data[0x0D] == (byte)'G' && data[0x0E] == (byte)'P' && data[0x0F] == (byte)'1';
    }

    // Lumina names only trigger type 1, the zone line. Type 2 is unnamed by Lumina: it carries no destination
    // territory and moves the character within its own territory, so it must be read as a raw value.
    private static LevelExitKind MapExitKind(LayerCommon.ExitRangeInstanceObject exit)
    {
        return (int)exit.ExitType switch
        {
            1 => LevelExitKind.ZoneLine,
            2 => LevelExitKind.IntraZoneTeleport,
            _ => LevelExitKind.None,
        };
    }

    private static LevelObject Map(LayerCommon.InstanceObject instance, ushort festivalId, ushort festivalPhase)
    {
        var translation = instance.Transform.Translation;
        var position = new Vector3(translation.X, translation.Y, translation.Z);

        switch (instance.AssetType)
        {
            case LayerEntryType.ExitRange when instance.Object is LayerCommon.ExitRangeInstanceObject exit:
                // An ExitRange is a box the character steps through; its rotation and scale reconstruct the boundary
                // wall, so the crossing can be described as a surface rather than as a single point.
                var rotation = instance.Transform.Rotation;
                var scale = instance.Transform.Scale;
                return new LevelObject(LevelObjectKind.ExitRange, instance.InstanceId, position,
                    DestTerritoryId: exit.TerritoryType, DestInstanceId: exit.DestInstanceId,
                    Yaw: rotation.Y, Scale: new Vector3(scale.X, scale.Y, scale.Z),
                    FestivalId: festivalId, FestivalPhase: festivalPhase,
                    ExitKind: MapExitKind(exit), ReturnInstanceId: exit.ReturnInstanceId);

            case LayerEntryType.PopRange:
                return new LevelObject(LevelObjectKind.PopRange, instance.InstanceId, position,
                    FestivalId: festivalId, FestivalPhase: festivalPhase);

            case LayerEntryType.Aetheryte when instance.Object is LayerCommon.AetheryteInstanceObject aetheryte:
                // The placed crystal carries its Aetheryte sheet row id, so a position is matched to a row by id
                // alone, with no marker, map projection, or name matching.
                return new LevelObject(LevelObjectKind.Aetheryte, instance.InstanceId, position,
                    BaseId: aetheryte.ParentData.BaseId,
                    FestivalId: festivalId, FestivalPhase: festivalPhase);

            case LayerEntryType.SharedGroup when instance.Object is LayerCommon.SharedGroupInstanceObject shared:
                return new LevelObject(LevelObjectKind.SharedGroup, instance.InstanceId, position,
                    AssetPath: shared.AssetPath ?? string.Empty,
                    FestivalId: festivalId, FestivalPhase: festivalPhase);

            case LayerEntryType.EventNPC when instance.Object is LayerCommon.ENPCInstanceObject npc:
                return new LevelObject(LevelObjectKind.EventNpc, instance.InstanceId, position,
                    BaseId: npc.ParentData.ParentData.BaseId,
                    FestivalId: festivalId, FestivalPhase: festivalPhase);

            case LayerEntryType.EventObject when instance.Object is LayerCommon.EventInstanceObject eventObject:
                // The placed object carries its EObj sheet row id, linking it to the event handler it runs. An
                // object whose handler is a Warp row is the "exit to somewhere" interactable that leaves nearly
                // every instance, reached the same way as an NPC's warp handler.
                return new LevelObject(LevelObjectKind.EventObject, instance.InstanceId, position,
                    BaseId: eventObject.ParentData.BaseId,
                    FestivalId: festivalId, FestivalPhase: festivalPhase);

            default:
                return new LevelObject(LevelObjectKind.Other, instance.InstanceId, position);
        }
    }
}
