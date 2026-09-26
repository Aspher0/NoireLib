using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// One column of a <see cref="NoireTable{T}"/>: what it is called, what it reads out of a row, and how it sorts,
/// filters and totals.
/// </summary>
/// <typeparam name="T">The row type.</typeparam>
public sealed class TableColumn<T>
{
    /// <summary>The name shown in the header.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// What this column reads out of a row.
    /// </summary>
    public Func<T, string>? Text { get; set; }

    /// <summary>
    /// What the column sorts on, when the text does not sort the way the data does.
    /// </summary>
    public Func<T, IComparable?>? SortKey { get; set; }

    /// <summary>
    /// Full control of the ordering, for a column that neither its text nor a single key describes.
    /// </summary>
    public Comparison<T>? Sort { get; set; }

    /// <summary>
    /// A predicate of your own that a row must pass to appear, applied on top of <see cref="FilterText"/>.
    /// </summary>
    public Func<T, bool>? Filter { get; set; }

    /// <summary>
    /// The text typed into this column's own filter box, matched against <see cref="Text"/>.
    /// </summary>
    public string FilterText { get; set; } = string.Empty;

    /// <summary>Whether the table's global search reads this column.</summary>
    public bool Searchable { get; set; } = true;

    /// <summary>Whether the header sorts.</summary>
    public bool Sortable { get; set; } = true;

    /// <summary>Whether the column is drawn at all.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// The column's width in real pixels, or zero to share the space with the others.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Whether the column shares out the width the fixed columns leave, weighted by <see cref="Width"/> when set.
    /// </summary>
    public bool Stretch { get; set; }

    /// <summary>Paints a cell in place of the plain text.</summary>
    public Action<UiTableCellDraw<T>>? Renderer { get; set; }

    /// <summary>
    /// What the footer says for this column, given the rows currently showing.
    /// </summary>
    public Func<IReadOnlyList<T>, string>? Aggregate { get; set; }

    /// <summary>
    /// Reads this column out of a row, falling back to an empty string when it has no <see cref="Text"/>.
    /// </summary>
    /// <param name="row">The row to read.</param>
    /// <returns>The column's text for that row.</returns>
    public string Read(T row)
    {
        if (Text == null)
            return string.Empty;

        try
        {
            return Text(row) ?? string.Empty;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The text callback of table column '{Header}' threw an exception.", "[NoireTable] ");
            return string.Empty;
        }
    }

    /// <summary>
    /// The ordering this column describes, or <see langword="null"/> when it describes none.
    /// </summary>
    /// <returns>The comparison, or <see langword="null"/>.</returns>
    public Comparison<T>? ResolveComparison()
    {
        if (!Sortable)
            return null;

        if (Sort != null)
            return Sort;

        // The delegate reads the key and text at the moment it runs.
        if (SortKey != null)
            return keyComparison ??= CompareKeys;

        return Text != null ? textComparison ??= CompareText : null;
    }

    private Comparison<T>? keyComparison;
    private Comparison<T>? textComparison;

    private int CompareKeys(T left, T right)
    {
        var a = SortKey!(left);
        var b = SortKey!(right);

        if (a == null)
            return b == null ? 0 : -1;

        return b == null ? 1 : a.CompareTo(b);
    }

    private int CompareText(T left, T right)
        => string.Compare(Read(left), Read(right), StringComparison.OrdinalIgnoreCase);
}
