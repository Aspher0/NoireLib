using FluentAssertions;
using NoireLib.Animations.Helpers;
using NoireLib.Enums;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The Emote sheet gives seven ActionTimeline columns and names none of them, so what each slot is for was
/// established by observation. Pinned here because four separate places used to carry their own copy of it.
/// </summary>
public class ActionTimelineSlotsTests
{
    [Fact]
    public void SlotCount_MatchesTheSheet()
        => ActionTimelineSlots.SlotCount.Should().Be(7);

    [Theory]
    [InlineData(ActionTimelineSlots.Standing, PostureFlags.Standing)]
    [InlineData(ActionTimelineSlots.GroundSit, PostureFlags.GroundSit)]
    [InlineData(ActionTimelineSlots.ChairSit, PostureFlags.ChairSit)]
    [InlineData(ActionTimelineSlots.UpperBody, PostureFlags.Mounted)]
    public void PostureForSlot_NamesTheBodyTheSlotDrives(int slot, PostureFlags expected)
        => ActionTimelineSlots.PostureForSlot(slot).Should().Be(expected);

    [Theory]
    [InlineData(ActionTimelineSlots.Intro)]
    [InlineData(ActionTimelineSlots.Facial)]
    [InlineData(ActionTimelineSlots.Adjust)]
    [InlineData(-1)]
    [InlineData(99)]
    public void PostureForSlot_DrivesNoBodyForTheChannelsThatServeNoState(int slot)
        => ActionTimelineSlots.PostureForSlot(slot).Should().Be(PostureFlags.None);

    [Theory]
    [InlineData(EmoteCondition.HoldingUmbrella)]
    [InlineData(EmoteCondition.HoldingTorch)]
    public void PlayableAsFor_TreatsAHeldPropAsStanding(EmoteCondition condition)
        => ActionTimelineSlots.PlayableAsFor(condition).Should().Be(EmoteCondition.Standing,
            "the game puts the prop away rather than refusing the emote");

    [Theory]
    [InlineData(EmoteCondition.Standing)]
    [InlineData(EmoteCondition.SittingInChair)]
    [InlineData(EmoteCondition.Mounted)]
    [InlineData(EmoteCondition.Fishing)]
    public void PlayableAsFor_LeavesEveryOtherStateAlone(EmoteCondition condition)
        => ActionTimelineSlots.PlayableAsFor(condition).Should().Be(condition);

    [Fact]
    public void SlotPreferenceFor_ASeatedCharacter_FallsBackToTheUpperBodyChannel()
    {
        ActionTimelineSlots.SlotPreferenceFor(EmoteCondition.SittingOnGround)
            .Should().Equal(ActionTimelineSlots.GroundSit, ActionTimelineSlots.UpperBody);

        ActionTimelineSlots.SlotPreferenceFor(EmoteCondition.SittingInChair)
            .Should().Equal(ActionTimelineSlots.ChairSit, ActionTimelineSlots.UpperBody);
    }

    [Theory]
    [InlineData(EmoteCondition.Mounted)]
    [InlineData(EmoteCondition.Swimming)]
    [InlineData(EmoteCondition.Diving)]
    public void SlotPreferenceFor_MountedSwimmingAndDiving_ShareTheUpperBodyChannel(EmoteCondition condition)
        => ActionTimelineSlots.SlotPreferenceFor(condition).Should().Equal(ActionTimelineSlots.UpperBody);

    // The rod occupies both arms, so nothing that moves them can play.
    [Fact]
    public void SlotPreferenceFor_Fishing_LeavesOnlyTheFacialChannel()
        => ActionTimelineSlots.SlotPreferenceFor(EmoteCondition.Fishing)
            .Should().Equal(ActionTimelineSlots.Facial);

    [Fact]
    public void SlotPreferenceFor_AnythingElse_UsesTheStandingChannel()
        => ActionTimelineSlots.SlotPreferenceFor(EmoteCondition.Standing)
            .Should().Equal(ActionTimelineSlots.Standing);

    [Theory]
    [InlineData(EmoteCondition.SittingOnGround, PostureFlags.GroundSit)]
    [InlineData(EmoteCondition.SittingInChair, PostureFlags.ChairSit)]
    [InlineData(EmoteCondition.Mounted, PostureFlags.Mounted)]
    [InlineData(EmoteCondition.Swimming, PostureFlags.Mounted)]
    [InlineData(EmoteCondition.Diving, PostureFlags.Mounted)]
    [InlineData(EmoteCondition.Standing, PostureFlags.Standing)]
    [InlineData(EmoteCondition.Fishing, PostureFlags.Standing)]
    public void PostureForCondition_AgreesWithTheSlotPreference(EmoteCondition condition, PostureFlags expected)
        => ActionTimelineSlots.PostureForCondition(condition).Should().Be(expected);
}
