using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>The current Diadem season as the sheets describe it.</summary>
/// <param name="TerritoryId">The TerritoryType row the season runs in.</param>
/// <param name="ContentFinderConditionId">The season's ContentFinderCondition row.</param>
/// <param name="JobCategoryId">The ClassJobCategory a class must belong to for entry.</param>
/// <param name="JobLevel">The class level required for entry.</param>
public readonly record struct DiademEntry(uint TerritoryId, uint ContentFinderConditionId, uint JobCategoryId, int JobLevel);

/// <summary>
/// Reads what the game's own data says about the Diadem: which season is current and what entering it requires.
/// The Diadem is content entered by talking to Aurvael in the Firmament rather than through the duty finder, so
/// its territory carries a ContentFinderCondition but no queue. Every read is guarded; a missing sheet yields null.
/// </summary>
public static class DiademHelper
{
    // The PublicContentType row of the Ishgardian Restoration Diadem. Its rows are the gathering seasons; each
    // later season takes a later row, so the highest row of this type is the one Aurvael currently opens.
    private const uint DiademPublicContentType = 6;

    // The script name of Aurvael's entry service. A CustomTalk's name is its script identifier, never localised.
    private const string EntranceTalkName = "CtsHwdSkyIsland";

    /// <summary>Reads the current Diadem season: the highest Diadem-typed PublicContent row and its entry conditions.</summary>
    /// <returns>The entry, or null when the sheets do not describe one.</returns>
    public static DiademEntry? ReadCurrentEntry()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var contents = ExcelSheetHelper.GetSheet<PublicContent>();
            if (contents == null)
                return (DiademEntry?)null;

            DiademEntry? current = null;
            foreach (var content in contents)
            {
                if (content.Type.RowId != DiademPublicContentType)
                    continue;

                if (content.ContentFinderCondition.ValueNullable is not { } condition
                    || condition.TerritoryType.RowId == 0)
                    continue;

                current = new DiademEntry(
                    condition.TerritoryType.RowId,
                    content.ContentFinderCondition.RowId,
                    condition.AcceptClassJobCategory.RowId,
                    condition.ClassJobLevelRequired);
            }

            return current;
        }, null);
    }

    /// <summary>Finds Aurvael's entry service by its script name, for the NPC scan that locates who runs it.</summary>
    /// <returns>The matching CustomTalk row ids, empty when the sheet is missing.</returns>
    public static IReadOnlySet<uint> ReadEntranceTalkIds()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var ids = new HashSet<uint>();
            var sheet = ExcelSheetHelper.GetSheet<CustomTalk>();
            if (sheet == null)
                return (IReadOnlySet<uint>)ids;

            foreach (var talk in sheet)
            {
                if (talk.Name.ExtractText().StartsWith(EntranceTalkName, StringComparison.Ordinal))
                    ids.Add(talk.RowId);
            }

            return ids;
        }, new HashSet<uint>()) ?? new HashSet<uint>();
    }
}
