using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the fuzzy scorer: what matches, what a match is worth relative to another, and where it says the match
/// landed.
/// <br/>
/// Ordering is the whole product here. A filter that returns the right set in the wrong order is experienced as
/// broken, because the answer a person wanted is the one they expect first and nowhere else.
/// </summary>
public class FuzzyMatcherTests
{
    private static int[] Indices(string candidate, string query)
    {
        Span<int> hits = stackalloc int[FuzzyMatcher.MaxQueryLength];

        FuzzyMatcher.TryMatch(candidate, query, hits, out var match).Should().BeTrue();

        return hits[..match.MatchedCount].ToArray();
    }

    #region What matches

    [Theory]
    [InlineData("Combat Log", "cmbl")]
    [InlineData("Combat Log", "combat log")]
    [InlineData("Combat Log", "clog")]
    [InlineData("Combat Log", "")]
    [InlineData("Combat Log", "COMBAT")]
    public void IsMatch_AcceptsCharactersInOrder(string candidate, string query)
    {
        FuzzyMatcher.IsMatch(candidate, query).Should().BeTrue();
    }

    [Theory]
    [InlineData("Combat Log", "xyz")]
    [InlineData("Combat Log", "gol")]
    [InlineData("Combat Log", "combatt")]
    [InlineData("", "a")]
    [InlineData(null, "a")]
    public void IsMatch_RefusesAnythingElse(string? candidate, string query)
    {
        FuzzyMatcher.IsMatch(candidate, query).Should().BeFalse();
    }

    [Fact]
    public void IsMatch_AcceptsEverything_WhenTheQueryIsEmpty()
    {
        FuzzyMatcher.IsMatch("anything", string.Empty).Should().BeTrue("because a filter box nobody has typed in filters nothing");
        FuzzyMatcher.IsMatch(null, null).Should().BeTrue();
    }

    #endregion

    #region What it is worth

    [Fact]
    public void Score_PrefersConsecutiveCharacters()
    {
        var together = FuzzyMatcher.Score("Combat", "com");
        var apart = FuzzyMatcher.Score("Casino Omen", "com");

        together.Should().BeGreaterThan(apart, "because a run of characters is the strongest signal a match is the intended one");
    }

    [Fact]
    public void Score_PrefersAMatchAtTheStart()
    {
        var atStart = FuzzyMatcher.Score("Log Combat", "log");
        var later = FuzzyMatcher.Score("Combat Log", "log");

        atStart.Should().BeGreaterThan(later);
    }

    [Fact]
    public void Score_PrefersWordBoundaries()
    {
        var boundary = FuzzyMatcher.Score("Combat Log", "cl");
        var inside = FuzzyMatcher.Score("Circle", "cl");

        boundary.Should().BeGreaterThan(inside, "because the l of Log is a word start and the l of Circle is not");
    }

    [Fact]
    public void Score_PrefersShorterCandidates_WhenTheMatchIsOtherwiseTheSame()
    {
        var shorter = FuzzyMatcher.Score("Log", "log");
        var longer = FuzzyMatcher.Score("Logarithmic Scale", "log");

        shorter.Should().BeGreaterThan(longer);
    }

    [Fact]
    public void Score_PrefersAnExactCaseMatch()
    {
        var exact = FuzzyMatcher.Score("Log", "Log");
        var different = FuzzyMatcher.Score("Log", "log");

        exact.Should().BeGreaterThan(different, "because it only breaks ties, but it should break them towards the obvious answer");
    }

    [Fact]
    public void Score_IsZero_WhenNothingMatches()
    {
        FuzzyMatcher.Score("Combat Log", "xyz").Should().Be(0);
    }

    #endregion

    #region Where it matched

    [Fact]
    public void TryMatch_ReportsOnePositionPerQueryCharacter()
    {
        Indices("Combat Log", "cmbl").Should().Equal(0, 2, 3, 7);
    }

    [Fact]
    public void TryMatch_PicksTheBetterOfTwoPossiblePositions()
    {
        // Taken greedily this would be the C of Combat and the l of Combat, which highlights the wrong letters and
        // scores as though the match were mid-word. The exploration budget exists for exactly this.
        Indices("Combat Log", "cl").Should().Equal(new[] { 0, 7 }, "because the L of Log is a word start and the l of Combat is not");
    }

    [Fact]
    public void TryMatch_ReportsPositionsInOrder()
    {
        var indices = Indices("Logarithmic Scale", "lgsc");

        indices.Should().BeInAscendingOrder();
        indices.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TryMatch_RefusesRatherThanReportingPartially_WhenTheBufferIsTooSmall()
    {
        Span<int> hits = stackalloc int[2];

        var matched = FuzzyMatcher.TryMatch("Combat Log", "cmbl", hits, out var match);

        matched.Should().BeFalse("because a partly written set of positions would highlight the wrong characters");
        match.Success.Should().BeFalse();
    }

    [Fact]
    public void TryMatch_SucceedsWithNoPositions_WhenTheQueryIsEmpty()
    {
        Span<int> hits = stackalloc int[8];

        FuzzyMatcher.TryMatch("Combat Log", string.Empty, hits, out var match).Should().BeTrue();

        match.MatchedCount.Should().Be(0);
    }

    #endregion

    #region Ranking

    private static readonly string[] Commands =
    [
        "Open Settings", "Combat Log", "Close Window", "Copy Link", "Colour Picker", "Reload Config",
    ];

    [Fact]
    public void Rank_PutsInitialismsAboveMidWordMatches()
    {
        // "cl" read as initials is what a command palette is for, so the two candidates whose words begin with those
        // letters lead. Which of the two leads is decided only by length, so this deliberately does not pin the pair's
        // internal order.
        var ranked = FuzzyMatcher.Rank(Commands, "cl", text => text);

        ranked.Take(2).Should().BeEquivalentTo(["Copy Link", "Combat Log"]);
        ranked.Should().EndWith("Colour Picker", "because its l is buried inside a word");
    }

    [Fact]
    public void Rank_PutsACompleteRunAboveAnInitialism()
    {
        // The counterweight to the test above: two adjacent characters say little, but a whole word says the query is
        // simply a substring of the answer, and that has to beat a pair of word starts.
        var ranked = FuzzyMatcher.Rank(Commands, "colour", text => text);

        ranked[0].Should().Be("Colour Picker");
    }

    [Fact]
    public void Rank_DropsWhatDoesNotMatch()
    {
        var ranked = FuzzyMatcher.Rank(Commands, "config", text => text);

        ranked.Should().ContainSingle().Which.Should().Be("Reload Config");
    }

    [Fact]
    public void Rank_KeepsTheOriginalOrder_WhenTheQueryIsEmpty()
    {
        FuzzyMatcher.Rank(Commands, string.Empty, text => text).Should().Equal(Commands);
    }

    [Fact]
    public void Rank_BreaksTiesByTheOriginalOrder()
    {
        var items = new[] { "aaa", "bbb", "ccc" };

        var ranked = FuzzyMatcher.Rank(items, string.Empty, text => text);

        ranked.Should().Equal(items, "because a list that reshuffles itself between equal-scoring keystrokes reads as broken");
    }

    [Fact]
    public void Rank_ClearsTheDestinationFirst()
    {
        var destination = new List<string> { "stale" };

        FuzzyMatcher.Rank(destination, Commands, "config", text => text);

        destination.Should().ContainSingle().Which.Should().Be("Reload Config");
    }

    [Fact]
    public void Rank_RefusesNullArguments()
    {
        var act = () => FuzzyMatcher.Rank(null!, "a", (string text) => text);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Weights

    [Fact]
    public void Score_FollowsTheWeightsItIsGiven()
    {
        var indifferent = new FuzzyScoring { SeparatorBonus = 0, CamelBonus = 0, FirstLetterBonus = 0 };

        var boundary = FuzzyMatcher.Score("Combat Log", "cl", indifferent);
        var inside = FuzzyMatcher.Score("Circle", "cl", indifferent);

        boundary.Should().BeLessThan(inside,
            "because with the boundary bonuses off the shorter candidate wins on the unmatched-character penalty alone");
    }

    [Fact]
    public void Clone_LeavesTheOriginalAlone()
    {
        var scoring = new FuzzyScoring();

        var clone = scoring.Clone();
        clone.SequentialBonus = 999;
        clone.Separators[0] = '!';

        scoring.SequentialBonus.Should().Be(15);
        scoring.Separators[0].Should().Be(' ', "because a shallow copy would have shared the separator array");
    }

    #endregion

    #region Pathological input

    [Fact]
    public void TryMatch_DoesNotBlowUp_OnARepeatedCharacter()
    {
        // Every position matches every query character, which is the shape that makes a naive explorer take
        // exponential time. The recursion budget is what bounds it.
        var candidate = new string('a', 2000);
        var query = new string('a', 64);

        // Warmed outside the measurement, because the first call through here also pays for JIT and this is not a
        // benchmark. The bound below is deliberately far looser than the real cost: the regression being guarded
        // against is exponential, which takes minutes rather than milliseconds, and anything tighter would fail on a
        // loaded machine for reasons that have nothing to do with the algorithm.
        FuzzyMatcher.Score(candidate, query);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var score = FuzzyMatcher.Score(candidate, query);
        elapsed.Stop();

        score.Should().BeGreaterThan(0, "because zero is reserved for not matching at all");
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "because the search is bounded rather than exhaustive");
    }

    [Fact]
    public void TryMatch_HandlesAQueryLongerThanTheCandidate()
    {
        FuzzyMatcher.Score("ab", "abcdef").Should().Be(0);
    }

    #endregion
}
