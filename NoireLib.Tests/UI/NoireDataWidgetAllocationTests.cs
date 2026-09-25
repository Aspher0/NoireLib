using FluentAssertions;
using NoireLib.UI;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the two row-based widgets at zero allocation per frame, and holds the ids they build to the exact bytes the
/// interpolations they replaced produced.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireDataWidgetAllocationTests : IClassFixture<UiHarness>
{
    private sealed record Row(string Name, string Category, int Level);

    private static readonly NoireTable<Row> SmallTable = BuildTable("small", 10);
    private static readonly NoireTable<Row> BigTable = BuildTable("big", 500);

    private static readonly NoireReorderableList<string> ShortList = BuildList("short", 10);
    private static readonly NoireReorderableList<string> ScreenfulList = BuildList("screenful", 60);
    private static readonly NoireReorderableList<string> LongList = BuildList("long", 500);

    private readonly UiHarness harness;

    public NoireDataWidgetAllocationTests(UiHarness harness) => this.harness = harness;

    private static NoireTable<Row> BuildTable(string id, int count)
    {
        var rows = new List<Row>(count);

        for (var i = 0; i < count; i++)
            rows.Add(new Row("Row " + i, i % 3 == 0 ? "Alpha" : "Beta", i));

        return new NoireTable<Row>("alloc_table_" + id, rows)
        {
            Height = 300f,
            Columns =
            {
                new TableColumn<Row> { Header = "Name", Text = r => r.Name },
                new TableColumn<Row> { Header = "Category", Text = r => r.Category },
            },
        };
    }

    private static NoireReorderableList<string> BuildList(string id, int count)
    {
        var items = new List<string>(count);

        for (var i = 0; i < count; i++)
            items.Add("Item " + i);

        return new NoireReorderableList<string>("alloc_reorder_" + id, items) { AllowDelete = true, AllowDuplicate = true };
    }

    [Fact]
    public void Table_AllocatesNothing()
    {
        // Four frames: a table settles its column widths and sort specs over more frames than other widgets.
        var result = harness.Draw(static () => SmallTable.Draw(), warmUpFrames: 4);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Table_WithFiveHundredRows_CostsWhatTenCost()
    {
        var small = harness.Draw(static () => SmallTable.Draw(), warmUpFrames: 4);
        var big = harness.Draw(static () => BigTable.Draw(), warmUpFrames: 4);

        // The clipper this widget has always had, confirmed rather than assumed: fifty times the rows is the same
        // number of bytes and very nearly the same geometry, because only what fits is drawn.
        big.AllocatedBytes.Should().Be(small.AllocatedBytes);
    }

    [Fact]
    public void Reorder_AllocatesNothing()
    {
        // A shape is recorded for replay the second frame its size is seen, and this layout settles on the second.
        var result = harness.Draw(static () => ShortList.Draw(), warmUpFrames: 3);

        // 24 bytes a row before this, from three ids interpolated per row per frame: the row itself, its duplicate
        // button and its delete button.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Reorder_DrawsEveryRowThatFitsOnScreen()
    {
        var shortList = harness.Draw(static () => ShortList.Draw(), warmUpFrames: 2);
        var screenful = harness.Draw(static () => ScreenfulList.Draw(), warmUpFrames: 2);

        // The other half of the test below: a skip that painted nothing at all would satisfy that one perfectly.
        screenful.TotalVtxCount.Should().BeGreaterThan(shortList.TotalVtxCount);
    }

    [Fact]
    public void Reorder_DoesNotPaintRowsPastTheClipRect()
    {
        var screenful = harness.Draw(static () => ScreenfulList.Draw(), warmUpFrames: 2);
        var longList = harness.Draw(static () => LongList.Draw(), warmUpFrames: 2);

        // Both overflow the display: each paints only what fits.
        longList.TotalVtxCount.Should().Be(screenful.TotalVtxCount);
    }

    [Theory]
    [InlineData("###NoireTableFrame_", "myTable", "###NoireTableFrame_myTable")]
    [InlineData("###NoireTable_", "myTable", "###NoireTable_myTable")]
    [InlineData("###NoireTableSearch_", "myTable", "###NoireTableSearch_myTable")]
    public void TableIds_AreByteIdenticalToTheInterpolationTheyReplaced(string prefix, string owner, string expected)
        => UiIds.For(prefix, owner).Should().Be(expected);

    [Theory]
    [InlineData("###NoireTableRow_", "myTable", 42, "###NoireTableRow_myTable_42")]
    [InlineData("###NoireTableFooterGrip_", "myTable", 3, "###NoireTableFooterGrip_myTable_3")]
    [InlineData("###NoireReorderRow_", "myList", 7, "###NoireReorderRow_myList_7")]
    [InlineData("###NoireReorderCopy_", "myList", 7, "###NoireReorderCopy_myList_7")]
    [InlineData("###NoireReorderDelete_", "myList", 7, "###NoireReorderDelete_myList_7")]
    public void RowIds_AreByteIdenticalToTheInterpolationTheyReplaced(string prefix, string owner, int index, string expected)
        => UiIds.For(prefix, owner, index).Should().Be(expected);

    [Fact]
    public void ResolveComparison_HandsBackTheSameDelegateEveryTime()
    {
        var column = new TableColumn<Row> { Header = "Name", Text = r => r.Name };

        // The table asks three times a frame on a two-column table, and a method group of an instance method builds a
        // fresh delegate on every conversion. That was 192 bytes a frame, more than every id in the widget together.
        column.ResolveComparison().Should().BeSameAs(column.ResolveComparison());
    }

    [Fact]
    public void ResolveComparison_StillFollowsAKeyGivenAfterTheFirstAsk()
    {
        var column = new TableColumn<Row> { Header = "Level", SortKey = r => r.Level };
        var first = column.ResolveComparison()!;

        column.SortKey = r => -r.Level;

        var rows = new[] { new Row("a", "x", 1), new Row("b", "x", 2) };

        // Holding the delegate is only safe because it reads the key at the moment it runs rather than capturing it,
        // so a column reconfigured after its first draw sorts by the new key through the delegate already handed out.
        column.ResolveComparison().Should().BeSameAs(first);
        first(rows[0], rows[1]).Should().BePositive();
    }
}
