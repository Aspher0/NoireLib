using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The drawing half of the table.
/// </summary>
public sealed partial class NoireTable<T>
{
    private const ImGuiTableFlags BaseFlags =
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable
        | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY;

    private const float DefaultStretchWeight = 1f;

    /// <summary>
    /// Draws the table.
    /// </summary>
    /// <returns>True on the frame the selection changes.</returns>
    public bool Draw()
    {
        using var profile = UiProfile.Widget(nameof(NoireTable<T>), Id);

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

        var flags = BaseFlags | ImGuiTableFlags.SizingFixedFit;

        if (AnyColumnSorts())
            flags |= ImGuiTableFlags.Sortable;

        var footing = ShowFooter && AnyColumnTotals();
        var footerHeight = footing ? FooterHeight() : 0f;
        var width = NoireLayout.ContentWidth();
        var outerHeight = Height > 0f ? Height : ImGui.GetContentRegionAvail().Y;

        // ImGui only freezes rows at the top. The footer is a second table inside the same frame.
        bool opened;

        using (UiPush.Style(ImGuiStyleVar.ChildBorderSize, 1f))
        using (UiPush.Color(ImGuiCol.Border, NoireTheme.Current.Resolve(ThemeColor.Border)))
        {
            opened = ImGui.BeginChild(UiIds.For("###NoireTableFrame_", Id), new Vector2(width, outerHeight), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        }

        if (!opened)
        {
            ImGui.EndChild();
            return false;
        }

        var inner = ImGui.GetContentRegionAvail();
        var bodyHeight = MathF.Max(FooterHeight(), inner.Y - footerHeight);
        var bodyTop = ImGui.GetCursorScreenPos().Y;

        if (!ImGui.BeginTable(UiIds.For("###NoireTable_", Id), visibleColumns, flags, new Vector2(inner.X, bodyHeight)))
        {
            ImGui.EndChild();
            return false;
        }

        SetupColumns();
        ApplyPendingColumnWidth();

        ImGui.TableSetupScrollFreeze(0, ShowColumnFilters ? 2 : 1);
        ImGui.TableHeadersRow();

        ApplySortSpecs();

        if (ShowColumnFilters)
            DrawFilterRow();

        capturedLayout = false;
        changed |= DrawBody();

        ImGui.EndTable();

        if (footing)
        {
            // EndTable advances the cursor past a line of item spacing.
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, bodyTop + bodyHeight));
            DrawFooter(inner.X);
        }

        ImGui.EndChild();

        if (changed)
            ReportSelection();

        return changed;
    }

    private void DrawSearchBox()
    {
        var text = search;

        // Sized for the widest count. The field must not resize on the first keystroke.
        var counterWidth = NoireText.CalcSize(Counter(rows.Count, rows.Count), TextSize.Caption).X + NoireUI.Scaled(10f);

        ImGui.SetNextItemWidth(MathF.Max(NoireUI.Scaled(80f), NoireLayout.ContentWidth() - counterWidth));

        if (ImGui.InputTextWithHint(UiIds.For("###NoireTableSearch_", Id), SearchHint, ref text, 128))
            Search = text;

        if (string.IsNullOrWhiteSpace(search))
            return;

        ImGui.SameLine(0f, NoireUI.Scaled(10f));

        ImGui.PushTextWrapPos(-1f);
        NoireText.Muted(Counter(VisibleCount, rows.Count), TextSize.Caption);
        ImGui.PopTextWrapPos();
    }

    private static string Counter(int visible, int total)
    {
        var key = new CounterKey(visible, total);

        if (Counters.TryGet(key, out var cached))
            return cached;

        var text = $"{visible} of {total}";
        Counters.Set(key, text);

        return text;
    }

    private readonly record struct CounterKey(int Visible, int Total);

    private static readonly HotPathCache<CounterKey, string> Counters = new(512);

    private void SetupColumns()
    {
        var trailing = TrailingColumnSlot();
        var slot = -1;
        var anyStretch = false;

        for (var i = 0; i < Columns.Count; i++)
            anyStretch |= Columns[i].Visible && Columns[i].Stretch;

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            slot++;

            var flags = column.ResolveComparison() == null
                ? ImGuiTableColumnFlags.NoSort
                : ImGuiTableColumnFlags.None;

            // With no stretch column, the rightmost one fills the rest and has no grip.
            var isTrailing = !anyStretch && slot == trailing;
            var stretches = isTrailing || column.Stretch;

            // Auto-fitting a stretch column renormalises every other column's weight.
            flags |= stretches
                ? ImGuiTableColumnFlags.WidthStretch | (isTrailing ? ImGuiTableColumnFlags.NoResize : ImGuiTableColumnFlags.None)
                : ImGuiTableColumnFlags.WidthFixed;

            // Zero is ImGui's "fit the contents".
            var initial = stretches
                ? (column.Width > 0f ? column.Width : DefaultStretchWeight)
                : column.Width;

            // The user index is the declaration index. A hidden column must not shift sort specs.
            ImGui.TableSetupColumn(column.Header, flags, initial, (uint)i);
        }
    }

    private int TrailingColumnSlot()
        => columnLayout.Count > 0 ? columnLayout[^1].Column : CountVisibleColumns() - 1;

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

        var index = (int)primary.ColumnUserID;

        if (index < 0 || index >= Columns.Count)
            return;

        SortBy(Columns[index], primary.SortDirection == ImGuiSortDirection.Descending);
    }

    private void DrawFilterRow()
    {
        ImGui.TableNextRow();

        var slot = -1;

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            slot++;

            // Display order stops matching declaration order once a header is dragged.
            if (!ImGui.TableSetColumnIndex(slot))
                continue;

            var text = column.FilterText;

            ImGui.SetNextItemWidth(-1f);

            if (ImGui.InputTextWithHint(UiIds.For("###NoireTableFilter_", Id, i), "Filter", ref text, 64))
            {
                column.FilterText = text;
                Invalidate();
                Rebuild(force: true);
            }
        }
    }

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

        // A guessed row height that is too tall makes the clipper show fewer rows than fit.
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

        if (!capturedLayout)
            columnLayout.Clear();

        var first = true;
        var slot = -1;

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            slot++;

            // TableNextColumn walks display order. TableSetColumnIndex names the column.
            if (!ImGui.TableSetColumnIndex(slot))
                continue;

            if (!capturedLayout)
            {
                var contentLeft = ImGui.GetCursorScreenPos().X;

                columnLayout.Add(new ColumnGeometry(
                    slot,
                    contentLeft,
                    contentLeft + ImGui.GetContentRegionAvail().X));
            }

            // The first cell's selectable spans the row. The cell is drawn over its empty label.
            if (first && SelectionMode != TableSelection.None)
            {
                var cellStart = ImGui.GetCursorPos();
                var style = ImGui.GetStyle();

                // A selectable grows its hit box by half the item spacing. In a table that must be the cell padding, or two rows hover at once.
                using (UiPush.Style(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, style.CellPadding.Y * 2f)))
                {
                    // Hit target only. TableSetBgColor paints the highlight.
                    if (ImGui.Selectable(
                            UiIds.For("###NoireTableRow_", Id, index),
                            false,
                            ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap,
                            new Vector2(0f, 0f)))
                    {
                        Toggle(row, isSelected);
                        changed = true;
                    }
                }

                ImGui.SetItemAllowOverlap();

                if (isSelected || ImGui.IsItemHovered())
                {
                    var accent = NoireTheme.Current.Resolve(ThemeColor.Accent);

                    ImGui.TableSetBgColor(
                        ImGuiTableBgTarget.RowBg1,
                        ColorHelper.Vector4ToUint(ColorHelper.ScaleAlpha(accent, isSelected ? 0.30f : 0.12f)));
                }

                ImGui.SetCursorPos(cellStart);
            }

            first = false;
            DrawCell(column, i, row, index, isSelected);
        }

        if (!capturedLayout)
            columnLayout.Sort(static (left, right) => left.ContentLeft.CompareTo(right.ContentLeft));

        capturedLayout = true;
        return changed;
    }

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

    private void DrawCell(TableColumn<T> column, int columnIndex, T row, int rowIndex, bool isSelected)
    {
        if (column.Renderer == null)
        {
            ImGui.PushTextWrapPos(-1f);
            DrawCellText(column, row);
            ImGui.PopTextWrapPos();
            return;
        }

        try
        {
            column.Renderer(new UiTableCellDraw<T>(this, row, rowIndex, column, columnIndex, isSelected));
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The renderer of table column '{column.Header}' threw an exception.", "[NoireTable] ");
        }
    }

    private void DrawCellText(TableColumn<T> column, T row)
    {
        var text = column.Read(row);
        var query = !string.IsNullOrEmpty(column.FilterText)
            ? column.FilterText
            : column.Searchable ? search : string.Empty;

        if (!SearchFuzzy || string.IsNullOrWhiteSpace(query) || text.Length == 0)
        {
            NoireText.Draw(text);
            return;
        }

        Span<int> matched = stackalloc int[FuzzyMatcher.MaxQueryLength];

        if (FuzzyMatcher.TryMatch(text, query, matched, out var match))
            NoireText.Highlighted(text, matched[..match.MatchedCount]);
        else
            NoireText.Draw(text);
    }

    private void DrawFooter(float width)
    {
        var theme = NoireTheme.Current;
        var origin = ImGui.GetCursorScreenPos();
        var height = FooterHeight();
        var bottom = origin.Y + height;

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var border = ColorHelper.Vector4ToUint(theme.Resolve(ThemeColor.Border));

        drawList.AddRectFilled(
            origin,
            new Vector2(origin.X + width, bottom),
            ColorHelper.Vector4ToUint(theme.Resolve(ThemeColor.SurfaceSunken)));

        drawList.AddLine(origin, new Vector2(origin.X + width, origin.Y), border, 1f);

        for (var position = 0; position < columnLayout.Count; position++)
        {
            var geometry = columnLayout[position];

            if (position < columnLayout.Count - 1)
            {
                var boundary = MathF.Round((geometry.ContentRight + columnLayout[position + 1].ContentLeft) * 0.5f);

                drawList.AddLine(new Vector2(boundary, origin.Y), new Vector2(boundary, bottom), border, 1f);
                DrawFooterGrip(position, boundary, origin.Y, bottom);
            }

            var column = ColumnAt(position);

            if (column?.Aggregate == null)
                continue;

            string total;

            try
            {
                total = column.Aggregate(visibleRows) ?? string.Empty;
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The aggregate of table column '{column.Header}' threw an exception.", "[NoireTable] ");
                continue;
            }

            if (total.Length == 0)
                continue;

            drawList.PushClipRect(new Vector2(geometry.ContentLeft, origin.Y), new Vector2(geometry.ContentRight, bottom), true);

            ImGui.SetCursorScreenPos(new Vector2(
                geometry.ContentLeft,
                origin.Y + (height * 0.5f) - NoireText.CenterOffset(TextSize.Caption)));

            ImGui.PushTextWrapPos(-1f);
            NoireText.Muted(total, TextSize.Caption);
            ImGui.PopTextWrapPos();

            drawList.PopClipRect();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawFooterGrip(int position, float boundary, float top, float bottom)
    {
        var reach = NoireUI.Scaled(4f);

        ImGui.SetCursorScreenPos(new Vector2(boundary - reach, top));
        ImGui.InvisibleButton(UiIds.For("###NoireTableFooterGrip_", Id, position), new Vector2(reach * 2f, bottom - top));

        var active = ImGui.IsItemActive();

        if (ImGui.IsItemHovered() || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (!active || !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            return;

        var geometry = columnLayout[position];
        var padding = ImGui.GetStyle().CellPadding.X;

        pendingWidthColumn = geometry.Column;
        pendingWidth = MathF.Max(NoireUI.Scaled(24f), ImGui.GetIO().MousePos.X - geometry.ContentLeft + padding);
    }

    private void ApplyPendingColumnWidth()
    {
        if (pendingWidthColumn < 0)
            return;

        ImGuiP.TableSetColumnWidth(pendingWidthColumn, pendingWidth);
        pendingWidthColumn = -1;
    }

    private TableColumn<T>? ColumnAt(int position)
    {
        var wanted = position < columnLayout.Count ? columnLayout[position].Column : position;
        var seen = 0;

        for (var i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Visible)
                continue;

            if (seen == wanted)
                return Columns[i];

            seen++;
        }

        return null;
    }

    private static float FooterHeight()
        => NoireText.LineHeight() + (ImGui.GetStyle().CellPadding.Y * 2f);

    private readonly record struct ColumnGeometry(int Column, float ContentLeft, float ContentRight);

    private readonly List<ColumnGeometry> columnLayout = new();

    private bool capturedLayout;
    private int pendingWidthColumn = -1;
    private float pendingWidth;

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
            NoireLogger.LogError(ex, $"The selection callback of table '{Id}' threw an exception.", "[NoireTable] ");
        }
    }
}
