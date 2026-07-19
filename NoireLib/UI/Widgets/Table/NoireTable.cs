using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A data grid over a list you already have: sorted by clicking a header, narrowed by a search box and by per-column
/// filters, virtualized so a hundred thousand rows cost what a screenful costs, selectable, totalled, and exportable.
/// </summary>
/// <remarks>
/// ImGui's <c>BeginTable</c> already does the hard half, which is the layout: resizable, reorderable, scrollable
/// columns that behave. What it leaves to every caller is the half that is tedious rather than difficult, and this is
/// that half.<br/>
/// The table never copies your rows. It holds the list you gave it and works in indices into it, so the row a
/// selection or a renderer sees is the one you own.
/// </remarks>
/// <example>
/// <code>
/// var table = new NoireTable&lt;PlayerModel&gt;("players", players)
/// {
///     Columns =
///     {
///         new TableColumn&lt;PlayerModel&gt; { Header = "Name", Text = p =&gt; p.Name },
///         new TableColumn&lt;PlayerModel&gt; { Header = "World", Text = p =&gt; p.World },
///         new TableColumn&lt;PlayerModel&gt; { Header = "Level", Text = p =&gt; $"{p.Level}", SortKey = p =&gt; p.Level },
///     },
/// };
///
/// table.Draw();
/// </code>
/// </example>
/// <typeparam name="T">The row type.</typeparam>
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
    /// <param name="comparer">How two rows are compared for selection. Defaults to the type's own equality.</param>
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
    /// The rows to show. Held rather than copied, so the table sees your edits.
    /// </summary>
    /// <remarks>
    /// Assigning marks the table for a rebuild. Editing the list in place does not, since nothing tells the table it
    /// happened: call <see cref="Invalidate"/> for that.
    /// </remarks>
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

    /// <summary>Whether the search matches out of order, scored by <see cref="FuzzyMatcher"/>. On by default.</summary>
    public bool SearchFuzzy { get; set; } = true;

    /// <summary>Whether the search box is drawn above the table. On by default.</summary>
    public bool ShowSearch { get; set; } = true;

    /// <summary>The hint shown in the empty search box.</summary>
    public string SearchHint { get; set; } = "Search...";

    /// <summary>Whether each column draws its own filter box under its header. Off by default.</summary>
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
    /// <remarks>
    /// Filtering and sorting run when something changes rather than every frame, because a hundred thousand rows
    /// scored per keystroke is free and the same work at 144 frames a second is not.
    /// </remarks>
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

    /// <summary>Whether rows can be selected, and whether more than one at a time. Off by default.</summary>
    public TableSelection SelectionMode { get; set; } = TableSelection.None;

    /// <summary>
    /// The rows selected, in the order they appear in the source list.
    /// </summary>
    /// <remarks>
    /// Held by value rather than by index, for the reason every selection here is: an index keeps pointing at whatever
    /// moves into that slot when the rows are replaced, and the symptom is a selection that silently means something
    /// else after a reload.
    /// </remarks>
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

    /// <summary>Whether a footer of column totals is drawn. On when any column has an aggregate.</summary>
    public bool ShowFooter { get; set; } = true;

    /// <summary>How many rows were actually drawn last frame, which is what virtualization changes.</summary>
    public int DrawnRowCount { get; private set; }

    #endregion

    #region Export

    /// <summary>
    /// Writes what is on screen as CSV: the visible columns, the surviving rows, the chosen order.
    /// </summary>
    /// <returns>The CSV text.</returns>
    public string ToCsv()
    {
        Rebuild(force: false);
        return BuildCsv(rows, Columns, visible);
    }

    #endregion

    /// <summary>
    /// Runs the filter, the search and the sort, unless nothing has changed since the last time.
    /// </summary>
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
