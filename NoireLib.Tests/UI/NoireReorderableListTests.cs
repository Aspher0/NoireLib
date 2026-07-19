using FluentAssertions;
using NoireLib.UI;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests.UI;

/// <summary>
/// Reordering is the part of a drag-to-reorder list that a drag cannot demonstrate: every off-by-one lives in what
/// "dropped at index 4" means when the row came from above rather than from below it.
/// </summary>
public class NoireReorderableListTests
{
    private static List<string> Sample() => ["a", "b", "c", "d", "e"];

    private static bool Move(List<string> list, int from, int to)
        => NoireReorderableList<string>.MoveItem(list, from, to);

    #region Moving

    [Fact]
    public void MoveItem_MovesARowDown_ShiftingTheRestUp()
    {
        var list = Sample();

        Move(list, 1, 3).Should().BeTrue();

        list.Should().Equal(new[] { "a", "c", "d", "b", "e" });
    }

    [Fact]
    public void MoveItem_MovesARowUp_ShiftingTheRestDown()
    {
        var list = Sample();

        Move(list, 3, 1).Should().BeTrue();

        list.Should().Equal(new[] { "a", "d", "b", "c", "e" });
    }

    [Fact]
    public void MoveItem_MovesToTheStart()
    {
        var list = Sample();

        Move(list, 4, 0).Should().BeTrue();

        list.Should().Equal(new[] { "e", "a", "b", "c", "d" });
    }

    [Fact]
    public void MoveItem_MovesToTheEnd()
    {
        var list = Sample();

        Move(list, 0, 4).Should().BeTrue();

        list.Should().Equal(new[] { "b", "c", "d", "e", "a" });
    }

    [Fact]
    public void MoveItem_ClampsPastTheEnd()
    {
        // A drag that ends below the last row means "put it last", which is the one thing the user was unambiguously
        // asking for.
        var list = Sample();

        Move(list, 0, 99).Should().BeTrue();

        list.Should().Equal(new[] { "b", "c", "d", "e", "a" });
    }

    [Fact]
    public void MoveItem_ClampsPastTheStart()
    {
        var list = Sample();

        Move(list, 4, -99).Should().BeTrue();

        list.Should().Equal(new[] { "e", "a", "b", "c", "d" });
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(99, 2)]
    public void MoveItem_DoesNothing_ForAMoveThatIsNotOne(int from, int to)
    {
        var list = Sample();

        Move(list, from, to).Should().BeFalse();

        list.Should().Equal(new[] { "a", "b", "c", "d", "e" });
    }

    [Fact]
    public void MoveItem_KeepsEveryRow()
    {
        // The invariant that matters more than any single ordering: a reorder never loses or invents a row.
        var list = Sample();

        for (var from = 0; from < 5; from++)
        {
            for (var to = 0; to < 5; to++)
            {
                var working = Sample();
                Move(working, from, to);

                working.Should().BeEquivalentTo(list, "because moving {0} to {1} reorders the list, it does not edit it", from, to);
                working.Should().OnlyHaveUniqueItems();
            }
        }
    }

    [Fact]
    public void MoveItem_RoundTrips()
    {
        var list = Sample();

        Move(list, 1, 3);
        Move(list, 3, 1);

        list.Should().Equal(new[] { "a", "b", "c", "d", "e" }, "because moving a row back where it came from undoes it");
    }

    #endregion

    #region Nudging

    [Theory]
    [InlineData(2, -1, 1)]
    [InlineData(2, 1, 3)]
    [InlineData(0, -1, 0)]
    [InlineData(4, 1, 4)]
    public void Nudge_ReturnsWhereTheRowEndedUp(int index, int offset, int expected)
    {
        // The return value is what the keyboard path follows the focus with, so a nudge at the end of the list has to
        // report the index it did not move from rather than one off the end.
        var list = Sample();

        NoireReorderableList<string>.Nudge(list, index, offset).Should().Be(expected);
    }

    [Fact]
    public void Nudge_MovesTheRow()
    {
        var list = Sample();

        NoireReorderableList<string>.Nudge(list, 0, 1);

        list.Should().Equal(new[] { "b", "a", "c", "d", "e" });
    }

    #endregion

    #region Where a drag lands

    /// <summary>
    /// Rows 34 px apart starting at y = 100, so row 0 covers 100..134, row 1 covers 134..168, and so on.
    /// </summary>
    [Theory]
    [InlineData(100f, 0)]
    [InlineData(133f, 0)]
    [InlineData(134f, 1)]
    [InlineData(167f, 1)]
    [InlineData(168f, 2)]
    [InlineData(236f, 4)]
    public void ResolveSlot_FindsTheRowUnderThePointer(float pointerY, int expected)
    {
        NoireReorderableList<string>.ResolveSlot(pointerY, 100f, 34f, 5).Should().Be(expected);
    }

    [Theory]
    [InlineData(-500f, 0)]
    [InlineData(99f, 0)]
    [InlineData(5000f, 4)]
    public void ResolveSlot_ClampsAPointerOutsideTheList(float pointerY, int expected)
    {
        // A drag that leaves the widget still means something: the nearest end.
        NoireReorderableList<string>.ResolveSlot(pointerY, 100f, 34f, 5).Should().Be(expected);
    }

    [Fact]
    public void ResolveSlot_ResolvesBothDirections()
    {
        // The bug this replaced: the target came from which row reported itself hovered, and during a drag ImGui gives
        // the hover to nothing but the active row, so dragging worked one way and not the other.
        var upward = NoireReorderableList<string>.ResolveSlot(110f, 100f, 34f, 5);
        var downward = NoireReorderableList<string>.ResolveSlot(240f, 100f, 34f, 5);

        upward.Should().Be(0);
        downward.Should().Be(4);
    }

    [Fact]
    public void ResolveSlot_HandlesAnEmptyOrDegenerateList()
    {
        NoireReorderableList<string>.ResolveSlot(120f, 100f, 34f, 0).Should().Be(-1);
        NoireReorderableList<string>.ResolveSlot(120f, 100f, 0f, 5).Should().Be(0);
    }

    #endregion

    #region The widget around it

    [Fact]
    public void Move_ReportsTheChange()
    {
        var list = Sample();
        var widget = new NoireReorderableList<string>("test", list);
        var seen = 0;
        widget.OnChanged = _ => seen++;

        widget.Move(0, 1).Should().BeTrue();
        widget.Move(0, 0).Should().BeFalse();

        seen.Should().Be(1, "because a move that changed nothing is not a change");
    }

    [Fact]
    public void MoveUpAndDown_WalkTheRow()
    {
        var list = Sample();
        var widget = new NoireReorderableList<string>("test", list);

        widget.MoveDown(0);
        widget.MoveDown(1);

        list.Should().Equal(new[] { "b", "c", "a", "d", "e" });

        widget.MoveUp(2);

        list.Should().Equal(new[] { "b", "a", "c", "d", "e" });
    }

    [Fact]
    public void RemoveAt_TakesTheRowOut()
    {
        var list = Sample();
        var widget = new NoireReorderableList<string>("test", list);

        widget.RemoveAt(1).Should().BeTrue();
        widget.RemoveAt(99).Should().BeFalse();

        list.Should().Equal(new[] { "a", "c", "d", "e" });
    }

    [Fact]
    public void DuplicateAt_PutsTheCopyDirectlyBelow()
    {
        var list = Sample();
        var widget = new NoireReorderableList<string>("test", list);

        widget.DuplicateAt(1).Should().BeTrue();

        list.Should().Equal(new[] { "a", "b", "b", "c", "d", "e" });
    }

    [Fact]
    public void DuplicateAt_UsesTheCallback_ForAnythingMutable()
    {
        // Without the callback the copy and the original are one object, and editing either edits both.
        var list = new List<List<int>> { new() { 1, 2 } };
        var widget = new NoireReorderableList<List<int>>("test", list)
        {
            Duplicate = source => new List<int>(source),
        };

        widget.DuplicateAt(0);

        list.Should().HaveCount(2);
        list[1].Should().NotBeSameAs(list[0]);
        list[1].Should().Equal(list[0]);
    }

    [Fact]
    public void DuplicateAt_SharesTheRow_WithoutACallback()
    {
        var list = new List<List<int>> { new() { 1, 2 } };
        var widget = new NoireReorderableList<List<int>>("test", list);

        widget.DuplicateAt(0);

        list[1].Should().BeSameAs(list[0], "because that is what the documentation says happens, and why Duplicate exists");
    }

    [Fact]
    public void Items_DefaultsToAnEmptyList_RatherThanNull()
    {
        var widget = new NoireReorderableList<string>("test");

        widget.Items.Should().NotBeNull().And.BeEmpty();

        widget.Items = null!;

        widget.Items.Should().NotBeNull("because a null list must not turn every later call into an exception");
    }

    #endregion
}
