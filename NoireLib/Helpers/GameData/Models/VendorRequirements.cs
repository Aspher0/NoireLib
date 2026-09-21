using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>Every gate the sheets put between a character and a shop line.</summary>
/// <param name="QuestIds">The quests the shop, the menus and the line wait on.</param>
/// <param name="AchievementId">The Achievement row the line waits on, or zero.</param>
/// <param name="FestivalId">The Festival row the shop only opens during, or zero.</param>
/// <param name="FestivalPhase">The phase that festival has to have reached.</param>
/// <param name="ContentFinderConditionId">The duty the shop waits on, or zero.</param>
/// <param name="ContentFinderMustBeComplete">Whether the duty must be cleared.</param>
/// <param name="ClassJobCategoryId">The ClassJobCategory row the character's job has to belong to, or zero.</param>
/// <param name="GrandCompanyId">The grand company the line belongs to, or zero.</param>
/// <param name="GrandCompanyRank">The GCRankGridaniaMaleText row the character has to have reached, or zero.</param>
public sealed record VendorRequirements(
    IReadOnlyList<uint> QuestIds,
    uint AchievementId = 0,
    uint FestivalId = 0,
    uint FestivalPhase = 0,
    uint ContentFinderConditionId = 0,
    bool ContentFinderMustBeComplete = false,
    uint ClassJobCategoryId = 0,
    uint GrandCompanyId = 0,
    uint GrandCompanyRank = 0)
{
    /// <summary>A line nothing gates.</summary>
    public static VendorRequirements None { get; } = new([]);

    /// <summary>Whether the sheets put no gate at all on the line.</summary>
    public bool IsOpen => QuestIds.Count == 0
        && AchievementId == 0
        && FestivalId == 0
        && ContentFinderConditionId == 0
        && ClassJobCategoryId == 0
        && GrandCompanyId == 0
        && GrandCompanyRank == 0;

    /// <summary>Folds another set of gates into this one. A gate already set wins over a zero.</summary>
    /// <param name="other">The gates to add.</param>
    /// <returns>The two sets together.</returns>
    public VendorRequirements Merge(VendorRequirements other)
    {
        if (other.IsOpen)
            return this;

        if (IsOpen)
            return other;

        return new VendorRequirements(
            QuestIds.Count == 0 ? other.QuestIds : [.. QuestIds.Concat(other.QuestIds).Distinct()],
            AchievementId == 0 ? other.AchievementId : AchievementId,
            FestivalId == 0 ? other.FestivalId : FestivalId,
            FestivalId == 0 ? other.FestivalPhase : FestivalPhase,
            ContentFinderConditionId == 0 ? other.ContentFinderConditionId : ContentFinderConditionId,
            ContentFinderConditionId == 0 ? other.ContentFinderMustBeComplete : ContentFinderMustBeComplete,
            ClassJobCategoryId == 0 ? other.ClassJobCategoryId : ClassJobCategoryId,
            GrandCompanyId == 0 ? other.GrandCompanyId : GrandCompanyId,
            GrandCompanyRank == 0 ? other.GrandCompanyRank : GrandCompanyRank);
    }

    /// <summary>Adds the quests a shop line carries of its own.</summary>
    /// <param name="questIds">The line's quests.</param>
    /// <param name="achievementId">The line's achievement, or zero.</param>
    /// <returns>The gates with the line's own folded in.</returns>
    public VendorRequirements WithLine(IReadOnlyList<uint> questIds, uint achievementId)
    {
        if (questIds.Count == 0 && achievementId == 0)
            return this;

        return this with
        {
            QuestIds = questIds.Count == 0 ? QuestIds : [.. QuestIds.Concat(questIds).Distinct()],
            AchievementId = AchievementId == 0 ? achievementId : AchievementId,
        };
    }

    /// <summary>
    /// The first gate a character does not pass. A gate the client cannot answer reads as met, since the shop window
    /// settles it.
    /// </summary>
    /// <param name="access">What the character has done, read once.</param>
    /// <returns>A sentence naming the gate, or null when every gate is met.</returns>
    public string? UnmetBy(VendorAccess access)
    {
        if (access == null)
            return null;

        if (access.CompletedQuests is { } quests)
        {
            foreach (var questId in QuestIds)
            {
                if (!quests.Contains(questId))
                    return $"The quest {Describe(NameHelper.Quest(questId), questId)} is not complete.";
            }
        }

        if (ClassJobCategoryId != 0 && access.ClassJobCategories is { } categories && !categories.Contains(ClassJobCategoryId))
            return $"This shop only serves {Describe(NameHelper.ClassJobCategory(ClassJobCategoryId), ClassJobCategoryId)}.";

        if (GrandCompanyId != 0 && access.GrandCompanyId != 0 && access.GrandCompanyId != GrandCompanyId)
            return "The character belongs to another grand company.";

        if (GrandCompanyRank != 0 && access.GrandCompanyRank is { } rank && rank < GrandCompanyRank)
            return $"Grand company rank {GrandCompanyRank} is required.";

        if (ContentFinderConditionId != 0 && access.ClearedDuties is { } duties && !duties.Contains(ContentFinderConditionId))
            return $"The duty {Describe(NameHelper.Duty(ContentFinderConditionId), ContentFinderConditionId)} is not cleared.";

        if (FestivalId != 0 && access.ActiveFestivals is { } festivals
            && (!festivals.TryGetValue(FestivalId, out var phase) || phase < FestivalPhase))
        {
            return "The seasonal event this shop belongs to is not running.";
        }

        if (AchievementId != 0 && access.EarnedAchievements is { } achievements && !achievements.Contains(AchievementId))
            return $"The achievement {AchievementId} is not earned.";

        return null;
    }

    private static string Describe(string name, uint rowId) => name.Length > 0 ? $"{name} ({rowId})" : $"#{rowId}";
}

internal static class NameHelper
{
    public static string Quest(uint questId)
        => SafeExecutor.ExecuteSafely(
            () => ExcelSheetHelper.TryGetRow<Lumina.Excel.Sheets.Quest>(questId, out var row) && row.HasValue
                ? row.Value.Name.ExtractText()
                : string.Empty,
            string.Empty) ?? string.Empty;

    public static string ClassJobCategory(uint categoryId) => ClassJobHelper.CategoryName(categoryId);

    public static string Duty(uint conditionId) => DutyHelper.Name(conditionId);
}
