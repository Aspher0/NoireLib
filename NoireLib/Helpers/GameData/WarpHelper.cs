using System;
using System.Collections.Generic;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>
/// What kind of placed interactable triggers a warp. Both kinds reach their warp through the same event handler, so
/// they read identically out of the sheets, but they are interacted with differently and are worth telling apart.
/// </summary>
public enum WarpTriggerKind : byte
{
    /// <summary>An event NPC the character talks to, such as a ferry ticketer or a lift attendant.</summary>
    EventNpc,

    /// <summary>
    /// An event object the character touches, such as the "exit to somewhere" object that leaves an instance. These
    /// are targetable objects rather than people, and they are how most solo duties and story zones are left.
    /// </summary>
    EventObject,
}

/// <summary>
/// One warp read out of the <c>Warp</c> sheet and the interactable that triggers it: where it goes, what it costs,
/// and what has to be true for it to open.
/// </summary>
/// <param name="TriggerBaseId">The ENpcBase or EObj row id of the interactable that triggers this warp.</param>
/// <param name="TriggerKind">Whether the trigger is an event NPC or an event object.</param>
/// <param name="WarpRowId">The Warp sheet row id, unique per warp.</param>
/// <param name="DestTerritoryId">The territory the warp lands in. The same territory for an in-place lift, another for a ferry.</param>
/// <param name="ArrivalInstanceId">
/// The destination PopRange instance id, resolved to a position against the destination territory's own level file
/// through <see cref="LevelFileHelper.BuildPopRangeIndex"/>.
/// </param>
/// <param name="GilCost">
/// The warp's gil fare from its <c>WarpCondition</c>, or zero when it is free. This is what the ride costs, not a sum
/// the character must already be carrying.
/// </param>
/// <param name="ClassLevel">The class or job level the warp's condition requires, or zero when it needs none.</param>
/// <param name="RequiredQuests">
/// The quests the warp's condition names, empty when it names none. A <c>WarpCondition</c> lists up to four.
/// </param>
/// <param name="QuestThreshold">
/// How many of <see cref="RequiredQuests"/> must be complete. The condition states this as a mode rather than a
/// number, so it is only ever one (any one of them opens the warp, an unlock reachable by several storylines) or the
/// full count (all of them are needed).
/// </param>
/// <param name="LogicId">
/// The warp's <c>WarpLogic</c> row, which names the family it belongs to. Nearly every warp in the game shares one
/// generic family; the handful that do not are the inn warps, the two housing doors, the rental-chocobo desks, the
/// wedding desk, and the story portal networks. Zero when the warp names no logic row. Resolve it with
/// <see cref="WarpHelper.LogicName"/>.
/// </param>
public readonly record struct WarpDefinition(
    uint TriggerBaseId,
    WarpTriggerKind TriggerKind,
    uint WarpRowId,
    uint DestTerritoryId,
    uint ArrivalInstanceId,
    int GilCost,
    int ClassLevel,
    IReadOnlyList<uint> RequiredQuests,
    int QuestThreshold,
    uint LogicId = 0);

/// <summary>
/// Finds the warps the game's own data describes: an interactable that teleports the character to a set landing spot.
/// Warps are found by the event handler they run rather than by name, so scanning picks up every one of them in every
/// client language and takes nothing that merely looks like one. Every read is guarded; a missing sheet yields empty.
/// </summary>
public static class WarpHelper
{
    // The WarpCondition.CompleteParam value meaning every quest the condition names must be complete. Any other
    // value means any one of them will do; see Build for the rows that establish it.
    private const byte AllOfMode = 1;

    /// <summary>
    /// The Warp rows that name a destination territory, which is the set of handler ids an interactable has to
    /// reference to be a warp trigger. Hand it to <see cref="EventNpcHelper.ScanHandlers"/> to keep that scan small.
    /// </summary>
    /// <returns>The Warp row ids.</returns>
    public static IReadOnlySet<uint> ReadWarpIds()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var ids = new HashSet<uint>();
            var sheet = ExcelSheetHelper.GetSheet<Warp>();
            if (sheet == null)
                return (IReadOnlySet<uint>)ids;

            foreach (var warp in sheet)
            {
                if (warp.RowId != 0 && warp.TerritoryType.RowId != 0)
                    ids.Add(warp.RowId);
            }

            return (IReadOnlySet<uint>)ids;
        }, new HashSet<uint>()) ?? new HashSet<uint>();
    }

    /// <summary>
    /// The warps triggered by talking to an event NPC, keyed by the ENpcBase row that triggers them. One NPC can
    /// carry several, which is a lift attendant's menu of rides.
    /// </summary>
    /// <param name="scan">
    /// A pre-built <see cref="EventNpcHandlerScan"/> to read from, so one sheet pass can serve several consumers.
    /// Null scans the sheet here, filtered to the warp handlers.
    /// </param>
    /// <returns>The warp definitions keyed by triggering ENpcBase row id.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>> ScanEventNpcWarps(EventNpcHandlerScan? scan = null)
    {
        var empty = (IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>>)new Dictionary<uint, IReadOnlyList<WarpDefinition>>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var warpSheet = ExcelSheetHelper.GetSheet<Warp>();
            if (warpSheet == null)
                return empty;

            var handlers = (scan ?? EventNpcHelper.ScanHandlers(ReadWarpIds())).HandlersByNpc;
            var warps = new Dictionary<uint, IReadOnlyList<WarpDefinition>>();
            foreach (var (npcId, handlerIds) in handlers)
            {
                foreach (var handlerId in handlerIds)
                {
                    if (!warpSheet.TryGetRow(handlerId, out var warp) || warp.TerritoryType.RowId == 0)
                        continue;

                    Append(warps, npcId, Build(npcId, WarpTriggerKind.EventNpc, handlerId, warp));
                }
            }

            return (IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>>)warps;
        }, empty) ?? empty;
    }

    /// <summary>
    /// The warps triggered by touching an event object, keyed by the EObj row that triggers them. These are the "exit
    /// to somewhere" objects: the targetable things, not people, that leave a solo duty, a story zone, a guild hall,
    /// or a private area, and they are how most instances are left on foot.<br/>
    /// An object either runs a warp itself or runs an array handler that holds the warp it triggers. The second form
    /// is how a large share of doors and teleporters are wired, so reading only the direct form misses them entirely
    /// even though their destination is right there in the data.
    /// </summary>
    /// <returns>The warp definitions keyed by triggering EObj row id.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>> ScanEventObjectWarps()
    {
        var empty = (IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>>)new Dictionary<uint, IReadOnlyList<WarpDefinition>>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var objectSheet = ExcelSheetHelper.GetSheet<EObj>();
            var warpSheet = ExcelSheetHelper.GetSheet<Warp>();
            if (objectSheet == null || warpSheet == null)
                return empty;

            var indirect = ScanArrayHandlerWarps();
            var warps = new Dictionary<uint, IReadOnlyList<WarpDefinition>>();
            foreach (var eventObject in objectSheet)
            {
                if (eventObject.RowId == 0)
                    continue;

                var handlerId = eventObject.Data.RowId;
                if (handlerId == 0)
                    continue;

                var warpIds = warpSheet.HasRow(handlerId) ? [handlerId] : indirect.GetValueOrDefault(handlerId);
                if (warpIds == null)
                    continue;

                foreach (var warpId in warpIds)
                {
                    if (!warpSheet.TryGetRow(warpId, out var warp) || warp.TerritoryType.RowId == 0)
                        continue;

                    Append(warps, eventObject.RowId, Build(eventObject.RowId, WarpTriggerKind.EventObject, warpId, warp));
                }
            }

            return (IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>>)warps;
        }, empty) ?? empty;
    }

    /// <summary>
    /// The array event handlers that hold a warp, keyed by handler id. An array handler is a small list of other
    /// handler ids an interactable runs; when one of them is a Warp row, interacting with the object warps the
    /// character exactly as a direct warp handler would.
    /// </summary>
    /// <returns>The Warp row ids each array handler holds.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<uint>> ScanArrayHandlerWarps()
    {
        var empty = (IReadOnlyDictionary<uint, IReadOnlyList<uint>>)new Dictionary<uint, IReadOnlyList<uint>>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var warpSheet = ExcelSheetHelper.GetSheet<Warp>();
            var sheet = ExcelSheetHelper.GetSheet<ArrayEventHandler>();
            if (warpSheet == null || sheet == null)
                return empty;

            var result = new Dictionary<uint, IReadOnlyList<uint>>();
            foreach (var handler in sheet)
            {
                if (handler.RowId == 0)
                    continue;

                List<uint>? found = null;
                foreach (var entry in handler.Data)
                {
                    var id = entry.RowId;
                    if (id == 0 || !warpSheet.HasRow(id))
                        continue;

                    found ??= [];
                    found.Add(id);
                }

                if (found != null)
                    result[handler.RowId] = found;
            }

            return (IReadOnlyDictionary<uint, IReadOnlyList<uint>>)result;
        }, empty) ?? empty;
    }

    /// <summary>Reads one warp by its row id, for a trigger already known.</summary>
    /// <param name="warpRowId">The Warp sheet row id.</param>
    /// <param name="triggerBaseId">The triggering interactable's row id, or zero when it is not known.</param>
    /// <param name="triggerKind">Whether the trigger is an event NPC or an event object.</param>
    /// <returns>The definition, or null when the row does not resolve or names no destination.</returns>
    public static WarpDefinition? ReadDefinition(
        uint warpRowId,
        uint triggerBaseId = 0,
        WarpTriggerKind triggerKind = WarpTriggerKind.EventNpc)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (warpRowId != 0 && ExcelSheetHelper.TryGetRow<Warp>(warpRowId, out var row) && row is { } warp
                && warp.TerritoryType.RowId != 0)
            {
                return Build(triggerBaseId, triggerKind, warpRowId, warp);
            }

            return (WarpDefinition?)null;
        }, null);
    }

    /// <summary>
    /// Resolves a <c>WarpLogic</c> row's own name, which is the internal family a warp belongs to rather than
    /// anything shown to a player: <c>WarpInnLimsaLominsa</c>, <c>WarpHousingEnterHouse</c>,
    /// <c>WarpRentalChocobo</c>, <c>WarpSEElpis</c>, and so on. It is not localised and never changes with the
    /// client language, so it is safe to use as a classifier.<br/>
    /// The overwhelming majority of warps share one generic family whose name is empty, so an empty result means
    /// "an ordinary warp" rather than a failed lookup.
    /// </summary>
    /// <param name="logicId">The WarpLogic row id, from <see cref="WarpDefinition.LogicId"/>.</param>
    /// <returns>The family name, or empty when the row is generic or does not resolve.</returns>
    public static string LogicName(uint logicId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (logicId != 0 && ExcelSheetHelper.TryGetRow<WarpLogic>(logicId, out var row) && row is { } logic)
                return logic.WarpName.ExtractText() ?? string.Empty;

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>
    /// Resolves a warp's display text in the client's own language, from ids rather than from text frozen when the
    /// data was read. An event object is named by the object itself ("exit to the Rising Stones"), which reads better
    /// than the confirmation the warp asks; an NPC warp has no object name, so it takes the warp's own name and falls
    /// back to its confirmation question.
    /// </summary>
    /// <param name="definition">The warp to label.</param>
    /// <returns>The label, or empty when nothing resolves.</returns>
    public static string Label(WarpDefinition definition)
        => Label(definition.WarpRowId, definition.TriggerBaseId, definition.TriggerKind);

    /// <inheritdoc cref="Label(WarpDefinition)"/>
    /// <param name="warpRowId">The Warp sheet row id.</param>
    /// <param name="triggerBaseId">The triggering interactable's row id.</param>
    /// <param name="triggerKind">Whether the trigger is an event NPC or an event object.</param>
    /// <returns>The label, or empty when nothing resolves.</returns>
    public static string Label(uint warpRowId, uint triggerBaseId, WarpTriggerKind triggerKind)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (triggerKind == WarpTriggerKind.EventObject
                && triggerBaseId != 0
                && ExcelSheetHelper.TryGetRow<EObjName>(triggerBaseId, out var objectRow)
                && objectRow is { } eobjName)
            {
                var objectName = eobjName.Singular.ExtractText();
                if (!string.IsNullOrWhiteSpace(objectName))
                    return objectName;
            }

            if (warpRowId != 0 && ExcelSheetHelper.TryGetRow<Warp>(warpRowId, out var warpRow) && warpRow is { } warp)
            {
                var name = warp.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;

                var question = warp.Question.ExtractText();
                if (!string.IsNullOrWhiteSpace(question))
                    return question;
            }

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    // The fare and the unlock come from the warp's WarpCondition. Its Gil is what the ride costs, not something the
    // character must already be carrying, so it is a price rather than a condition; the actual unlocks are the quests
    // and the class level.
    // CompleteParam is the MODE the condition combines its quests in, not how many of them are needed. Only four
    // rows in the sheet name more than one quest, and they settle it: the two airship rows and the ferry row name the
    // three city envoy quests with param 2, and a character only ever completes the envoy quest of the city they
    // started in, so any reading that demands two of them locks every inter-city airship for everyone. The row that
    // names two quests with param 1 gates the Gold Saucer passage from Ishgard behind both being in Ishgard at all
    // and having unlocked the Saucer, which genuinely needs both. So param 1 is "all of these" and param 2 is "any
    // one of these"; anything else is read as "any one", the looser reading, since a wrong strict answer silently
    // removes a passage while a wrong loose one merely offers a trip the game then declines.
    private static WarpDefinition Build(uint triggerBaseId, WarpTriggerKind triggerKind, uint warpRowId, Warp warp)
    {
        var gil = 0;
        var classLevel = 0;
        IReadOnlyList<uint> quests = [];
        var threshold = 0;

        if (warp.WarpCondition.ValueNullable is { } condition)
        {
            gil = condition.Gil;
            if (condition.ClassLevel > 0)
                classLevel = condition.ClassLevel;

            var required = new List<uint>(4);
            foreach (var questRef in new[]
                     {
                         condition.RequiredQuest1, condition.RequiredQuest2,
                         condition.RequiredQuest3, condition.RequiredQuest4,
                     })
            {
                if (questRef.RowId != 0)
                    required.Add(questRef.RowId);
            }

            if (required.Count > 0)
            {
                quests = required;
                threshold = condition.CompleteParam == AllOfMode ? required.Count : 1;
            }
        }

        return new WarpDefinition(triggerBaseId, triggerKind, warpRowId, warp.TerritoryType.RowId,
            warp.PopRange.RowId, gil, classLevel, quests, threshold, warp.WarpLogic.RowId);
    }

    private static void Append(Dictionary<uint, IReadOnlyList<WarpDefinition>> index, uint key, WarpDefinition value)
    {
        if (index.TryGetValue(key, out var existing))
        {
            ((List<WarpDefinition>)existing).Add(value);
            return;
        }

        index[key] = new List<WarpDefinition> { value };
    }
}
