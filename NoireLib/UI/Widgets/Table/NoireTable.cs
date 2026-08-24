using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A data grid over a list you already have: sortable, searchable, filterable, virtualized, selectable, totalled and
/// exportable.
/// </summary>
/// <remarks>The table never copies your rows; it holds the list you gave it and works in indices into it.</remarks>
/// <typeparam name="T">The row type.</typeparam>
[NoireFacadeFactory]
public sealed partial class NoireTable<T>
{
    private readonly List<int> visible = new();
    private readonly List<T> visibleRows = new();
    private readonly HashSet<T> selected;

    private IReadOnlyList<T> rows = Array.Empty<T>();
    private string search = string.Empty;
    private bool dirty = true;

    /// <summary>
    /// Creates a table.
    /// </summary>
    /// <param name="id">A stable id for the widget. When <see langword="null"/>, a random one is generated.</param>
    /// <param name="rows">The rows to show. Held, never copied.</param>
    /// <param name="comparer">How two rows are compared for selection. When <see langword="null"/>, the type's own equality.</param>
    public NoireTable(string? id = null, IReadOnlyList<T>? rows = null, IEqualityComparer<T>? comparer = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;
        selected = new HashSet<T>(comparer ?? EqualityComparer<T>.Default);

        if (rows != null)
            Rows = rows;
    }

    /// <summary>The unique identifier of this widget, used for the ImGui ids.</summary>
    public string Id { get; }

    /// <summary>The columns, in the order they are drawn.</summary>
    public List<TableColumn<T>> Columns { get; } = new();

    /// <summary>
    /// The rows to show, held rather than copied.
    /// </summary>
    /// <remarks>Assigning marks the table for a rebuild; editing the list in place needs <see cref="Invalidate"/>.</remarks>
    public IReadOnlyList<T> Rows
    {
        get => rows;
        set
        {
            rows = value ?? Array.Empty<T>();
            Invalidate();
        }
    }

    #region Searching and filtering

    /// <summary>The global search text, matched against every searchable column.</summary>
    public string Search
    {
        get => search;
        set
        {
            var next = value ?? string.Empty;

            if (search == next)
                return;

            search = next;
            Invalidate();
        }
    }

    /// <summary>Whether the search matches out of order, scored by <see cref="FuzzyMatcher"/>.</summary>
    public bool SearchFuzzy { get; set; } = true;

    /// <summary>Whether the search box is drawn above the table.</summary>
    public bool ShowSearch { get; set; } = true;

    /// <summary>The hint shown in the empty search box.</summary>
    public string SearchHint { get; set; } = "Search...";

    /// <summary>Whether each column draws its own filter box under its header.</summary>
    public bool ShowColumnFilters { get; set; }

    /// <summary>How many rows survived the filters and the search.</summary>
    public int VisibleCount
    {
        get
        {
            Rebuild(force: false);
            return visible.Count;
        }
    }

    /// <summary>
    /// The rows currently showing, in the order they are drawn.
    /// </summary>
    public IReadOnlyList<T> VisibleRows
    {
        get
        {
            Rebuild(force: false);
            return visibleRows;
        }
    }

    /// <summary>
    /// Tells the table its rows or its rules changed and the visible set has to be worked out again.
    /// </summary>
    public void Invalidate() => dirty = true;

    #endregion

    #region Sorting

    /// <summary>The column being sorted on, or <see langword="null"/> for the order the rows arrived in.</summary>
    public TableColumn<T>? SortColumn { get; private set; }

    /// <summary>Whether the sort is reversed.</summary>
    public bool SortDescending { get; private set; }

    /// <summary>
    /// Sorts on a column.
    /// </summary>
    /// <param name="column">The column to sort on, or <see langword="null"/> for source order.</param>
    /// <param name="descending">Whether to reverse it.</param>
    public void SortBy(TableColumn<T>? column, bool descending = false)
    {
        SortColumn = column;
        SortDescending = descending;
        Invalidate();
    }

    #endregion

    #region Selection

    /// <summary>Whether rows can be selected, and whether more than one at a time.</summary>
    public TableSelection SelectionMode { get; set; } = TableSelection.None;

    /// <summary>
    /// The rows selected, in the order they appear in the source list.
    /// </summary>
    public IReadOnlyList<T> Selected
    {
        get
        {
            var result = new List<T>(selected.Count);

            for (var i = 0; i < rows.Count; i++)
            {
                if (selected.Contains(rows[i]))
                    result.Add(rows[i]);
            }

            return result;
        }
    }

    /// <summary>Whether a row is selected.</summary>
    /// <param name="row">The row to test.</param>
    /// <returns>True when it is selected.</returns>
    public bool IsSelected(T row) => selected.Contains(row);

    /// <summary>Selects or deselects a row, honouring <see cref="SelectionMode"/>.</summary>
    /// <param name="row">The row.</param>
    /// <param name="isSelected">Whether it should be selected.</param>
    public void SetSelected(T row, bool isSelected)
    {
        if (!isSelected)
        {
            selected.Remove(row);
            return;
        }

        if (SelectionMode == TableSelection.Single)
            selected.Clear();

        selected.Add(row);
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection() => selected.Clear();

    /// <summary>Invoked when the selection changes, with the rows selected.</summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; set; }

    #endregion

    #region Appearance

    /// <summary>
    /// The height of the table in real pixels. Zero fills the space available.
    /// </summary>
    public float Height { get; set; }

    /// <summary>How many rows there must be before the table draws only what is on screen.</summary>
    public int VirtualizeThreshold { get; set; } = 100;

    /// <summary>
    /// Whether only the rows on screen are drawn. When <see langword="null"/>, past
    /// <see cref="VirtualizeThreshold"/>.
    /// </summary>
    public bool? Virtualize { get; set; }

    /// <summary>Whether a footer of column totals is drawn, when any column has an aggregate.</summary>
    public bool ShowFooter { get; set; } = true;

    /// <summary>How many rows were actually drawn last frame.</summary>
    public int DrawnRowCount { get; private set; }

    #endregion

    #region Export

    /// <summary>
    /// Writes what is on screen as CSV.
    /// </summary>
    /// <returns>The CSV text.</returns>
    public string ToCsv()
    {
        Rebuild(force: false);
        return BuildCsv(rows, Columns, visible);
    }

    #endregion

    private void Rebuild(bool force)
    {
        if (!dirty && !force)
            return;

        dirty = false;

        BuildVisible(rows, Columns, search, SearchFuzzy, visible);
        SortVisible(rows, visible, SortColumn, SortDescending);

        visibleRows.Clear();

        foreach (var index in visible)
            visibleRows.Add(rows[index]);
    }
}
