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
    /// <param name="id">An optional unique identifier used for the ImGui ids. When <see langword="null"/>, a random one is generated.</param>
    /// <param name="items">The initial items of the combo box.</param>
    /// <param name="displayFunc">How an item is converted to its display text. Defaults to <c>ToString()</c>.</param>
    public NoireComboBox(string? id = null, IEnumerable<T>? items = null, Func<T, string>? displayFunc = null)
    {
        HasGeneratedId = string.IsNullOrWhiteSpace(id);
        Id = HasGeneratedId ? RandomGenerator.GenerateGuidString() : id!;
        DisplayFunc = displayFunc;

        if (items != null)
            this.items.AddRange(items);
    }

    /// <summary>
    /// Whether this combo's id was generated rather than given. A generated id is a new GUID every session, so
    /// <see cref="FilterMemory"/> refuses to persist against one; session-scoped memory is unaffected.
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
    /// The width of the combo box. When <see langword="null"/>, the default ImGui item width is used. In real
    /// pixels, not scaled.
    /// </summary>
    public float? Width { get; set; } = null;

    /// <summary>
    /// Extra ImGui combo flags applied to the widget (e.g. <see cref="ImGuiComboFlags.NoArrowButton"/>).
    /// </summary>
    public ImGuiComboFlags ComboFlags { get; set; } = ImGuiComboFlags.None;

    /// <summary>
    /// Paints the closed combo as a <see cref="NoireShapes.Plate"/> instead of as an ImGui frame.
    /// </summary>
    /// <remarks>
    /// The plate is drawn first and ImGui's own frame is pushed transparent over it, so the preview text, the hit
    /// box, the popup and the keyboard all keep working. The arrow becomes ours too; set it with
    /// <see cref="BoxArrowColor"/>, or use <see cref="ImGuiComboFlags.NoArrowButton"/> for none. When
    /// <see langword="null"/>, the combo is an ordinary ImGui one.
    /// </remarks>
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
    /// <remarks>
    /// Every value falls back to the theme, so setting one thing does not mean setting all of them. Applied as ImGui
    /// style pushes around the popup, so the filter box, the scrollbar and the rows all follow it automatically.
    /// </remarks>
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
    /// Whether the filter input stays pinned above the options while they scroll (the default), or scrolls away with them.<br/>
    /// Either way the dropdown shows a single scrollbar: when pinned, only the option list scrolls; when not, the whole
    /// dropdown does.
    /// </summary>
    public bool FilterPinned { get; set; } = true;

    /// <summary>
    /// Whether the filter text is cleared every time the dropdown opens. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Turn it off to keep the search between openings of one live combo. <see cref="FilterMemory"/> is the stronger
    /// form, surviving the widget itself, and setting it to anything but None turns this off on its own.
    /// </remarks>
    public bool ClearFilterOnOpen { get; set; } = true;

    /// <summary>
    /// How long the search text is remembered. Defaults to <see cref="UiMemoryScope.None"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="UiMemoryScope.Session"/> keeps it until the plugin reloads; <see cref="UiMemoryScope.Persisted"/>
    /// keeps it across reloads and needs a stable id. Anything other than <see cref="UiMemoryScope.None"/> implies
    /// <see cref="ClearFilterOnOpen"/> being off.
    /// </remarks>
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
    /// <remarks>
    /// With no search text this changes nothing, because everything matches. When the current selection is not
    /// itself a match, cycling enters the matches at one end rather than refusing.
    /// </remarks>
    public bool WheelCycleFiltered { get; set; }

    /// <summary>
    /// Whether the filter matches fuzzily and orders the options by how well they matched. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Fuzzy means the typed characters need only appear in order, and the best match is listed first. See
    /// <see cref="FuzzyMatcher"/>. Turn it off for a plain case-insensitive "contains" match that leaves the options
    /// in the order they were given. A <see cref="FilterPredicate"/> of your own overrides both.
    /// </remarks>
    public bool FilterFuzzy { get; set; } = true;

    /// <summary>
    /// Whether the characters the filter matched are picked out in the option list. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>Only applies while <see cref="FilterFuzzy"/> is on and something has been typed.</remarks>
    public bool FilterHighlight { get; set; } = true;

    /// <summary>
    /// The color the matched characters are drawn in. When <see langword="null"/>, the theme's accent is used.
    /// </summary>
    public Vector4? FilterHighlightColor { get; set; }

    /// <summary>Draws each option yourself.</summary>
    /// <remarks>
    /// The combo keeps the row and everything about it that is not paint: its size, its hit testing, its selection
    /// and keyboard state, its filtering and its scrolling. Call <see cref="UiComboItemDraw{T}.DrawLabel"/> for the
    /// ordinary text, filter highlighting included, rather than reimplementing it. Set <see cref="ItemHeight"/>
    /// alongside this when the rows are taller than one line, or virtualization will place them wrongly. An
    /// exception thrown here is caught and logged once rather than taking the frame down.
    /// </remarks>
    public Action<UiComboItemDraw<T>>? ItemRenderer { get; set; }

    /// <summary>
    /// The height of one option at 100%. When <see langword="null"/>, one line of text.
    /// </summary>
    /// <remarks>
    /// Only worth setting alongside an <see cref="ItemRenderer"/> that draws taller rows. Virtualization positions
    /// rows arithmetically rather than by measuring them, so a row that does not match this value scrolls out of step
    /// with the list.
    /// </remarks>
    public float? ItemHeight { get; set; }

    /// <summary>
    /// Whether the option list is drawn through a clipper, so only the visible rows cost anything.<br/>
    /// When <see langword="null"/>, the default, it turns itself on past <see cref="VirtualizeThreshold"/> options.
    /// </summary>
    /// <remarks>
    /// A dropdown over every item in the game is forty thousand rows; virtualizing draws only what is on screen.
    /// Every row must be the same height, which is automatic for ordinary text options; an
    /// <see cref="ItemRenderer"/> drawing taller rows must declare <see cref="ItemHeight"/>, or turn this off if
    /// rows genuinely vary.
    /// </remarks>
    public bool? Virtualize { get; set; }

    /// <summary>
    /// How many options it takes before <see cref="Virtualize"/> turns itself on. Defaults to 100.
    /// </summary>
    public int VirtualizeThreshold { get; set; } = 100;

    /// <summary>
    /// How many option rows were actually drawn the last time the dropdown was open.
    /// </summary>
    /// <remarks>
    /// What virtualization removes is the per-row work done before ImGui decides an item is off screen: a display
    /// string, a fuzzy match and a font push.
    /// </remarks>
    public int DrawnRowCount { get; private set; }

    /// <summary>
    /// How an item is matched against the filter text. When <see langword="null"/>, <see cref="FilterFuzzy"/> decides.
    /// </summary>
    /// <remarks>
    /// Setting this takes the decision over completely, including from <see cref="FilterFuzzy"/>: a predicate answers
    /// yes or no and has no score to order by, so the options keep the order they were given and nothing is
    /// highlighted.
    /// </remarks>
    public Func<T, string, bool>? FilterPredicate { get; set; } = null;

    /// <summary>
    /// The text displayed in the dropdown when no item matches the filter.
    /// </summary>
    public string NoResultsText { get; set; } = "No results";

    #endregion

    #region Dropdown options

    /// <summary>
    /// The maximum number of options visible in the dropdown before it scrolls. Defaults to 8.<br/>
    /// The dropdown is sized to hold exactly this many options (plus the filter input when there is one), so it never
    /// grows a second scrollbar of its own around the option list.
    /// </summary>
    public int VisibleItemCount { get; set; } = 8;

    /// <summary>
    /// Whether cycling the highlighted option with the arrow keys wraps around when reaching the first/last option. Defaults to <see langword="false"/>.
    /// </summary>
    public bool DropdownCycleLoop { get; set; } = false;

    #endregion

    #region Closed combo wheel cycling options

    /// <summary>
    /// Whether scrolling the mouse wheel over the closed combo cycles the selection. Defaults to <see langword="false"/>.<br/>
    /// See also <see cref="WheelCycleBinding"/> and <see cref="WheelCycleLoop"/>.
    /// </summary>
    public bool WheelCycleEnabled { get; set; } = false;

    /// <summary>
    /// An optional binding that must be held for the closed-combo wheel cycling to trigger. A plain
    /// <see cref="VirtualKey"/> converts implicitly; the full <see cref="HotkeyBinding"/> surface covers a modifier
    /// combination, a key plus modifiers, or a gamepad button. Defaults to an empty binding, meaning no key is
    /// required. Matched with the same rules as a <see cref="NoireHotkeyManager"/> hotkey (see
    /// <see cref="KeybindsHelper.IsBindingHeld"/>). Ignored while a hotkey is attached through
    /// <see cref="BindWheelCycleHotkey"/>; read <see cref="ResolvedWheelCycleBinding"/> for the binding actually in
    /// effect.
    /// </summary>
    public HotkeyBinding WheelCycleBinding { get; set; } = default;

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
    /// A custom content for the wheel shortcut hint tooltip. When <see langword="null"/>, a default hint is generated from
    /// <see cref="ResolvedWheelCycleBinding"/> (e.g. "Ctrl + &lt;mouse icon&gt; to cycle"), and it follows a rebinding automatically.
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
    /// <remarks>
    /// The per-widget override. A style whose <see cref="FocusStyle.Shape"/> is <see cref="FocusShape.None"/> leaves
    /// this combo unmarked while the rest of the interface keeps its mark.
    /// </remarks>
    public FocusStyle? FocusStyle { get; set; }

    /// <summary>
    /// The binding the closed-combo wheel cycling currently requires: the live binding of the hotkey attached
    /// through <see cref="BindWheelCycleHotkey"/> when there is one, otherwise <see cref="WheelCycleBinding"/>. An
    /// attached hotkey is read on every access rather than copied, so a rebinding through
    /// <see cref="NoireHotkeyManager.DrawKeybindInputButton"/> (or any other path) applies immediately. An attached
    /// hotkey that is disabled or no longer registered resolves to an empty binding, which disables the cycling
    /// rather than making it unconditional.
    /// </summary>
    public HotkeyBinding ResolvedWheelCycleBinding
    {
        get
        {
            TryResolveWheelCycleBinding(out var binding);
            return binding;
        }
    }

    /// <summary>
    /// Resolves the binding that currently gates the cycling, and reports whether the cycling may run at all.
    /// </summary>
    /// <param name="binding">The resolved binding, or an empty binding when there is none to honor.</param>
    /// <returns>
    /// False when an attached hotkey cannot be honored (disabled, or unregistered since attaching), so the cycling
    /// stays off rather than becoming unconditional.
    /// </returns>
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
    /// Drives the closed-combo wheel cycling from a hotkey registered on a <see cref="NoireHotkeyManager"/>, so the
    /// user can rebind the shortcut through the manager and the combo (and its hint tooltip) follow automatically.
    /// The hotkey's binding is only ever read, never triggered: the manager keeps owning it, and it stays usable as
    /// a regular hotkey at the same time. This is a convenience over <see cref="WheelCycleBinding"/>, which stays
    /// available. Does not enable the cycling on its own; set <see cref="WheelCycleEnabled"/> as well.
    /// <see cref="NoireHotkeyManager.RegisterHotkey"/> requires every hotkey to carry a callback, so one registered
    /// only to gate a combo takes an empty one: the combo reads the binding, not the trigger.
    /// </summary>
    /// <example>
    /// <code>
    /// hotkeyManager.RegisterHotkey(new HotkeyEntry("combo.cycle", "Cycle combo", VirtualKey.CONTROL, () => { }, true, HotkeyActivationMode.Pressed));
    /// combo.WheelCycleEnabled = true;
    /// combo.BindWheelCycleHotkey(hotkeyManager, "combo.cycle");
    ///
    /// // Anywhere in your settings, let the user rebind it. The combo and its hint tooltip follow automatically:
    /// hotkeyManager.DrawKeybindInputButton("combo.cycle");
    /// </code>
    /// </example>
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
    /// Detaches the hotkey attached through <see cref="BindWheelCycleHotkey"/>, so the cycling falls back to <see cref="WheelCycleBinding"/>.<br/>
    /// Safe to call when no hotkey is attached.
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

    /// <summary>
    /// Pushes the dropdown's own look, so the popup ImGui opens is drawn to this combo's style.
    /// </summary>
    /// <remarks>
    /// Pushed before the combo rather than inside the popup, because ImGui reads these when the popup window is begun
    /// and that happens inside <c>BeginCombo</c>. Held for the whole call and released after the popup has closed.
    /// </remarks>
    /// <returns>The pushed style, or an empty scope when the dropdown is left as ImGui's.</returns>
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

    /// <summary>
    /// Pushes the filter box's own border and text colours, for the moment it is drawn.
    /// </summary>
    /// <returns>The pushed colours. Always at least a border, since the box is drawn either way.</returns>
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

    /// <summary>
    /// Paints the plate under the closed combo and uncovers it, when the combo is drawn as one.
    /// </summary>
    /// <remarks>
    /// The rectangle is worked out before the combo is submitted rather than read back off it afterwards, because a
    /// plate drawn after the combo would cover its own preview text. <see cref="ImGui.CalcItemWidth"/> is what ImGui
    /// itself is about to use, including a width set by <see cref="Width"/>, so the two cannot disagree.
    /// </remarks>
    /// <param name="rect">Where the box was drawn, for the arrow to line up with.</param>
    /// <returns>The pushed colours, to release once the box has been drawn, or an empty scope when there is no plate.</returns>
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

    /// <summary>
    /// Draws the chevron on a plated combo, in its own colour.
    /// </summary>
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


    /// <summary>
    /// The height the dropdown is capped at: the filter row, when shown, plus exactly <see cref="VisibleItemCount"/>
    /// options. Mirrors how ImGui itself sizes a popup from an option count.
    /// </summary>
    /// <summary>
    /// Records the height the dropdown actually needs, taken from ImGui rather than worked out.
    /// </summary>
    /// <remarks>
    /// The exact quantity ImGui compares against the window's height to decide the scrollbar, so asking for it back
    /// as a minimum next frame cannot disagree with it. <c>ContentSize</c> is what <c>Begin</c> derived from the
    /// previous frame, which is the same value that test uses.
    /// </remarks>
    private void RecordPopupHeight()
    {
        if (!NoireService.IsInitialized())
            return;

        var window = ImGuiP.GetCurrentWindow();

        if (!window.IsNull)
            neededPopupHeight = window.ContentSize.Y + (window.WindowPadding.Y * 2f);
    }

    /// <summary>
    /// Constrains the dropdown: never shorter than its own contents, and never taller than
    /// <see cref="VisibleItemCount"/> options once there are more of them than that.
    /// </summary>
    /// <remarks>
    /// The minimum is a measurement rather than a prediction, on purpose: ImGui decides the scrollbar with
    /// <c>ContentSize + WindowPadding * 2 &gt; SizeFull.y</c> and floors <c>SizeFull</c> whenever a size constraint
    /// is present, so a height worked out in advance would be compared against a rounded-down value and any
    /// fraction in the layout becomes a scrollbar. Asking for the measured height back, rounded up, forces the
    /// comparison the other way.
    /// </remarks>
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

    /// <summary>The height the dropdown reported needing last time it was drawn.</summary>
    private float neededPopupHeight;

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

    /// <summary>
    /// Draws the option list, in a scrolling child of its own when the filter is pinned above it, or straight into
    /// the dropdown when the filter is free to scroll away with the options. Exactly one of the two scrolls in
    /// either case, keeping the dropdown to a single scrollbar.
    /// </summary>
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

    /// <summary>
    /// Records the height the option list actually needs, taken from ImGui rather than worked out.
    /// </summary>
    private void RecordListHeight()
    {
        if (!NoireService.IsInitialized())
            return;

        var window = ImGuiP.GetCurrentWindow();

        if (!window.IsNull)
            neededListHeight = window.ContentSize.Y + (window.WindowPadding.Y * 2f);
    }

    /// <summary>The height the option list reported needing last time it was drawn.</summary>
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

    /// <summary>
    /// Draws one option of the filtered list.
    /// </summary>
    /// <param name="position">The option's position in the filtered list.</param>
    /// <param name="mouseMoved">Whether the mouse moved this frame, so hovering only takes the highlight when it did.</param>
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

    /// <summary>
    /// Draws an option's label the way the combo would, filter highlighting included. Called by
    /// <see cref="UiComboItemDraw{T}.DrawLabel"/>.
    /// </summary>
    /// <param name="display">The display text.</param>
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

    /// <summary>
    /// Whether the option list is drawn through a clipper this frame.
    /// </summary>
    internal bool IsVirtualizing => Virtualize ?? filteredIndices.Count >= VirtualizeThreshold;

    /// <summary>
    /// The height one option occupies, which every row must share for virtualization to place them correctly.
    /// </summary>
    /// <remarks>
    /// Measured through <see cref="NoireText"/>, which draws the labels, rather than ImGui's current font: a
    /// theme change to body size moves the rows with it, and sizing from the other font would be wrong by
    /// exactly the difference.
    /// </remarks>
    private float ResolveItemHeight()
    {
        if (ItemHeight.HasValue)
            return NoireUI.Scaled(ItemHeight.Value);

        // The dropdown's text need not be the size in force outside it, and measuring at the outer size lays the rows
        // out for a font they are not drawn in. The padding is deliberately not added on top: it is already pushed as
        // ImGui's frame padding, and counting it twice leaves each row a line of empty space taller than its text.
        if (PopupStyle?.TextSizePx is { } size)
            return NoireText.CalcSize(" ", size).Y;

        return NoireText.LineHeight();
    }

    /// <summary>
    /// The vertical distance from one option to the next: the option itself plus the spacing after it.
    /// </summary>
    /// <remarks>
    /// The one number the dropdown's height, the option list's height and the clipper all have to agree on. A clipper
    /// given the bare row height instead positions rows closer together than they are drawn, and the list slides out
    /// of step with its own scrollbar as it goes.
    /// </remarks>
    private float ResolveRowStep() => ResolveItemHeight() + ImGui.GetStyle().ItemSpacing.Y;

    /// <summary>
    /// Selects an option from the list and closes the dropdown.
    /// </summary>
    /// <param name="itemIndex">The index of the option chosen.</param>
    private void Choose(int itemIndex)
    {
        SelectFromUi(itemIndex);
        ImGui.CloseCurrentPopup();
    }

    /// <summary>
    /// Handles the held binding + wheel cycling and the hint tooltip on the closed combo, including the wheel claim
    /// that stops a cycling scroll from also moving the window.
    /// </summary>
    /// <remarks>
    /// One pass for everything the hover needs: the binding is resolved and the held keys are read once per frame,
    /// since reading a key is a system call. The wheel claim must be made while the combo is still the last
    /// submitted item, since ImGui attaches the claim to that item and the tooltip submits items of its own
    /// afterward. ImGui settles what a wheel event scrolls at the start of the frame, before any widget code runs,
    /// so the claim can only be declared in advance; it then drops the event for scrolling while leaving the raw
    /// wheel value readable, which the cycling runs on. Made only while the combo is really cycling, so an ordinary
    /// scroll over an idle combo still moves the window, and it applies from the next frame on, since ImGui matches
    /// it against the item hovered the previous one.
    /// </remarks>
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

    /// <summary>
    /// The item index the wheel lands on when cycling is scoped to what the search matches.
    /// </summary>
    /// <param name="currentIndex">The currently selected item index, or -1.</param>
    /// <param name="direction">+1 for the next match, -1 for the previous one.</param>
    /// <returns>The item index to select, or -1 when there is nothing to move to.</returns>
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
    /// Applies the remembered search, once, the first time the combo draws after <see cref="FilterMemory"/> is set.
    /// </summary>
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

    /// <summary>
    /// Remembers the current search.
    /// </summary>
    private void SavePersistedFilter()
    {
        if (filterMemory == UiMemoryScope.None || !TryGetFilterKey(out var key))
            return;

        if (filterMemory == UiMemoryScope.Session)
            NoireUiSession.Set(key, filterText);
        else
            NoireUiState.Set(key, filterText);
    }

    /// <summary>
    /// Builds the key the search is remembered under.
    /// </summary>
    /// <remarks>
    /// A generated id is refused only for the persisted scope; session memory lasts exactly as long as that id does,
    /// so keying on one there is safe.
    /// </remarks>
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

    /// <summary>
    /// Rebuilds the list of item indices matching the current filter text.
    /// </summary>
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
    /// <remarks>
    /// Setting it rebuilds the matches immediately, so <see cref="WheelCycleFiltered"/> is scoped correctly even if
    /// the dropdown is never opened.
    /// </remarks>
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

    /// <summary>
    /// Builds the default hint tooltip advertising the wheel shortcut, rebuilding it only when the binding it describes changed
    /// (a hotkey attached through <see cref="BindWheelCycleHotkey"/> can be rebound at any time).
    /// </summary>
    /// <param name="binding">The binding currently gating the cycling, from <see cref="ResolvedWheelCycleBinding"/>.</param>
    /// <returns>The hint content to show.</returns>
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
