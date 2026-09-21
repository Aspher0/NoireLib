using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using NoireLib.HotkeyManager;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A stateful combo box widget: an optional filter with fuzzy matching, arrow-key and wheel navigation, and an
/// optional held-binding wheel shortcut on the closed combo. Create one instance per combo, keep it, and call
/// <see cref="Draw"/> every frame inside your window.
/// </summary>
/// <typeparam name="T">The type of the items of the combo box.</typeparam>
[NoireFacadeFactory]
public class NoireComboBox<T>
{
    private readonly List<T> items = new();
    private readonly List<int> filteredIndices = new();
    private readonly List<(int Index, int Score)> scored = new();

    private string filterText = string.Empty;
    private int selectedIndex = -1;
    private int highlightIndex = -1;
    private bool scrollToHighlight;
    private bool changedThisFrame;
    private bool showMatches;
    private UiMemoryScope filterMemory;
    private bool persistRefusalLogged;
    private bool filterRestored;

    private NoireHotkeyManager? wheelCycleHotkeyManager;
    private string? wheelCycleHotkeyId;

    private NoireContent? cachedHintContent;
    private HotkeyBinding cachedHintBinding;
    private bool hasCachedHint;

    /// <summary>
    /// Initializes a new combo box.
    /// </summary>
    /// <param name="id">An optional unique identifier used for the ImGui ids.</param>
    /// <param name="items">The initial items of the combo box.</param>
    /// <param name="displayFunc">How an item is converted to its display text.</param>
    public NoireComboBox(string? id = null, IEnumerable<T>? items = null, Func<T, string>? displayFunc = null)
    {
        HasGeneratedId = string.IsNullOrWhiteSpace(id);
        Id = HasGeneratedId ? RandomGenerator.GenerateGuidString() : id!;
        DisplayFunc = displayFunc;

        if (items != null)
            this.items.AddRange(items);
    }

    /// <summary>Whether this combo's id was generated.</summary>
    public bool HasGeneratedId { get; }

    /// <summary>
    /// The unique identifier of this combo box, used for the ImGui ids.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The label displayed next to the combo box. When <see langword="null"/> or empty, no label is displayed.
    /// </summary>
    public string? Label { get; set; } = null;

    /// <summary>The width of the combo box, in unscaled pixels.</summary>
    public float? Width { get; set; } = null;

    /// <summary>
    /// Extra ImGui combo flags applied to the widget (e.g. <see cref="ImGuiComboFlags.NoArrowButton"/>).
    /// </summary>
    public ImGuiComboFlags ComboFlags { get; set; } = ImGuiComboFlags.None;

    /// <summary>Paints the closed combo as a <see cref="NoireShapes.Plate"/>.</summary>
    public PlateStyle? BoxStyle { get; set; }

    /// <summary>
    /// The colour of the chevron drawn while <see cref="BoxStyle"/> is set. When <see langword="null"/>, the accent.
    /// </summary>
    public Vector4? BoxArrowColor { get; set; }

    /// <summary>How wide the chevron is, at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public float BoxArrowSize { get; set; } = 6f;

    /// <summary>How far the chevron sits from the box's right edge, at 100%.</summary>
    public float BoxArrowInset { get; set; } = 12f;

    /// <summary>
    /// How the dropdown itself is drawn: its surface, its border, its padding and its rows.
    /// </summary>
    public ComboPopupStyle? PopupStyle { get; set; }

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
    /// Whether the dropdown shows a filter text input at the top.
    /// </summary>
    public bool FilterEnabled { get; set; } = false;

    /// <summary>
    /// The hint text of the filter input.
    /// </summary>
    public string FilterHint { get; set; } = "Filter...";

    /// <summary>
    /// Whether the filter input is automatically focused when the dropdown opens.
    /// </summary>
    public bool FilterAutoFocus { get; set; } = true;

    /// <summary>
    /// Whether the filter input stays pinned above the options while they scroll, or scrolls away with them.
    /// </summary>
    public bool FilterPinned { get; set; } = true;

    /// <summary>
    /// Whether the filter text is cleared every time the dropdown opens.<br/>
    /// Any <see cref="FilterMemory"/> other than <see cref="UiMemoryScope.None"/> turns this off.
    /// </summary>
    public bool ClearFilterOnOpen { get; set; } = true;

    /// <summary>
    /// How long the search text is remembered.<br/>
    /// <see cref="UiMemoryScope.Persisted"/> needs a stable id.
    /// </summary>
    public UiMemoryScope FilterMemory
    {
        get => filterMemory;
        set
        {
            if (filterMemory == value)
                return;

            filterMemory = value;

            // Restored on the next draw. This is usually set in a constructor, before the state file is read.
            if (value != UiMemoryScope.None)
                filterRestored = false;
        }
    }

    /// <summary>
    /// Whether the closed-combo wheel shortcut cycles only what the current search matches.
    /// </summary>
    public bool WheelCycleFiltered { get; set; }

    /// <summary>
    /// Whether the filter matches fuzzily and orders the options by score.<br/>
    /// A <see cref="FilterPredicate"/> overrides this.
    /// </summary>
    public bool FilterFuzzy { get; set; } = true;

    /// <summary>Whether the matched characters are highlighted while <see cref="FilterFuzzy"/> is on.</summary>
    public bool FilterHighlight { get; set; } = true;

    /// <summary>
    /// The color the matched characters are drawn in. When <see langword="null"/>, the theme's accent is used.
    /// </summary>
    public Vector4? FilterHighlightColor { get; set; }

    /// <summary>
    /// Draws each option yourself.<br/>
    /// Set <see cref="ItemHeight"/> as well when the rows are taller than one line.
    /// </summary>
    public Action<UiComboItemDraw<T>>? ItemRenderer { get; set; }

    /// <summary>The height of one option at 100%. When <see langword="null"/>, one line of text.</summary>
    public float? ItemHeight { get; set; }

    /// <summary>
    /// Whether the option list is drawn through a clipper. When <see langword="null"/>, it turns on past <see cref="VirtualizeThreshold"/> options.<br/>
    /// Every row must be the same height.
    /// </summary>
    public bool? Virtualize { get; set; }

    /// <summary>
    /// How many options it takes before <see cref="Virtualize"/> turns itself on.
    /// </summary>
    public int VirtualizeThreshold { get; set; } = 100;

    /// <summary>
    /// How many option rows were actually drawn the last time the dropdown was open.
    /// </summary>
    public int DrawnRowCount { get; private set; }

    /// <summary>How an item is matched against the filter text. When <see langword="null"/>, <see cref="FilterFuzzy"/> decides.</summary>
    public Func<T, string, bool>? FilterPredicate { get; set; } = null;

    /// <summary>
    /// The text displayed in the dropdown when no item matches the filter.
    /// </summary>
    public string NoResultsText { get; set; } = "No results";

    #endregion

    #region Dropdown options

    /// <summary>
    /// The maximum number of options visible in the dropdown before it scrolls.
    /// </summary>
    public int VisibleItemCount { get; set; } = 8;

    /// <summary>
    /// Whether cycling the highlighted option with the arrow keys wraps around when reaching the first/last option.
    /// </summary>
    public bool DropdownCycleLoop { get; set; } = false;

    #endregion

    #region Closed combo wheel cycling options

    /// <summary>
    /// Whether scrolling the mouse wheel over the closed combo cycles the selection.
    /// </summary>
    public bool WheelCycleEnabled { get; set; } = false;

    /// <summary>
    /// The binding that must be held for the closed-combo wheel cycling.<br/>
    /// Ignored while a hotkey is attached through <see cref="BindWheelCycleHotkey"/>.
    /// </summary>
    public HotkeyBinding WheelCycleBinding { get; set; } = default;

    /// <summary>Whether the closed-combo wheel cycling wraps around at the first and last item.</summary>
    public bool WheelCycleLoop { get; set; } = false;

    /// <summary>
    /// Whether a hint tooltip advertising the wheel shortcut is shown when hovering the closed combo.
    /// </summary>
    public bool WheelCycleHintEnabled { get; set; } = true;

    /// <summary>The wheel hint tooltip's content. When <see langword="null"/>, it is generated from the binding.</summary>
    public NoireContent? WheelCycleHintContent { get; set; } = null;

    /// <summary>
    /// The style of the wheel shortcut hint tooltip. When <see langword="null"/>, the default style is used.
    /// </summary>
    public TooltipStyle? WheelCycleHintStyle { get; set; } = null;

    /// <summary>
    /// How the keyboard focus mark looks on this combo and its filter. When <see langword="null"/>,
    /// <see cref="NoireFocus.Style"/>.
    /// </summary>
    public FocusStyle? FocusStyle { get; set; }

    /// <summary>
    /// The binding the closed-combo wheel cycling currently requires: the live binding of the hotkey attached
    /// through <see cref="BindWheelCycleHotkey"/> when there is one, otherwise <see cref="WheelCycleBinding"/>.
    /// </summary>
    public HotkeyBinding ResolvedWheelCycleBinding
    {
        get
        {
            TryResolveWheelCycleBinding(out var binding);
            return binding;
        }
    }

    private bool TryResolveWheelCycleBinding(out HotkeyBinding binding)
    {
        if (wheelCycleHotkeyManager == null || wheelCycleHotkeyId == null)
        {
            binding = WheelCycleBinding;
            return true;
        }

        if (!wheelCycleHotkeyManager.TryGetHotkey(wheelCycleHotkeyId, out var entry) || !entry.Enabled)
        {
            binding = default;
            return false;
        }

        binding = entry.Binding;
        return true;
    }

    /// <summary>
    /// Drives the closed-combo wheel cycling from a hotkey registered on a <see cref="NoireHotkeyManager"/>.<br/>
    /// <see cref="WheelCycleEnabled"/> must be set as well.
    /// </summary>
    /// <param name="hotkeyManager">The hotkey manager owning the hotkey.</param>
    /// <param name="hotkeyId">The hotkey's id.</param>
    /// <returns>This instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hotkeyManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="hotkeyId"/> is null, empty or blank.</exception>
    public NoireComboBox<T> BindWheelCycleHotkey(NoireHotkeyManager hotkeyManager, string hotkeyId)
    {
        ArgumentNullException.ThrowIfNull(hotkeyManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(hotkeyId);

        wheelCycleHotkeyManager = hotkeyManager;
        wheelCycleHotkeyId = hotkeyId;
        return this;
    }

    /// <summary>Detaches the hotkey attached through <see cref="BindWheelCycleHotkey"/>.</summary>
    /// <returns>This instance, for chaining.</returns>
    public NoireComboBox<T> UnbindWheelCycleHotkey()
    {
        wheelCycleHotkeyManager = null;
        wheelCycleHotkeyId = null;
        return this;
    }

    #endregion

    #region Items & Selection

    /// <summary>
    /// The items of the combo box, modified through <see cref="SetItems"/>.
    /// </summary>
    public IReadOnlyList<T> Items => items;

    /// <summary>
    /// The index of the selected item, or -1 when nothing is selected.<br/>
    /// Setting it does not invoke <see cref="OnSelectionChanged"/>. Use <see cref="Select(int)"/> for that.
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

    /// <summary>Draws the combo box. Call it every frame.</summary>
    /// <returns>True when the selection changed this frame.</returns>
    public bool Draw()
    {
        using var profile = UiProfile.Widget(nameof(NoireComboBox<T>), Id);

        changedThisFrame = false;
        RestorePersistedFilter();
        ClampSelection();

        if (Width.HasValue)
            ImGui.SetNextItemWidth(Width.Value);

        var preview = selectedIndex >= 0 ? DisplayOf(items[selectedIndex]) : PreviewPlaceholder;
        var label = string.IsNullOrEmpty(Label) ? UiIds.For("###NoireCombo_", Id) : UiIds.Labelled(Label, "###NoireCombo_", Id);

        // ImGui budgets the popup at eight options and ignores the filter input. Uncapped, the list grows a nested scrollbar.
        // The style is pushed before measuring. The measurement reads the padding and spacing in force.
        var popup = BeginPopupStyle();

        ApplyPopupConstraints();

        var box = BeginBox(out var boxRect);

        // Read before the popup. Afterwards the last item is whatever the dropdown submitted.
        var boxFocused = NoireFocus.IsLastFocused();
        var boxItem = boxFocused ? ImGuiP.GetItemID() : 0u;

        // Inside the popup the current window is the popup.
        var ownerInFront = UiWindowOrder.InTopLayer;

        var comboOpen = false;
        using (var combo = ImRaii.Combo(label, preview, ComboFlags | (BoxStyle != null ? ImGuiComboFlags.NoArrowButton : ImGuiComboFlags.None)))
        {
            box.Dispose();

            if (combo)
            {
                comboOpen = true;

                if (ownerInFront)
                    UiWindowOrder.KeepInFront();

                // A font handle pushed before Begin is not what the popup draws with.
                if (PopupStyle?.TextSizePx is { } size)
                    NoireText.At(size, this, static self => self.DrawPopupContent());
                else
                    DrawPopupContent();

                RecordPopupHeight();
            }
        }

        box.Dispose();
        popup.Dispose();
        DrawBoxArrow(boxRect);

        NoireFocus.On(UiRect.FromBounds(boxRect.Min, boxRect.Max), boxFocused, boxItem, FocusStyle);

        if (comboOpen)
            return changedThisFrame;

        HandleClosedComboInteractions();

        return changedThisFrame;
    }

    private UiPush BeginPopupStyle()
    {
        if (PopupStyle == null)
            return default;

        var theme = NoireTheme.Current;
        var style = PopupStyle;

        var filterBackground = style.FilterBackground ?? theme.Resolve(ThemeColor.SurfaceSunken);
        var scrollbar = style.ScrollbarColor ?? theme.Resolve(ThemeColor.Accent);

        var pushed = UiPush.Color(ImGuiCol.PopupBg, style.Background ?? theme.Resolve(ThemeColor.Surface));

        pushed.Push(ImGuiCol.FrameBg, filterBackground);
        pushed.Push(ImGuiCol.FrameBgHovered, filterBackground);
        pushed.Push(ImGuiCol.FrameBgActive, filterBackground);
        pushed.Push(ImGuiCol.Border, style.BorderColor ?? theme.Resolve(ThemeColor.Border));
        pushed.Push(ImGuiCol.Header, style.SelectedColor ?? ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.Accent), 0.30f));
        pushed.Push(ImGuiCol.HeaderHovered, style.HoveredColor ?? ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.Accent), 0.18f));
        pushed.Push(ImGuiCol.HeaderActive, style.SelectedColor ?? ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.Accent), 0.38f));
        pushed.Push(ImGuiCol.ScrollbarBg, style.ScrollbarBackground ?? ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.SurfaceSunken), 0.5f));
        pushed.Push(ImGuiCol.ScrollbarGrab, scrollbar);
        pushed.Push(ImGuiCol.ScrollbarGrabHovered, scrollbar);
        pushed.Push(ImGuiCol.ScrollbarGrabActive, scrollbar);

        if (style.TextColor is { } text)
            pushed.Push(ImGuiCol.Text, text);

        // ImGui floors a constrained window's size but compares it against an unfloored content size. Fractional padding leaves the popup short.
        var padding = NoireUI.Scaled(style.Padding);

        pushed.Push(ImGuiStyleVar.FrameBorderSize, style.FilterBorderSize);
        pushed.Push(ImGuiStyleVar.WindowPadding, new Vector2(MathF.Round(padding.X), MathF.Round(padding.Y)));

        // The popup flag makes ImGui read the Popup* style fields.
        pushed.Push(ImGuiStyleVar.PopupRounding, NoireUI.Scaled(style.Rounding ?? theme.ResolveRounding()));
        pushed.Push(ImGuiStyleVar.PopupBorderSize, style.BorderSize);
        pushed.Push(ImGuiStyleVar.ScrollbarSize, NoireUI.Scaled(style.ScrollbarWidth));
        pushed.Push(ImGuiStyleVar.ItemSpacing, NoireUI.Scaled(style.RowSpacing));
        pushed.Push(ImGuiStyleVar.FramePadding, NoireUI.Scaled(style.RowPadding));

        return pushed;
    }

    private UiPush PushFilterStyle()
    {
        var style = PopupStyle;

        if (style == null)
            return UiPush.Color(ImGuiCol.Border, ImGui.GetStyle().Colors[(int)ImGuiCol.Border]);

        var pushed = UiPush.Color(ImGuiCol.Border, style.FilterBorderColor ?? style.BorderColor ?? NoireTheme.Current.Resolve(ThemeColor.Border));

        if ((style.FilterTextColor ?? style.TextColor) is { } text)
            pushed.Push(ImGuiCol.Text, text);

        return pushed;
    }

    private UiPush BeginBox(out (Vector2 Min, Vector2 Max) rect)
    {
        rect = default;

        if (BoxStyle == null)
            return default;

        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight());

        rect = (origin, origin + size);
        NoireShapes.Plate(rect.Min, rect.Max, BoxStyle);

        var clear = new Vector4(0f, 0f, 0f, 0f);

        // ImGui draws the border rounded from its own style, around a square plate.
        var pushed = UiPush.Color(ImGuiCol.FrameBg, clear);

        pushed.Push(ImGuiCol.FrameBgHovered, clear);
        pushed.Push(ImGuiCol.FrameBgActive, clear);
        pushed.Push(ImGuiCol.Border, clear);

        return pushed;
    }

    private void DrawBoxArrow((Vector2 Min, Vector2 Max) rect)
    {
        if (BoxStyle == null || ComboFlags.HasFlag(ImGuiComboFlags.NoArrowButton))
            return;

        var color = BoxArrowColor ?? NoireTheme.Current.Resolve(ThemeColor.Accent);
        var height = rect.Max.Y - rect.Min.Y;
        var width = NoireUI.Scaled(BoxArrowSize);
        var centre = new Vector2(rect.Max.X - NoireUI.Scaled(BoxArrowInset) - width, rect.Min.Y + (height * 0.5f));

        Span<Vector2> chevron =
        [
            new(centre.X - width, centre.Y - (width * 0.4f)),
            new(centre.X, centre.Y + (width * 0.5f)),
            new(centre.X + width, centre.Y - (width * 0.4f)),
        ];

        NoireShapes.Stroke(chevron, color, MathF.Max(1f, NoireUI.Scaled(1.5f)), closed: false);
    }

    private void RecordPopupHeight()
    {
        if (!NoireService.IsInitialized())
            return;

        var window = ImGuiP.GetCurrentWindow();

        if (!window.IsNull)
            neededPopupHeight = window.ContentSize.Y + (window.WindowPadding.Y * 2f);
    }

    private void ApplyPopupConstraints()
    {
        if (filteredIndices.Count > Math.Max(1, VisibleItemCount))
        {
            ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, MeasureMaxPopupHeight()));
            return;
        }

        var floor = RoundedUpTo(MeasurePopupHeightFor(filteredIndices.Count), MathF.Ceiling(neededPopupHeight));

        ImGui.SetNextWindowSizeConstraints(new Vector2(0f, floor), new Vector2(float.MaxValue, float.MaxValue));
    }

    private float neededPopupHeight;

    private float MeasureMaxPopupHeight() => MeasurePopupHeightFor(VisibleItemCount);

    private float MeasurePopupHeightFor(int rowCount)
    {
        var style = ImGui.GetStyle();
        var visibleCount = Math.Max(1, Math.Min(rowCount, Math.Max(1, VisibleItemCount)));
        var height = (visibleCount * ResolveRowStep()) - style.ItemSpacing.Y + (style.WindowPadding.Y * 2f);

        if (FilterEnabled)
        {
            // This runs before the popup begins. ImGui's frame height would answer for the host's font.
            var line = PopupStyle?.TextSizePx is { } filterSize
                ? NoireText.CalcSize(" ", filterSize).Y
                : ImGui.GetTextLineHeight();

            height += line + (style.FramePadding.Y * 2f) + style.ItemSpacing.Y;

            if (FilterPinned)
                height += style.ItemSpacing.Y;
        }

        return MathF.Ceiling(height);
    }

    private void DrawPopupContent()
    {
        var appearing = ImGui.IsWindowAppearing();
        if (appearing)
        {
            if (ClearFilterOnOpen && filterMemory == UiMemoryScope.None)
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

            // ImGui draws a frame's border with a window's border colour. Pushed here, it only reaches the field.
            using (PushFilterStyle())
                confirm |= ImGui.InputTextWithHint(UiIds.For("###NoireComboFilter_", Id), FilterHint, ref filterText, 256, ImGuiInputTextFlags.EnterReturnsTrue);

            NoireFocus.OnLast(FocusStyle);

            if (ImGui.IsItemEdited())
            {
                RebuildFilteredIndices();
                SavePersistedFilter();
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
        if (!FilterEnabled || !FilterPinned)
        {
            DrawItemRows();
            return;
        }

        // ImGui floors a child's size while the content keeps its fraction. A pixel short is a scrollbar.
        var visibleCount = Math.Max(1, Math.Min(VisibleItemCount, Math.Max(filteredIndices.Count, 1)));
        var listHeight = MathF.Ceiling((visibleCount * ResolveRowStep()) - ImGui.GetStyle().ItemSpacing.Y);

        // The computed height disagrees with ImGui's measurement by what the layout rounded off.
        if (filteredIndices.Count <= VisibleItemCount)
            listHeight = RoundedUpTo(listHeight, MathF.Ceiling(neededListHeight));

        // NoBackground: the list must not paint ChildBg over the dropdown's own background.
        using var child = ImRaii.Child(UiIds.For("###NoireComboItems_", Id), new Vector2(0f, listHeight), false, ImGuiWindowFlags.NoBackground);
        if (!child)
            return;

        DrawItemRows();
        RecordListHeight();
    }

    private void RecordListHeight()
    {
        if (!NoireService.IsInitialized())
            return;

        var window = ImGuiP.GetCurrentWindow();

        if (!window.IsNull)
            neededListHeight = window.ContentSize.Y + (window.WindowPadding.Y * 2f);
    }

    private float neededListHeight;

    // A measurement off by a row or more describes a list no longer on screen.
    private float RoundedUpTo(float computed, float measured)
        => measured > computed && measured - computed < ResolveRowStep() ? measured : computed;

    private void DrawItemRows()
    {
        if (filteredIndices.Count == 0)
        {
            ImGui.TextDisabled(NoResultsText);
            return;
        }

        var mouseMoved = ImGui.GetIO().MouseDelta != Vector2.Zero;
        showMatches = FilterHighlight && FilterFuzzy && FilterPredicate == null && FilterEnabled && filterText.Length > 0;
        DrawnRowCount = 0;

        if (!IsVirtualizing)
        {
            for (var position = 0; position < filteredIndices.Count; position++)
                DrawItemRow(position, mouseMoved);

            return;
        }

        // Only correct while rows share one height. A taller renderer must set ItemHeight.
        var clipper = new ImGuiListClipper();
        clipper.Begin(filteredIndices.Count, ResolveRowStep());

        // The highlighted row must be drawn even off screen, or scrolling to it never runs.
        if (highlightIndex >= 0 && highlightIndex < filteredIndices.Count)
            clipper.ForceDisplayRangeByIndices(highlightIndex, highlightIndex + 1);

        while (clipper.Step())
        {
            for (var position = clipper.DisplayStart; position < clipper.DisplayEnd; position++)
                DrawItemRow(position, mouseMoved);
        }

        clipper.End();
    }

    private void DrawItemRow(int position, bool mouseMoved)
    {
        var itemIndex = filteredIndices[position];

        if (itemIndex >= items.Count)
            return;

        DrawnRowCount++;

        var item = items[itemIndex];
        var isSelected = itemIndex == selectedIndex;
        var isHighlighted = position == highlightIndex;
        var display = DisplayOf(item);

        // The content is drawn over a label-less selectable, where its label would have been.
        var start = ImGui.GetCursorPos();

        if (ImGui.Selectable(UiIds.For("###NoireComboItem_", Id, itemIndex), isSelected || isHighlighted, ImGuiSelectableFlags.None, new Vector2(0f, ResolveItemHeight())))
            Choose(itemIndex);

        var hovered = ImGui.IsItemHovered();
        var after = ImGui.GetCursorPos();

        ImGui.SetCursorPos(start);

        if (ItemRenderer is { } renderer)
        {
            try
            {
                renderer(new UiComboItemDraw<T>(this, item, itemIndex, display, isSelected, isHighlighted));
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"The item renderer of combo box '{Id}' threw an exception.");
            }
        }
        else
        {
            DrawItemLabel(display);
        }

        ImGui.SetCursorPos(after);

        if (isHighlighted && scrollToHighlight)
        {
            ImGui.SetScrollHereY(0.5f);
            scrollToHighlight = false;
        }

        if (mouseMoved && hovered)
            highlightIndex = position;
    }

    internal void DrawItemLabel(string display)
    {
        if (!showMatches)
        {
            NoireText.Draw(display);
            return;
        }

        Span<int> matched = stackalloc int[FuzzyMatcher.MaxQueryLength];

        if (FuzzyMatcher.TryMatch(display, filterText, matched, out var match))
            NoireText.Highlighted(display, matched[..match.MatchedCount], FilterHighlightColor);
        else
            NoireText.Draw(display);
    }

    internal bool IsVirtualizing => Virtualize ?? filteredIndices.Count >= VirtualizeThreshold;

    private float ResolveItemHeight()
    {
        if (ItemHeight.HasValue)
            return NoireUI.Scaled(ItemHeight.Value);

        // The padding is already pushed as frame padding.
        if (PopupStyle?.TextSizePx is { } size)
            return NoireText.CalcSize(" ", size).Y;

        return NoireText.LineHeight();
    }

    private float ResolveRowStep() => ResolveItemHeight() + ImGui.GetStyle().ItemSpacing.Y;

    private void Choose(int itemIndex)
    {
        SelectFromUi(itemIndex);
        ImGui.CloseCurrentPopup();
    }

    private void HandleClosedComboInteractions()
    {
        if (!WheelCycleEnabled || !ImGui.IsItemHovered())
            return;

        if (!TryResolveWheelCycleBinding(out var binding))
            return;

        var held = binding.IsEmpty || KeybindsHelper.IsBindingHeld(binding);

        if (held && items.Count > 0)
            ImGuiP.SetItemUsingMouseWheel();

        if (WheelCycleHintEnabled)
            NoireTooltip.Show(GetWheelCycleHint(binding), WheelCycleHintStyle, UiIds.For("NoireComboHint_", Id));

        if (items.Count == 0 || !held)
            return;

        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f)
            return;

        var direction = wheel > 0f ? -1 : 1;
        var newIndex = WheelCycleFiltered
            ? ComputeFilteredCycleTarget(selectedIndex, direction)
            : ComputeCycledIndex(selectedIndex, direction, items.Count, WheelCycleLoop);

        if (newIndex >= 0 && newIndex != selectedIndex)
            SelectFromUi(newIndex);
    }

    private int ComputeFilteredCycleTarget(int currentIndex, int direction)
    {
        // A persisted search can outlive the filtered set.
        RebuildFilteredIndices();

        var position = filteredIndices.IndexOf(currentIndex);
        var next = ComputeCycledIndex(position, direction, filteredIndices.Count, WheelCycleLoop);

        return next >= 0 && next < filteredIndices.Count ? filteredIndices[next] : -1;
    }

    #endregion

    #region Internal logic

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

    internal static bool DefaultFilterMatch(string displayText, string filter)
        => displayText.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void RestorePersistedFilter()
    {
        if (filterMemory == UiMemoryScope.None || filterRestored)
            return;

        filterRestored = true;

        if (!TryGetFilterKey(out var key))
            return;

        var found = filterMemory == UiMemoryScope.Session
            ? NoireUiSession.TryGet<string>(key, out var saved)
            : NoireUiState.TryGet(key, out saved);

        if (found && !string.IsNullOrEmpty(saved))
        {
            filterText = saved;
            RebuildFilteredIndices();
        }
    }

    private void SavePersistedFilter()
    {
        if (filterMemory == UiMemoryScope.None || !TryGetFilterKey(out var key))
            return;

        if (filterMemory == UiMemoryScope.Session)
            NoireUiSession.Set(key, filterText);
        else
            NoireUiState.Set(key, filterText);
    }

    private bool TryGetFilterKey(out string key)
    {
        if (filterMemory == UiMemoryScope.Session)
        {
            key = UiIds.Join("ComboBox.", Id, ".filter");
            return true;
        }

        return UiPersistKey.TryBuild("ComboBox", Id, HasGeneratedId, "filter", ref persistRefusalLogged, out key);
    }

    internal bool TryGetFilterKeyForTests(out string key) => TryGetFilterKey(out key);

    internal int CycleFiltered(int direction) => ComputeFilteredCycleTarget(selectedIndex, direction);

    internal void RebuildFilteredIndices()
    {
        filteredIndices.Clear();

        if (!FilterEnabled || string.IsNullOrEmpty(filterText))
        {
            for (var i = 0; i < items.Count; i++)
                filteredIndices.Add(i);

            return;
        }

        if (FilterPredicate != null || !FilterFuzzy)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (MatchesFilter(items[i]))
                    filteredIndices.Add(i);
            }

            return;
        }

        scored.Clear();

        for (var i = 0; i < items.Count; i++)
        {
            var score = FuzzyMatcher.Score(DisplayOf(items[i]), filterText);

            if (score > 0)
                scored.Add((i, score));
        }

        // Stable on ties. The list must not reshuffle between keystrokes.
        scored.Sort(static (left, right) => right.Score != left.Score
            ? right.Score.CompareTo(left.Score)
            : left.Index.CompareTo(right.Index));

        foreach (var entry in scored)
            filteredIndices.Add(entry.Index);
    }

    internal IReadOnlyList<int> FilteredIndices => filteredIndices;

    /// <summary>
    /// The current search text.
    /// </summary>
    public string FilterText
    {
        get => filterText;
        set
        {
            filterText = value ?? string.Empty;
            RebuildFilteredIndices();
        }
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

    private NoireContent GetWheelCycleHint(HotkeyBinding binding)
    {
        if (WheelCycleHintContent != null)
            return WheelCycleHintContent;

        if (!hasCachedHint || cachedHintContent == null || cachedHintBinding != binding)
        {
            cachedHintBinding = binding;
            hasCachedHint = true;
            // The icon font is out of reach of the consumer's styling. Keycaps are drawn from the theme.
            cachedHintContent = new NoireContent();

            if (binding.IsEmpty)
            {
                cachedHintContent.AddText("Scroll to cycle");
                return cachedHintContent;
            }

            // One cap per key. "Ctrl + G" in a single tile reads as one key.
            var keys = KeybindsHelper.FormatBinding(binding).Split(" + ", StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < keys.Length; i++)
            {
                if (i > 0)
                    cachedHintContent.AddText(" + ");

                cachedHintContent.AddKeyCap(keys[i]);
            }

            cachedHintContent.AddText(" + Scroll");
        }

        return cachedHintContent;
    }

    #endregion
}
