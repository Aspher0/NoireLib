using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.UI;

/// <summary>
/// The part of a table that decides which rows are shown and in what order, kept away from the drawing so it can be
/// tested without an ImGui context and run only when something it depends on has actually changed.
/// </summary>
public sealed partial class NoireTable<T>
{
    /// <summary>
    /// Fills a list with the indices of the rows that survive the column filters and the search, in source order.
    /// </summary>
    /// <remarks>
    /// Indices rather than rows: the table keeps pointing at the caller's own list, so nothing is copied per frame and
    /// a row's identity is wherever the caller put it.<br/>
    /// Filters are applied before the search because a column filter is usually the cheaper test and always the
    /// narrower one.
    /// </remarks>
    /// <param name="rows">The rows to consider.</param>
    /// <param name="columns">The columns whose filters and searchability apply.</param>
    /// <param name="search">The global search text, or empty for none.</param>
    /// <param name="fuzzy">Whether the search matches out of order.</param>
    /// <param name="destination">The list filled with the surviving indices. Cleared first.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    internal static void BuildVisible(
        IReadOnlyList<T> rows,
        IReadOnlyList<TableColumn<T>> columns,
        string search,
        bool fuzzy,
        List<int> destination)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(destination);

        destination.Clear();

        var searching = !string.IsNullOrWhiteSpace(search);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            if (!PassesFilters(row, columns))
                continue;

            if (searching && !MatchesSearch(row, columns, search, fuzzy))
                continue;

            destination.Add(index);
        }
    }

    /// <summary>
    /// Whether a row passes every column's filter.
    /// </summary>
    private static bool PassesFilters(T row, IReadOnlyList<TableColumn<T>> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            if (!column.Visible)
                continue;

            if (!string.IsNullOrEmpty(column.FilterText)
                && !column.Read(row).Contains(column.FilterText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (column.Filter == null)
                continue;

            try
            {
                if (!column.Filter(row))
                    return false;
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The filter callback of table column '{column.Header}' threw an exception.", nameof(NoireTable<T>));
            }
        }

        return true;
    }

    /// <summary>
    /// Whether any searchable column of a row matches the search text.
    /// </summary>
    /// <remarks>
    /// A hit in one column is enough: someone typing into a search box above a table is looking for a row, not for a
    /// row whose every column says the same thing.
    /// </remarks>
    private static bool MatchesSearch(T row, IReadOnlyList<TableColumn<T>> columns, string search, bool fuzzy)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            if (!column.Visible || !column.Searchable || column.Text == null)
                continue;

            var text = column.Read(row);

            if (fuzzy ? FuzzyMatcher.IsMatch(text, search) : text.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Orders a list of row indices by one column.
    /// </summary>
    /// <remarks>
    /// Ties break on the source index, which makes the order **stable** and, more to the point, deterministic:
    /// <see cref="List{T}.Sort(Comparison{T})"/> is an introsort and gives no guarantee otherwise, so a table sorted
    /// on a column full of equal values would reshuffle its rows every time anything else changed.<br/>
    /// The search deliberately does not reorder here, unlike the combo box's filter. A table has an explicit sort that
    /// the user chose by clicking a header, and quietly reordering it by search score would take that away.
    /// </remarks>
    /// <param name="rows">The rows the indices point into.</param>
    /// <param name="indices">The indices to order, in place.</param>
    /// <param name="column">The column to order by, or <see langword="null"/> to leave source order.</param>
    /// <param name="descending">Whether to reverse the column's order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rows"/> or <paramref name="indices"/> is <see langword="null"/>.</exception>
    internal static void SortVisible(IReadOnlyList<T> rows, List<int> indices, TableColumn<T>? column, bool descending)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(indices);

        var comparison = column?.ResolveComparison();

        if (comparison == null)
            return;

        indices.Sort((left, right) =>
        {
            int result;

            try
            {
                result = comparison(rows[left], rows[right]);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The sort of table column '{column!.Header}' threw an exception.", nameof(NoireTable<T>));
                result = 0;
            }

            if (result != 0)
                return descending ? -result : result;

            return left.CompareTo(right);
        });
    }

    /// <summary>
    /// Writes rows as CSV, in the order and with the columns they are currently shown in.
    /// </summary>
    /// <remarks>
    /// What is exported is what is on screen: the visible columns, the surviving rows, the chosen order. An export
    /// that quietly hands back the unfiltered table is the one thing a user cannot check by looking.<br/>
    /// Quoting follows RFC 4180, so a field containing a comma, a quote or a newline survives the trip into a
    /// spreadsheet instead of splitting the row.
    /// </remarks>
    /// <param name="rows">The rows the indices point into.</param>
    /// <param name="columns">The columns to write. Hidden ones are skipped.</param>
    /// <param name="indices">The rows to write, in order.</param>
    /// <returns>The CSV text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    internal static string BuildCsv(IReadOnlyList<T> rows, IReadOnlyList<TableColumn<T>> columns, IReadOnlyList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(indices);

        var builder = new StringBuilder();
        var first = true;

        for (var i = 0; i < columns.Count; i++)
        {
            if (!columns[i].Visible)
                continue;

            if (!first)
                builder.Append(',');

            AppendField(builder, columns[i].Header);
            first = false;
        }

        builder.Append('\n');

        for (var r = 0; r < indices.Count; r++)
        {
            var index = indices[r];

            if (index < 0 || index >= rows.Count)
                continue;

            first = true;

            for (var i = 0; i < columns.Count; i++)
            {
                if (!columns[i].Visible)
                    continue;

                if (!first)
                    builder.Append(',');

                AppendField(builder, columns[i].Read(rows[index]));
                first = false;
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes one CSV field, quoted only when it has to be.
    /// </summary>
    private static void AppendField(StringBuilder builder, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');

        foreach (var character in value)
        {
            if (character == '"')
                builder.Append('"');

            builder.Append(character);
        }

        builder.Append('"');
    }
}
