using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>
/// Finds the interactables the game's data describes as warping the character to a set landing spot.
/// Warps are matched by the event handler they run rather than by name, so the scan is language independent.
/// Every read is guarded and a missing sheet yields empty.
/// </summary>
public static class WarpHelper
{
    // The WarpCondition.CompleteParam value meaning every quest the condition names must be complete; any other
    // value means any one of them will do.
    private const byte AllOfMode = 1;

    /// <summary>
    /// The Warp rows that name a destination territory, which is the set of handler ids an interactable has to
    /// reference to be a warp trigger, and the filter <see cref="EventNpcHelper.ScanHandlers"/> takes.
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
    /// The warps triggered by talking to an event NPC, keyed by the ENpcBase row that triggers them, where one NPC
    /// can carry several.
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
    /// The warps triggered by touching an event object, keyed by the EObj row that triggers them.
    /// An object either runs a warp itself or runs an array handler holding it, and a large share of doors and
    /// teleporters use the second form; both are covered here, as are the <c>WKSWarp</c> rows.
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

            // Neither of the two forms above reaches the WKSWarp rows.
            foreach (var (objectId, definitions) in ScanCosmicWarps())
            {
                foreach (var definition in definitions)
                    Append(warps, objectId, definition);
            }

            return (IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>>)warps;
        }, empty) ?? empty;
    }

    /// <summary>
    /// The warps wired through the <c>WKSWarp</c> table rather than through an event object's own handler, which is
    /// how Cosmic Exploration's elevators are built and is already folded into <see cref="ScanEventObjectWarps"/>.
    /// The two columns are unnamed in the schema and read positionally, the first being the EObj row and the second
    /// the Warp row.
    /// </summary>
    /// <returns>The warp definitions keyed by triggering EObj row id.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>> ScanCosmicWarps()
    {
        var empty = (IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>>)new Dictionary<uint, IReadOnlyList<WarpDefinition>>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var sheet = ExcelSheetHelper.GetSheet<WKSWarp>();
            var warpSheet = ExcelSheetHelper.GetSheet<Warp>();
            if (sheet == null || warpSheet == null)
                return empty;

            var warps = new Dictionary<uint, IReadOnlyList<WarpDefinition>>();
            foreach (var row in sheet)
            {
                var objectId = row.Unknown0;
                var warpId = row.Unknown1;
                if (objectId == 0 || warpId == 0)
                    continue;

                if (!warpSheet.TryGetRow(warpId, out var warp) || warp.TerritoryType.RowId == 0)
                    continue;

                Append(warps, objectId, Build(objectId, WarpTriggerKind.EventObject, warpId, warp));
            }

            return (IReadOnlyDictionary<uint, IReadOnlyList<WarpDefinition>>)warps;
        }, empty) ?? empty;
    }

    /// <summary>
    /// The array event handlers that hold a warp, keyed by handler id, an array handler being a list of other handler
    /// ids an interactable runs.
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
    /// Resolves a <c>WarpLogic</c> row's script name, such as <c>WarpInnLimsaLominsa</c>, which is not localised and
    /// so is safe to use as a classifier.
    /// Most warps share the generic rows whose name is empty, so an empty result means an ordinary warp rather than a
    /// failed lookup.
    /// </summary>
    /// <param name="logicId">The WarpLogic row id, from <see cref="WarpDefinition.LogicId"/>.</param>
    /// <returns>The script name, or empty when the row is generic or does not resolve.</returns>
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
    /// Reads a <c>WarpLogic</c> row whole: the script it names, the confirmation it shows, and the arguments it
    /// hands that script.
    /// </summary>
    /// <param name="logicId">The WarpLogic row id, from <see cref="WarpDefinition.LogicId"/>.</param>
    /// <returns>The row, or null when it does not resolve.</returns>
    public static WarpLogicInfo? ReadLogic(uint logicId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (logicId == 0 || !ExcelSheetHelper.TryGetRow<WarpLogic>(logicId, out var row) || row is not { } logic)
                return (WarpLogicInfo?)null;

            return new WarpLogicInfo(
                logicId,
                logic.WarpName.ExtractText() ?? string.Empty,
                logic.Question.ExtractText() ?? string.Empty,
                logic.ResponseYes.ExtractText() ?? string.Empty,
                logic.ResponseNo.ExtractText() ?? string.Empty,
                logic.CanSkipCutscene,
                ReadParams(logic));
        }, null);
    }

    /// <summary>
    /// Whether a warp's logic arguments gate it on content the character may not have.
    /// Decided from the argument name, since the column is an untyped row reference: <c>QST_</c> and <c>QUEST_</c>
    /// name a quest and <c>ITEM_</c> an item, while <c>QST_SEQ_</c> is a sequence number and <c>HOWTO_</c> a tutorial
    /// popup, neither of which gates anything.
    /// This reports that a gate exists, not whether it is passed, since how the script combines its arguments is not
    /// in the sheets.
    /// </summary>
    /// <param name="logic">The logic row, from <see cref="ReadLogic"/>.</param>
    /// <returns>True when any argument names a quest or an item.</returns>
    public static bool NamesContentGate(WarpLogicInfo logic)
    {
        foreach (var param in logic.Params)
        {
            if (IsContentGate(param.Function))
                return true;
        }

        return false;
    }

    /// <inheritdoc cref="NamesContentGate(WarpLogicInfo)"/>
    /// <param name="definition">The warp, whose <see cref="WarpDefinition.LogicParams"/> are read.</param>
    /// <returns>True when any argument names a quest or an item.</returns>
    public static bool NamesContentGate(WarpDefinition definition)
    {
        var params_ = definition.LogicParams;
        if (params_ == null)
            return false;

        for (var i = 0; i < params_.Count; i++)
        {
            if (IsContentGate(params_[i].Function))
                return true;
        }

        return false;
    }

    private static bool IsContentGate(string function)
        => !function.StartsWith("QST_SEQ_", StringComparison.Ordinal)
           && (function.StartsWith("QST_", StringComparison.Ordinal)
               || function.StartsWith("QUEST_", StringComparison.Ordinal)
               || function.StartsWith("ITEM_", StringComparison.Ordinal));

    private static IReadOnlyList<WarpLogicParam> ReadParams(WarpLogic logic)
    {
        List<WarpLogicParam>? read = null;

        foreach (var param in logic.WarpParams)
        {
            var function = param.Function.ExtractText();
            if (string.IsNullOrEmpty(function))
                continue;

            read ??= [];
            read.Add(new WarpLogicParam(function, param.Argument.RowId));
        }

        return read ?? [];
    }

    /// <summary>
    /// Resolves a warp's display text in the client's current language from ids rather than from frozen text.
    /// An event object is labelled by the object name; an NPC warp takes the warp name, then its confirmation
    /// question, then the NPC name, since some rows carry neither text of their own.
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

            if (triggerKind == WarpTriggerKind.EventNpc
                && triggerBaseId != 0
                && ExcelSheetHelper.TryGetRow<ENpcResident>(triggerBaseId, out var npcRow)
                && npcRow is { } npc)
            {
                var npcName = npc.Singular.ExtractText();
                if (!string.IsNullOrWhiteSpace(npcName))
                    return npcName;
            }

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    // CompleteParam is the mode the condition combines its quests in, not how many are needed: 1 means all of them,
    // and anything else is read as any one of them, the looser reading that cannot silently remove a passage.
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

        // Read here rather than left to the caller: for three warps these arguments are the only gate there is.
        var logicParams = warp.WarpLogic.ValueNullable is { } logic ? ReadParams(logic) : [];

        return new WarpDefinition(triggerBaseId, triggerKind, warpRowId, warp.TerritoryType.RowId,
            warp.PopRange.RowId, gil, classLevel, quests, threshold, warp.WarpLogic.RowId, logicParams);
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
