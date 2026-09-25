using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace NoireLib.HistoryLogger;

// The log window's drawing and state, one instance per window. Every text is built once until it changes: drawing
// allocates nothing.
internal sealed class HistoryLogDraw
{
    // ImGui returns the column id unchanged in the sort specs.
    private enum EntryColumn : uint
    {
        Time = 1,
        Level = 2,
        Category = 3,
        Message = 4,
        Source = 5,
    }

    private const int MaxCachedEntries = 4096;

    private static readonly HistoryLogLevel[] Levels = Enum.GetValues<HistoryLogLevel>();
    private static readonly string[] LevelNames = Array.ConvertAll(Levels, level => level.ToString());
    private static readonly int[] ItemsPerPageOptions = [5, 10, 25, 50, 100, 200, 500, 1000];
    private static readonly string[] ItemsPerPageLabels = Array.ConvertAll(ItemsPerPageOptions, option => option.ToString(CultureInfo.InvariantCulture));
    private static readonly char[] LineBreaks = ['\r', '\n'];
    private static readonly string[] NoLines = [string.Empty];
    private static readonly uint SelectedRowColor = ColorHelper.HexToUint("#FFFFFF44");

    private static readonly string CollapseButton = FontAwesomeIcon.ChevronUp.ToIconString() + "##HeaderToggle";
    private static readonly string ExpandButton = FontAwesomeIcon.ChevronDown.ToIconString() + "##HeaderToggle";
    private static readonly string FirstPageButton = FontAwesomeIcon.AngleDoubleLeft.ToIconString() + "##FirstPage";
    private static readonly string PreviousPageButton = FontAwesomeIcon.AngleLeft.ToIconString() + "##PrevPage";
    private static readonly string NextPageButton = FontAwesomeIcon.AngleRight.ToIconString() + "##NextPage";
    private static readonly string LastPageButton = FontAwesomeIcon.AngleDoubleRight.ToIconString() + "##LastPage";

    private static readonly List<string> RowIds = [];
    private static readonly List<string> RowMenuIds = [];
    private static readonly List<string> LineIds = [];
    private static readonly List<string> LineMenuIds = [];

    private readonly HistoryLogView view;
    private string filterText = string.Empty;
    private string newMessage = string.Empty;
    private string newCategory = "General";
    private int newEntryLevelIndex = (int)HistoryLogLevel.Info;
    private readonly HashSet<HistoryLogEntry> selectedEntries = new();
    private readonly HashSet<(HistoryLogEntry Entry, int LineIndex)> selectedLines = new();
    private readonly HashSet<HistoryLogEntry> scratchEntries = new(ReferenceEqualityComparer.Instance);
    private int lastSelectedIndex = -1;
    private HistoryLogEntry? contextEntry;
    private int prunedRevision = -1;

    private readonly Dictionary<HistoryLogEntry, string> timeTexts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HistoryLogEntry, string[]> messageLines = new(ReferenceEqualityComparer.Instance);

    private int labelRevision = -1;
    private string persistLabel = string.Empty;
    private string colorsLabel = string.Empty;
    private string splitLabel = string.Empty;
    private string hideCategoryLabel = string.Empty;
    private string hideSourceLabel = string.Empty;
    private string refreshLabel = string.Empty;
    private string clearMemoryLabel = string.Empty;
    private string clearDatabaseLabel = string.Empty;
    private string addEntryLabel = string.Empty;

    private readonly CountedText categoryCount = new();
    private readonly CountedText levelCount = new();
    private readonly CountedText pageCount = new();
    private readonly CountedText deleteSelectedLabel = new();
    private readonly CountedText copyAcrossLabel = new();
    private readonly CountedText copySelectedLabel = new();
    private readonly CountedText copyMessagesLabel = new();

    private string showingText = string.Empty;
    private (int Start, int End, int Filtered, int Total, int Revision) showingKey = (-1, -1, -1, -1, -1);

    internal HistoryLogDraw(NoireHistoryLogger logger)
    {
        ParentModule = logger;
        view = new HistoryLogView(logger);
    }

    internal HistoryLogView View => view;

    private NoireHistoryLogger ParentModule { get; }

    internal void Draw()
    {
        Header();
        DrawLogEntries();
    }

    // The filters and the manual entry, while the user keeps them expanded.
    internal void Header()
    {
        if (!HistoryLoggerConfig.IsHeaderPanelExpanded)
            return;

        DrawHeader();
        ImGui.Dummy(new Vector2(0, 1));
    }

    internal void Entries() => DrawLogEntries();

    private void EnsureLabels()
    {
        if (labelRevision == NoireLanguages.Revision)
            return;

        labelRevision = NoireLanguages.Revision;
        persistLabel = NoireStrings.LogsPersist.Text + "##persist";
        colorsLabel = NoireStrings.LogsShowColors.Text + "##colors";
        splitLabel = NoireStrings.LogsSplitLines.Text + "##split";
        hideCategoryLabel = NoireStrings.LogsHideCategory.Text + "##hidecategory";
        hideSourceLabel = NoireStrings.LogsHideSource.Text + "##hidesource";
        refreshLabel = NoireStrings.LogsRefresh.Text + "##refresh";
        clearMemoryLabel = NoireStrings.LogsClearMemory.Text + "##clearmemory";
        clearDatabaseLabel = NoireStrings.LogsClearDatabase.Text + "##cleardatabase";
        addEntryLabel = NoireStrings.LogsAddEntry.Text + "##addentry";
    }

    private void DrawHeader()
    {
        EnsureLabels();

        var headerHeight = ParentModule.AllowManualEntryCreation ? 178f : 98f;
        using var child = ImRaii.Child("HistoryLoggerHeader", new Vector2(0, headerHeight), true, ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!child)
            return;

        DrawHeaderToggleButton();
        ImGui.SameLine();
        ImGui.TextDisabled(NoireStrings.LogsFiltersStorage.Text);
        DrawFiltersSection();
        if (ParentModule.AllowManualEntryCreation)
        {
            ImGui.Separator();
            ImGui.TextDisabled(NoireStrings.LogsManualEntry.Text);
            DrawManualEntrySection();
        }
    }

    private static void DrawHeaderToggleButton()
    {
        var isExpanded = HistoryLoggerConfig.IsHeaderPanelExpanded;

        using (UiPush.Font(UiIconFont.Current))
        {
            if (ImGui.Button(isExpanded ? CollapseButton : ExpandButton))
                HistoryLoggerConfig.IsHeaderPanelExpanded = !isExpanded;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(isExpanded ? NoireStrings.LogsCollapsePanel.Text : NoireStrings.LogsExpandPanel.Text);
    }

    private void DrawFiltersSection()
    {
        var panelWidth = ImGui.GetContentRegionAvail().X;
        var itemWidth = Math.Max(140f, panelWidth * 0.33f);

        ImGui.SetNextItemWidth(itemWidth);
        ImGui.InputTextWithHint("##HistoryLoggerSearch", NoireStrings.LogsSearchHint.Text, ref filterText, 200);
        view.SearchText = filterText;

        ImGui.SameLine();

        ImGui.SetNextItemWidth(itemWidth);
        var selectedCategories = view.SelectedCategories.Count;
        var categoryLabel = selectedCategories switch
        {
            0 => NoireStrings.LogsAllCategories.Text,
            1 => SoleSelectedCategory(),
            _ => categoryCount.Get(NoireStrings.LogsCategories, selectedCategories),
        };

        if (ImGui.BeginCombo("##HistoryLoggerCategory", categoryLabel, ImGuiComboFlags.None))
        {
            if (ImGui.Selectable(NoireStrings.LogsAllCategories.Text, selectedCategories == 0))
                view.ClearCategoryFilter();

            var categories = view.Categories;

            for (var i = 0; i < categories.Count; i++)
            {
                var category = categories[i];

                if (ImGui.Selectable(category, view.IsCategorySelected(category), ImGuiSelectableFlags.DontClosePopups))
                    view.ToggleCategory(category);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(itemWidth);
        var selectedLevels = view.SelectedLevels.Count;
        var levelLabel = selectedLevels switch
        {
            0 => NoireStrings.LogsAllLevels.Text,
            1 => SoleSelectedLevel(),
            _ => levelCount.Get(NoireStrings.LogsLevels, selectedLevels),
        };

        if (ImGui.BeginCombo("##HistoryLoggerLevel", levelLabel, ImGuiComboFlags.None))
        {
            if (ImGui.Selectable(NoireStrings.LogsAllLevels.Text, selectedLevels == 0))
                view.ClearLevelFilter();

            for (var i = 0; i < Levels.Length; i++)
            {
                if (ImGui.Selectable(LevelNames[i], view.IsLevelSelected(Levels[i]), ImGuiSelectableFlags.DontClosePopups))
                    view.ToggleLevel(Levels[i]);
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        if (ParentModule.AllowUserTogglePersistence)
        {
            var persistLogs = ParentModule.PersistLogs;
            if (ImGui.Checkbox(persistLabel, ref persistLogs))
                ParentModule.SetPersistLogs(persistLogs, true);
            ImGui.SameLine();
        }

        var showLevelBackgroundColors = HistoryLoggerConfig.ShowLevelBackgroundColors;
        if (ImGui.Checkbox(colorsLabel, ref showLevelBackgroundColors))
            HistoryLoggerConfig.ShowLevelBackgroundColors = showLevelBackgroundColors;

        ImGui.SameLine();

        var allowSelectLinesSeparately = HistoryLoggerConfig.SelectLinesSeparately;
        if (ImGui.Checkbox(splitLabel, ref allowSelectLinesSeparately))
        {
            var previousMode = HistoryLoggerConfig.SelectLinesSeparately;
            HistoryLoggerConfig.SelectLinesSeparately = allowSelectLinesSeparately;

            if (allowSelectLinesSeparately && !previousMode)
            {
                selectedLines.Clear();
                foreach (var entry in selectedEntries)
                {
                    var lineCount = LinesOf(entry).Length;
                    for (var i = 0; i < lineCount; i++)
                        selectedLines.Add((entry, i));
                }
            }
            else if (!allowSelectLinesSeparately && previousMode)
            {
                selectedEntries.Clear();
                foreach (var line in selectedLines)
                    selectedEntries.Add(line.Entry);
            }
        }

        ImGui.SameLine();
        var hideCategoryColumn = HistoryLoggerConfig.HideCategoryColumn;
        if (ImGui.Checkbox(hideCategoryLabel, ref hideCategoryColumn))
            HistoryLoggerConfig.HideCategoryColumn = hideCategoryColumn;

        ImGui.SameLine();

        var hideSourceColumn = HistoryLoggerConfig.HideSourceColumn;
        if (ImGui.Checkbox(hideSourceLabel, ref hideSourceColumn))
            HistoryLoggerConfig.HideSourceColumn = hideSourceColumn;

        ImGui.SameLine();

        var panelWidthRemaining = ImGui.GetContentRegionAvail().X;
        var buttonSpacing = ImGui.GetStyle().ItemSpacing.X;

        var buttonCount = 0;
        if (ParentModule.PersistLogs)
            buttonCount++;
        if (!ParentModule.PersistLogs && ParentModule.AllowUserClearInMemory)
            buttonCount++;
        if (ParentModule.PersistLogs && ParentModule.AllowUserClearDatabase)
            buttonCount++;

        var buttonWidth = buttonCount > 0 ? (panelWidthRemaining - buttonSpacing * (buttonCount - 1)) / buttonCount : panelWidthRemaining;

        var isFirstButton = true;

        if (ParentModule.PersistLogs)
        {
            if (!isFirstButton)
                ImGui.SameLine();
            isFirstButton = false;

            if (ImGui.Button(refreshLabel, new Vector2(buttonWidth, 0)))
                ParentModule.LoadEntriesFromDatabase(true);
        }

        if (!ParentModule.PersistLogs && ParentModule.AllowUserClearInMemory)
        {
            if (!isFirstButton)
                ImGui.SameLine();
            isFirstButton = false;

            var io = ImGui.GetIO();
            var ctrlShiftPressed = io.KeyCtrl && io.KeyShift;

            using (UiPush.Disabled(!ctrlShiftPressed))
            {
                if (ImGui.Button(clearMemoryLabel, new Vector2(buttonWidth, 0)))
                    ParentModule.ClearEntries();
            }

            if (!ctrlShiftPressed && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(NoireStrings.LogsHoldToClear.Text);
        }

        if (ParentModule.PersistLogs && ParentModule.AllowUserClearDatabase)
        {
            if (!isFirstButton)
                ImGui.SameLine();
            isFirstButton = false;

            var io = ImGui.GetIO();
            var ctrlShiftPressed = io.KeyCtrl && io.KeyShift;

            using (UiPush.Disabled(!ctrlShiftPressed))
            {
                if (ImGui.Button(clearDatabaseLabel, new Vector2(buttonWidth, 0)))
                    ParentModule.ClearDatabaseEntries();
            }

            if (!ctrlShiftPressed && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(NoireStrings.LogsHoldToClear.Text);
        }
    }

    // The one selected category, found through the list rather than by enumerating the set behind an interface.
    private string SoleSelectedCategory()
    {
        var categories = view.Categories;

        for (var i = 0; i < categories.Count; i++)
        {
            if (view.IsCategorySelected(categories[i]))
                return categories[i];
        }

        return NoireStrings.LogsAllCategories.Text;
    }

    private string SoleSelectedLevel()
    {
        for (var i = 0; i < Levels.Length; i++)
        {
            if (view.IsLevelSelected(Levels[i]))
                return LevelNames[i];
        }

        return NoireStrings.LogsAllLevels.Text;
    }

    private void DrawManualEntrySection()
    {
        var panelWidth = ImGui.GetContentRegionAvail().X;
        var itemWidth = Math.Max(140f, panelWidth * 0.25f);

        ImGui.SetNextItemWidth(itemWidth);
        ImGui.InputTextWithHint("##HistoryLoggerNewCategory", NoireStrings.LogsCategoryHint.Text, ref newCategory, 120);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(panelWidth - itemWidth - 110f);
        ImGui.InputTextWithHint("##HistoryLoggerNewMessage", NoireStrings.LogsMessageHint.Text, ref newMessage, 300);
        ImGui.SameLine();

        var levelIndex = Math.Clamp(newEntryLevelIndex, 0, LevelNames.Length - 1);

        ImGui.SetNextItemWidth(90f);
        if (ImGui.Combo("##HistoryLoggerNewLevel", ref levelIndex, LevelNames, LevelNames.Length))
            newEntryLevelIndex = levelIndex;

        if (ImGui.Button(addEntryLabel, new Vector2(-1, -1)))
        {
            if (!string.IsNullOrWhiteSpace(newMessage))
            {
                ParentModule.AddEntry(newMessage.Trim(), string.IsNullOrWhiteSpace(newCategory) ? "General" : newCategory.Trim(), Levels[levelIndex], "Manual");
                newMessage = string.Empty;
            }
        }
    }

    private void DrawLogEntries()
    {
        EnsureLabels();

        view.ItemsPerPage = HistoryLoggerConfig.ItemsPerPage;
        var filtered = GetFilteredEntries();

        var itemsPerPage = view.ItemsPerPage;
        var totalPages = view.PageCount;
        var startIndex = view.PageStartIndex;
        var pagedEntries = view.PageEntries;
        var endIndex = startIndex + pagedEntries.Count;

        var isExpanded = HistoryLoggerConfig.IsHeaderPanelExpanded;
        if (!isExpanded)
        {
            DrawHeaderToggleButton();
            ImGui.SameLine();
        }

        ImGui.TextDisabled(ShowingText(startIndex, endIndex, filtered.Count, view.TotalCount));
        ImGui.SameLine();
        DrawPaginationControls(totalPages, itemsPerPage);

        ImGui.Spacing();

        using var child = ImRaii.Child("##HistoryLoggerEntries", new Vector2(0, 0), true, ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!child)
            return;

        bool hideCategory = HistoryLoggerConfig.HideCategoryColumn;
        bool hideSource = HistoryLoggerConfig.HideSourceColumn;
        int columnCount = 3 + (hideCategory ? 0 : 1) + (hideSource ? 0 : 1); // Time, Level, Message, [Category], [Source]

        using var table = ImRaii.Table("HistoryLoggerEntriesTable", columnCount, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable);
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);

        // The conditional columns shift every later index. Sort specs use these ids.
        ImGui.TableSetupColumn(NoireStrings.LogsColumnTime.Text, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.NoResize, 0f, (uint)EntryColumn.Time);
        ImGui.TableSetupColumn(NoireStrings.LogsColumnLevel.Text, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 0f, (uint)EntryColumn.Level);
        if (!hideCategory)
            ImGui.TableSetupColumn(NoireStrings.LogsColumnCategory.Text, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 100f, (uint)EntryColumn.Category);
        ImGui.TableSetupColumn(NoireStrings.LogsColumnMessage.Text, ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 250f, (uint)EntryColumn.Message);
        if (!hideSource)
            ImGui.TableSetupColumn(NoireStrings.LogsColumnSource.Text, ImGuiTableColumnFlags.WidthFixed, 200f, (uint)EntryColumn.Source);
        ImGui.TableHeadersRow();

        UpdateSortSpecs();

        int messageColIndex = 2;
        if (!hideCategory) messageColIndex++;
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(messageColIndex);
        var messageColumnWidth = ImGui.GetContentRegionAvail().X;

        if (HistoryLoggerConfig.SelectLinesSeparately)
            DrawLogEntriesWithLineSeparation(pagedEntries, messageColumnWidth, hideCategory, hideSource);
        else
            DrawLogEntriesStandard(pagedEntries, messageColumnWidth, hideCategory, hideSource);
    }

    private string ShowingText(int start, int end, int filtered, int total)
    {
        var key = (start, end, filtered, total, NoireLanguages.Revision);

        if (key == showingKey)
            return showingText;

        showingKey = key;
        showingText = NoireStrings.LogsShowing.With("range", NoireLanguages.Number(start + 1) + "-" + NoireLanguages.Number(end), "count", NoireLanguages.Number(filtered))
            + " " + NoireStrings.LogsTotal.With("total", NoireLanguages.Number(total));

        return showingText;
    }

    private void DrawLogEntriesStandard(IReadOnlyList<HistoryLogEntry> pagedEntries, float messageColumnWidth, bool hideCategory, bool hideSource)
    {
        var tintLevels = HistoryLoggerConfig.ShowLevelBackgroundColors;
        for (var index = 0; index < pagedEntries.Count; index++)
        {
            var entry = pagedEntries[index];

            var messageSize = ImGui.CalcTextSize(entry.Message ?? string.Empty, false, messageColumnWidth);
            var rowHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing(), messageSize.Y + ImGui.GetStyle().CellPadding.Y * 2f);

            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);

            var isSelected = selectedEntries.Contains(entry);

            if (isSelected)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, SelectedRowColor);
            else if (tintLevels)
            {
                var levelColor = GetLevelBackgroundColor(entry.Level);
                if (levelColor.W > 0f)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(levelColor));
                }
            }

            int col = 0;
            ImGui.TableSetColumnIndex(col++); // Time
            var timeCursor = ImGui.GetCursorPos();
            var popupId = IdAt(RowMenuIds, "HistoryLoggerRowMenu_", index);

            using (var pushed = UiPush.Color(ImGuiCol.Header, new Vector4(0, 0, 0, 0)))
            {
                pushed.Push(ImGuiCol.HeaderHovered, new Vector4(1, 1, 1, 0.05f));
                pushed.Push(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));

                if (ImGui.Selectable(IdAt(RowIds, "##HistoryLoggerRow_", index), isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0, rowHeight)))
                    ToggleSelection(entry, index, pagedEntries);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                contextEntry = entry;
                if (!isSelected)
                {
                    selectedEntries.Clear();
                    selectedEntries.Add(entry);
                    lastSelectedIndex = index;
                }
                ImGui.OpenPopup(popupId);
            }

            ImGui.SetCursorPos(timeCursor);
            ImGui.TextUnformatted(TimeOf(entry));

            if (ImGui.BeginPopup(popupId))
            {
                DrawEntryContextMenu(pagedEntries);
                ImGui.EndPopup();
            }

            ImGui.TableSetColumnIndex(col++); // Level
            using (UiPush.Color(ImGuiCol.Text, GetLevelColor(entry.Level)))
                ImGui.TextUnformatted(LevelName(entry.Level));

            if (!hideCategory)
            {
                ImGui.TableSetColumnIndex(col++); // Category
                ImGui.TextUnformatted(entry.Category);
            }

            ImGui.TableSetColumnIndex(col++); // Message
            ImGui.TextWrapped(entry.Message);

            if (!hideSource)
            {
                ImGui.TableSetColumnIndex(col++); // Source
                ImGui.TextDisabled(entry.Source ?? string.Empty);
            }
        }
    }

    private void DrawLogEntriesWithLineSeparation(IReadOnlyList<HistoryLogEntry> pagedEntries, float messageColumnWidth, bool hideCategory, bool hideSource)
    {
        var tintLevels = HistoryLoggerConfig.ShowLevelBackgroundColors;
        var rowIndex = 0;
        for (var entryIndex = 0; entryIndex < pagedEntries.Count; entryIndex++)
        {
            var entry = pagedEntries[entryIndex];
            var lines = LinesOf(entry);

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var messageSize = ImGui.CalcTextSize(line, false, messageColumnWidth);
                var rowHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing(), messageSize.Y + ImGui.GetStyle().CellPadding.Y * 2f);

                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);

                var isLineSelected = selectedLines.Contains((entry, lineIndex));

                if (isLineSelected)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, SelectedRowColor);
                }
                else if (tintLevels)
                {
                    var levelColor = GetLevelBackgroundColor(entry.Level);
                    if (levelColor.W > 0f)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(levelColor));
                    }
                }

                int col = 0;
                ImGui.TableSetColumnIndex(col++); // Time
                var timeCursor = ImGui.GetCursorPos();
                var popupId = IdAt(LineMenuIds, "HistoryLoggerLineMenu_", rowIndex);

                using (var pushed = UiPush.Color(ImGuiCol.Header, new Vector4(0, 0, 0, 0)))
                {
                    pushed.Push(ImGuiCol.HeaderHovered, new Vector4(1, 1, 1, 0.05f));
                    pushed.Push(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));

                    if (ImGui.Selectable(IdAt(LineIds, "##HistoryLoggerLine_", rowIndex), isLineSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0, rowHeight)))
                        ToggleLineSelection(entry, lineIndex, rowIndex, pagedEntries);
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    contextEntry = entry;
                    if (!isLineSelected)
                    {
                        selectedLines.Clear();
                        selectedLines.Add((entry, lineIndex));
                        lastSelectedIndex = rowIndex;
                    }
                    ImGui.OpenPopup(popupId);
                }

                ImGui.SetCursorPos(timeCursor);
                if (lineIndex == 0)
                    ImGui.TextUnformatted(TimeOf(entry));

                if (ImGui.BeginPopup(popupId))
                {
                    DrawEntryContextMenu(pagedEntries);
                    ImGui.EndPopup();
                }

                ImGui.TableSetColumnIndex(col++); // Level
                if (lineIndex == 0)
                {
                    using (UiPush.Color(ImGuiCol.Text, GetLevelColor(entry.Level)))
                        ImGui.TextUnformatted(LevelName(entry.Level));
                }

                if (!hideCategory)
                {
                    ImGui.TableSetColumnIndex(col++); // Category
                    if (lineIndex == 0)
                        ImGui.TextUnformatted(entry.Category);
                }

                ImGui.TableSetColumnIndex(col++); // Message
                ImGui.TextWrapped(line);

                if (!hideSource)
                {
                    ImGui.TableSetColumnIndex(col++); // Source
                    if (lineIndex == 0)
                        ImGui.TextDisabled(entry.Source ?? string.Empty);
                }

                rowIndex++;
            }
        }
    }

    private static string IdAt(List<string> ids, string prefix, int index)
    {
        while (ids.Count <= index)
            ids.Add(prefix + ids.Count.ToString(CultureInfo.InvariantCulture));

        return ids[index];
    }

    private static string LevelName(HistoryLogLevel level)
    {
        var index = Array.IndexOf(Levels, level);
        return index >= 0 ? LevelNames[index] : level.ToString();
    }

    private string TimeOf(HistoryLogEntry entry)
    {
        if (timeTexts.TryGetValue(entry, out var text))
            return text;

        if (timeTexts.Count >= MaxCachedEntries)
            timeTexts.Clear();

        text = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        timeTexts[entry] = text;
        return text;
    }

    private string[] LinesOf(HistoryLogEntry entry)
    {
        if (messageLines.TryGetValue(entry, out var lines))
            return lines;

        if (messageLines.Count >= MaxCachedEntries)
            messageLines.Clear();

        lines = (entry.Message ?? string.Empty).Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            lines = NoLines;

        messageLines[entry] = lines;
        return lines;
    }

    private void ToggleLineSelection(HistoryLogEntry entry, int lineIndex, int rowIndex, IReadOnlyList<HistoryLogEntry> entries)
    {
        var io = ImGui.GetIO();
        var ctrlPressed = io.KeyCtrl;
        var shiftPressed = io.KeyShift;

        if (shiftPressed && lastSelectedIndex >= 0)
        {
            if (!ctrlPressed)
                selectedLines.Clear();

            var start = Math.Min(lastSelectedIndex, rowIndex);
            var end = Math.Max(lastSelectedIndex, rowIndex);
            var row = 0;

            foreach (var e in entries)
            {
                var lineCount = LinesOf(e).Length;

                for (var i = 0; i < lineCount; i++, row++)
                {
                    if (row >= start && row <= end)
                        selectedLines.Add((e, i));
                }

                if (row > end)
                    break;
            }
        }
        else if (ctrlPressed)
        {
            var lineKey = (entry, lineIndex);
            if (!selectedLines.Add(lineKey))
                selectedLines.Remove(lineKey);
        }
        else
        {
            var lineKey = (entry, lineIndex);
            var isCurrentlySelected = selectedLines.Contains(lineKey);
            var isOnlyOneSelected = selectedLines.Count == 1;

            if (isCurrentlySelected && isOnlyOneSelected)
                selectedLines.Clear();
            else
            {
                selectedLines.Clear();
                selectedLines.Add(lineKey);
            }
        }

        lastSelectedIndex = rowIndex;
    }

    private void DrawPaginationControls(int totalPages, int currentItemsPerPage)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 380f);

        ImGui.SetNextItemWidth(80f);
        var selectedIndex = Array.IndexOf(ItemsPerPageOptions, currentItemsPerPage);
        if (selectedIndex < 0) selectedIndex = 4; // 100

        if (ImGui.Combo("##ItemsPerPage", ref selectedIndex, ItemsPerPageLabels, ItemsPerPageLabels.Length))
        {
            HistoryLoggerConfig.ItemsPerPage = ItemsPerPageOptions[selectedIndex];
            view.ItemsPerPage = ItemsPerPageOptions[selectedIndex];
        }

        ImGui.SameLine();
        ImGui.TextDisabled(NoireStrings.LogsPerPage.Text);

        ImGui.SameLine();

        var currentPage = view.Page;

        using (UiPush.Font(UiIconFont.Current))
        {
            using (UiPush.Disabled(currentPage <= 1))
            {
                if (ImGui.Button(FirstPageButton))
                    view.Page = 1;
            }

            ImGui.SameLine();

            using (UiPush.Disabled(currentPage <= 1))
            {
                if (ImGui.Button(PreviousPageButton))
                    view.Page = currentPage - 1;
            }
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(60f);
        var pageInput = currentPage;
        if (ImGui.InputInt("##PageNumber", ref pageInput, 0, 0))
            view.Page = pageInput;

        ImGui.SameLine();
        ImGui.TextDisabled(pageCount.Get(totalPages));

        ImGui.SameLine();

        using (UiPush.Font(UiIconFont.Current))
        {
            using (UiPush.Disabled(currentPage >= totalPages))
            {
                if (ImGui.Button(NextPageButton))
                    view.Page = currentPage + 1;
            }

            ImGui.SameLine();

            using (UiPush.Disabled(currentPage >= totalPages))
            {
                if (ImGui.Button(LastPageButton))
                    view.Page = totalPages;
            }
        }
    }

    private IReadOnlyList<HistoryLogEntry> GetFilteredEntries()
    {
        var entries = view.Entries;

        if (prunedRevision != view.Revision)
        {
            prunedRevision = view.Revision;
            PruneSelectionsOutsideFilter(entries);
        }

        return entries;
    }

    // Hidden rows cannot be acted on from the context menu.
    private void PruneSelectionsOutsideFilter(IReadOnlyList<HistoryLogEntry> filtered)
    {
        if (selectedEntries.Count == 0 && selectedLines.Count == 0)
            return;

        var filteredSet = new HashSet<HistoryLogEntry>(filtered);

        if (selectedEntries.Count > 0)
            selectedEntries.RemoveWhere(entry => !filteredSet.Contains(entry));

        if (selectedLines.Count > 0)
            selectedLines.RemoveWhere(line => !filteredSet.Contains(line.Entry));
    }

    private void UpdateSortSpecs()
    {
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty && sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs[0];
            HistoryLogSortColumn? column = (EntryColumn)spec.ColumnUserID switch
            {
                EntryColumn.Time => HistoryLogSortColumn.Time,
                EntryColumn.Level => HistoryLogSortColumn.Level,
                EntryColumn.Category => HistoryLogSortColumn.Category,
                EntryColumn.Source => HistoryLogSortColumn.Source,
                _ => null,
            };

            if (column.HasValue)
            {
                view.SortColumn = column.Value;
                view.SortDescending = spec.SortDirection == ImGuiSortDirection.Descending;
            }

            sortSpecs.SpecsDirty = false;
        }
    }

    private void ToggleSelection(HistoryLogEntry entry, int index, IReadOnlyList<HistoryLogEntry> entries)
    {
        var io = ImGui.GetIO();
        var ctrlPressed = io.KeyCtrl;
        var shiftPressed = io.KeyShift;

        if (shiftPressed && lastSelectedIndex >= 0 && lastSelectedIndex < entries.Count)
        {
            if (!ctrlPressed)
                selectedEntries.Clear();

            var start = Math.Min(lastSelectedIndex, index);
            var end = Math.Max(lastSelectedIndex, index);
            for (var i = start; i <= end; i++)
                selectedEntries.Add(entries[i]);
        }
        else if (ctrlPressed)
        {
            if (!selectedEntries.Add(entry))
                selectedEntries.Remove(entry);
        }
        else
        {
            // A plain click toggles off only the sole selection.
            var isCurrentlySelected = selectedEntries.Contains(entry);
            var isOnlyOneSelected = selectedEntries.Count == 1;

            if (isCurrentlySelected && isOnlyOneSelected)
            {
                selectedEntries.Clear();
            }
            else
            {
                selectedEntries.Clear();
                selectedEntries.Add(entry);
            }
        }

        lastSelectedIndex = index;
    }

    // The entries the selected lines belong to, gathered into a set reused from frame to frame.
    private HashSet<HistoryLogEntry> EntriesOfSelectedLines()
    {
        scratchEntries.Clear();

        foreach (var line in selectedLines)
            scratchEntries.Add(line.Entry);

        return scratchEntries;
    }

    private void DrawEntryContextMenu(IReadOnlyList<HistoryLogEntry> orderedEntries)
    {
        var ctrlPressed = ImGui.GetIO().KeyCtrl;

        string deleteLabel;
        string copyLabel;

        int selectionCount;
        var hasFirstLine = false;

        if (HistoryLoggerConfig.SelectLinesSeparately)
        {
            selectionCount = EntriesOfSelectedLines().Count;
            var totalLines = selectedLines.Count;

            foreach (var line in selectedLines)
            {
                if (line.LineIndex == 0)
                {
                    hasFirstLine = true;
                    break;
                }
            }

            deleteLabel = selectionCount > 1
                ? deleteSelectedLabel.Get(NoireStrings.LogsDeleteSelected, selectionCount)
                : NoireStrings.LogsDeleteEntry.Text;

            if (selectionCount > 1)
                copyLabel = copyAcrossLabel.Get(NoireStrings.LogsCopyLinesAcross, selectionCount);
            else if (totalLines == 1)
                copyLabel = NoireStrings.LogsCopyLine.Text;
            else
                copyLabel = NoireStrings.LogsCopyLines.Text;
        }
        else
        {
            selectionCount = selectedEntries.Count;

            if (selectionCount > 1)
            {
                deleteLabel = deleteSelectedLabel.Get(NoireStrings.LogsDeleteSelected, selectionCount);
                copyLabel = copySelectedLabel.Get(NoireStrings.LogsCopySelected, selectionCount);
            }
            else
            {
                deleteLabel = NoireStrings.LogsDeleteEntry.Text;
                copyLabel = NoireStrings.LogsCopyEntry.Text;
            }
        }

        var canDelete = ParentModule.PersistLogs
            ? ParentModule.AllowUserClearDatabase
            : ParentModule.AllowUserClearInMemory;

        if (canDelete)
        {
            using (UiPush.Disabled(!ctrlPressed))
            {
                if (ImGui.MenuItem(deleteLabel, string.Empty, false, ctrlPressed) && ctrlPressed)
                {
                    var entriesToDelete = HistoryLoggerConfig.SelectLinesSeparately
                        ? new List<HistoryLogEntry>(EntriesOfSelectedLines())
                        : new List<HistoryLogEntry>(selectedEntries);

                    if (entriesToDelete.Count == 0 && contextEntry != null)
                        entriesToDelete.Add(contextEntry);

                    foreach (var entry in entriesToDelete)
                    {
                        ParentModule.RemoveEntry(entry);
                        selectedEntries.Remove(entry);
                        selectedLines.RemoveWhere(line => ReferenceEquals(line.Entry, entry));
                    }

                    contextEntry = null;
                }
            }

            if (!ctrlPressed && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(NoireStrings.LogsHoldToDelete.Text);
        }

        if (ImGui.MenuItem(copyLabel))
        {
            var copyEntries = new List<HistoryLogEntry>();

            if (selectionCount > 1)
            {
                foreach (var entry in orderedEntries)
                {
                    if (selectedEntries.Contains(entry))
                        copyEntries.Add(entry);
                }
            }
            else if (selectionCount == 1)
            {
                copyEntries.AddRange(selectedEntries);
            }
            else if (contextEntry != null)
            {
                copyEntries.Add(contextEntry);
            }

            CopyEntriesToClipboard(copyEntries);
        }

        if (!HistoryLoggerConfig.SelectLinesSeparately || hasFirstLine)
        {
            var copyMessageOnlyLabel = selectionCount > 1
                ? copyMessagesLabel.Get(NoireStrings.LogsCopyMessages, selectionCount)
                : NoireStrings.LogsCopyMessage.Text;

            if (ImGui.MenuItem(copyMessageOnlyLabel))
                CopyMessagesOnlyToClipboard();
        }
    }

    // The line numbers each selected entry has selected, in order. Only built on a click, never while drawing.
    private Dictionary<HistoryLogEntry, List<int>> SelectedLineIndices()
    {
        var byEntry = new Dictionary<HistoryLogEntry, List<int>>();

        foreach (var (entry, lineIndex) in selectedLines)
        {
            if (!byEntry.TryGetValue(entry, out var indices))
                byEntry[entry] = indices = [];

            indices.Add(lineIndex);
        }

        foreach (var indices in byEntry.Values)
            indices.Sort();

        return byEntry;
    }

    private void CopyEntriesToClipboard(IEnumerable<HistoryLogEntry> entries)
    {
        if (HistoryLoggerConfig.SelectLinesSeparately && selectedLines.Count > 0)
        {
            var selectedByEntry = SelectedLineIndices();
            var linesToCopy = new List<string>();

            foreach (var entry in GetFilteredEntries())
            {
                if (!selectedByEntry.TryGetValue(entry, out var sortedIndices))
                    continue;

                var entryLines = LinesOf(entry);
                var includesFirstLine = sortedIndices.Contains(0);

                for (var i = 0; i < sortedIndices.Count; i++)
                {
                    var lineIndex = sortedIndices[i];
                    if (lineIndex >= entryLines.Length)
                        continue;

                    var line = entryLines[lineIndex];
                    var isFirstLine = lineIndex == 0;
                    var isLastLineOfThisEntry = i == sortedIndices.Count - 1;

                    if (isFirstLine && isLastLineOfThisEntry)
                    {
                        var source = string.IsNullOrWhiteSpace(entry.Source) ? "-" : entry.Source;
                        linesToCopy.Add($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Level} | {entry.Category} | {line} | {source}");
                    }
                    else if (isFirstLine)
                        linesToCopy.Add($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Level} | {entry.Category} | {line}");
                    else if (isLastLineOfThisEntry && includesFirstLine)
                    {
                        var source = string.IsNullOrWhiteSpace(entry.Source) ? "-" : entry.Source;
                        linesToCopy.Add($"{line} | {source}");
                    }
                    else
                        linesToCopy.Add(line);
                }
            }

            ImGui.SetClipboardText(string.Join(Environment.NewLine, linesToCopy));
        }
        else
        {
            ImGui.SetClipboardText(NoireHistoryLogger.FormatEntries(entries));
        }
    }

    private void CopyMessagesOnlyToClipboard()
    {
        if (HistoryLoggerConfig.SelectLinesSeparately && selectedLines.Count > 0)
        {
            var selectedByEntry = SelectedLineIndices();
            var linesToCopy = new List<string>();

            foreach (var entry in GetFilteredEntries())
            {
                if (!selectedByEntry.TryGetValue(entry, out var sortedIndices))
                    continue;

                var entryLines = LinesOf(entry);

                foreach (var lineIndex in sortedIndices)
                {
                    if (lineIndex < entryLines.Length)
                        linesToCopy.Add(entryLines[lineIndex]);
                }
            }

            ImGui.SetClipboardText(string.Join(Environment.NewLine, linesToCopy));
        }
        else
        {
            var messages = new List<string>();

            if (selectedEntries.Count > 0)
            {
                foreach (var entry in selectedEntries)
                    messages.Add(entry.Message);
            }
            else
            {
                messages.Add(contextEntry?.Message ?? string.Empty);
            }

            ImGui.SetClipboardText(string.Join(Environment.NewLine, messages));
        }
    }

    private static Vector4 GetLevelColor(HistoryLogLevel level)
    {
        return level switch
        {
            HistoryLogLevel.Trace => new Vector4(0.7f, 0.7f, 0.7f, 1f),
            HistoryLogLevel.Debug => new Vector4(0.45f, 0.75f, 0.9f, 1f),
            HistoryLogLevel.Info => new Vector4(0.8f, 0.9f, 0.95f, 1f),
            HistoryLogLevel.Warning => new Vector4(0.95f, 0.7f, 0.2f, 1f),
            HistoryLogLevel.Error => new Vector4(0.95f, 0.35f, 0.35f, 1f),
            HistoryLogLevel.Critical => new Vector4(0.8f, 0.25f, 0.6f, 1f),
            _ => new Vector4(1f, 1f, 1f, 1f)
        };
    }

    private static Vector4 GetLevelBackgroundColor(HistoryLogLevel level)
    {
        return level switch
        {
            HistoryLogLevel.Trace => new Vector4(0f, 0f, 0f, 0f),
            HistoryLogLevel.Debug => new Vector4(0f, 0f, 0f, 0f),
            HistoryLogLevel.Info => new Vector4(0f, 0f, 0f, 0f),
            HistoryLogLevel.Warning => new Vector4(0.8f, 0.65f, 0.15f, 0.35f),
            HistoryLogLevel.Error => new Vector4(0.85f, 0.2f, 0.2f, 0.4f),
            HistoryLogLevel.Critical => new Vector4(0.85f, 0.15f, 0.5f, 0.45f),
            _ => new Vector4(0f, 0f, 0f, 0f)
        };
    }

    // A text holding one number, rebuilt only when the number or the language changes.
    private sealed class CountedText
    {
        private string text = string.Empty;
        private int count = -1;
        private int revision = -1;
        private object? source;

        public string Get(NoirePlural plural, int value)
        {
            if (Current(plural, value))
                return text;

            text = plural.For(value);
            return text;
        }

        public string Get(NoireString format, int value)
        {
            if (Current(format, value))
                return text;

            text = format.With("count", NoireLanguages.Number(value));
            return text;
        }

        public string Get(int pages)
        {
            if (Current(null, pages))
                return text;

            text = "/ " + NoireLanguages.Number(pages);
            return text;
        }

        private bool Current(object? of, int value)
        {
            if (count == value && revision == NoireLanguages.Revision && ReferenceEquals(source, of))
                return true;

            count = value;
            revision = NoireLanguages.Revision;
            source = of;
            return false;
        }
    }
}
