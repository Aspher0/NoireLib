using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests.UI;

/// <summary>
/// The table's index pipeline: which rows survive, in what order, and what an export writes. All of it runs without
/// an ImGui context, which is the point of keeping it out of the drawing.
/// </summary>
public class NoireTableTests
{
    private sealed record Row(string Name, string World, int Level);

    private static List<Row> Sample() =>
    [
        new("Alphinaud", "Ravana", 90),
        new("Yshtola", "Ravana", 100),
        new("Estinien", "Zodiark", 100),
        new("Krile", "Zodiark", 80),
    ];

    private static List<TableColumn<Row>> Columns() =>
    [
        new() { Header = "Name", Text = r => r.Name },
        new() { Header = "World", Text = r => r.World },
        new() { Header = "Level", Text = r => r.Level.ToString(), SortKey = r => r.Level },
    ];

    private static List<int> Build(IReadOnlyList<Row> rows, IReadOnlyList<TableColumn<Row>> columns, string search = "", bool fuzzy = false)
    {
        var indices = new List<int>();
        NoireTable<Row>.BuildVisible(rows, columns, search, fuzzy, indices);
        return indices;
    }

    #region Filtering and search

    [Fact]
    public void BuildVisible_KeepsEverything_WhenNothingFilters()
    {
        Build(Sample(), Columns()).Should().Equal(new[] { 0, 1, 2, 3 }, "because an untouched table shows the list as given");
    }

    [Fact]
    public void BuildVisible_AppliesAColumnFilterText()
    {
        var columns = Columns();
        columns[1].FilterText = "zodiark";

        Build(Sample(), columns).Should().Equal(new[] { 2, 3 }, "because a column filter is matched case-insensitively");
    }

    [Fact]
    public void BuildVisible_AppliesAColumnPredicate()
    {
        var columns = Columns();
        columns[2].Filter = row => row.Level >= 90;

        Build(Sample(), columns).Should().Equal(new[] { 0, 1, 2 });
    }

    [Fact]
    public void BuildVisible_RequiresEveryColumnFilterToPass()
    {
        var columns = Columns();
        columns[1].FilterText = "zodiark";
        columns[2].Filter = row => row.Level >= 90;

        Build(Sample(), columns).Should().Equal(new[] { 2 }, "because filters narrow together rather than each on their own");
    }

    [Fact]
    public void BuildVisible_MatchesTheSearchAgainstAnyColumn()
    {
        // A hit in one column is enough: someone searching above a table is looking for a row, not for a row whose
        // every column says the same thing.
        Build(Sample(), Columns(), "ravana").Should().Equal(new[] { 0, 1 });
    }

    [Fact]
    public void BuildVisible_SkipsColumnsThatOptOutOfSearch()
    {
        var columns = Columns();
        columns[1].Searchable = false;

        Build(Sample(), columns, "ravana").Should().BeEmpty("because the only column that said it was searchable no longer matches");
    }

    [Fact]
    public void BuildVisible_SkipsHiddenColumnsEntirely()
    {
        var columns = Columns();
        columns[1].Visible = false;
        columns[1].FilterText = "zodiark";

        Build(Sample(), columns).Should().HaveCount(4, "because a column nobody can see must not be filtering behind their back");
    }

    [Fact]
    public void BuildVisible_MatchesOutOfOrder_WhenFuzzy()
    {
        Build(Sample(), Columns(), "alph", fuzzy: true).Should().Equal(new[] { 0 });
        Build(Sample(), Columns(), "apnd", fuzzy: true).Should().Equal(new[] { 0 }, "because a fuzzy search matches a subsequence");
        Build(Sample(), Columns(), "apnd").Should().BeEmpty("because a plain search does not");
    }

    [Fact]
    public void BuildVisible_IgnoresAWhitespaceSearch()
    {
        Build(Sample(), Columns(), "   ").Should().HaveCount(4);
    }

    [Fact]
    public void BuildVisible_ClearsWhatItWasGiven()
    {
        var indices = new List<int> { 99, 98 };

        NoireTable<Row>.BuildVisible(Sample(), Columns(), "ravana", false, indices);

        indices.Should().Equal(new[] { 0, 1 }, "because a reused buffer that keeps its old contents is a table showing rows twice");
    }

    [Fact]
    public void BuildVisible_SurvivesAThrowingCallback()
    {
        var columns = Columns();
        columns[0].Text = _ => throw new InvalidOperationException("boom");

        var act = () => Build(Sample(), columns, "ravana");

        act.Should().NotThrow("because a callback of the caller's must not take a frame down with it");
    }

    #endregion

    #region Sorting

    [Fact]
    public void SortVisible_OrdersByTheColumnsText()
    {
        var rows = Sample();
        var columns = Columns();
        var indices = Build(rows, columns);

        NoireTable<Row>.SortVisible(rows, indices, columns[0], descending: false);

        indices.Select(i => rows[i].Name).Should().Equal(new[] { "Alphinaud", "Estinien", "Krile", "Yshtola" });
    }

    [Fact]
    public void SortVisible_OrdersByTheSortKey_NotTheTextThatShows()
    {
        // The level column shows "80" and "100"; as text "100" sorts before "80", and as a number it does not.
        var rows = Sample();
        var columns = Columns();
        var indices = Build(rows, columns);

        NoireTable<Row>.SortVisible(rows, indices, columns[2], descending: false);

        indices.Select(i => rows[i].Level).Should().Equal(new[] { 80, 90, 100, 100 });
    }

    [Fact]
    public void SortVisible_Reverses_WhenDescending()
    {
        var rows = Sample();
        var columns = Columns();
        var indices = Build(rows, columns);

        NoireTable<Row>.SortVisible(rows, indices, columns[2], descending: true);

        indices.Select(i => rows[i].Level).Should().Equal(new[] { 100, 100, 90, 80 });
    }

    [Fact]
    public void SortVisible_BreaksTiesOnSourceOrder()
    {
        // List.Sort is an introsort and promises nothing for equal elements, so a column full of ties would reshuffle
        // its rows every time anything else changed the table.
        var rows = Sample();
        var columns = Columns();
        var indices = Build(rows, columns);

        NoireTable<Row>.SortVisible(rows, indices, columns[1], descending: false);

        indices.Should().Equal(new[] { 0, 1, 2, 3 }, "because two rows on the same world keep the order they arrived in");
    }

    [Fact]
    public void SortVisible_IsDeterministicAcrossRuns()
    {
        var rows = Sample();
        var columns = Columns();

        var first = Build(rows, columns);
        var second = Build(rows, columns);

        NoireTable<Row>.SortVisible(rows, first, columns[1], descending: true);
        NoireTable<Row>.SortVisible(rows, second, columns[1], descending: true);

        second.Should().Equal(first);
    }

    [Fact]
    public void SortVisible_LeavesSourceOrder_ForNoColumn()
    {
        var rows = Sample();
        var indices = Build(rows, Columns());

        NoireTable<Row>.SortVisible(rows, indices, null, descending: false);

        indices.Should().Equal(new[] { 0, 1, 2, 3 });
    }

    [Fact]
    public void SortVisible_LeavesSourceOrder_ForAColumnThatDescribesNoOrder()
    {
        var rows = Sample();
        var indices = Build(rows, Columns());
        var column = new TableColumn<Row> { Header = "Icon" };

        NoireTable<Row>.SortVisible(rows, indices, column, descending: false);

        indices.Should().Equal(new[] { 0, 1, 2, 3 }, "because a column with no text and no key has nothing to sort on");
    }

    [Fact]
    public void SortVisible_RespectsSortableBeingOff()
    {
        var rows = Sample();
        var indices = Build(rows, Columns());
        var column = new TableColumn<Row> { Header = "Name", Text = r => r.Name, Sortable = false };

        NoireTable<Row>.SortVisible(rows, indices, column, descending: false);

        indices.Should().Equal(new[] { 0, 1, 2, 3 });
    }

    [Fact]
    public void SortVisible_PutsMissingKeysFirst()
    {
        var rows = new List<Row> { new("A", "W", 1), new("B", "W", 2) };
        var indices = new List<int> { 0, 1 };
        var column = new TableColumn<Row> { Header = "Maybe", SortKey = r => r.Level == 1 ? null : r.Level };

        NoireTable<Row>.SortVisible(rows, indices, column, descending: false);

        indices.Should().Equal(new[] { 0, 1 }, "because a row with nothing to compare sorts before one that has something");
    }

    #endregion

    #region Export

    [Fact]
    public void BuildCsv_WritesTheHeaderAndTheVisibleRowsInOrder()
    {
        var rows = Sample();
        var columns = Columns();

        var csv = NoireTable<Row>.BuildCsv(rows, columns, new[] { 2, 0 });

        csv.Should().Be("Name,World,Level\nEstinien,Zodiark,100\nAlphinaud,Ravana,90\n");
    }

    [Fact]
    public void BuildCsv_SkipsHiddenColumns()
    {
        var columns = Columns();
        columns[1].Visible = false;

        var csv = NoireTable<Row>.BuildCsv(Sample(), columns, new[] { 0 });

        csv.Should().Be("Name,Level\nAlphinaud,90\n", "because an export of what is on screen does not include what is not");
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    [InlineData("", "")]
    public void BuildCsv_QuotesOnlyWhatHasToBe(string value, string expected)
    {
        var rows = new List<Row> { new(value, "w", 1) };
        var columns = new List<TableColumn<Row>> { new() { Header = "Name", Text = r => r.Name } };

        var csv = NoireTable<Row>.BuildCsv(rows, columns, new[] { 0 });

        csv.Should().Be($"Name\n{expected}\n", "because a field with a comma in it must not split the row in a spreadsheet");
    }

    [Fact]
    public void BuildCsv_IgnoresAnIndexThatIsNotThere()
    {
        var act = () => NoireTable<Row>.BuildCsv(Sample(), Columns(), new[] { 0, 99, -1 });

        act.Should().NotThrow();
        NoireTable<Row>.BuildCsv(Sample(), Columns(), new[] { 0, 99, -1 })
            .Should().Be("Name,World,Level\nAlphinaud,Ravana,90\n");
    }

    #endregion

    #region The widget around it

    [Fact]
    public void VisibleRows_RunsThePipelineOnDemand()
    {
        var table = new NoireTable<Row>("test", Sample());
        table.Columns.AddRange(Columns());
        table.Search = "zodiark";

        table.VisibleRows.Select(r => r.Name).Should().Equal(new[] { "Estinien", "Krile" });
        table.VisibleCount.Should().Be(2);
    }

    [Fact]
    public void SortBy_OrdersTheVisibleRows()
    {
        var table = new NoireTable<Row>("test", Sample());
        table.Columns.AddRange(Columns());

        table.SortBy(table.Columns[2], descending: true);

        table.VisibleRows.Select(r => r.Level).Should().Equal(new[] { 100, 100, 90, 80 });
    }

    [Fact]
    public void Selected_ComesBackInSourceOrder()
    {
        // A set has no order; the source list is the one a reader expects, and the one the table draws in.
        var sample = Sample();
        var table = new NoireTable<Row>("test", sample);
        table.SelectionMode = TableSelection.Multiple;

        table.SetSelected(sample[3], true);
        table.SetSelected(sample[1], true);

        table.Selected.Select(r => r.Name).Should().Equal(new[] { "Yshtola", "Krile" });
    }

    [Fact]
    public void SetSelected_ReleasesTheOthers_WhenSingle()
    {
        var sample = Sample();
        var table = new NoireTable<Row>("test", sample) { SelectionMode = TableSelection.Single };

        table.SetSelected(sample[0], true);
        table.SetSelected(sample[2], true);

        table.Selected.Should().HaveCount(1);
        table.IsSelected(sample[2]).Should().BeTrue();
    }

    [Fact]
    public void ToCsv_ExportsWhatIsShowing()
    {
        var table = new NoireTable<Row>("test", Sample());
        table.Columns.AddRange(Columns());
        table.Search = "zodiark";
        table.SortBy(table.Columns[0], descending: true);

        table.ToCsv().Should().Be("Name,World,Level\nKrile,Zodiark,80\nEstinien,Zodiark,100\n",
            "because an export that quietly hands back the unfiltered table is the one thing nobody can check by looking");
    }

    [Fact]
    public void Rows_ReplacedRebuildsTheVisibleSet()
    {
        var table = new NoireTable<Row>("test", Sample());
        table.Columns.AddRange(Columns());
        table.Search = "ravana";

        table.VisibleCount.Should().Be(2);

        table.Rows = [new Row("Solo", "Ravana", 1)];

        table.VisibleRows.Select(r => r.Name).Should().Equal(new[] { "Solo" });
    }

    #endregion
}
