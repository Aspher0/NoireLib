using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// One pass over the <c>ENpcBase</c> sheet, indexed both ways: which event handlers an NPC runs, and which NPCs run
/// a given handler. Scanning by handler rather than by name picks only NPCs that actually run it, not every NPC
/// sharing a name.
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
/// Finds event NPCs by what they do and where they stand: the sheet scan answers which NPC runs an event, and the
/// level-object lookups answer where that NPC is placed.
/// </summary>
public static class EventNpcHelper
{
    /// <summary>
    /// Scans every <c>ENpcBase</c> row once and indexes the event handlers it references.<br/>
    /// The sheet holds tens of thousands of references, so pass <paramref name="handlerIds"/> to filter during the
    /// scan when only a known set is wanted. <see cref="WarpHelper.ScanEventNpcWarps"/> and
    /// <see cref="ChocoboTaxiHelper.ScanPorters"/> both accept a scan, so one pass can serve both.
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

    /// <summary>
    /// Finds the world position of each wanted base id among a set of level objects, keeping the first placement of
    /// each. Use it when the objects all come from one territory.
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
    /// Finds where each wanted base id stands across several territories, keeping the placement in the lowest-numbered
    /// territory rather than whichever the enumeration reached first, so the same game files always give the same
    /// answer.
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
