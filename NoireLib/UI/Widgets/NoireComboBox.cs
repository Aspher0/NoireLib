using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A stateful combo box widget with quality-of-life features:<br/>
/// - An optional filter text input at the top of the dropdown, automatically focused when the dropdown opens.<br/>
/// - Wheel scrolling and arrow keys cycle the highlighted option inside the open dropdown; Enter confirms it.<br/>
/// - An optional "hold key + mouse wheel" shortcut cycling the selection while hovering the closed combo, with or without looping.<br/>
/// - An optional automatic hint tooltip advertising the wheel shortcut, drawn with <see cref="NoireTooltip"/>.<br/>
/// Create one instance per combo, keep it, and call <see cref="Draw"/> every frame inside your window.
/// </summary>
/// <typeparam name="T">The type of the items of the combo box.</typeparam>
public class NoireComboBox<T>
{
    private readonly List<T> items = new();
    private readonly List<int> filteredIndices = new();

    private string filterText = string.Empty;
    private int selectedIndex = -1;
    private int highlightIndex = -1;
    private bool scrollToHighlight;
    private bool changedThisFrame;

    private float lastParentScrollY;
    private bool hasLastParentScroll;

    private TooltipContent? cachedHintContent;
    private VirtualKey? cachedHintKey;

    /// <summary>
    /// Initializes a new combo box.
    /// </summary>
    /// <param name="id">An optional unique identifier used for the ImGui ids. When <see langword="null"/>, a random one is generated.</param>
    /// <param name="items">The initial items of the combo box.</param>
    /// <param name="displayFunc">How an item is converted to its display text. Defaults to <c>ToString()</c>.</param>
    public NoireComboBox(string? id = null, IEnumerable<T>? items = null, Func<T, string>? displayFunc = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;
        DisplayFunc = displayFunc;

        if (items != null)
            this.items.AddRange(items);
    }

    /// <summary>
    /// The unique identifier of this combo box, used for the ImGui ids.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The label displayed next to the combo box. When <see langword="null"/> or empty, no label is displayed.
    /// </summary>
    public string? Label { get; set; } = null;

    /// <summary>
    /// The width of the combo box. When <see langword="null"/>, the default ImGui item width is used.
    /// </summary>
    public float? Width { get; set; } = null;

    /// <summary>
    /// Extra ImGui combo flags applied to the widget (e.g. <see cref="ImGuiComboFlags.NoArrowButton"/>).
    /// </summary>
    public ImGuiComboFlags ComboFlags { get; set; } = ImGuiComboFlags.None;

    /// <summary>
    /// How an item is converted to its display text. When <see langword="null"/>, <c>ToString()</c> is used.
    /// </summary>
    public Func<T, string>? DisplayFunc { get; set; }

    /// <summary>
    /// The preview text shown when no item is selected.
    /// </summary>
    public string PreviewPlaceholder { get; set; } = "Select...";

    /// <summary>
    /// Invoked when the selection changes, with the old and the new selected items.
    /// </summary>
    public Action<T?, T?>? OnSelectionChanged { get; set; } = null;

    #region Filter options

    /// <summary>
    /// Whether the dropdown shows a filter text input at the top. Defaults to <see langword="false"/>.
    /// </summary>
    public bool FilterEnabled { get; set; } = false;

    /// <summary>
    /// The hint text of the filter input.
    /// </summary>
    public string FilterHint { get; set; } = "Filter...";

    /// <summary>
    /// Whether the filter input is automatically focused when the dropdown opens. Defaults to <see langword="true"/>.
    /// </summary>
    public bool FilterAutoFocus { get; set; } = true;

    /// <summary>
    /// Whether the filter text is cleared every time the dropdown opens. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ClearFilterOnOpen { get; set; } = true;

    /// <summary>
    /// How an item is matched against the filter text. When <see langword="null"/>, a case-insensitive "contains" match on the display text is used.
    /// </summary>
    public Func<T, string, bool>? FilterPredicate { get; set; } = null;

    /// <summary>
    /// The text displayed in the dropdown when no item matches the filter.
    /// </summary>
    public string NoResultsText { get; set; } = "No results";

    #endregion

    #region Dropdown options

    /// <summary>
    /// The maximum number of items visible in the dropdown before it scrolls. Defaults to 8.
    /// </summary>
    public int VisibleItemCount { get; set; } = 8;

    /// <summary>
    /// Whether scrolling the mouse wheel over the open dropdown cycles the highlighted option (Enter confirms it). Defaults to <see langword="true"/>.<br/>
    /// When disabled, the wheel scrolls the item list normally.
    /// </summary>
    public bool DropdownWheelCycle { get; set; } = true;

    /// <summary>
    /// Whether cycling the highlighted option (wheel or arrow keys) wraps around when reaching the first/last option. Defaults to <see langword="false"/>.
    /// </summary>
    public bool DropdownCycleLoop { get; set; } = false;

    #endregion

    #region Closed combo wheel cycling options

    /// <summary>
    /// Whether scrolling the mouse wheel over the closed combo cycles the selection. Defaults to <see langword="false"/>.<br/>
    /// See also <see cref="WheelCycleHoldKey"/> and <see cref="WheelCycleLoop"/>.
    /// </summary>
    public bool WheelCycleEnabled { get; set; } = false;

    /// <summary>
    /// An optional key that must be held for the closed-combo wheel cycling to trigger (e.g. <see cref="VirtualKey.CONTROL"/>).<br/>
    /// When <see langword="null"/>, no key is required.
    /// </summary>
    public VirtualKey? WheelCycleHoldKey { get; set; } = null;

    /// <summary>
    /// Whether the closed-combo wheel cycling wraps around when reaching the first/last item,
    /// instead of stopping at the boundaries. Defaults to <see langword="false"/>.
    /// </summary>
    public bool WheelCycleLoop { get; set; } = false;

    /// <summary>
    /// Whether a hint tooltip advertising the wheel shortcut is shown when hovering the closed combo. Defaults to <see langword="true"/>.<br/>
    /// Only shown when <see cref="WheelCycleEnabled"/> is <see langword="true"/>. See <see cref="WheelCycleHintContent"/> to customize it.
    /// </summary>
    public bool WheelCycleHintEnabled { get; set; } = true;

    /// <summary>
    /// A custom content for the wheel shortcut hint tooltip. When <see langword="null"/>, a default hint is generated
    /// (e.g. "CTRL + &lt;mouse icon&gt; to cycle").
    /// </summary>
    public TooltipContent? WheelCycleHintContent { get; set; } = null;

    /// <summary>
    /// The style of the wheel shortcut hint tooltip. When <see langword="null"/>, the default style is used.
    /// </summary>
    public TooltipStyle? WheelCycleHintStyle { get; set; } = null;

    /// <summary>
    /// Whether the scroll position of the parent window is restored when the wheel cycling consumes a scroll over the closed combo. Defaults to <see langword="true"/>.<br/>
    /// Only relevant when the combo lives in a scrollable window and <see cref="WheelCycleHoldKey"/> is not Ctrl
    /// (ImGui already suppresses window scrolling while Ctrl is held).
    /// </summary>
    public bool RestoreParentScrollOnWheelCycle { get; set; } = true;

    #endregion

    #region Items & Selection

    /// <summary>
    /// The items of the combo box. Use <see cref="SetItems"/> to modify them.
    /// </summary>
    public IReadOnlyList<T> Items => items;

    /// <summary>
    /// The index of the selected item, or -1 when nothing is selected.<br/>
    /// Setting this property does not invoke <see cref="OnSelectionChanged"/>; use <see cref="Select(int)"/> for that.
    /// </summary>
    public int SelectedIndex
    {
        get => selectedIndex;
        set => selectedIndex = value < 0 || items.Count == 0 ? -1 : Math.Min(value, items.Count - 1);
    }

    /// <summary>
    /// The currently selected item, or <see langword="default"/> when nothing is selected.
    /// </summary>
    public T? SelectedItem => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : default;

    /// <summary>
    /// Replaces the items of the combo box.
    /// </summary>
    /// <param name="newItems">The new items.</param>
    /// <param name="keepSelection">Whether to keep the currently selected item selected if it is still present in the new items.</param>
    /// <returns>This <see cref="NoireComboBox{T}"/> instance, for chaining.</returns>
    public NoireComboBox<T> SetItems(IEnumerable<T> newItems, bool keepSelection = true)
    {
        var previous = SelectedItem;
        var hadSelection = selectedIndex >= 0;

        items.Clear();
        if (newItems != null)
            items.AddRange(newItems);

        selectedIndex = keepSelection && hadSelection && previous != null
            ? items.FindIndex(item => EqualityComparer<T>.Default.Equals(item, previous))
            : -1;

        RebuildFilteredIndices();
        return this;
    }

    /// <summary>
    /// Selects the item at the given index and invokes <see cref="OnSelectionChanged"/> if the selection changed.
    /// </summary>
    /// <param name="index">The index of the item to select, or -1 to clear the selection.</param>
    /// <returns>True if the selection changed, false otherwise.</returns>
    public bool Select(int index)
    {
        var newIndex = index < 0 || items.Count == 0 ? -1 : Math.Min(index, items.Count - 1);
        if (newIndex == selectedIndex)
            return false;

        var oldItem = SelectedItem;
        selectedIndex = newIndex;
        InvokeSelectionChanged(oldItem, SelectedItem);
        return true;
    }

    /// <summary>
    /// Selects the given item and invokes <see cref="OnSelectionChanged"/> if the selection changed.
    /// </summary>
    /// <param name="item">The item to select.</param>
    /// <returns>True if the item was found and the selection changed, false otherwise.</returns>
    public bool Select(T item)
    {
        var index = items.FindIndex(existing => EqualityComparer<T>.Default.Equals(existing, item));
        return index >= 0 && Select(index);
    }

    /// <summary>
    /// Clears the selection and invokes <see cref="OnSelectionChanged"/> if something was selected.
    /// </summary>
    /// <returns>True if the selection changed, false otherwise.</returns>
    public bool ClearSelection() => Select(-1);

    #endregion

    #region Drawing

    /// <summary>
    /// Draws the combo box. Call this every frame inside your window.
    /// </summary>
    /// <returns>True if the selection changed this frame, false otherwise.</returns>
    public bool Draw()
    {
        changedThisFrame = false;
        ClampSelection();

        if (Width.HasValue)
            ImGui.SetNextItemWidth(Width.Value);

        var preview = selectedIndex >= 0 ? DisplayOf(items[selectedIndex]) : PreviewPlaceholder;
        var label = string.IsNullOrEmpty(Label) ? $"###NoireCombo_{Id}" : $"{Label}###NoireCombo_{Id}";

        var comboOpen = false;
        using (var combo = ImRaii.Combo(label, preview, ComboFlags))
        {
            if (combo)
            {
                comboOpen = true;
                DrawPopupContent();
            }
        }

        var wheelConsumed = false;
        if (!comboOpen)
            wheelConsumed = HandleClosedComboInteractions();

        // Remember the scroll position of the parent window so it can be restored
        // when a wheel event is consumed by the closed-combo cycling (see HandleClosedComboInteractions).
        if (!wheelConsumed)
        {
            lastParentScrollY = ImGui.GetScrollY();
            hasLastParentScroll = true;
        }

        return changedThisFrame;
    }

    private void DrawPopupContent()
    {
        var appearing = ImGui.IsWindowAppearing();
        if (appearing)
        {
            if (ClearFilterOnOpen)
                filterText = string.Empty;

            RebuildFilteredIndices();
            highlightIndex = Math.Max(0, filteredIndices.IndexOf(selectedIndex));
            scrollToHighlight = true;
        }

        var confirm = false;

        if (FilterEnabled)
        {
            if (appearing && FilterAutoFocus)
                ImGui.SetKeyboardFocusHere();

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            confirm |= ImGui.InputTextWithHint($"###NoireComboFilter_{Id}", FilterHint, ref filterText, 256, ImGuiInputTextFlags.EnterReturnsTrue);

            if (ImGui.IsItemEdited())
            {
                RebuildFilteredIndices();
                highlightIndex = 0;
                scrollToHighlight = true;
            }
        }

        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            MoveHighlight(1);
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            MoveHighlight(-1);

        if (!confirm && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)))
            confirm = true;

        if (DropdownWheelCycle)
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
                MoveHighlight(wheel > 0f ? -1 : 1);
        }

        DrawItemList();

        if (confirm)
        {
            if (highlightIndex >= 0 && highlightIndex < filteredIndices.Count)
                SelectFromUi(filteredIndices[highlightIndex]);

            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawItemList()
    {
        var itemHeight = ImGui.GetTextLineHeightWithSpacing();
        var visibleCount = Math.Max(1, Math.Min(VisibleItemCount, Math.Max(filteredIndices.Count, 1)));
        var childHeight = visibleCount * itemHeight;

        // The item list lives in a child that never scrolls with the wheel when wheel cycling is enabled:
        // the wheel moves the highlight instead, and the list follows it.
        var childFlags = DropdownWheelCycle ? ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None;

        using var child = ImRaii.Child($"###NoireComboItems_{Id}", new Vector2(0f, childHeight), false, childFlags);
        if (!child)
            return;

        if (filteredIndices.Count == 0)
        {
            ImGui.TextDisabled(NoResultsText);
            return;
        }

        var mouseMoved = ImGui.GetIO().MouseDelta != Vector2.Zero;

        for (var filteredPosition = 0; filteredPosition < filteredIndices.Count; filteredPosition++)
        {
            var itemIndex = filteredIndices[filteredPosition];
            if (itemIndex >= items.Count)
                continue; // The items changed while the dropdown was open.

            var isSelected = itemIndex == selectedIndex;
            var isHighlighted = filteredPosition == highlightIndex;

            if (ImGui.Selectable($"{DisplayOf(items[itemIndex])}###NoireComboItem_{Id}_{itemIndex}", isSelected || isHighlighted))
            {
                SelectFromUi(itemIndex);
                ImGui.CloseCurrentPopup();
            }

            if (isHighlighted && scrollToHighlight)
            {
                ImGui.SetScrollHereY(0.5f);
                scrollToHighlight = false;
            }

            if (mouseMoved && ImGui.IsItemHovered())
                highlightIndex = filteredPosition;
        }
    }

    /// <summary>
    /// Handles the hold key + wheel cycling and the hint tooltip on the closed combo.
    /// </summary>
    /// <returns>True if a wheel event was consumed this frame.</returns>
    private bool HandleClosedComboInteractions()
    {
        if (!WheelCycleEnabled || !ImGui.IsItemHovered())
            return false;

        if (WheelCycleHintEnabled)
            NoireTooltip.Show(GetWheelCycleHint(), WheelCycleHintStyle, $"NoireComboHint_{Id}");

        if (items.Count == 0)
            return false;

        var holdKeySatisfied = WheelCycleHoldKey == null || KeybindsHelper.IsAsyncKeyDown((int)WheelCycleHoldKey.Value);
        if (!holdKeySatisfied)
            return false;

        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f)
            return false;

        var newIndex = ComputeCycledIndex(selectedIndex, wheel > 0f ? -1 : 1, items.Count, WheelCycleLoop);
        if (newIndex != selectedIndex)
            SelectFromUi(newIndex);

        // The parent window already scrolled during this frame's ImGui input processing.
        // Restore its previous scroll position so the wheel event only cycles the combo.
        if (RestoreParentScrollOnWheelCycle && hasLastParentScroll)
            ImGui.SetScrollY(lastParentScrollY);

        return true;
    }

    #endregion

    #region Internal logic

    /// <summary>
    /// Computes the next index when cycling a selection by a direction, with or without looping.
    /// </summary>
    /// <param name="current">The current index, or -1 when nothing is selected.</param>
    /// <param name="direction">The cycling direction: +1 for the next item, -1 for the previous one.</param>
    /// <param name="count">The total number of items.</param>
    /// <param name="loop">Whether to wrap around at the boundaries instead of clamping.</param>
    /// <returns>The new index, or -1 when there are no items.</returns>
    internal static int ComputeCycledIndex(int current, int direction, int count, bool loop)
    {
        if (count <= 0)
            return -1;

        if (current < 0)
            return direction > 0 ? 0 : count - 1;

        var next = current + direction;
        return loop
            ? ((next % count) + count) % count
            : Math.Clamp(next, 0, count - 1);
    }

    /// <summary>
    /// The default filter match: case-insensitive "contains" on the display text.
    /// </summary>
    /// <param name="displayText">The display text of the item.</param>
    /// <param name="filter">The filter text.</param>
    /// <returns>True if the item matches the filter.</returns>
    internal static bool DefaultFilterMatch(string displayText, string filter)
        => displayText.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rebuilds the list of item indices matching the current filter text.
    /// </summary>
    internal void RebuildFilteredIndices()
    {
        filteredIndices.Clear();

        for (var i = 0; i < items.Count; i++)
        {
            if (!FilterEnabled || string.IsNullOrEmpty(filterText) || MatchesFilter(items[i]))
                filteredIndices.Add(i);
        }
    }

    internal IReadOnlyList<int> FilteredIndices => filteredIndices;

    internal string FilterText
    {
        get => filterText;
        set => filterText = value ?? string.Empty;
    }

    private bool MatchesFilter(T item)
    {
        try
        {
            return FilterPredicate?.Invoke(item, filterText) ?? DefaultFilterMatch(DisplayOf(item), filterText);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"The filter predicate of combo box '{Id}' threw an exception.");
            return false;
        }
    }

    private void MoveHighlight(int direction)
    {
        if (filteredIndices.Count == 0)
            return;

        highlightIndex = ComputeCycledIndex(highlightIndex, direction, filteredIndices.Count, DropdownCycleLoop);
        scrollToHighlight = true;
    }

    private void SelectFromUi(int itemIndex)
    {
        if (itemIndex == selectedIndex)
            return;

        var oldItem = SelectedItem;
        selectedIndex = itemIndex;
        changedThisFrame = true;
        InvokeSelectionChanged(oldItem, SelectedItem);
    }

    private void InvokeSelectionChanged(T? oldItem, T? newItem)
    {
        try
        {
            OnSelectionChanged?.Invoke(oldItem, newItem);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"The selection changed callback of combo box '{Id}' threw an exception.");
        }
    }

    private void ClampSelection()
    {
        if (selectedIndex >= items.Count)
            selectedIndex = items.Count - 1;
    }

    private string DisplayOf(T item)
    {
        try
        {
            return DisplayFunc?.Invoke(item) ?? item?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"The display function of combo box '{Id}' threw an exception.");
            return string.Empty;
        }
    }

    private TooltipContent GetWheelCycleHint()
    {
        if (WheelCycleHintContent != null)
            return WheelCycleHintContent;

        if (cachedHintContent == null || cachedHintKey != WheelCycleHoldKey)
        {
            cachedHintKey = WheelCycleHoldKey;
            cachedHintContent = new TooltipContent();

            if (WheelCycleHoldKey != null)
                cachedHintContent.AddText($"{KeybindsHelper.GetKeyName((int)WheelCycleHoldKey.Value)} + ");

            cachedHintContent
                .AddIcon(FontAwesomeIcon.Mouse)
                .AddSpacing(2f)
                .AddIcon(FontAwesomeIcon.ArrowsAltV)
                .AddText(" to cycle");
        }

        return cachedHintContent;
    }

    #endregion
}
