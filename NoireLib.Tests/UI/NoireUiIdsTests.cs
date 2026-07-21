using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the id cache against the interpolation it replaced.
/// </summary>
/// <remarks>
/// Every assertion here compares against the interpolated string written out longhand, which is the whole point: widget
/// ids reach <see cref="NoireUiState"/> keys, so an id that changed shape would not look like a bug, it would look like
/// every user's saved values having quietly vanished.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireUiIdsTests
{
    private const string Owner = "myWidget";

    [Fact]
    public void For_Owner_MatchesTheInterpolation()
        => UiIds.For("###NoireCombo_", Owner).Should().Be($"###NoireCombo_{Owner}");

    [Fact]
    public void For_OwnerAndIndex_MatchesTheInterpolation()
    {
        UiIds.For("###NoireComboItem_", Owner, 42).Should().Be($"###NoireComboItem_{Owner}_{42}");
        UiIds.For("###NoireComboItem_", Owner, 0).Should().Be($"###NoireComboItem_{Owner}_{0}");
    }

    [Fact]
    public void For_OwnerAndSuffix_MatchesTheInterpolation()
    {
        const string suggestion = "healer";
        UiIds.For("###NoireTagSuggestion_", Owner, suggestion).Should().Be($"###NoireTagSuggestion_{Owner}_{suggestion}");
    }

    [Fact]
    public void Join_MatchesTheInterpolation()
        => UiIds.Join("ComboBox.", Owner, ".filter").Should().Be($"ComboBox.{Owner}.filter");

    [Fact]
    public void Labelled_MatchesTheInterpolation()
    {
        const string label = "Choose a job";
        UiIds.Labelled(label, "###NoireCombo_", Owner).Should().Be($"{label}###NoireCombo_{Owner}");
    }

    [Fact]
    public void Labelled_WithSuffix_MatchesTheInterpolation()
    {
        const string label = "Settings";
        const string tab = "general";

        UiIds.Labelled(label, "###NoireTab_", Owner, tab).Should().Be($"{label}###NoireTab_{Owner}_{tab}");
    }

    [Fact]
    public void Labelled_WithSuffixAndIndex_MatchesTheInterpolation()
    {
        const string label = "Left";
        UiIds.Labelled(label, "##", Owner, "Segment", 2).Should().Be($"{label}##{Owner}Segment{2}");
    }

    [Fact]
    public void Shapes_ThatShareTheirParts_DoNotCollide()
    {
        // The separator is the only thing telling these two apart, so the shape has to be part of the key rather than
        // inferred from which arguments were passed.
        UiIds.For("p", Owner, "x").Should().NotBe(UiIds.Join("p", Owner, "x"));
    }

    [Fact]
    public void Repeated_AsksReturnTheSameInstance()
    {
        // Not merely equal: the point of the cache is that the second frame allocates nothing at all.
        var first = UiIds.For("###NoireComboItem_", "repeat", 7);
        var second = UiIds.For("###NoireComboItem_", "repeat", 7);

        ReferenceEquals(first, second).Should().BeTrue();
    }
}
