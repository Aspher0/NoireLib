using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the non-drawing logic of <see cref="NoireMultiCombo{T}"/>: what is selected, what the closed widget says
/// about it, and what survives the options being replaced.
/// <br/>
/// Selection here is by value rather than by index precisely so it survives that replacement, and nothing about that
/// is visible until the day a plugin swaps its option list and the selection silently points at the wrong things.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireMultiComboTests
{
    private static NoireMultiCombo<string> Create(params string[] items)
        => new("TestMulti", items);

    #region Selection

    [Fact]
    public void Toggle_FlipsSelection_AndReportsWhereItLanded()
    {
        var combo = Create("Apple", "Banana");

        combo.Toggle("Apple").Should().BeTrue();
        combo.IsSelected("Apple").Should().BeTrue();

        combo.Toggle("Apple").Should().BeFalse();
        combo.IsSelected("Apple").Should().BeFalse();
    }

    [Fact]
    public void Set_ReportsOnlyRealChanges()
    {
        var combo = Create("Apple", "Banana");

        combo.Set("Apple", true).Should().BeTrue();
        combo.Set("Apple", true).Should().BeFalse("because selecting what is already selected changes nothing");
    }

    [Fact]
    public void Selected_KeepsTheOrderTheOptionsWereGivenIn()
    {
        var combo = Create("Apple", "Banana", "Cherry");

        combo.Toggle("Cherry");
        combo.Toggle("Apple");

        combo.Selected.Should().Equal(new[] { "Apple", "Cherry" },
            "because a set has no order of its own and the option list is the one a reader expects");
    }

    [Fact]
    public void SelectAll_And_ClearSelection_MoveEverything()
    {
        var combo = Create("Apple", "Banana", "Cherry");

        combo.SelectAll();
        combo.SelectedCount.Should().Be(3);

        combo.ClearSelection();
        combo.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void SetSelection_ReplacesOutright()
    {
        var combo = Create("Apple", "Banana", "Cherry");
        combo.SelectAll();

        combo.SetSelection(new[] { "Banana" });

        combo.Selected.Should().Equal(new[] { "Banana" });
    }

    [Fact]
    public void OnSelectionChanged_FiresOncePerChange()
    {
        var combo = Create("Apple", "Banana");
        var notifications = new List<int>();
        combo.OnSelectionChanged = chosen => notifications.Add(chosen.Count);

        combo.Toggle("Apple");
        combo.Set("Apple", true);      // no change
        combo.Toggle("Banana");

        notifications.Should().Equal(new[] { 1, 2 });
    }

    #endregion

    #region Replacing the options

    [Fact]
    public void SetItems_DropsSelectionsNoLongerOnOffer()
    {
        var combo = Create("Apple", "Banana", "Cherry");
        combo.SelectAll();

        combo.SetItems(new[] { "Banana", "Damson" });

        combo.Selected.Should().Equal(new[] { "Banana" },
            "because a selection that reports items the widget no longer offers is a lie about its own state");
    }

    [Fact]
    public void SetItems_KeepsASelectionThatStillApplies()
    {
        var combo = Create("Apple", "Banana");
        combo.Toggle("Banana");

        combo.SetItems(new[] { "Banana", "Cherry" });

        combo.IsSelected("Banana").Should().BeTrue();
    }

    #endregion

    #region Preview

    [Fact]
    public void BuildPreview_ShowsThePlaceholder_WhenNothingIsSelected()
    {
        var combo = Create("Apple", "Banana");
        combo.PreviewPlaceholder = "Nothing";

        combo.BuildPreview().Should().Be("Nothing");
    }

    [Fact]
    public void BuildPreview_NamesTheItems_WhileTheyFit()
    {
        var combo = Create("Apple", "Banana", "Cherry");
        combo.Toggle("Apple");
        combo.Toggle("Banana");

        combo.BuildPreview().Should().Be("Apple, Banana");
    }

    [Fact]
    public void BuildPreview_SummarisesTheRest_OnceThereAreTooMany()
    {
        var combo = Create("Apple", "Banana", "Cherry", "Damson");
        combo.SelectAll();
        combo.PreviewMaxItems = 2;

        combo.BuildPreview().Should().Contain("Apple, Banana").And.Contain("+2 more");
    }

    [Fact]
    public void BuildPreview_UsesACallbackWhenGivenOne()
    {
        var combo = Create("Apple", "Banana");
        combo.SelectAll();
        combo.PreviewFunc = chosen => $"{chosen.Count} things";

        combo.BuildPreview().Should().Be("2 things");
    }

    [Fact]
    public void BuildPreview_FallsBackToTheDefault_WhenACallbackThrows()
    {
        var combo = Create("Apple");
        combo.Toggle("Apple");
        combo.PreviewFunc = _ => throw new InvalidOperationException("boom");

        combo.BuildPreview().Should().Be("Apple", "because a broken preview should not take the widget with it");
    }

    #endregion

    #region Filtering

    [Fact]
    public void RebuildFilteredIndices_MatchesFuzzilyAndOrdersByScore()
    {
        var combo = Create("Pineapple", "Apple", "Banana");
        combo.FilterText = "apple";
        combo.RebuildFilteredIndices();

        combo.FilteredIndices.Should().Equal(new[] { 1, 0 }, "because the best match leads");
    }

    [Fact]
    public void RebuildFilteredIndices_FallsBackToContains_WhenFuzzyIsOff()
    {
        var combo = Create("Pineapple", "Apple", "Combat Log");
        combo.FilterFuzzy = false;
        combo.FilterText = "apple";
        combo.RebuildFilteredIndices();

        combo.FilteredIndices.Should().Equal(new[] { 0, 1 });
    }

    [Fact]
    public void RebuildFilteredIndices_KeepsEverything_WhenTheFilterIsEmpty()
    {
        var combo = Create("Apple", "Banana");
        combo.FilterText = string.Empty;
        combo.RebuildFilteredIndices();

        combo.FilteredIndices.Should().Equal(new[] { 0, 1 });
    }

    #endregion
}
