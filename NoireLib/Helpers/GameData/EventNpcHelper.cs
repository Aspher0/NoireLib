using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// One pass over the <c>ENpcBase</c> sheet, indexed both ways: which event handlers an NPC runs, and which NPCs run
/// a given handler.
/// </summary>
/// <param name="HandlersByNpc">The handler ids each ENpcBase row references.</param>
/// <param name="NpcsByHandler">The ENpcBase rows that reference each handler id, in ascending row order.</param>
public sealed record EventNpcHandlerScan(
    IReadOnlyDictionary<uint, IReadOnlyList<uint>> HandlersByNpc,
    IReadOnlyDictionary<uint, IReadOnlyList<uint>> NpcsByHandler)
{
    /// <summary>An empty scan, which every lookup misses.</summary>
    public static EventNpcHandlerScan Empty { get; } = new(
        new Dictionary<uint, IReadOnlyList<uint>>(),
        new Dictionary<uint, IReadOnlyList<uint>>());
}

/// <summary>
/// Finds which event NPC runs a given handler, and where that NPC is placed.
/// </summary>
public static class EventNpcHelper
{
    /// <summary>
    /// Scans every <c>ENpcBase</c> row once and indexes the event handlers it references, filtering during the scan
    /// when <paramref name="handlerIds"/> is given. <see cref="WarpHelper.ScanEventNpcWarps"/> and
    /// <see cref="ChocoboTaxiHelper.ScanPorters(IReadOnlySet{uint}, EventNpcHandlerScan)"/> both accept a scan, so one
    /// pass serves both.
    /// </summary>
    /// <param name="handlerIds">The handler ids to keep, or null to index every reference the sheet holds.</param>
    /// <returns>The scan, or <see cref="EventNpcHandlerScan.Empty"/> when the sheet could not be read.</returns>
    public static EventNpcHandlerScan ScanHandlers(IReadOnlySet<uint>? handlerIds = null)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var byNpc = new Dictionary<uint, IReadOnlyList<uint>>();
            var byHandler = new Dictionary<uint, IReadOnlyList<uint>>();
            var sheet = ExcelSheetHelper.GetSheet<ENpcBase>();
            if (sheet == null)
                return EventNpcHandlerScan.Empty;

            foreach (var npc in sheet)
            {
                if (npc.RowId == 0)
                    continue;

                foreach (var data in npc.ENpcData)
                {
                    var handlerId = data.RowId;
                    if (handlerId == 0 || (handlerIds != null && !handlerIds.Contains(handlerId)))
                        continue;

                    Append(byNpc, npc.RowId, handlerId);
                    Append(byHandler, handlerId, npc.RowId);
                }
            }

            return new EventNpcHandlerScan(byNpc, byHandler);
        }, EventNpcHandlerScan.Empty) ?? EventNpcHandlerScan.Empty;
    }

    /// <summary>Resolves an event NPC's display name from its ENpcResident row, in the client's own language.</summary>
    /// <param name="npcBaseId">The ENpcBase row id, which is also its resident row.</param>
    /// <returns>The name, or empty when it does not resolve.</returns>
    public static string Name(uint npcBaseId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (npcBaseId != 0 && ExcelSheetHelper.TryGetRow<ENpcResident>(npcBaseId, out var row) && row is { } npc)
                return npc.Singular.ExtractText() ?? string.Empty;

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>
    /// Finds the world position of each wanted base id among one territory's level objects, keeping the first
    /// placement of each.
    /// </summary>
    /// <param name="objects">Level objects, of which the event-NPC ones are read.</param>
    /// <param name="baseIds">The ENpcBase row ids to locate.</param>
    /// <returns>The first found position per wanted base id.</returns>
    public static IReadOnlyDictionary<uint, Vector3> FindPositions(
        IReadOnlyList<LevelObject> objects,
        IReadOnlySet<uint> baseIds)
    {
        var result = new Dictionary<uint, Vector3>();
        foreach (var levelObject in objects)
        {
            if (levelObject.Kind != LevelObjectKind.EventNpc || !baseIds.Contains(levelObject.BaseId))
                continue;

            result.TryAdd(levelObject.BaseId, levelObject.Position);
        }

        return result;
    }

    /// <summary>
    /// Finds where each wanted base id stands across several territories, keeping the placement in the
    /// lowest-numbered territory so the result does not depend on enumeration order.
    /// </summary>
    /// <param name="objectsByTerritory">Each territory's placed level objects.</param>
    /// <param name="baseIds">The ENpcBase row ids to locate.</param>
    /// <returns>Each wanted base id's position and the territory it was found in.</returns>
    public static IReadOnlyDictionary<uint, (Vector3 Position, uint TerritoryId)> FindPlacements(
        IReadOnlyDictionary<uint, IReadOnlyList<LevelObject>> objectsByTerritory,
        IReadOnlySet<uint> baseIds)
    {
        var placements = new Dictionary<uint, (Vector3, uint)>();
        if (baseIds.Count == 0)
            return placements;

        foreach (var (territoryId, objects) in objectsByTerritory)
        {
            foreach (var levelObject in objects)
            {
                if (levelObject.Kind != LevelObjectKind.EventNpc || !baseIds.Contains(levelObject.BaseId))
                    continue;

                if (!placements.TryGetValue(levelObject.BaseId, out var existing) || territoryId < existing.Item2)
                    placements[levelObject.BaseId] = (levelObject.Position, territoryId);
            }
        }

        return placements;
    }

    private static void Append(Dictionary<uint, IReadOnlyList<uint>> index, uint key, uint value)
    {
        if (index.TryGetValue(key, out var existing))
        {
            ((List<uint>)existing).Add(value);
            return;
        }

        index[key] = new List<uint> { value };
    }
}
