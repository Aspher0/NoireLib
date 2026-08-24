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

    /// <summary>
    /// Whether this combo's id was generated rather than given.
    /// </summary>
    public bool HasGeneratedId { get; }

    /// <summary>
    /// The unique identifier of this combo box, used for the ImGui ids.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The label displayed next to the combo box. When <see langword="null"/> or empty, no label is displayed.
    /// </summary>
    public string? Label { get; set; } = null;

    /// <summary>
    /// The width of the combo box, in real pixels, not scaled.
    /// </summary>
    public float? Width { get; set; } = null;

    /// <summary>
    /// Extra ImGui combo flags applied to the widget (e.g. <see cref="ImGuiComboFlags.NoArrowButton"/>).
    /// </summary>
    public ImGuiComboFlags ComboFlags { get; set; } = ImGuiComboFlags.None;

    /// <summary>
    /// Paints the closed combo as a <see cref="NoireShapes.Plate"/> instead of as an ImGui frame.
    /// </summary>
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
    /// Whether the filter text is cleared every time the dropdown opens.
    /// </summary>
    /// <remarks>Setting <see cref="FilterMemory"/> to anything but <see cref="UiMemoryScope.None"/> turns this off.</remarks>
    public bool ClearFilterOnOpen { get; set; } = true;

    /// <summary>
    /// How long the search text is remembered.
    /// </summary>
    /// <remarks><see cref="UiMemoryScope.Persisted"/> needs a stable id.</remarks>
    public UiMemoryScope FilterMemory
    {
        get => filterMemory;
        set
        {
            if (filterMemory == value)
                return;

            filterMemory = value;

            // Restored on the next draw rather than here, because this is usually set in a constructor, before the
            // state file has been read and possibly before there is a plugin interface to read it with.
            if (value != UiMemoryScope.None)
                filterRestored = false;
        }
    }

    /// <summary>
    /// Whether the closed-combo wheel shortcut cycles only what the current search matches.
    /// </summary>
    public bool WheelCycleFiltered { get; set; }

    /// <summary>
    /// Whether the filter matches fuzzily and orders the options by how well they matched.
    /// </summary>
    /// <remarks>A <see cref="FilterPredicate"/> of your own overrides this.</remarks>
    public bool FilterFuzzy { get; set; } = true;

    /// <summary>
    /// Whether the characters the filter matched are picked out in the option list.
    /// </summary>
    /// <remarks>Only applies while <see cref="FilterFuzzy"/> is on and something has been typed.</remarks>
    public bool FilterHighlight { get; set; } = true;

    /// <summary>
    /// The color the matched characters are drawn in. When <see langword="null"/>, the theme's accent is used.
    /// </summary>
    public Vector4? FilterHighlightColor { get; set; }

    /// <summary>Draws each option yourself.</summary>
    /// <remarks>Set <see cref="ItemHeight"/> alongside this when the rows are taller than one line.</remarks>
    public Action<UiComboItemDraw<T>>? ItemRenderer { get; set; }

    /// <summary>
    /// The height of one option at 100%. When <see langword="null"/>, one line of text.
    /// </summary>
    /// <remarks>A row that does not match this value scrolls out of step with the list.</remarks>
    public float? ItemHeight { get; set; }

    /// <summary>
    /// Whether the option list is drawn through a clipper; when <see langword="null"/>, the default, it turns itself
    /// on past <see cref="VirtualizeThreshold"/> options.
    /// </summary>
    /// <remarks>Every row must be the same height.</remarks>
    public bool? Virtualize { get; set; }

    /// <summary>
    /// How many options it takes before <see cref="Virtualize"/> turns itself on.
    /// </summary>
    public int VirtualizeThreshold { get; set; } = 100;

    /// <summary>
    /// How many option rows were actually drawn the last time the dropdown was open.
    /// </summary>
    public int DrawnRowCount { get; private set; }

    /// <summary>
    /// How an item is matched against the filter text. When <see langword="null"/>, <see cref="FilterFuzzy"/> decides.
    /// </summary>
    /// <remarks>A predicate has no score to order by: the options keep the order they were given.</remarks>
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
    /// An optional binding that must be held for the closed-combo wheel cycling to trigger; a plain
    /// <see cref="VirtualKey"/> converts implicitly.
    /// </summary>
    /// <remarks>
    /// Ignored while a hotkey is attached through <see cref="BindWheelCycleHotkey"/>; read
    /// <see cref="ResolvedWheelCycleBinding"/> for the binding actually in effect.
    /// </remarks>
    public HotkeyBinding WheelCycleBinding { get; set; } = default;

    /// <summary>
    /// Whether the closed-combo wheel cycling wraps around when reaching the first/last item, instead of stopping at
    /// the boundaries.
    /// </summary>
    public bool WheelCycleLoop { get; set; } = false;

    /// <summary>
    /// Whether a hint tooltip advertising the wheel shortcut is shown when hovering the closed combo.
    /// </summary>
    public bool WheelCycleHintEnabled { get; set; } = true;

    /// <summary>
    /// A custom content for the wheel shortcut hint tooltip; when <see langword="null"/>, a default hint is generated
    /// from <see cref="ResolvedWheelCycleBinding"/>.
    /// </summary>
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

    // False when an attached hotkey cannot be honored (disabled, or unregistered since attaching), so the cycling
    // stays off rather than becoming unconditional.
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
    /// Drives the closed-combo wheel cycling from a hotkey registered on a <see cref="NoireHotkeyManager"/>.
    /// </summary>
    /// <remarks>Does not enable the cycling on its own; set <see cref="WheelCycleEnabled"/> as well.</remarks>
    /// <param name="hotkeyManager">The hotkey manager owning the hotkey.</param>
    /// <param name="hotkeyId">The id of the hotkey whose binding gates the cycling.</param>
    /// <returns>This <see cref="NoireComboBox{T}"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hotkeyManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="hotkeyId"/> is null, empty or blank.</exception>
    public NoireComboBox<T> BindWheelCycleHotkey(NoireHotkeyManager hotkeyManager, string hotkeyId)
    {
        ArgumentNullException.ThrowIfNull(hotkeyManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(hotkeyId);

        wheelCycleHotkeyManager = hotkeyManager;
        wheelCycleHotkeyId = hotkeyId;
        return this;
    }

    /// <summary>
    /// Detaches the hotkey attached through <see cref="BindWheelCycleHotkey"/>, so the cycling falls back to
    /// <see cref="WheelCycleBinding"/>.
    /// </summary>
    /// <returns>This <see cref="NoireComboBox{T}"/> instance, for chaining.</returns>
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
    /// The index of the selected item, or -1 when nothing is selected.
    /// </summary>
    /// <remarks>Setting this property does not invoke <see cref="OnSelectionChanged"/>; use <see cref="Select(int)"/> for that.</remarks>
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
    /// Draws the combo box.
    /// </summary>
    /// <remarks>Call this every frame inside your window.</remarks>
    /// <returns>True if the selection changed this frame, false otherwise.</returns>
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

        // Capped to exactly what it is meant to show: left alone, ImGui budgets the popup at eight options and knows
        // nothing about the filter input, so the filter would push the list past the budget and grow a second,
        // nested scrollbar.
        // The dropdown's style is pushed before its height is measured, not after: the measurement reads the window
        // padding and item spacing in force, and pushing afterwards would budget the popup against the host's own
        // settings instead, coming out too short and growing that same second scrollbar.
        var popup = BeginPopupStyle();

        ApplyPopupConstraints();

        var box = BeginBox(out var boxRect);

        // Read here rather than after the popup, because the last item by then is whatever the dropdown's contents
        // submitted last. The mark itself is painted further down, once the box and its arrow are drawn.
        var boxFocused = NoireFocus.IsLastFocused();
        var boxItem = boxFocused ? ImGuiP.GetItemID() : 0u;

        // Read out here, because inside the popup the current window is the popup. A dropdown opened from a window that
        // is holding itself in front has to be held there too, or it opens underneath the combo it belongs to.
        var ownerInFront = UiWindowOrder.InTopLayer;

        var comboOpen = false;
        using (var combo = ImRaii.Combo(label, preview, ComboFlags | (BoxStyle != null ? ImGuiComboFlags.NoArrowButton : ImGuiComboFlags.None)))
        {
            // Released as soon as the box has been drawn, so the popup and everything drawn inside it is styled
            // normally rather than inheriting a transparent frame that exists only to uncover the plate.
            box.Dispose();

            if (combo)
            {
                comboOpen = true;

                if (ownerInFront)
                    UiWindowOrder.KeepInFront();

                // The size has to be pushed inside the popup rather than around it: a font handle pushed before Begin
                // is not what the popup window draws with.
                if (PopupStyle?.TextSizePx is { } size)
                    NoireText.At(size, this, static self => self.DrawPopupContent());
                else
                    DrawPopupContent();

                RecordPopupHeight();
            }
        }

        // A second call on a scope already released inside the combo pops nothing, so the plated and unplated paths
        // both land here rather than one of them needing to remember it has already let go.
        box.Dispose();
        popup.Dispose();
        DrawBoxArrow(boxRect);

        NoireFocus.On(UiRect.FromBounds(boxRect.Min, boxRect.Max), boxFocused, boxItem, FocusStyle);

        // While the dropdown is open the popup is a separate window that owns the wheel itself, so the cycling stands down.
        if (comboOpen)
            return changedThisFrame;

        HandleClosedComboInteractions();

        return changedThisFrame;
    }

    // Returns an empty scope when the dropdown is left as ImGui's.
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

        // Snapped to whole pixels: ImGui floors a window's size whenever a size constraint is present, and a combo
        // popup always has one, but compares that floored size against an unfloored ContentSize + WindowPadding * 2.
        // The two differ by exactly the padding's fraction, so at a non-whole UI scale a padding of 6 becomes 7.5
        // and the popup ends up half a pixel short of its own contents.
        var padding = NoireUI.Scaled(style.Padding);

        pushed.Push(ImGuiStyleVar.FrameBorderSize, style.FilterBorderSize);
        pushed.Push(ImGuiStyleVar.WindowPadding, new Vector2(MathF.Round(padding.X), MathF.Round(padding.Y)));

        // The popup fields, not the window ones: ImGui picks the style field by window flag and this window carries
        // the popup flag, so pushing WindowRounding and WindowBorderSize here was silent.
        pushed.Push(ImGuiStyleVar.PopupRounding, NoireUI.Scaled(style.Rounding ?? theme.ResolveRounding()));
        pushed.Push(ImGuiStyleVar.PopupBorderSize, style.BorderSize);
        pushed.Push(ImGuiStyleVar.ScrollbarSize, NoireUI.Scaled(style.ScrollbarWidth));
        pushed.Push(ImGuiStyleVar.ItemSpacing, NoireUI.Scaled(style.RowSpacing));
        pushed.Push(ImGuiStyleVar.FramePadding, NoireUI.Scaled(style.RowPadding));

        return pushed;
    }

    // Always pushes at least a border, since the box is drawn either way.
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

    // The pushed colours are released once the box has been drawn; an empty scope when there is no plate.
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

        // The border goes with the background. ImGui draws it rounded from its own style, so leaving it lit puts a
        // rounded outline around a square plate, which is the one part of the old frame that would still show.
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

    // Never shorter than its own contents, and never taller than VisibleItemCount options once there are more of them.
    private void ApplyPopupConstraints()
    {
        // A list longer than the dropdown is capped and scrolls. Only a list that fits gets the floor it needs to
        // stop scrolling.
        if (filteredIndices.Count > Math.Max(1, VisibleItemCount))
        {
            ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, MeasureMaxPopupHeight()));
            return;
        }

        ImGui.SetNextWindowSizeConstraints(
            new Vector2(0f, MathF.Ceiling(neededPopupHeight)),
            new Vector2(float.MaxValue, float.MaxValue));
    }

    // The height the dropdown reported needing last time it was drawn.
    private float neededPopupHeight;

    // The height the dropdown is capped at: the filter row, when shown, plus exactly VisibleItemCount options.
    private float MeasureMaxPopupHeight()
    {
        var style = ImGui.GetStyle();
        var visibleCount = Math.Max(1, VisibleItemCount);
        var height = (visibleCount * ResolveRowStep()) - style.ItemSpacing.Y + (style.WindowPadding.Y * 2f);

        if (FilterEnabled)
        {
            // Measured at the dropdown's own text size, not whatever is in force out here: this runs before the
            // popup begins, so ImGui's frame height would otherwise answer for the host's font. A dropdown with
            // larger text than its host would be budgeted a filter row shorter than the one it draws, overflowing
            // the cap and growing a scrollbar around a list that fits.
            var line = PopupStyle?.TextSizePx is { } filterSize
                ? NoireText.CalcSize(" ", filterSize).Y
                : ImGui.GetTextLineHeight();

            height += line + (style.FramePadding.Y * 2f) + style.ItemSpacing.Y;

            // With the filter pinned, the options live in a fixed-height child sized to this same cap: a budget
            // equal to its own content means a rounding hair of difference becomes a stray scrollbar.
            if (FilterPinned)
                height += style.ItemSpacing.Y;
        }

        // Rounded up for the reason the option list is: a cap carrying a fraction is a cap the content cannot fit in.
        return MathF.Ceiling(height);
    }

    private void DrawPopupContent()
    {
        var appearing = ImGui.IsWindowAppearing();
        if (appearing)
        {
            // A search restored from disk and then cleared on the first opening would have been restored for nothing,
            // so persisting it implies keeping it.
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

            // The filter box's own border and text, pushed here rather than with the rest of the dropdown's style:
            // ImGui draws a frame's border from the same colour as a window's, so the two cannot be set apart from
            // outside the popup without the window's border following the field's.
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

    // A scrolling child of its own when the filter is pinned above it, straight into the dropdown otherwise. Exactly
    // one of the two scrolls in either case, keeping the dropdown to a single scrollbar.
    private void DrawItemList()
    {
        if (!FilterEnabled || !FilterPinned)
        {
            DrawItemRows();
            return;
        }

        // Sized to the options it holds rather than the space left over, so a short list shrinks the dropdown instead
        // of leaving it padded with dead space.
        // Rounded up, because ImGui floors a child's size while the content keeps its fraction: a row step landing
        // off a whole pixel leaves the region a pixel short, and a pixel short is a scrollbar on a list that fits.
        var visibleCount = Math.Max(1, Math.Min(VisibleItemCount, Math.Max(filteredIndices.Count, 1)));
        var listHeight = MathF.Ceiling((visibleCount * ResolveRowStep()) - ImGui.GetStyle().ItemSpacing.Y);

        // Taken up to what the list reported needing last time, for the same reason as the dropdown itself: a height
        // worked out from the row step disagrees with what ImGui measured by whatever the layout rounded off; the
        // measurement cannot.
        if (filteredIndices.Count <= VisibleItemCount)
            listHeight = MathF.Max(listHeight, MathF.Ceiling(neededListHeight));

        // NoBackground: the dropdown's own background is the backdrop here, and the list must not paint a second panel of
        // its own over it out of whatever ImGuiCol.ChildBg the consumer happens to have pushed around the combo.
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

    // The height the option list reported needing last time it was drawn.
    private float neededListHeight;

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

        // A clipper is told the row height rather than left to measure it, which would cost a pass over every row
        // anyway. Only correct while rows are a uniform height. A taller renderer must declare ItemHeight.
        var clipper = new ImGuiListClipper();
        clipper.Begin(filteredIndices.Count, ResolveRowStep());

        // Without this the row the arrow keys are on is simply not drawn once it scrolls out of view, so the call
        // that scrolls the list to it never runs and keyboard navigation stops at the edge of the visible range.
        if (highlightIndex >= 0 && highlightIndex < filteredIndices.Count)
            clipper.ForceDisplayRangeByIndices(highlightIndex, highlightIndex + 1);

        while (clipper.Step())
        {
            for (var position = clipper.DisplayStart; position < clipper.DisplayEnd; position++)
                DrawItemRow(position, mouseMoved);
        }

        clipper.End();
    }

    // Hovering only takes the highlight when the mouse moved this frame.
    private void DrawItemRow(int position, bool mouseMoved)
    {
        var itemIndex = filteredIndices[position];

        if (itemIndex >= items.Count)
            return; // The items changed while the dropdown was open.

        DrawnRowCount++;

        var item = items[itemIndex];
        var isSelected = itemIndex == selectedIndex;
        var isHighlighted = position == highlightIndex;
        var display = DisplayOf(item);

        // The selectable carries no label of its own; the content is drawn over it instead, since this needs the
        // theme's type scale, filter highlighting and possibly a renderer's icons, not one colour and one font. The
        // content lands where the label would have, since a selectable renders its own at the given cursor.
        var start = ImGui.GetCursorPos();

        if (ImGui.Selectable(UiIds.For("###NoireComboItem_", Id, itemIndex), isSelected || isHighlighted, ImGuiSelectableFlags.None, new Vector2(0f, ResolveItemHeight())))
            Choose(itemIndex);

        // Read before anything is drawn on top, so the hover stays the row rather than the last piece of text in it.
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

    // Called by UiComboItemDraw.DrawLabel.
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

    // Every row must share this height for virtualization to place them correctly.
    private float ResolveItemHeight()
    {
        if (ItemHeight.HasValue)
            return NoireUI.Scaled(ItemHeight.Value);

        // The dropdown's text need not be the size in force outside it, and measuring at the outer size lays the rows
        // out for a font they are not drawn in. The padding is not added on top: it is already pushed as ImGui's
        // frame padding, and counting it twice leaves each row a line of empty space taller than its text.
        if (PopupStyle?.TextSizePx is { } size)
            return NoireText.CalcSize(" ", size).Y;

        return NoireText.LineHeight();
    }

    // The vertical distance from one option to the next: the option itself plus the spacing after it.
    private float ResolveRowStep() => ResolveItemHeight() + ImGui.GetStyle().ItemSpacing.Y;

    private void Choose(int itemIndex)
    {
        SelectFromUi(itemIndex);
        ImGui.CloseCurrentPopup();
    }

    // Includes the wheel claim that stops a cycling scroll from also moving the window.
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

    // Direction is +1 for the next match, -1 for the previous one; returns -1 when there is nothing to move to.
    private int ComputeFilteredCycleTarget(int currentIndex, int direction)
    {
        // The filtered set is rebuilt when the dropdown opens or the search changes, and the search can outlive both
        // when it is persisted, so it is refreshed here rather than assumed current.
        RebuildFilteredIndices();

        var position = filteredIndices.IndexOf(currentIndex);
        var next = ComputeCycledIndex(position, direction, filteredIndices.Count, WheelCycleLoop);

        return next >= 0 && next < filteredIndices.Count ? filteredIndices[next] : -1;
    }

    #endregion

    #region Internal logic

    // Direction is +1 for the next item, -1 for the previous one; returns -1 when there are no items.
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

    // Applies the remembered search once, the first time the combo draws after FilterMemory is set.
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

        // Ties keep the order the items were given in, so a list does not reshuffle itself between keystrokes that
        // happen to score the same.
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

    // Rebuilt only when the binding it describes changed, since an attached hotkey can be rebound at any time.
    private NoireContent GetWheelCycleHint(HotkeyBinding binding)
    {
        if (WheelCycleHintContent != null)
            return WheelCycleHintContent;

        if (!hasCachedHint || cachedHintContent == null || cachedHintBinding != binding)
        {
            cachedHintBinding = binding;
            hasCachedHint = true;
            // Keycaps and text rather than mouse and arrow glyphs: the icon font is the one part of the hint a
            // consumer's own font and styling cannot reach, and a keycap is drawn from the theme instead.
            cachedHintContent = new NoireContent();

            if (binding.IsEmpty)
            {
                cachedHintContent.AddText("Scroll to cycle");
                return cachedHintContent;
            }

            // One cap per key rather than one around the whole shortcut: "Ctrl + G" in a single tile reads as a key
            // called "Ctrl + G".
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
