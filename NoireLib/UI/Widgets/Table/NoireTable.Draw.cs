using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The drawing half of the table. Everything that decides <em>what</em> is shown lives in the pipeline beside it.
/// </summary>
public sealed partial class NoireTable<T>
{
    private const ImGuiTableFlags BaseFlags =
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable
        | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV
        | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;

    /// <summary>
    /// The width a column with no <see cref="TableColumn{T}.Width"/> stretches with, relative to the others.
    /// </summary>
    private const float DefaultStretchWeight = 1f;

    /// <summary>
    /// Draws the table.
    /// </summary>
    /// <returns>True on the frame the selection changes.</returns>
    public bool Draw()
    {
        NoireUI.EnsureFrameServices();

        var changed = false;
        DrawnRowCount = 0;

        if (ShowSearch)
            DrawSearchBox();

        Rebuild(force: false);

        var visibleColumns = CountVisibleColumns();

        if (visibleColumns == 0)
        {
            NoireText.Muted("This table has no columns to show.", TextSize.Caption);
            return false;
        }

        var flags = BaseFlags;

        if (AnyColumnSorts())
            flags |= ImGuiTableFlags.Sortable;

        var height = Height > 0f ? Height : ImGui.GetContentRegionAvail().Y;

        if (!ImGui.BeginTable($"###NoireTable_{Id}", visibleColumns, flags, new Vector2(NoireLayout.ContentWidth(), height)))
            return false;

        SetupColumns();

        // Frozen so the headers stay put while the body scrolls, which is the whole reason a table beats a list of
        // rows once it is longer than a screen.
        ImGui.TableSetupScrollFreeze(0, ShowColumnFilters ? 2 : 1);
        ImGui.TableHeadersRow();

        ApplySortSpecs();

        if (ShowColumnFilters)
            DrawFilterRow();

        changed |= DrawBody();

        if (ShowFooter && AnyColumnTotals())
            DrawFooter();

        ImGui.EndTable();

        if (changed)
            ReportSelection();

        return changed;
    }

    /// <summary>
    /// Draws the box above the table that narrows every searchable column at once.
    /// </summary>
    private void DrawSearchBox()
    {
        var text = search;

        ImGui.SetNextItemWidth(NoireLayout.ContentWidth());

        if (ImGui.InputTextWithHint($"###NoireTableSearch_{Id}", SearchHint, ref text, 128))
            Search = text;

        // Said plainly rather than left to be inferred from a short list, because a search that matches nothing and a
        // table that happens to be empty look identical.
        if (!string.IsNullOrWhiteSpace(search))
        {
            ImGui.SameLine(0f, NoireUI.Scaled(8f));
            NoireText.Muted($"{VisibleCount} of {rows.Count}", TextSize.Caption);
        }
    }

    /// <summary>
    /// Declares every visible column to ImGui, in order.
    /// </summary>
    private void SetupColumns()
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            var flags = column.ResolveComparison() == null
                ? ImGuiTableColumnFlags.NoSort
                : ImGuiTableColumnFlags.None;

            // Stated per column rather than inherited from the table's policy, so a Width is always pixels and a
            // column without one always shares the rest. Left to be inferred, an explicit width is read as a stretch
            // weight instead, and "size column to fit" then renormalises every other column by a pixel or two.
            flags |= column.Width > 0f
                ? ImGuiTableColumnFlags.WidthFixed
                : ImGuiTableColumnFlags.WidthStretch;

            // The user index is the column's position in our own list, not in the visible subset, so a hidden column
            // does not shift what a sort spec refers to.
            ImGui.TableSetupColumn(column.Header, flags, column.Width > 0f ? column.Width : DefaultStretchWeight, (uint)i);
        }
    }

    /// <summary>
    /// Takes the order from the header the user clicked.
    /// </summary>
    /// <remarks>
    /// Read only when ImGui says it changed. The specs are ImGui's own state and the table's is ours, so copying them
    /// every frame would fight <see cref="SortBy"/> and make setting the order from code impossible.
    /// </remarks>
    private unsafe void ApplySortSpecs()
    {
        var specs = ImGui.TableGetSortSpecs();

        if (specs.IsNull || !specs.SpecsDirty)
            return;

        specs.SpecsDirty = false;

        var primary = specs.Specs;

        if (specs.SpecsCount <= 0 || primary.IsNull)
        {
            SortBy(null);
            return;
        }

        // The first spec is the primary one, which is the only order a single sort column needs. Reading further would
        // mean walking the array by hand for a tiebreak the pipeline already provides from the source index.
        var index = (int)primary.ColumnUserID;

        if (index < 0 || index >= Columns.Count)
            return;

        SortBy(Columns[index], primary.SortDirection == ImGuiSortDirection.Descending);
    }

    /// <summary>
    /// Draws the row of per-column filter boxes under the headers.
    /// </summary>
    private void DrawFilterRow()
    {
        ImGui.TableNextRow();

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            ImGui.TableNextColumn();

            var text = column.FilterText;

            ImGui.SetNextItemWidth(-1f);

            if (ImGui.InputTextWithHint($"###NoireTableFilter_{Id}_{i}", "Filter", ref text, 64))
            {
                column.FilterText = text;
                Invalidate();
                Rebuild(force: true);
            }
        }
    }

    /// <summary>
    /// Draws the rows, only the ones on screen when there are enough of them to be worth it.
    /// </summary>
    private bool DrawBody()
    {
        var changed = false;
        var virtualize = Virtualize ?? visible.Count > VirtualizeThreshold;

        if (!virtualize)
        {
            for (var position = 0; position < visible.Count; position++)
                changed |= DrawRow(position);

            return changed;
        }

        // The height is left for the clipper to measure rather than guessed at. A table row is as tall as its cell
        // padding plus its tallest cell, which a caller's renderer can change, and a guess that is too tall makes the
        // clipper show fewer rows than fit: the body ends in a gap and the footer is pushed out of the table.
        var clipper = new ImGuiListClipper();
        clipper.Begin(visible.Count, -1f);

        while (clipper.Step())
        {
            for (var position = clipper.DisplayStart; position < clipper.DisplayEnd; position++)
                changed |= DrawRow(position);
        }

        clipper.End();
        return changed;
    }

    /// <summary>
    /// Draws one row of the visible set.
    /// </summary>
    private bool DrawRow(int position)
    {
        if (position < 0 || position >= visible.Count)
            return false;

        var index = visible[position];

        if (index < 0 || index >= rows.Count)
            return false;

        DrawnRowCount++;

        var row = rows[index];
        var isSelected = SelectionMode != TableSelection.None && selected.Contains(row);
        var changed = false;

        ImGui.TableNextRow();

        var first = true;

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            ImGui.TableNextColumn();

            // The selectable goes in the first cell and spans the row, so clicking anywhere on the row selects it
            // without a column of its own for the hit target. Its label is empty and the cell is drawn over it at the
            // same cursor: a selectable renders its label wherever it was given, and SameLine would put the cell after
            // an item that is as wide as the whole row.
            if (first && SelectionMode != TableSelection.None)
            {
                var cellStart = ImGui.GetCursorPos();

                if (ImGui.Selectable(
                        $"###NoireTableRow_{Id}_{index}",
                        isSelected,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap,
                        new Vector2(0f, 0f)))
                {
                    Toggle(row, isSelected);
                    changed = true;
                }

                // So a control a renderer puts in a later cell can still be clicked through the row's own hit box.
                ImGui.SetItemAllowOverlap();
                ImGui.SetCursorPos(cellStart);
            }

            first = false;
            DrawCell(column, i, row, index, isSelected);
        }

        return changed;
    }

    /// <summary>
    /// Selects a row from a click, adding to the selection when a modifier says so.
    /// </summary>
    /// <remarks>
    /// A plain click always selects rather than toggling, which is what every list in every application does: a click
    /// that deselects the row under the cursor reads as the click having missed. Toggling is what the modifier is for.
    /// </remarks>
    private void Toggle(T row, bool wasSelected)
    {
        var additive = SelectionMode == TableSelection.Multiple
            && (ImGui.GetIO().KeyCtrl || ImGui.GetIO().KeyShift);

        if (!additive)
        {
            selected.Clear();
            selected.Add(row);
            return;
        }

        if (wasSelected)
            selected.Remove(row);
        else
            selected.Add(row);
    }

    /// <summary>
    /// Draws one cell, through the column's renderer when it has one.
    /// </summary>
    private void DrawCell(TableColumn<T> column, int columnIndex, T row, int rowIndex, bool isSelected)
    {
        if (column.Renderer == null)
        {
            ImGui.PushTextWrapPos(-1f);
            NoireText.Draw(column.Read(row));
            ImGui.PopTextWrapPos();
            return;
        }

        try
        {
            column.Renderer(new UiTableCellDraw<T>(this, row, rowIndex, column, columnIndex, isSelected));
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The renderer of table column '{column.Header}' threw an exception.", nameof(NoireTable<T>));
        }
    }

    /// <summary>
    /// Draws the row of totals, computed over the rows that survived rather than over all of them.
    /// </summary>
    private void DrawFooter()
    {
        ImGui.TableNextRow();

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            ImGui.TableNextColumn();

            if (column.Aggregate == null)
                continue;

            try
            {
                ImGui.PushTextWrapPos(-1f);
                NoireText.Muted(column.Aggregate(visibleRows) ?? string.Empty, TextSize.Caption);
                ImGui.PopTextWrapPos();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The aggregate of table column '{column.Header}' threw an exception.", nameof(NoireTable<T>));
            }
        }
    }

    private int CountVisibleColumns()
    {
        var count = 0;

        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Visible)
                count++;
        }

        return count;
    }

    private bool AnyColumnSorts()
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Visible && Columns[i].ResolveComparison() != null)
                return true;
        }

        return false;
    }

    private bool AnyColumnTotals()
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Visible && Columns[i].Aggregate != null)
                return true;
        }

        return false;
    }

    private void ReportSelection()
    {
        try
        {
            OnSelectionChanged?.Invoke(Selected);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The selection callback of table '{Id}' threw an exception.", nameof(NoireTable<T>));
        }
    }
}
