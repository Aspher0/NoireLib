using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using NoireLib.Core.Modules;
using NoireLib.Helpers;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NoireLib.HistoryLogger;

/// <summary>
/// The window class that represents the user interface window for displaying and managing history log entries in <see cref="NoireHistoryLogger"/>.
/// </summary>
public class HistoryLoggerWindow : NoireModuleWindowBase<NoireHistoryLogger>
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

    private readonly HistoryLogView view;
    private string filterText = string.Empty;
    private string newMessage = string.Empty;
    private string newCategory = "General";
    private int newEntryLevelIndex = (int)HistoryLogLevel.Info;
    private readonly HashSet<HistoryLogEntry> selectedEntries = new();
    private readonly HashSet<(HistoryLogEntry Entry, int LineIndex)> selectedLines = new();
    private int lastSelectedIndex = -1;
    private HistoryLogEntry? contextEntry;
    private int prunedRevision = -1;

    /// <summary>
    /// Gets or sets the name of the display window.
    /// </summary>
    public override string DisplayWindowName { get; set; } = "History Logger";

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryLoggerWindow"/> class.
    /// </summary>
    /// <param name="noireHistoryLogger">The parent module instance.</param>
    public HistoryLoggerWindow(NoireHistoryLogger noireHistoryLogger)
        : base(noireHistoryLogger, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        view = new HistoryLogView(noireHistoryLogger);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        UpdateTitleBarButtons();
    }

    /// <summary>
    /// Draws the content of the history logger window.
    /// </summary>
    public override void Draw()
    {
        var isExpanded = HistoryLoggerConfig.IsHeaderPanelExpanded;

        if (isExpanded)
        {
            DrawHeader();
            ImGui.Dummy(new Vector2(0, 1));
        }

        DrawLogEntries();
    }

    private void DrawHeader()
    {
        var headerHeight = ParentModule.AllowManualEntryCreation ? 178f : 98f;
        using var child = ImRaii.Child("HistoryLoggerHeader", new Vector2(0, headerHeight), true, ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!child)
            return;

        DrawHeaderToggleButton();
        ImGui.SameLine();
        ImGui.TextDisabled("Filters & Storage");
        DrawFiltersSection();
        if (ParentModule.AllowManualEntryCreation)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Manual Entry");
            DrawManualEntrySection();
        }
    }

    private void DrawHeaderToggleButton()
    {
        var isExpanded = HistoryLoggerConfig.IsHeaderPanelExpanded;
        var buttonText = isExpanded ? FontAwesomeIcon.ChevronUp.ToIconString() : FontAwesomeIcon.ChevronDown.ToIconString();

        ImGui.PushFont(NoireService.PluginInterface.UiBuilder.FontIcon);
        if (ImGui.Button($"{buttonText}##HeaderToggle"))
        {
            HistoryLoggerConfig.IsHeaderPanelExpanded = !isExpanded;
        }
        ImGui.PopFont();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(isExpanded ? "Collapse panel" : "Expand panel");
    }

    private void DrawFiltersSection()
    {
        var panelWidth = ImGui.GetContentRegionAvail().X;
        var itemWidth = Math.Max(140f, panelWidth * 0.33f);

        ImGui.SetNextItemWidth(itemWidth);
        ImGui.InputTextWithHint("##HistoryLoggerSearch", "Search for a message, category, source...", ref filterText, 200);
        view.SearchText = filterText;

        ImGui.SameLine();

        ImGui.SetNextItemWidth(itemWidth);
        var selectedCategories = view.SelectedCategories;
        var categoryLabel = selectedCategories.Count switch
        {
            0 => "All categories",
            1 => selectedCategories.First(),
            _ => $"{selectedCategories.Count} categories"
        };

        if (ImGui.BeginCombo("##HistoryLoggerCategory", categoryLabel, ImGuiComboFlags.None))
        {
            if (ImGui.Selectable("All categories", selectedCategories.Count == 0))
                view.ClearCategoryFilter();

            foreach (var category in view.Categories)
            {
                if (ImGui.Selectable(category, view.IsCategorySelected(category), ImGuiSelectableFlags.DontClosePopups))
                    view.ToggleCategory(category);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(itemWidth);
        var selectedLevels = view.SelectedLevels;
        var levelLabel = selectedLevels.Count switch
        {
            0 => "All levels",
            1 => selectedLevels.First().ToString(),
            _ => $"{selectedLevels.Count} levels"
        };

        if (ImGui.BeginCombo("##HistoryLoggerLevel", levelLabel, ImGuiComboFlags.None))
        {
            if (ImGui.Selectable("All levels", selectedLevels.Count == 0))
                view.ClearLevelFilter();

            var levels = Enum.GetValues<HistoryLogLevel>();
            foreach (var level in levels)
            {
                if (ImGui.Selectable(level.ToString(), view.IsLevelSelected(level), ImGuiSelectableFlags.DontClosePopups))
                    view.ToggleLevel(level);
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        if (ParentModule.AllowUserTogglePersistence)
        {
            var persistLogs = ParentModule.PersistLogs;
            if (ImGui.Checkbox("Persist to database", ref persistLogs))
                ParentModule.SetPersistLogs(persistLogs, true);
            ImGui.SameLine();
        }

        var showLevelBackgroundColors = HistoryLoggerConfig.ShowLevelBackgroundColors;
        if (ImGui.Checkbox("Show colors", ref showLevelBackgroundColors))
            HistoryLoggerConfig.ShowLevelBackgroundColors = showLevelBackgroundColors;

        ImGui.SameLine();

        var allowSelectLinesSeparately = HistoryLoggerConfig.SelectLinesSeparately;
        if (ImGui.Checkbox("Split lines", ref allowSelectLinesSeparately))
        {
            var previousMode = HistoryLoggerConfig.SelectLinesSeparately;
            HistoryLoggerConfig.SelectLinesSeparately = allowSelectLinesSeparately;

            if (allowSelectLinesSeparately && !previousMode)
            {
                selectedLines.Clear();
                foreach (var entry in selectedEntries)
                {
                    var messageLines = (entry.Message ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var lineCount = messageLines.Length == 0 ? 1 : messageLines.Length;
                    for (var i = 0; i < lineCount; i++)
                        selectedLines.Add((entry, i));
                }
            }
            else if (!allowSelectLinesSeparately && previousMode)
            {
                selectedEntries.Clear();
                var entriesWithAnyLine = selectedLines.Select(l => l.Entry).Distinct();
                foreach (var entry in entriesWithAnyLine)
                    selectedEntries.Add(entry);
            }
        }

        ImGui.SameLine();
        var hideCategoryColumn = HistoryLoggerConfig.HideCategoryColumn;
        if (ImGui.Checkbox("Hide category", ref hideCategoryColumn))
            HistoryLoggerConfig.HideCategoryColumn = hideCategoryColumn;

        ImGui.SameLine();

        var hideSourceColumn = HistoryLoggerConfig.HideSourceColumn;
        if (ImGui.Checkbox("Hide source", ref hideSourceColumn))
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

            if (ImGui.Button("Refresh entries", new Vector2(buttonWidth, 0)))
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
                if (ImGui.Button("Clear in-memory entries", new Vector2(buttonWidth, 0)))
                    ParentModule.ClearEntries();
            }

            if (!ctrlShiftPressed && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold CTRL + SHIFT to clear");
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
                if (ImGui.Button("Clear database entries", new Vector2(buttonWidth, 0)))
                    ParentModule.ClearDatabaseEntries();
            }

            if (!ctrlShiftPressed && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold CTRL + SHIFT to clear");
        }

    }

    private void DrawManualEntrySection()
    {
        var panelWidth = ImGui.GetContentRegionAvail().X;
        var itemWidth = Math.Max(140f, panelWidth * 0.25f);

        ImGui.SetNextItemWidth(itemWidth);
        ImGui.InputTextWithHint("##HistoryLoggerNewCategory", "Category", ref newCategory, 120);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(panelWidth - itemWidth - 110f);
        ImGui.InputTextWithHint("##HistoryLoggerNewMessage", "What happened?", ref newMessage, 300);
        ImGui.SameLine();

        var levels = Enum.GetValues<HistoryLogLevel>();
        var levelLabels = levels.Select(level => level.ToString()).ToArray();
        var levelIndex = Math.Clamp(newEntryLevelIndex, 0, levelLabels.Length - 1);

        ImGui.SetNextItemWidth(90f);
        if (ImGui.Combo("##HistoryLoggerNewLevel", ref levelIndex, levelLabels, levelLabels.Length))
            newEntryLevelIndex = levelIndex;

        if (ImGui.Button("Add entry", new Vector2(-1, -1)))
        {
            if (!string.IsNullOrWhiteSpace(newMessage))
            {
                ParentModule.AddEntry(newMessage.Trim(), string.IsNullOrWhiteSpace(newCategory) ? "General" : newCategory.Trim(), levels[levelIndex], "Manual");
                newMessage = string.Empty;
            }
        }
    }

    private void DrawLogEntries()
    {
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

        ImGui.TextDisabled($"Showing {startIndex + 1}-{endIndex} of {filtered.Count} entries ({view.TotalCount} total)");
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
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.NoResize, 0f, (uint)EntryColumn.Time);
        ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 0f, (uint)EntryColumn.Level);
        if (!hideCategory)
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 100f, (uint)EntryColumn.Category);
        ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 250f, (uint)EntryColumn.Message);
        if (!hideSource)
            ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 200f, (uint)EntryColumn.Source);
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

    private void DrawLogEntriesStandard(IReadOnlyList<HistoryLogEntry> pagedEntries, float messageColumnWidth, bool hideCategory, bool hideSource)
    {
        for (var index = 0; index < pagedEntries.Count; index++)
        {
            var entry = pagedEntries[index];

            var messageSize = ImGui.CalcTextSize(entry.Message ?? string.Empty, false, messageColumnWidth);
            var rowHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing(), messageSize.Y + ImGui.GetStyle().CellPadding.Y * 2f);

            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);

            var isSelected = selectedEntries.Contains(entry);

            if (isSelected)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ColorHelper.HexToUint("#FFFFFF44"));
            else if (HistoryLoggerConfig.ShowLevelBackgroundColors)
            {
                var levelColor = GetLevelBackgroundColor(entry.Level);
                if (levelColor.W > 0f)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(levelColor));
                }
            }

            int col = 0;
            ImGui.TableSetColumnIndex(col++); // Time
            var timeText = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var timeCursor = ImGui.GetCursorPos();
            var rowId = $"##HistoryLoggerRow_{index}";
            var popupId = $"HistoryLoggerRowMenu_{index}";

            using (var pushed = UiPush.Color(ImGuiCol.Header, new Vector4(0, 0, 0, 0)))
            {
                pushed.Push(ImGuiCol.HeaderHovered, new Vector4(1, 1, 1, 0.05f));
                pushed.Push(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));

                if (ImGui.Selectable(rowId, isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0, rowHeight)))
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
            ImGui.TextUnformatted(timeText);

            if (ImGui.BeginPopup(popupId))
            {
                DrawEntryContextMenu(pagedEntries);
                ImGui.EndPopup();
            }

            ImGui.TableSetColumnIndex(col++); // Level
            using (UiPush.Color(ImGuiCol.Text, GetLevelColor(entry.Level)))
                ImGui.TextUnformatted(entry.Level.ToString());

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
        var rowIndex = 0;
        for (var entryIndex = 0; entryIndex < pagedEntries.Count; entryIndex++)
        {
            var entry = pagedEntries[entryIndex];
            var message = entry.Message ?? string.Empty;
            var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
                lines = new[] { string.Empty };

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var messageSize = ImGui.CalcTextSize(line, false, messageColumnWidth);
                var rowHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing(), messageSize.Y + ImGui.GetStyle().CellPadding.Y * 2f);

                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);

                var isLineSelected = selectedLines.Contains((entry, lineIndex));

                if (isLineSelected)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ColorHelper.HexToUint("#FFFFFF44"));
                }
                else if (HistoryLoggerConfig.ShowLevelBackgroundColors)
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
                var rowId = $"##HistoryLoggerLine_{rowIndex}";
                var popupId = $"HistoryLoggerLineMenu_{rowIndex}";

                using (var pushed = UiPush.Color(ImGuiCol.Header, new Vector4(0, 0, 0, 0)))
                {
                    pushed.Push(ImGuiCol.HeaderHovered, new Vector4(1, 1, 1, 0.05f));
                    pushed.Push(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));

                    if (ImGui.Selectable(rowId, isLineSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0, rowHeight)))
                        ToggleLineSelection(entry, lineIndex, rowIndex, pagedEntries, lines.Length);
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
                    ImGui.TextUnformatted(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

                if (ImGui.BeginPopup(popupId))
                {
                    DrawEntryContextMenu(pagedEntries);
                    ImGui.EndPopup();
                }

                ImGui.TableSetColumnIndex(col++); // Level
                if (lineIndex == 0)
                {
                    using (UiPush.Color(ImGuiCol.Text, GetLevelColor(entry.Level)))
                        ImGui.TextUnformatted(entry.Level.ToString());
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

    private void ToggleLineSelection(HistoryLogEntry entry, int lineIndex, int rowIndex, IReadOnlyList<HistoryLogEntry> entries, int totalLinesInEntry)
    {
        var io = ImGui.GetIO();
        var ctrlPressed = io.KeyCtrl;
        var shiftPressed = io.KeyShift;

        if (shiftPressed && lastSelectedIndex >= 0)
        {
            if (!ctrlPressed)
                selectedLines.Clear();

            var allLines = new List<(HistoryLogEntry Entry, int LineIndex)>();
            foreach (var e in entries)
            {
                var lines = (e.Message ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0)
                    lines = new[] { string.Empty };
                for (var i = 0; i < lines.Length; i++)
                    allLines.Add((e, i));
            }

            var start = Math.Min(lastSelectedIndex, rowIndex);
            var end = Math.Max(lastSelectedIndex, rowIndex);
            for (var i = start; i <= end && i < allLines.Count; i++)
                selectedLines.Add(allLines[i]);
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
        var itemsPerPageOptions = new[] { 5, 10, 25, 50, 100, 200, 500, 1000 };
        var selectedIndex = Array.IndexOf(itemsPerPageOptions, currentItemsPerPage);
        if (selectedIndex < 0) selectedIndex = 4; // 100

        if (ImGui.Combo("##ItemsPerPage", ref selectedIndex, itemsPerPageOptions.Select(x => x.ToString()).ToArray(), itemsPerPageOptions.Length))
        {
            HistoryLoggerConfig.ItemsPerPage = itemsPerPageOptions[selectedIndex];
            view.ItemsPerPage = itemsPerPageOptions[selectedIndex];
        }

        ImGui.SameLine();
        ImGui.TextDisabled("per page");

        ImGui.SameLine();

        ImGui.PushFont(NoireService.PluginInterface.UiBuilder.FontIcon);

        var currentPage = view.Page;

        using (UiPush.Disabled(currentPage <= 1))
        {
            if (ImGui.Button($"{FontAwesomeIcon.AngleDoubleLeft.ToIconString()}##FirstPage"))
                view.Page = 1;
        }

        ImGui.SameLine();

        using (UiPush.Disabled(currentPage <= 1))
        {
            if (ImGui.Button($"{FontAwesomeIcon.AngleLeft.ToIconString()}##PrevPage"))
                view.Page = currentPage - 1;
        }

        ImGui.PopFont();

        ImGui.SameLine();

        ImGui.SetNextItemWidth(60f);
        var pageInput = currentPage;
        if (ImGui.InputInt("##PageNumber", ref pageInput, 0, 0))
            view.Page = pageInput;

        ImGui.SameLine();
        ImGui.TextDisabled($"/ {totalPages}");

        ImGui.SameLine();

        ImGui.PushFont(NoireService.PluginInterface.UiBuilder.FontIcon);

        using (UiPush.Disabled(currentPage >= totalPages))
        {
            if (ImGui.Button($"{FontAwesomeIcon.AngleRight.ToIconString()}##NextPage"))
                view.Page = currentPage + 1;
        }

        ImGui.SameLine();

        using (UiPush.Disabled(currentPage >= totalPages))
        {
            if (ImGui.Button($"{FontAwesomeIcon.AngleDoubleRight.ToIconString()}##LastPage"))
                view.Page = totalPages;
        }

        ImGui.PopFont();
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

    private void DrawEntryContextMenu(IReadOnlyList<HistoryLogEntry> orderedEntries)
    {
        var ctrlPressed = ImGui.GetIO().KeyCtrl;

        string deleteLabel;
        string copyLabel;

        int selectionCount;
        if (HistoryLoggerConfig.SelectLinesSeparately)
        {
            selectionCount = selectedLines.Select(l => l.Entry).Distinct().Count();
            var totalLines = selectedLines.Count;

            if (selectionCount > 1)
                deleteLabel = $"Delete selected entries ({selectionCount})";
            else
                deleteLabel = "Delete entry";

            if (selectionCount > 1)
                copyLabel = $"Copy selected lines from multiple entries to clipboard ({selectionCount})";
            else if (totalLines == 1)
                copyLabel = "Copy selected line to clipboard";
            else
                copyLabel = "Copy selected lines to clipboard";
        }
        else
        {
            selectionCount = selectedEntries.Count;

            if (selectionCount > 1)
            {
                deleteLabel = $"Delete selected entries ({selectionCount})";
                copyLabel = $"Copy selected entries to clipboard ({selectionCount})";
            }
            else
            {
                deleteLabel = "Delete entry";
                copyLabel = "Copy entry to clipboard";
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
                        ? selectedLines.Select(l => l.Entry).Distinct().ToList()
                        : selectedEntries.ToList();

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
                ImGui.SetTooltip("Hold CTRL to delete");
        }

        if (ImGui.MenuItem(copyLabel))
        {
            var copyEntries = selectionCount > 1
                ? orderedEntries.Where(selectedEntries.Contains)
                : (selectionCount == 1 ? selectedEntries : new[] { contextEntry! });

            CopyEntriesToClipboard(copyEntries);
        }

        var showMessageOnlyCopy = !HistoryLoggerConfig.SelectLinesSeparately ||
            selectedLines.Any(l => l.LineIndex == 0);

        if (showMessageOnlyCopy)
        {
            var copyMessageOnlyLabel = selectionCount > 1
                ? $"Copy only messages to clipboard ({selectionCount})"
                : "Copy only message to clipboard";

            if (ImGui.MenuItem(copyMessageOnlyLabel))
            {
                CopyMessagesOnlyToClipboard();
            }
        }
    }
    private void CopyEntriesToClipboard(IEnumerable<HistoryLogEntry> entries)
    {
        if (HistoryLoggerConfig.SelectLinesSeparately && selectedLines.Count > 0)
        {
            var selectedByEntry = selectedLines.GroupBy(l => l.Entry).ToDictionary(g => g.Key, g => g.Select(l => l.LineIndex).ToHashSet());

            var linesToCopy = new List<string>();

            var sortedEntries = GetFilteredEntries();

            foreach (var entry in sortedEntries)
            {
                if (!selectedByEntry.TryGetValue(entry, out var selectedLineIndices))
                    continue;

                var messageLines = (entry.Message ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (messageLines.Length == 0)
                    messageLines = new[] { string.Empty };

                var sortedIndices = selectedLineIndices.OrderBy(idx => idx).ToList();
                var includesFirstLine = sortedIndices.Contains(0);

                for (var i = 0; i < sortedIndices.Count; i++)
                {
                    var lineIndex = sortedIndices[i];
                    if (lineIndex >= messageLines.Length)
                        continue;

                    var line = messageLines[lineIndex];
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
            var selectedByEntry = selectedLines.GroupBy(l => l.Entry).ToDictionary(g => g.Key, g => g.Select(l => l.LineIndex).ToHashSet());

            var linesToCopy = new List<string>();

            var sortedEntries = GetFilteredEntries();

            foreach (var entry in sortedEntries)
            {
                if (!selectedByEntry.TryGetValue(entry, out var selectedLineIndices))
                    continue;

                var messageLines = (entry.Message ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (messageLines.Length == 0)
                    messageLines = new[] { string.Empty };

                var sortedIndices = selectedLineIndices.OrderBy(idx => idx).ToList();

                foreach (var lineIndex in sortedIndices)
                {
                    if (lineIndex < messageLines.Length)
                        linesToCopy.Add(messageLines[lineIndex]);
                }
            }

            ImGui.SetClipboardText(string.Join(Environment.NewLine, linesToCopy));
        }
        else
        {
            var messages = selectedEntries.Count > 0
                ? selectedEntries.Select(e => e.Message)
                : new[] { contextEntry?.Message ?? string.Empty };

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

    /// <inheritdoc />
    public override void Dispose() { }
}
