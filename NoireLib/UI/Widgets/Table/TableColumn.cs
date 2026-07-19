using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// One column of a <see cref="NoireTable{T}"/>: what it is called, what it reads out of a row, and how it sorts,
/// filters and totals.
/// </summary>
/// <remarks>
/// The minimum is a header and a <see cref="Text"/>, and everything else follows from it: the column sorts on that
/// text, the global search reads it, a per-column filter matches it, and a CSV export writes it. Set
/// <see cref="SortKey"/> when the text does not sort the way the data does (a number written "1,024", a date written
/// "yesterday"), and <see cref="Sort"/> when neither is enough.
/// </remarks>
/// <typeparam name="T">The row type.</typeparam>
public sealed class TableColumn<T>
{
    /// <summary>The name shown in the header.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// What this column reads out of a row. This is what is shown, searched, filtered, sorted and exported unless
    /// something more specific says otherwise.
    /// </summary>
    public Func<T, string>? Text { get; set; }

    /// <summary>
    /// What the column sorts on, when the text does not sort the way the data does.
    /// </summary>
    /// <remarks>
    /// A duration written "1m30s" sorts after "1h" as text and before it as a value; a number written with thousands
    /// separators sorts by its first digit. Return the underlying value and the column sorts on that while still
    /// showing the text.
    /// </remarks>
    public Func<T, IComparable?>? SortKey { get; set; }

    /// <summary>
    /// Full control of the ordering, for a column that neither its text nor a single key describes.
    /// </summary>
    public Comparison<T>? Sort { get; set; }

    /// <summary>
    /// A predicate of your own that a row must pass to appear. Applied on top of <see cref="FilterText"/>.
    /// </summary>
    public Func<T, bool>? Filter { get; set; }

    /// <summary>
    /// The text typed into this column's own filter box. Matched against <see cref="Text"/>.
    /// </summary>
    public string FilterText { get; set; } = string.Empty;

    /// <summary>Whether the table's global search reads this column. On by default.</summary>
    public bool Searchable { get; set; } = true;

    /// <summary>Whether the header sorts. On by default, and ignored when nothing describes an order.</summary>
    public bool Sortable { get; set; } = true;

    /// <summary>Whether the column is drawn at all, which is what a column picker turns off.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// The column's width in real pixels, or zero to share the space with the others.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Paints a cell instead of the plain text: a badge, a colour, a progress bar, a button.
    /// </summary>
    /// <remarks>
    /// Only the painting. The table keeps the sizing, the selection, the sort and the filtering, and the hook is
    /// handed everything it needs to draw the row it was given.
    /// </remarks>
    public Action<UiTableCellDraw<T>>? Renderer { get; set; }

    /// <summary>
    /// What the footer says for this column, given the rows currently showing.
    /// </summary>
    /// <remarks>
    /// Handed the filtered rows rather than all of them, because a total that ignores the filter above it is a total
    /// of something the user is not looking at.
    /// </remarks>
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
            NoireLogger.LogError(ex, $"The text callback of table column '{Header}' threw an exception.", nameof(NoireTable<T>));
            return string.Empty;
        }
    }

    /// <summary>
    /// The ordering this column describes, or <see langword="null"/> when it describes none.
    /// </summary>
    /// <remarks>
    /// Resolved in order of how much the caller said: an explicit <see cref="Sort"/>, then a <see cref="SortKey"/>,
    /// then the text. Rule 5's shape applied to behaviour rather than to style.
    /// </remarks>
    /// <returns>The comparison, or <see langword="null"/>.</returns>
    public Comparison<T>? ResolveComparison()
    {
        if (!Sortable)
            return null;

        if (Sort != null)
            return Sort;

        if (SortKey != null)
            return CompareKeys;

        return Text != null ? CompareText : null;
    }

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
