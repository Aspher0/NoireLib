using Dalamud.Utility;
using Lumina.Excel.Sheets;
using NoireLib.Helpers;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// Which file an ActionTimeline row plays and which row the game substitutes for another, read from the sheets
/// once and cached. Nothing here reads or writes character state.
/// </summary>
public static class ActionTimelineHelper
{
    /// <summary> The folder every shared, skeleton-relative combat and emote animation lives under. </summary>
    private const string SharedAnimationFolder = "bt_common";

    private static Dictionary<ushort, ushort>? replacements;

    /// <summary>
    /// The skeleton-relative path of the pap an ActionTimeline row plays, such as <c>bt_common/emote/box.pap</c>,
    /// which combines with <see cref="EmotePathHelper.GetSkeletonPath"/> into a full game path.
    /// </summary>
    /// <param name="timelineId">The ActionTimeline row id.</param>
    /// <returns>The relative path, or null for row 0, a row that is not in the sheet, or a row with no key.</returns>
    public static string? GetRelativePapPath(ushort timelineId)
    {
        if (timelineId == 0 || !ExcelSheetHelper.TryGetRow<ActionTimeline>(timelineId, out var row) || row is not { } timeline)
            return null;

        var key = timeline.Key.ExtractText();

        return string.IsNullOrEmpty(key) ? null : $"{SharedAnimationFolder}/{key}.pap";
    }

    /// <summary>
    /// The row the game plays instead of <paramref name="timelineId"/> when the ActionTimelineReplace sheet names
    /// one, such as the underwater variant of a surface emote, or the same row when it does not.
    /// </summary>
    /// <param name="timelineId">The ActionTimeline row id to look up.</param>
    /// <returns>The replacement row id, or <paramref name="timelineId"/> unchanged.</returns>
    public static ushort GetReplacement(ushort timelineId)
        => Replacements().TryGetValue(timelineId, out var replacement) ? replacement : timelineId;

    /// <summary> Whether the sheet names a different row for this one. </summary>
    /// <param name="timelineId">The ActionTimeline row id to look up.</param>
    /// <param name="replacement">The replacement row id, when there is one.</param>
    /// <returns>True when a replacement exists.</returns>
    public static bool TryGetReplacement(ushort timelineId, out ushort replacement)
        => Replacements().TryGetValue(timelineId, out replacement);

    private static Dictionary<ushort, ushort> Replacements()
    {
        // Published with a single reference assignment: racing threads build the same table from an immutable
        // sheet, so the loser's copy is wasted rather than wrong.
        var built = Volatile.Read(ref replacements);
        if (built != null)
            return built;

        built = BuildReplacements();
        Volatile.Write(ref replacements, built);

        return built;
    }

    private static Dictionary<ushort, ushort> BuildReplacements()
    {
        var map = new Dictionary<ushort, ushort>();
        var sheet = ExcelSheetHelper.GetSheet<ActionTimelineReplace>();

        if (sheet == null)
            return map;

        foreach (var row in sheet)
        {
            var oldId = (ushort)row.Old.RowId;
            var newId = (ushort)row.New.RowId;

            // Plenty of rows map a timeline to itself; those carry no information.
            if (oldId != 0 && newId != 0 && oldId != newId)
                map[oldId] = newId;
        }

        return map;
    }
}
