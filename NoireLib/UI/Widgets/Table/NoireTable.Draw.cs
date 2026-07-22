using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
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
        | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY;

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

        // Body and footer are two tables inside one bordered frame, so they read as one table with a row pinned to the
        // bottom of it. A table has a single scroll region and ImGui can only freeze rows at the *top*, so a totals row
        // inside the body is one you have to scroll to the end of the list to read; and a second table outside the
        // frame is visibly a separate box rather than part of the table it belongs to.
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

        // Frozen so the headers stay put while the body scrolls, which is the whole reason a table beats a list of
        // rows once it is longer than a screen.
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
            // Put back where the table actually ends. EndTable submits the table as an item, so by the time it returns
            // the cursor has already been advanced past a line of item spacing, and the footer would sit that far below
            // the rows however the spacing is pushed afterwards. Where the body starts plus how tall it was told to be
            // is the one answer that owes nothing to ImGui's own bookkeeping.
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, bodyTop + bodyHeight));
            DrawFooter(inner.X);
        }

        ImGui.EndChild();

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

        // Room is reserved for the widest count the table can report, whether or not one is showing. Sized to the
        // count actually there, the field would resize on the first keystroke, and with no room at all the count wraps
        // under the field and pushes the whole table down.
        var counterWidth = NoireText.CalcSize(Counter(rows.Count, rows.Count), TextSize.Caption).X + NoireUI.Scaled(10f);

        ImGui.SetNextItemWidth(MathF.Max(NoireUI.Scaled(80f), NoireLayout.ContentWidth() - counterWidth));

        if (ImGui.InputTextWithHint(UiIds.For("###NoireTableSearch_", Id), SearchHint, ref text, 128))
            Search = text;

        // Said plainly rather than left to be inferred from a short list, because a search that matches nothing and a
        // table that happens to be empty look identical.
        if (string.IsNullOrWhiteSpace(search))
            return;

        ImGui.SameLine(0f, NoireUI.Scaled(10f));

        ImGui.PushTextWrapPos(-1f);
        NoireText.Muted(Counter(VisibleCount, rows.Count), TextSize.Caption);
        ImGui.PopTextWrapPos();
    }

    /// <summary>
    /// How many rows the search left, written as <c>12 of 340</c>.
    /// </summary>
    /// <remarks>
    /// Cached because the search box asks for it on every frame, twice: once for the count it is showing and once for
    /// the widest count it could show, which is what the column beside the field is sized to. Both change when rows are
    /// added or the search is retyped, and at no other time.
    /// </remarks>
    /// <param name="visible">How many rows the search left.</param>
    /// <param name="total">How many rows there are.</param>
    /// <returns>The counter text.</returns>
    private static string Counter(int visible, int total)
    {
        var key = new CounterKey(visible, total);

        if (Counters.TryGet(key, out var cached))
            return cached;

        var text = $"{visible} of {total}";
        Counters.Set(key, text);

        return text;
    }

    /// <summary>A pair of counts the counter text is written from.</summary>
    private readonly record struct CounterKey(int Visible, int Total);

    private static readonly HotPathCache<CounterKey, string> Counters = new(512);

    /// <summary>
    /// Declares every visible column to ImGui, in order.
    /// </summary>
    private void SetupColumns()
    {
        var trailing = TrailingColumnSlot();
        var slot = -1;

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            slot++;

            var flags = column.ResolveComparison() == null
                ? ImGuiTableColumnFlags.NoSort
                : ImGuiTableColumnFlags.None;

            // The rightmost column takes whatever width is left over, so the table always fills itself: with every
            // column keeping a width of its own, resizing one leaves a strip of nothing on the right and the last
            // column's cells stop short of the edge. Its own Width is ignored for that reason, and it carries no grip
            // either, there being nothing to its right to hand width to. Which column that is follows the display
            // order, so dragging a header takes the behaviour with it.
            var isTrailing = slot == trailing;

            // Every other column keeps a width of its own, which is what makes the header menu's "size column to fit"
            // correct: auto-fitting a fixed column sets an exact pixel width, while auto-fitting a stretch column sets
            // a weight that is then renormalised against every other column, moving all of them a pixel or two.
            flags |= isTrailing
                ? ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize
                : ImGuiTableColumnFlags.WidthFixed;

            // A fixed column with no width of its own is given zero, which is ImGui's "fit the contents". Handing it a
            // stretch weight instead would open the table with every unsized column one pixel wide.
            var initial = isTrailing
                ? (column.Width > 0f ? column.Width : DefaultStretchWeight)
                : column.Width;

            // The user index is the column's position in our own list, not in the visible subset, so a hidden column
            // does not shift what a sort spec refers to.
            ImGui.TableSetupColumn(column.Header, flags, initial, (uint)i);
        }
    }

    /// <summary>
    /// The column currently sitting rightmost, which is not the last one declared once a header has been dragged.
    /// </summary>
    /// <remarks>
    /// Read off the previous frame's layout, since ImGui does not report a display order before the columns have been
    /// laid out. One frame behind a reorder, which nobody can see; getting it from the declaration order instead
    /// leaves a column stranded in the middle with no grip while the one that really is last still has one.<br/>
    /// Column *flags* are re-read from every <c>TableSetupColumn</c> call, unlike the width, so this can change from
    /// frame to frame.
    /// </remarks>
    private int TrailingColumnSlot()
        => columnLayout.Count > 0 ? columnLayout[^1].Column : CountVisibleColumns() - 1;

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

        var slot = -1;

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];

            if (!column.Visible)
                continue;

            slot++;

            // Named rather than advanced to, for the same reason the rows are: display order stops matching
            // declaration order the moment a header is dragged somewhere else.
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

        // Read off the first row drawn this frame, in display order, so the footer follows a column the user has
        // resized or dragged somewhere else. Kept from the last frame that had rows, so an empty table still lines up.
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

            // Addressed rather than advanced to. TableNextColumn walks the columns in *display* order, so once the
            // user has dragged a header somewhere else it no longer lines up with this loop's declaration order and
            // every cell's contents go into the wrong column. TableSetColumnIndex names the column outright.
            if (!ImGui.TableSetColumnIndex(slot))
                continue;

            // The cell's actual screen span, taken from the one place it is knowable. Screen coordinates rather than
            // widths, so the footer lines up with the body whatever else is going on: a scrollbar, a resized column,
            // a column dragged somewhere else.
            if (!capturedLayout)
            {
                var contentLeft = ImGui.GetCursorScreenPos().X;

                columnLayout.Add(new ColumnGeometry(
                    slot,
                    contentLeft,
                    contentLeft + ImGui.GetContentRegionAvail().X));
            }

            // The selectable goes in the first cell and spans the row, so clicking anywhere on the row selects it
            // without a column of its own for the hit target. Its label is empty and the cell is drawn over it at the
            // same cursor: a selectable renders its label wherever it was given, and SameLine would put the cell after
            // an item that is as wide as the whole row.
            if (first && SelectionMode != TableSelection.None)
            {
                var cellStart = ImGui.GetCursorPos();
                var style = ImGui.GetStyle();

                // A selectable grows its hit box by half the item spacing above and below, on purpose, so that stacked
                // selectables leave no click-gap between them. In a table the gap between rows is the *cell padding*,
                // not the item spacing, so a theme whose spacing is the larger of the two overshoots into the rows
                // either side: two rows report hovered at once and the click goes to whichever was submitted last.
                // Handing it exactly the cell padding makes that expansion land on the row's own edges.
                using (UiPush.Style(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, style.CellPadding.Y * 2f)))
                {
                    // Passed as never selected: this is the hit target only. A selectable paints its highlight over
                    // that same expanded box, where TableSetBgColor fills exactly the row and nothing else.
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

                // So a control a renderer puts in a later cell can still be clicked through the row's own hit box.
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

        // Sorted into display order, because the loop above visits the columns in the order they were declared and the
        // footer draws its dividers between neighbours on screen.
        if (!capturedLayout)
            columnLayout.Sort(static (left, right) => left.ContentLeft.CompareTo(right.ContentLeft));

        capturedLayout = true;
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
            NoireLogger.LogError(ex, $"The renderer of table column '{column.Header}' threw an exception.", nameof(NoireTable<T>));
        }
    }

    /// <summary>
    /// Draws a cell's text, picking out the characters whatever narrowed the table matched on.
    /// </summary>
    /// <remarks>
    /// The column's own filter wins over the box above it for that column, since it is the more specific thing the
    /// user typed about it. Showing the matched characters is most of what makes a fuzzy filter feel trustworthy
    /// rather than arbitrary: without them a table that quietly keeps a row looks like it is guessing.
    /// </remarks>
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

    /// <summary>
    /// Draws the row of totals, pinned under the rows and lined up with them.
    /// </summary>
    /// <remarks>
    /// Drawn rather than tabled, deliberately. A second table cannot be made to match the body's columns:
    /// <c>TableSetupColumn</c>'s width is only honoured while the table is initialising, so after its first frame
    /// ImGui keeps its own widths and every later value is ignored. The footer would size itself to its own text and
    /// nothing could talk it out of that.<br/>
    /// The body's cells report exactly where they are, so the footer is drawn against that instead: the same
    /// coordinates, and therefore aligned whatever the columns have been resized, reordered or scrolled to.
    /// </remarks>
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

        // The rule that closes the rows off, spanning the table rather than only the columns that have totals.
        drawList.AddLine(origin, new Vector2(origin.X + width, origin.Y), border, 1f);

        for (var position = 0; position < columnLayout.Count; position++)
        {
            var geometry = columnLayout[position];

            if (position < columnLayout.Count - 1)
            {
                // Halfway between one cell's content and the next is where the body draws its own border, so the two
                // lines are the same line.
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
                NoireLogger.LogError(ex, $"The aggregate of table column '{column.Header}' threw an exception.", nameof(NoireTable<T>));
                continue;
            }

            if (total.Length == 0)
                continue;

            // Clipped to its own column, so a long total is cut off at the column edge instead of running into the
            // one beside it.
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

    /// <summary>
    /// Puts a resize grip on a footer divider, so a column can be sized from the bottom of the table as well as the top.
    /// </summary>
    /// <remarks>
    /// The width is handed to the body on the next frame rather than applied here: ImGui only accepts a column width
    /// while its table's layout is still open, which is long past by the time the footer is drawn.
    /// </remarks>
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

    /// <summary>
    /// Hands the body the width a footer grip was dragged to, while its layout will still take one.
    /// </summary>
    private void ApplyPendingColumnWidth()
    {
        if (pendingWidthColumn < 0)
            return;

        ImGuiP.TableSetColumnWidth(pendingWidthColumn, pendingWidth);
        pendingWidthColumn = -1;
    }

    /// <summary>
    /// The column sitting at a position of the footer, following the body's display order when it is known.
    /// </summary>
    /// <remarks>
    /// ImGui numbers only the columns it was given, so its index counts visible columns while ours counts every
    /// declared one. Walking the visible ones is what keeps the two the same thing once a column is hidden.
    /// </remarks>
    private TableColumn<T>? ColumnAt(int position)
    {
        // Before the first row has ever been drawn there is no layout to follow, so the declared order is the only
        // honest guess.
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

    /// <summary>
    /// How much room the footer takes, so the body can be shortened by exactly that much.
    /// </summary>
    private static float FooterHeight()
        => NoireText.LineHeight() + (ImGui.GetStyle().CellPadding.Y * 2f);

    /// <summary>
    /// Where one of the body's cells actually sat this frame: which column it belongs to, and the screen span of its
    /// content.
    /// </summary>
    private readonly record struct ColumnGeometry(int Column, float ContentLeft, float ContentRight);

    /// <summary>
    /// Where the body laid its columns out this frame, in display order. Kept from the last frame that had rows, so an
    /// empty or fully filtered table still lines up.
    /// </summary>
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
            NoireLogger.LogError(ex, $"The selection callback of table '{Id}' threw an exception.", nameof(NoireTable<T>));
        }
    }
}
