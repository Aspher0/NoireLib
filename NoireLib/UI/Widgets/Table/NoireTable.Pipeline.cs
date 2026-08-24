using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.UI;

/// <summary>
/// The part of a table that decides which rows are shown and in what order.
/// </summary>
public sealed partial class NoireTable<T>
{
    // Fills the destination with the indices of the rows that survive the column filters and the search, in source
    // order. The destination is cleared first.
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

            if (!PassesFilters(row, columns, fuzzy))
                continue;

            if (searching && !MatchesSearch(row, columns, search, fuzzy))
                continue;

            destination.Add(index);
        }
    }

    private static bool PassesFilters(T row, IReadOnlyList<TableColumn<T>> columns, bool fuzzy)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            if (!column.Visible)
                continue;

            // Matched the same way the global search is, so a column filter and the box above it do not behave
            // differently for the same typing.
            if (!string.IsNullOrEmpty(column.FilterText) && !Matches(column.Read(row), column.FilterText, fuzzy))
                return false;

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

    private static bool MatchesSearch(T row, IReadOnlyList<TableColumn<T>> columns, string search, bool fuzzy)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            if (!column.Visible || !column.Searchable || column.Text == null)
                continue;

            var text = column.Read(row);

            if (Matches(text, search, fuzzy))
                return true;
        }

        return false;
    }

    internal static bool Matches(string text, string query, bool fuzzy)
        => fuzzy ? FuzzyMatcher.IsMatch(text, query) : text.Contains(query, StringComparison.OrdinalIgnoreCase);

    // Orders the indices in place by one column, or leaves source order when the column is null.
    // Ties break on the source index, making the order stable and deterministic.
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

    // Writes rows as CSV, skipping hidden columns. Quoting follows RFC 4180.
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

    // Quoted only when the field has to be.
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
