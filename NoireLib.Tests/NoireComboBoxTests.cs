using FluentAssertions;
using NoireLib.UI;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the non-drawing logic of <see cref="NoireComboBox{T}"/>: cycling, filtering and selection management.
/// </summary>
public class NoireComboBoxTests
{
    private static NoireComboBox<string> CreateCombo(params string[] items)
        => new("TestCombo", items);

    #region Cycling

    [Theory]
    [InlineData(0, 1, 5, false, 1)] // Forward
    [InlineData(4, 1, 5, false, 4)] // Clamped at the end
    [InlineData(0, -1, 5, false, 0)] // Clamped at the start
    [InlineData(4, 1, 5, true, 0)] // Loops end -> start
    [InlineData(0, -1, 5, true, 4)] // Loops start -> end
    [InlineData(2, -1, 5, true, 1)] // Backward
    public void ComputeCycledIndex_CyclesWithAndWithoutLooping(int current, int direction, int count, bool loop, int expected)
    {
        NoireComboBox<string>.ComputeCycledIndex(current, direction, count, loop).Should().Be(expected);
    }

    [Fact]
    public void ComputeCycledIndex_NoItems_ReturnsMinusOne()
    {
        NoireComboBox<string>.ComputeCycledIndex(0, 1, 0, true).Should().Be(-1);
    }

    [Fact]
    public void ComputeCycledIndex_NoSelection_StartsAtBoundary()
    {
        NoireComboBox<string>.ComputeCycledIndex(-1, 1, 5, false).Should().Be(0);
        NoireComboBox<string>.ComputeCycledIndex(-1, -1, 5, false).Should().Be(4);
    }

    #endregion

    #region Filtering

    [Theory]
    [InlineData("Hello World", "world", true)]
    [InlineData("Hello World", "WORLD", true)]
    [InlineData("Hello World", "lo Wo", true)]
    [InlineData("Hello World", "banana", false)]
    public void DefaultFilterMatch_IsCaseInsensitiveContains(string display, string filter, bool expected)
    {
        NoireComboBox<string>.DefaultFilterMatch(display, filter).Should().Be(expected);
    }

    [Fact]
    public void RebuildFilteredIndices_WithoutFilterEnabled_KeepsEverything()
    {
        var combo = CreateCombo("Apple", "Banana", "Cherry");
        combo.FilterText = "apple";
        combo.RebuildFilteredIndices();

        combo.FilteredIndices.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void RebuildFilteredIndices_WithFilter_KeepsMatchingItemsOnly()
    {
        var combo = CreateCombo("Apple", "Banana", "Pineapple");
        combo.FilterEnabled = true;
        combo.FilterText = "apple";
        combo.RebuildFilteredIndices();

        combo.FilteredIndices.Should().Equal(0, 2);
    }

    [Fact]
    public void RebuildFilteredIndices_WithEmptyFilter_KeepsEverything()
    {
        var combo = CreateCombo("Apple", "Banana");
        combo.FilterEnabled = true;
        combo.FilterText = string.Empty;
        combo.RebuildFilteredIndices();

        combo.FilteredIndices.Should().Equal(0, 1);
    }

    [Fact]
    public void RebuildFilteredIndices_UsesCustomPredicate()
    {
        var combo = CreateCombo("Apple", "Banana", "Cherry");
        combo.FilterEnabled = true;
        combo.FilterPredicate = (item, filter) => item.Length == int.Parse(filter);
        combo.FilterText = "6";
        combo.RebuildFilteredIndices();

        combo.FilteredIndices.Should().Equal(1, 2); // Banana and Cherry both have 6 characters.
    }

    #endregion

    #region Selection

    [Fact]
    public void Select_ByIndex_ChangesSelectionAndNotifies()
    {
        var combo = CreateCombo("A", "B", "C");
        var notifications = new List<(string? Old, string? New)>();
        combo.OnSelectionChanged = (oldItem, newItem) => notifications.Add((oldItem, newItem));

        combo.Select(1).Should().BeTrue();

        combo.SelectedIndex.Should().Be(1);
        combo.SelectedItem.Should().Be("B");
        notifications.Should().ContainSingle().Which.Should().Be(((string?)null, "B"));
    }

    [Fact]
    public void Select_SameIndex_DoesNothing()
    {
        var combo = CreateCombo("A", "B");
        combo.Select(1);

        var notified = false;
        combo.OnSelectionChanged = (_, _) => notified = true;

        combo.Select(1).Should().BeFalse();
        notified.Should().BeFalse();
    }

    [Fact]
    public void Select_ByItem_FindsTheItem()
    {
        var combo = CreateCombo("A", "B", "C");
        combo.Select("C").Should().BeTrue();
        combo.SelectedIndex.Should().Be(2);
    }

    [Fact]
    public void Select_UnknownItem_ReturnsFalse()
    {
        var combo = CreateCombo("A", "B");
        combo.Select("Z").Should().BeFalse();
        combo.SelectedIndex.Should().Be(-1);
    }

    [Fact]
    public void ClearSelection_ResetsToMinusOneAndNotifies()
    {
        var combo = CreateCombo("A", "B");
        combo.Select(0);

        (string? Old, string? New)? notification = null;
        combo.OnSelectionChanged = (oldItem, newItem) => notification = (oldItem, newItem);

        combo.ClearSelection().Should().BeTrue();

        combo.SelectedIndex.Should().Be(-1);
        combo.SelectedItem.Should().BeNull();
        notification.Should().Be(("A", (string?)null));
    }

    [Fact]
    public void SelectedIndex_Setter_ClampsWithoutNotifying()
    {
        var combo = CreateCombo("A", "B");
        var notified = false;
        combo.OnSelectionChanged = (_, _) => notified = true;

        combo.SelectedIndex = 10;
        combo.SelectedIndex.Should().Be(1);

        combo.SelectedIndex = -5;
        combo.SelectedIndex.Should().Be(-1);

        notified.Should().BeFalse();
    }

    #endregion

    #region SetItems

    [Fact]
    public void SetItems_KeepsSelectionWhenItemStillPresent()
    {
        var combo = CreateCombo("A", "B", "C");
        combo.Select(1);

        combo.SetItems(new[] { "C", "B", "A" });

        combo.SelectedIndex.Should().Be(1);
        combo.SelectedItem.Should().Be("B");
    }

    [Fact]
    public void SetItems_ClearsSelectionWhenItemRemoved()
    {
        var combo = CreateCombo("A", "B");
        combo.Select(1);

        combo.SetItems(new[] { "X", "Y" });

        combo.SelectedIndex.Should().Be(-1);
    }

    [Fact]
    public void SetItems_WithoutKeepSelection_AlwaysClears()
    {
        var combo = CreateCombo("A", "B");
        combo.Select(0);

        combo.SetItems(new[] { "A", "B" }, keepSelection: false);

        combo.SelectedIndex.Should().Be(-1);
    }

    #endregion
}
