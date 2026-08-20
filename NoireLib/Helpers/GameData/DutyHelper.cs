using FFXIVClientStructs.FFXIV.Client.Game;
using ContentType = FFXIVClientStructs.FFXIV.Client.Game.Event.ContentType;
using UIState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the duty finder's description of a duty, keyed everywhere by ContentFinderCondition row id.
/// <br/>
/// Sheet reads work without a character; unlock and completion answer false without one.
/// </summary>
public static unsafe class DutyHelper
{
    private static IReadOnlyList<PropertyInfo>? cachedRouletteColumns;
    private static IReadOnlyList<RouletteInfo>? cachedRoulettes;

    /// <summary>Reads a duty out of the duty finder's sheet.</summary>
    /// <param name="conditionId">The ContentFinderCondition row id.</param>
    /// <returns>The duty, or null when the id names no duty.</returns>
    public static DutyInfo? Read(uint conditionId)
    {
        if (conditionId == 0)
            return null;

        return SafeExecutor.ExecuteSafely<DutyInfo?>(() =>
        {
            if (!ExcelSheetHelper.TryGetRow<ContentFinderCondition>(conditionId, out var row) || !row.HasValue)
                return null;

            return Describe(row.Value);
        }, null);
    }

    /// <summary>A duty's name.</summary>
    /// <param name="conditionId">The ContentFinderCondition row id.</param>
    /// <returns>The name, or an empty string.</returns>
    public static string Name(uint conditionId)
    {
        return SafeExecutor.ExecuteSafely(
            () => ExcelSheetHelper.TryGetRow<ContentFinderCondition>(conditionId, out var row) && row.HasValue
                ? row.Value.Name.ExtractText()
                : string.Empty,
            string.Empty) ?? string.Empty;
    }

    /// <summary>
    /// Every duty the duty finder lists. Rows the finder does not list are skipped, since the sheet also holds the
    /// blank and internal rows the client never shows.
    /// </summary>
    /// <param name="includeHidden">Whether to include rows the duty finder does not list.</param>
    /// <returns>The duties, in ascending row order.</returns>
    public static IReadOnlyList<DutyInfo> ReadAll(bool includeHidden = false)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var duties = new List<DutyInfo>();
            var sheet = ExcelSheetHelper.GetSheet<ContentFinderCondition>();
            if (sheet == null)
                return duties;

            foreach (var row in sheet)
            {
                if (row.RowId == 0 || (!includeHidden && !row.IsInDutyFinder))
                    continue;

                duties.Add(Describe(row));
            }

            return duties;
        }, []) ?? [];
    }

    #region Roulettes

    /// <summary>
    /// The roulettes the game defines, read from <c>ContentRoulette</c>. The sheet reserves unnamed rows, which are
    /// skipped unless asked for, so this is the list a duty finder would actually show.
    /// </summary>
    /// <param name="includeUnnamed">Whether to include the reserved rows the client never shows.</param>
    /// <returns>The roulettes, in ascending row order.</returns>
    public static IReadOnlyList<RouletteInfo> ReadRoulettes(bool includeUnnamed = false)
    {
        cachedRoulettes ??= SafeExecutor.ExecuteSafely(() =>
        {
            var roulettes = new List<RouletteInfo>();
            var sheet = ExcelSheetHelper.GetSheet<ContentRoulette>();
            if (sheet == null)
                return roulettes;

            foreach (var row in sheet)
            {
                if (row.RowId == 0)
                    continue;

                roulettes.Add(new RouletteInfo(
                    row.RowId,
                    row.Name.ExtractText(),
                    row.Category.ExtractText(),
                    row.RequiredLevel,
                    row.IsInDutyFinder,
                    row.IsPvP));
            }

            return roulettes;
        }, []) ?? [];

        if (includeUnnamed)
            return cachedRoulettes;

        var named = new List<RouletteInfo>();

        foreach (var roulette in cachedRoulettes)
        {
            if (roulette.Name.Length > 0)
                named.Add(roulette);
        }

        return named;
    }

    /// <summary>A roulette's name.</summary>
    /// <param name="contentRouletteId">The ContentRoulette row id.</param>
    /// <returns>The name, or an empty string.</returns>
    public static string RouletteName(uint contentRouletteId)
    {
        return SafeExecutor.ExecuteSafely(
            () => ExcelSheetHelper.TryGetRow<ContentRoulette>(contentRouletteId, out var row) && row.HasValue
                ? row.Value.Name.ExtractText()
                : string.Empty,
            string.Empty) ?? string.Empty;
    }

    /// <summary>The duties a roulette can draw.</summary>
    /// <param name="contentRouletteId">The ContentRoulette row id.</param>
    /// <returns>The duties, in ascending row order.</returns>
    public static IReadOnlyList<DutyInfo> InRoulette(uint contentRouletteId)
    {
        var found = new List<DutyInfo>();

        if (contentRouletteId == 0)
            return found;

        foreach (var duty in ReadAll())
        {
            if (duty.IsInRoulette(contentRouletteId))
                found.Add(duty);
        }

        return found;
    }

    #endregion

    /// <summary>The duties that take place in a territory, which is how an arbitrary zone is traced back to its duty.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The duties, in ascending row order.</returns>
    public static IReadOnlyList<DutyInfo> InTerritory(uint territoryId)
    {
        var found = new List<DutyInfo>();

        if (territoryId == 0)
            return found;

        foreach (var duty in ReadAll(true))
        {
            if (duty.TerritoryId == territoryId)
                found.Add(duty);
        }

        return found;
    }

    #region Character state

    /// <summary>The duty the character is currently inside, or zero when they are not in one.</summary>
    /// <returns>The ContentFinderCondition row id.</returns>
    public static uint Current()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var gameMain = GameMain.Instance();
            return gameMain == null ? 0u : gameMain->CurrentContentFinderConditionId;
        });
    }

    /// <summary>Whether the character is currently inside a duty.</summary>
    /// <returns>True when a duty is running.</returns>
    public static bool IsInDuty() => Current() != 0;

    /// <summary>
    /// Whether the character has unlocked a duty. Only instanced content records this, so a duty whose content is
    /// defined elsewhere answers false.
    /// </summary>
    /// <param name="conditionId">The ContentFinderCondition row id.</param>
    /// <returns>True when the duty is unlocked.</returns>
    public static bool IsUnlocked(uint conditionId) => ReadInstanceContentState(conditionId, true);

    /// <summary>
    /// Whether the character has completed a duty. Only instanced content records this, so a duty whose content is
    /// defined elsewhere answers false.
    /// </summary>
    /// <param name="conditionId">The ContentFinderCondition row id.</param>
    /// <returns>True when the duty is complete.</returns>
    public static bool IsCompleted(uint conditionId) => ReadInstanceContentState(conditionId, false);

    /// <summary>
    /// Reads the unlock and completion state of a set of duties in one pass.
    /// </summary>
    /// <param name="conditionIds">The ContentFinderCondition row ids to read.</param>
    /// <returns>The unlocked and completed sets.</returns>
    public static (IReadOnlySet<uint> Unlocked, IReadOnlySet<uint> Completed) ReadProgress(IReadOnlyCollection<uint> conditionIds)
    {
        var unlocked = new HashSet<uint>();
        var completed = new HashSet<uint>();

        if (conditionIds.Count == 0 || !CharacterHelper.IsStateReady)
            return (unlocked, completed);

        SafeExecutor.ExecuteSafely(() =>
        {
            foreach (var conditionId in conditionIds)
            {
                var contentId = InstanceContentIdOf(conditionId);
                if (contentId == 0)
                    continue;

                if (UIState.IsInstanceContentUnlocked(contentId))
                    unlocked.Add(conditionId);

                if (UIState.IsInstanceContentCompleted(contentId))
                    completed.Add(conditionId);
            }
        });

        return (unlocked, completed);
    }

    #endregion

    #region Internals

    private static bool ReadInstanceContentState(uint conditionId, bool wantUnlocked)
    {
        if (conditionId == 0 || !CharacterHelper.IsStateReady)
            return false;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var contentId = InstanceContentIdOf(conditionId);
            if (contentId == 0)
                return false;

            return wantUnlocked
                ? UIState.IsInstanceContentUnlocked(contentId)
                : UIState.IsInstanceContentCompleted(contentId);
        });
    }

    private static uint InstanceContentIdOf(uint conditionId)
    {
        if (!ExcelSheetHelper.TryGetRow<ContentFinderCondition>(conditionId, out var row) || !row.HasValue)
            return 0;

        // The Content column points into whichever sheet ContentLinkType names, and only the instance kind means
        // InstanceContent. Reading it unconditionally would hand an unrelated row id to the unlock check.
        return (ContentType)row.Value.ContentLinkType == ContentType.Instance ? row.Value.Content.RowId : 0;
    }

    private static DutyInfo Describe(ContentFinderCondition row) => new(
        row.RowId,
        row.Name.ExtractText(),
        row.ShortCode.ExtractText(),
        row.TerritoryType.RowId,
        row.ContentType.RowId,
        row.Content.RowId,
        (ContentType)row.ContentLinkType,
        row.ClassJobLevelRequired,
        row.ClassJobLevelSync,
        row.ItemLevelRequired,
        row.ItemLevelSync,
        ReadPartySize(row),
        row.AcceptClassJobCategory.RowId,
        ReadRouletteIds(row),
        row.IsInDutyFinder,
        row.HighEndDuty,
        row.PvP,
        row.AllowUndersized);

    private static byte ReadPartySize(ContentFinderCondition row)
    {
        // The member type describes the party shape rather than a headcount: size is a party times how many parties
        // queue together, one for a dungeon, three for an alliance raid. Row zero resolves to an all-zero member
        // type, so the answer is only taken when it is not zero.
        if (row.ContentMemberType.ValueNullable is { } members)
        {
            var size = members.MembersPerParty * Math.Max((byte)1, members.PartyCount);

            if (size > 0)
                return (byte)Math.Min(size, byte.MaxValue);
        }

        return row.QueueMaxPlayers;
    }

    /// <summary>
    /// Reads which roulettes draw a duty. <c>ContentFinderCondition</c> opens with one boolean column per roulette in
    /// <c>ContentRoulette</c> row order, so column n is roulette row n + 1, and the block length is that sheet's row
    /// count. The reserved unnamed rows hold columns too, so the count cannot come from the named ones.
    /// </summary>
    private static IReadOnlyList<uint> ReadRouletteIds(ContentFinderCondition condition)
    {
        var columns = RouletteColumns();

        if (columns.Count == 0)
            return [];

        List<uint>? roulettes = null;
        object row = condition;

        for (var column = 0; column < columns.Count; column++)
        {
            if (columns[column].GetValue(row) is true)
                (roulettes ??= []).Add((uint)column + 1);
        }

        return roulettes ?? (IReadOnlyList<uint>)[];
    }

    private static IReadOnlyList<PropertyInfo> RouletteColumns()
    {
        if (cachedRouletteColumns != null)
            return cachedRouletteColumns;

        var built = SafeExecutor.ExecuteSafely(() =>
        {
            var highestRoulette = 0u;

            foreach (var roulette in ReadRoulettes(true))
                highestRoulette = Math.Max(highestRoulette, roulette.RowId);

            if (highestRoulette == 0)
                return [];

            return typeof(ContentFinderCondition)
                .GetProperties()
                .Where(property => property.PropertyType == typeof(bool))
                .Take((int)highestRoulette)
                .ToArray();
        }, []) ?? [];

        return cachedRouletteColumns = built;
    }

    #endregion
}
