using FluentAssertions;
using NoireLib.Animations.PapFormat;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Ported from PapEdit.Tests: locks how PapFormat groups emote parts into shared files and matches a file's
/// animations back onto the names a retargeter needs to write.
/// </summary>
public class PapSharingTests
{
    /// <summary> The animator's case: two emotes on one file, each of them a start and a loop. </summary>
    [Fact]
    public void Group_PutsThePartsOfOneEmoteTogether()
    {
        var groups = PapSharing.Group(["Ball Dance", "Ball Dance", "Yellow Ranger Pose", "Yellow Ranger Pose"]);

        groups.Should().HaveCount(2);
        groups[0].Should().Equal(0, 1);
        groups[1].Should().Equal(2, 3);
    }

    [Fact]
    public void Group_KeepsFirstAppearanceOrder()
    {
        var groups = PapSharing.Group(["Loop", "Other", "Loop"]);

        groups[0].Should().Equal(0, 2);
        groups[1].Should().Equal(1);
    }

    /// <summary> A part told to take a file of its own leaves the rest of the emote sharing. </summary>
    [Fact]
    public void Group_GivesANullKeyAGroupOfItsOwn()
    {
        var groups = PapSharing.Group(["Embrace", null, "Embrace"]);

        groups.Should().HaveCount(2);
        groups[0].Should().Equal(0, 2);
        groups[1].Should().Equal(1);
    }

    [Fact]
    public void Group_MatchesKeysWhateverTheirCase()
        => PapSharing.Group(["Ball Dance", "ball dance"]).Should().ContainSingle();

    [Fact]
    public void Group_HasNothingToDoWithNoEmotes()
        => PapSharing.Group([]).Should().BeEmpty();

    [Fact]
    public void Match_LinesThePartsUpInOrder()
    {
        var matches = PapSharing.Match(["a_start", "a_loop"], ["b_start", "b_loop"]);

        matches.Should().Equal(0, 1);
    }

    /// <summary> A loop must take the loop, whatever order the emote lists its parts in. </summary>
    [Fact]
    public void Match_PrefersThePartWhoseNameEndsTheSameWay()
    {
        var matches = PapSharing.Match(["a_start", "a_loop"], ["b_loop", "b_start"]);

        matches.Should().Equal(1, 0);
    }

    /// <summary> Retargeting one part of a two-part file takes the part that fits, not the first. </summary>
    [Fact]
    public void Match_TakesTheLoopWhenOnlyALoopIsWanted()
    {
        var matches = PapSharing.Match(["a_start", "a_loop"], ["b_loop"]);

        matches.Should().Equal(1);
    }

    [Fact]
    public void Match_FallsBackToOrderWhenNothingEndsAlike()
    {
        var matches = PapSharing.Match(["one", "two"], ["three", "four"]);

        matches.Should().Equal(0, 1);
    }

    /// <summary> A file holding one animation cannot answer to two names, and says so with a -1. </summary>
    [Fact]
    public void Match_LeavesANameUnansweredWhenThePartsRunOut()
    {
        var matches = PapSharing.Match(["a_loop"], ["b_loop", "b_start"]);

        matches.Should().Equal(0, -1);
    }

    [Fact]
    public void Match_AnswersNothingFromAFileWithNoAnimations()
        => PapSharing.Match([], ["b_loop"]).Should().Equal(-1);

    /// <summary> One part matched by its ending does not stop another from taking a different one. </summary>
    [Fact]
    public void Match_NeverGivesOnePartToTwoNames()
    {
        var matches = PapSharing.Match(["a_loop", "a_start", "a_add"], ["b_loop", "b_add", "b_start"]);

        matches.Should().Equal(0, 2, 1);
    }
}
