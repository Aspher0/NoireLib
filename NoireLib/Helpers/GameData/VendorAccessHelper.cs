using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ClientAchievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the character once into a <see cref="VendorAccess"/> snapshot. The reads touch the client and belong on the
/// framework thread. The gates then read the snapshot from anywhere.
/// </summary>
public static unsafe class VendorAccessHelper
{
    /// <summary>
    /// Reads what the character has done, limited to the gates the vendors ask about.
    /// </summary>
    /// <param name="questIds">The quests to ask about. Null takes every quest the routes wait on.</param>
    /// <param name="dutyIds">The duties to ask about. Null takes every duty the routes wait on.</param>
    /// <param name="achievementIds">The achievements to ask about. Null takes every achievement the routes wait on.</param>
    /// <returns>The snapshot, or <see cref="VendorAccess.Unknown"/> when the character is not loaded.</returns>
    public static VendorAccess Read(
        IReadOnlyCollection<uint>? questIds = null,
        IReadOnlyCollection<uint>? dutyIds = null,
        IReadOnlyCollection<uint>? achievementIds = null)
    {
        if (!CharacterHelper.IsStateReady)
            return VendorAccess.Unknown;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var gates = GatheredGates(questIds, dutyIds, achievementIds);
            var classJobId = ClassJobHelper.CurrentId();

            return new VendorAccess(
                CompletedQuests(gates.Quests),
                DutyHelper.ReadProgress(gates.Duties).Completed,
                EarnedAchievements(gates.Achievements),
                ActiveFestivals(),
                ClassJobCategories(classJobId),
                classJobId,
                GrandCompanyId(),
                GrandCompanyRank(),
                FreeCompanyRank());
        }, VendorAccess.Unknown) ?? VendorAccess.Unknown;
    }

    private static (IReadOnlyCollection<uint> Quests, IReadOnlyCollection<uint> Duties, IReadOnlyCollection<uint> Achievements) GatheredGates(
        IReadOnlyCollection<uint>? questIds,
        IReadOnlyCollection<uint>? dutyIds,
        IReadOnlyCollection<uint>? achievementIds)
    {
        if (questIds != null && dutyIds != null && achievementIds != null)
            return (questIds, dutyIds, achievementIds);

        var quests = new HashSet<uint>();
        var duties = new HashSet<uint>();
        var achievements = new HashSet<uint>();

        foreach (var route in VendorHelper.ScanRoutes().Routes)
        {
            foreach (var questId in route.Requirements.QuestIds)
                quests.Add(questId);

            if (route.Requirements.ContentFinderConditionId != 0)
                duties.Add(route.Requirements.ContentFinderConditionId);

            if (route.Requirements.AchievementId != 0)
                achievements.Add(route.Requirements.AchievementId);
        }

        return (questIds ?? quests, dutyIds ?? duties, achievementIds ?? achievements);
    }

    private static IReadOnlySet<uint> CompletedQuests(IReadOnlyCollection<uint> questIds)
    {
        var complete = new HashSet<uint>();

        foreach (var questId in questIds)
        {
            if (QuestHelper.IsComplete(questId))
                complete.Add(questId);
        }

        return complete;
    }

    private static IReadOnlySet<uint>? EarnedAchievements(IReadOnlyCollection<uint> achievementIds)
    {
        var state = ClientAchievement.Instance();

        if (state == null || !state->IsLoaded())
            return null;

        var earned = new HashSet<uint>();

        foreach (var achievementId in achievementIds)
        {
            if (achievementId != 0 && state->IsComplete((int)achievementId))
                earned.Add(achievementId);
        }

        return earned;
    }

    private static IReadOnlyDictionary<uint, uint> ActiveFestivals()
    {
        var running = new Dictionary<uint, uint>();
        var game = GameMain.Instance();

        if (game == null)
            return running;

        foreach (var festival in game->ActiveFestivals)
        {
            if (festival.Id != 0)
                running[festival.Id] = festival.Phase;
        }

        return running;
    }

    private static IReadOnlySet<uint>? ClassJobCategories(uint classJobId)
    {
        if (classJobId == 0)
            return null;

        var sheet = ExcelSheetHelper.GetSheet<ClassJobCategory>();

        if (sheet == null)
            return null;

        var categories = new HashSet<uint>();

        foreach (var category in sheet)
        {
            if (category.RowId != 0 && ClassJobHelper.CategoryIncludes(category.RowId, classJobId))
                categories.Add(category.RowId);
        }

        return categories;
    }

    private static uint GrandCompanyId()
    {
        var state = PlayerState.Instance();
        return state == null ? 0u : state->GrandCompany;
    }

    private static uint? GrandCompanyRank()
    {
        var state = PlayerState.Instance();
        return state == null || state->GrandCompany == 0 ? null : state->GetGrandCompanyRank();
    }

    private static uint? FreeCompanyRank()
    {
        var company = InfoProxyFreeCompany.Instance();
        return company == null || company->Id == 0 ? null : company->Rank;
    }
}
