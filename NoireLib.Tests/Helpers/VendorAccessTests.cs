using FluentAssertions;
using NoireLib.Helpers;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the gate check between a shop line and a character. It is a function over two records. The snapshot stands in
/// for the client, and none of it needs a game.
/// </summary>
public sealed class VendorAccessTests
{
    private static VendorAccess Access(
        IReadOnlySet<uint>? quests = null,
        IReadOnlySet<uint>? duties = null,
        IReadOnlySet<uint>? achievements = null,
        IReadOnlyDictionary<uint, uint>? festivals = null,
        IReadOnlySet<uint>? categories = null,
        uint? grandCompanyRank = null)
        => new(quests, duties, achievements, festivals, categories, 0, 0, grandCompanyRank);

    #region Gates that fail

    [Fact]
    public void UnmetBy_NamesTheQuestThatIsNotComplete()
    {
        var requirements = new VendorRequirements([69190]);

        requirements.UnmetBy(Access(quests: new HashSet<uint>())).Should().Contain("quest").And.Contain("69190");
        requirements.UnmetBy(Access(quests: new HashSet<uint> { 69190 })).Should().BeNull();
    }

    [Fact]
    public void UnmetBy_NamesTheClassJobCategoryTheCharacterIsOutside()
    {
        var requirements = new VendorRequirements([], ClassJobCategoryId: 33);

        requirements.UnmetBy(Access(categories: new HashSet<uint> { 34 })).Should().Contain("33");
        requirements.UnmetBy(Access(categories: new HashSet<uint> { 33, 34 })).Should().BeNull();
    }

    [Fact]
    public void UnmetBy_NamesTheGrandCompanyRankWhenTheCharacterIsBelowIt()
    {
        var requirements = new VendorRequirements([], GrandCompanyRank: 5);

        requirements.UnmetBy(Access(grandCompanyRank: 4)).Should().Contain("rank").And.Contain("5");
        requirements.UnmetBy(Access(grandCompanyRank: 5)).Should().BeNull();
        requirements.UnmetBy(Access(grandCompanyRank: 8)).Should().BeNull();
    }

    [Fact]
    public void UnmetBy_SaysWhenTheFestivalIsNotRunning()
    {
        var requirements = new VendorRequirements([], FestivalId: 12, FestivalPhase: 2);

        requirements.UnmetBy(Access(festivals: new Dictionary<uint, uint>())).Should().NotBeNull();
        requirements.UnmetBy(Access(festivals: new Dictionary<uint, uint> { [12] = 1 })).Should().NotBeNull();
        requirements.UnmetBy(Access(festivals: new Dictionary<uint, uint> { [12] = 2 })).Should().BeNull();
    }

    [Fact]
    public void UnmetBy_NamesTheDutyThatIsNotCleared()
    {
        var requirements = new VendorRequirements([], ContentFinderConditionId: 730, ContentFinderMustBeComplete: true);

        requirements.UnmetBy(Access(duties: new HashSet<uint>())).Should().Contain("duty").And.Contain("730");
        requirements.UnmetBy(Access(duties: new HashSet<uint> { 730 })).Should().BeNull();
    }

    [Fact]
    public void UnmetBy_AnswersTheFirstGateThatFails()
    {
        var requirements = new VendorRequirements([69190], ClassJobCategoryId: 33);
        var access = Access(quests: new HashSet<uint>(), categories: new HashSet<uint>());

        requirements.UnmetBy(access).Should().Contain("69190");
    }

    #endregion

    #region Gates that read as met

    [Fact]
    public void UnmetBy_ReadsAnAchievementGateAsMetWhileTheListIsUnread()
    {
        var requirements = new VendorRequirements([], AchievementId: 777);

        requirements.UnmetBy(Access(achievements: null)).Should().BeNull();
        requirements.UnmetBy(Access(achievements: new HashSet<uint>())).Should().Contain("777");
        requirements.UnmetBy(Access(achievements: new HashSet<uint> { 777 })).Should().BeNull();
    }

    [Fact]
    public void UnmetBy_ReadsEveryGateAsMetForACharacterNothingIsKnownAbout()
    {
        var requirements = new VendorRequirements([69190], 777, 12, 2, 730, true, 33, 1, 5);

        requirements.UnmetBy(VendorAccess.Unknown).Should().BeNull();
    }

    [Fact]
    public void UnmetBy_AnswersNullForALineNothingGates()
    {
        VendorRequirements.None.IsOpen.Should().BeTrue();
        VendorRequirements.None.UnmetBy(Access(quests: new HashSet<uint>())).Should().BeNull();
    }

    #endregion

    #region Folding gates together

    [Fact]
    public void Merge_KeepsBothSetsOfQuestsAndTheGateAlreadySet()
    {
        var merged = new VendorRequirements([1], FestivalId: 12).Merge(new VendorRequirements([2], ClassJobCategoryId: 33));

        merged.QuestIds.Should().Equal(1u, 2u);
        merged.FestivalId.Should().Be(12u);
        merged.ClassJobCategoryId.Should().Be(33u);
    }

    [Fact]
    public void Merge_LeavesAnOpenSetAlone()
    {
        var gated = new VendorRequirements([1]);

        gated.Merge(VendorRequirements.None).Should().BeSameAs(gated);
        VendorRequirements.None.Merge(gated).Should().BeSameAs(gated);
    }

    [Fact]
    public void WithLine_AddsTheLinesOwnQuestsAndAchievement()
    {
        var merged = new VendorRequirements([1]).WithLine([2], 777);

        merged.QuestIds.Should().Equal(1u, 2u);
        merged.AchievementId.Should().Be(777u);
    }

    #endregion
}
