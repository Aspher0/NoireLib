using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the rules of <see cref="NoireTagInput"/>: what a pasted run splits into, what is accepted, and what a refusal
/// reports.
/// <br/>
/// The refusal reasons are the part worth being strict about. A tag that simply vanishes when the user presses Enter
/// reads as the widget being broken whichever rule actually rejected it, so every rule has to come back named.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireTagInputTests
{
    private static NoireTagInput Create(params string[] tags) => new("TestTags", tags);

    #region Splitting a paste

    [Fact]
    public void Split_BreaksOnEverySeparator()
    {
        var pieces = NoireTagInput.Split("alpha,beta;gamma\ndelta", [',', ';', '\n'], trim: true);

        pieces.Should().Equal(new[] { "alpha", "beta", "gamma", "delta" });
    }

    [Fact]
    public void Split_DropsEmptyPieces()
    {
        var pieces = NoireTagInput.Split("alpha,,beta,", [','], trim: true);

        pieces.Should().Equal(new[] { "alpha", "beta" }, "because a trailing comma is how every pasted list ends");
    }

    [Fact]
    public void Split_TrimsEachPiece_WhenAskedTo()
    {
        NoireTagInput.Split(" alpha , beta ", [','], trim: true).Should().Equal(new[] { "alpha", "beta" });
        NoireTagInput.Split(" alpha , beta ", [','], trim: false).Should().Equal(new[] { " alpha ", " beta " });
    }

    [Fact]
    public void Split_KeepsTheWholeText_WhenThereAreNoSeparators()
    {
        NoireTagInput.Split("one thing", [], trim: true).Should().Equal(new[] { "one thing" });
        NoireTagInput.Split("one thing", null, trim: true).Should().Equal(new[] { "one thing" });
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Split_HandlesNothing(string? text)
    {
        NoireTagInput.Split(text, [','], trim: true).Should().BeEmpty();
    }

    [Fact]
    public void AddRange_AddsEveryPieceOfAPaste()
    {
        var field = Create();

        field.AddRange("alpha, beta, gamma");

        field.Tags.Should().Equal(new[] { "alpha", "beta", "gamma" });
    }

    #endregion

    #region What is accepted

    [Fact]
    public void TryAdd_RefusesEmptyTags()
    {
        var field = Create();

        field.TryAdd("   ", out var rejection).Should().BeFalse();
        rejection.Should().Be(TagRejection.Empty);
    }

    [Fact]
    public void TryAdd_RefusesDuplicates_CaseInsensitivelyByDefault()
    {
        var field = Create("alpha");

        field.TryAdd("ALPHA", out var rejection).Should().BeFalse();
        rejection.Should().Be(TagRejection.Duplicate);
        field.LastError.Should().NotBeEmpty("because a refusal the user cannot account for reads as a broken widget");
    }

    [Fact]
    public void TryAdd_AllowsDuplicates_WhenToldTo()
    {
        var field = Create("alpha");
        field.AllowDuplicates = true;

        field.TryAdd("alpha", out _).Should().BeTrue();
        field.Tags.Should().HaveCount(2);
    }

    [Fact]
    public void TryAdd_RefusesOverlongTags()
    {
        var field = Create();
        field.MaxTagLength = 4;

        field.TryAdd("toolong", out var rejection).Should().BeFalse();
        rejection.Should().Be(TagRejection.TooLong);
    }

    [Fact]
    public void TryAdd_RefusesOnceFull()
    {
        var field = Create("alpha", "beta");
        field.MaxTags = 2;

        field.TryAdd("gamma", out var rejection).Should().BeFalse();
        rejection.Should().Be(TagRejection.Full);
    }

    [Fact]
    public void TryAdd_RefusesWhatValidationRefuses()
    {
        var field = Create();
        field.Validate = tag => tag.Contains(' ') ? "No spaces." : null;

        field.TryAdd("two words", out var rejection).Should().BeFalse();
        rejection.Should().Be(TagRejection.Invalid);
        field.LastError.Should().Be("No spaces.");
    }

    [Fact]
    public void TryAdd_SurvivesAThrowingValidator()
    {
        var field = Create();
        field.Validate = _ => throw new InvalidOperationException("boom");

        field.TryAdd("alpha", out var rejection).Should().BeFalse();
        rejection.Should().Be(TagRejection.Invalid, "because a broken validator must refuse rather than take the frame down");
    }

    [Fact]
    public void TryAdd_TrimsBeforeJudging()
    {
        var field = Create();

        field.TryAdd("  alpha  ", out _).Should().BeTrue();
        field.Tags.Should().Equal(new[] { "alpha" });
    }

    #endregion

    #region Editing

    [Fact]
    public void PopLastForEditing_TakesTheTagBackIntoTheInput()
    {
        var field = Create("alpha", "beta");

        field.PopLastForEditing().Should().BeTrue();

        field.Tags.Should().Equal(new[] { "alpha" });
        field.PendingText.Should().Be("beta", "because backspace is for fixing a typo, not for destroying the entry");
    }

    [Fact]
    public void PopLastForEditing_DoesNothing_WhenThereAreNoTags()
    {
        Create().PopLastForEditing().Should().BeFalse();
    }

    [Fact]
    public void Remove_MatchesWithTheComparer()
    {
        var field = Create("Alpha");

        field.Remove("alpha").Should().BeTrue();
        field.Tags.Should().BeEmpty();
    }

    [Fact]
    public void SetTags_DropsWhatTheRulesRefuse()
    {
        var field = Create();
        field.MaxTagLength = 5;

        field.SetTags(new[] { "ok", "waytoolong", "ok", "fine" });

        field.Tags.Should().Equal(new[] { "ok", "fine" }, "because the duplicate and the overlong entry both break a rule");
    }

    [Fact]
    public void OnChanged_FiresForEveryRealChange()
    {
        var field = Create();
        var counts = new List<int>();
        field.OnChanged = current => counts.Add(current.Count);

        field.Add("alpha");
        field.Add("alpha");     // duplicate, refused
        field.Add("beta");
        field.Remove("alpha");

        counts.Should().Equal(new[] { 1, 2, 1 });
    }

    #endregion

    #region Removing the right one

    [Fact]
    public void RemoveAt_RemovesThePositionRatherThanTheFirstMatch()
    {
        // With duplicates allowed, "the first tag that compares equal" is not the chip the user clicked, which is
        // what made every duplicate past the first impossible to remove.
        var field = Create();
        field.AllowDuplicates = true;
        field.SetTags(new[] { "alpha", "bravo", "alpha", "charlie" });

        field.RemoveAt(2).Should().BeTrue();

        field.Tags.Should().Equal(new[] { "alpha", "bravo", "charlie" },
            "because the third tag went, not the first one that happened to read the same");
    }

    [Fact]
    public void Remove_StillTakesTheFirstMatch()
    {
        var field = Create();
        field.AllowDuplicates = true;
        field.SetTags(new[] { "alpha", "bravo", "alpha", "charlie" });

        field.Remove("alpha").Should().BeTrue();

        field.Tags.Should().Equal(new[] { "bravo", "alpha", "charlie" },
            "because removing by value is documented as taking the first, and is the wrong tool for a chip");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void RemoveAt_RefusesAPositionThatIsNotThere(int index)
    {
        var field = Create();
        field.SetTags(new[] { "alpha", "bravo", "charlie" });

        field.RemoveAt(index).Should().BeFalse();

        field.Tags.Should().HaveCount(3, "because a position off the end is a no-op, not an exception in a draw call");
    }

    [Fact]
    public void RemoveAt_ReportsTheChange()
    {
        var field = Create();
        field.SetTags(new[] { "alpha", "bravo" });

        var seen = 0;
        field.OnChanged = _ => seen++;

        field.RemoveAt(0);

        seen.Should().Be(1);
    }

    #endregion
}
