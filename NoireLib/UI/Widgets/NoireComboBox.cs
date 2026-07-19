using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using NoireLib.HotkeyManager;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A stateful combo box widget with quality-of-life features:<br/>
/// - An optional filter text input at the top of the dropdown, automatically focused when the dropdown opens, and either
/// pinned above the options or scrolling with them (see <see cref="FilterPinned"/>).<br/>
/// - Arrow keys cycle the highlighted option inside the open dropdown; Enter confirms it. The wheel scrolls the option
/// list, as it does in any list.<br/>
/// - An optional "hold a binding + mouse wheel" shortcut cycling the selection while hovering the closed combo, with or without looping.
/// The binding is a <see cref="HotkeyBinding"/> matched with the same rules as a <see cref="NoireHotkeyManager"/> hotkey, and can be
/// driven straight from that module through <see cref="BindWheelCycleHotkey"/> so the user can rebind it.<br/>
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
    /// The width of the combo box. When <see langword="null"/>, the default ImGui item width is used.<br/>
    /// In real pixels, not scaled: this is handed straight to ImGui as the next item width, so it belongs in the same
    /// space as the <c>GetContentRegionAvail</c> it is usually set from. See <see cref="NoireUI.Scale"/>.
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
    /// Whether the filter input stays pinned above the options while they scroll (the default), or scrolls away with them.<br/>
    /// Either way the dropdown shows a single scrollbar: when pinned, only the option list scrolls; when not, the whole
    /// dropdown does.
    /// </summary>
    public bool FilterPinned { get; set; } = true;

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
    /// An optional binding that must be held for the closed-combo wheel cycling to trigger.<br/>
    /// A plain <see cref="VirtualKey"/> converts implicitly (<c>combo.WheelCycleBinding = VirtualKey.CONTROL;</c>), and the full
    /// <see cref="HotkeyBinding"/> surface is available for a modifier combination, a key plus modifiers, or a gamepad button
    /// (<c>new HotkeyBinding(VirtualKey.G, ctrl: true)</c>).<br/>
    /// Defaults to an empty binding, meaning no key is required. The binding is matched with the same rules as a
    /// <see cref="NoireHotkeyManager"/> hotkey (see <see cref="KeybindsHelper.IsBindingHeld"/>).<br/>
    /// Ignored while a hotkey is attached through <see cref="BindWheelCycleHotkey"/>; read <see cref="ResolvedWheelCycleBinding"/>
    /// for the binding actually in effect.
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
    /// The binding the closed-combo wheel cycling currently requires: the live binding of the hotkey attached through
    /// <see cref="BindWheelCycleHotkey"/> when there is one, otherwise <see cref="WheelCycleBinding"/>.<br/>
    /// An attached hotkey is read on every access rather than copied, so a rebinding (through
    /// <see cref="NoireHotkeyManager.DrawKeybindInputButton"/> or any other path) applies immediately, with no bookkeeping on your side.
    /// An attached hotkey that is disabled or no longer registered resolves to an empty binding, which disables the cycling
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
    /// False when a hotkey is attached but cannot be honored (disabled, or unregistered from its manager since it was attached),
    /// in which case the cycling must stay off entirely: an empty binding would otherwise read as "no key required" and make the
    /// cycling unconditional, the opposite of what gating it behind that hotkey asked for.
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
    /// Drives the closed-combo wheel cycling from a hotkey registered on a <see cref="NoireHotkeyManager"/>, so the user can rebind the
    /// shortcut through the manager and the combo (and its hint tooltip) follows automatically.<br/>
    /// The hotkey's binding is only ever read, never triggered: the manager keeps owning it, its own callback is untouched, and it stays
    /// usable as a regular hotkey at the same time. This is a convenience over <see cref="WheelCycleBinding"/>, which stays available.<br/>
    /// Does not enable the cycling on its own; set <see cref="WheelCycleEnabled"/> as well.<br/>
    /// Note that <see cref="NoireHotkeyManager.RegisterHotkey"/> requires every hotkey to carry a callback, so a hotkey registered only to
    /// gate a combo takes an empty one: the combo reads the binding, not the trigger.
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
        changedThisFrame = false;
        ClampSelection();

        if (Width.HasValue)
            ImGui.SetNextItemWidth(Width.Value);

        var preview = selectedIndex >= 0 ? DisplayOf(items[selectedIndex]) : PreviewPlaceholder;
        var label = string.IsNullOrEmpty(Label) ? $"###NoireCombo_{Id}" : $"{Label}###NoireCombo_{Id}";

        // Cap the dropdown to exactly what it is meant to show. Left alone, ImGui budgets the popup at eight options and
        // knows nothing about the filter input, so the filter pushes the option list past the budget and the popup grows
        // a scrollbar around the list's own: two nested scrollbars for one list.
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, MeasureMaxPopupHeight()));

        var comboOpen = false;
        using (var combo = ImRaii.Combo(label, preview, ComboFlags))
        {
            if (combo)
            {
                comboOpen = true;
                DrawPopupContent();
            }
        }

        // While the dropdown is open the popup is a separate window that owns the wheel itself, so the cycling stands down.
        if (comboOpen)
            return changedThisFrame;

        ClaimWheelIfCycling();
        HandleClosedComboInteractions();

        return changedThisFrame;
    }

    /// <summary>
    /// Tells ImGui that the combo, not the window it sits in, is what the wheel is for right now.
    /// </summary>
    /// <remarks>
    /// A wheel event the combo is cycling with must not also move the window behind it, and ImGui settles what a wheel
    /// event scrolls at the start of a frame, before any widget code runs: it cannot be handed back afterwards, only
    /// claimed in advance. Declaring the item as using the wheel is exactly that claim, and ImGui honors it by dropping
    /// the event for scrolling purposes entirely, so nothing anywhere moves. The raw wheel value stays readable, which is
    /// what the cycling itself runs on.<br/>
    /// The claim is made only while the combo is really cycling, so an ordinary scroll over an idle combo still moves the
    /// window as the user expects. It applies from the next frame on, since ImGui matches the claim against the item that
    /// was hovered on the previous one; in practice a binding is always already held for a frame before the wheel turns.<br/>
    /// Must be called while the combo is still the last submitted item: the claim attaches to that item, and the hint
    /// tooltip drawn afterwards submits items of its own.
    /// </remarks>
    private void ClaimWheelIfCycling()
    {
        if (!WheelCycleEnabled || items.Count == 0 || !ImGui.IsItemHovered())
            return;

        if (!TryResolveWheelCycleBinding(out var binding))
            return;

        if (!binding.IsEmpty && !KeybindsHelper.IsBindingHeld(binding))
            return;

        ImGuiP.SetItemUsingMouseWheel();
    }

    /// <summary>
    /// The height the dropdown is capped at: the filter row, when shown, plus exactly <see cref="VisibleItemCount"/> options.<br/>
    /// Mirrors how ImGui itself sizes a popup from an option count, so a dropdown holding fewer options still shrinks to fit.
    /// </summary>
    private float MeasureMaxPopupHeight()
    {
        var style = ImGui.GetStyle();
        var visibleCount = Math.Max(1, VisibleItemCount);
        var height = (visibleCount * ImGui.GetTextLineHeightWithSpacing()) - style.ItemSpacing.Y + (style.WindowPadding.Y * 2f);

        if (FilterEnabled)
        {
            height += ImGui.GetFrameHeight() + style.ItemSpacing.Y;

            // With the filter pinned, the options live in a fixed-height child that scrolls on its own and the popup is
            // meant to wrap it snugly. The popup then measures exactly this cap, and asking it to fit its content into a
            // budget equal to that content is what a rounding hair away from equality turns into a stray scrollbar.
            if (FilterPinned)
                height += style.ItemSpacing.Y;
        }

        return height;
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

        DrawItemList();

        if (confirm)
        {
            if (highlightIndex >= 0 && highlightIndex < filteredIndices.Count)
                SelectFromUi(filteredIndices[highlightIndex]);

            ImGui.CloseCurrentPopup();
        }
    }

    /// <summary>
    /// Draws the option list, in a scrolling child of its own when the filter is pinned above it, or straight into the
    /// dropdown when the filter is free to scroll away with the options.<br/>
    /// Exactly one of the two scrolls in either case, which is what keeps the dropdown to a single scrollbar.
    /// </summary>
    private void DrawItemList()
    {
        if (!FilterEnabled || !FilterPinned)
        {
            DrawItemRows();
            return;
        }

        // Sized to the options it holds rather than to the space left over, so a short list shrinks the dropdown instead
        // of leaving it padded out with dead space.
        var visibleCount = Math.Max(1, Math.Min(VisibleItemCount, Math.Max(filteredIndices.Count, 1)));
        var listHeight = (visibleCount * ImGui.GetTextLineHeightWithSpacing()) - ImGui.GetStyle().ItemSpacing.Y;

        // NoBackground: the dropdown's own background is the backdrop here, and the list must not paint a second panel of
        // its own over it out of whatever ImGuiCol.ChildBg the consumer happens to have pushed around the combo.
        using var child = ImRaii.Child($"###NoireComboItems_{Id}", new Vector2(0f, listHeight), false, ImGuiWindowFlags.NoBackground);
        if (!child)
            return;

        DrawItemRows();
    }

    private void DrawItemRows()
    {
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
    /// Handles the held binding + wheel cycling and the hint tooltip on the closed combo.
    /// </summary>
    private void HandleClosedComboInteractions()
    {
        if (!WheelCycleEnabled || !ImGui.IsItemHovered())
            return;

        if (!TryResolveWheelCycleBinding(out var binding))
            return;

        if (WheelCycleHintEnabled)
            NoireTooltip.Show(GetWheelCycleHint(binding), WheelCycleHintStyle, $"NoireComboHint_{Id}");

        if (items.Count == 0)
            return;

        if (!binding.IsEmpty && !KeybindsHelper.IsBindingHeld(binding))
            return;

        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f)
            return;

        var newIndex = ComputeCycledIndex(selectedIndex, wheel > 0f ? -1 : 1, items.Count, WheelCycleLoop);
        if (newIndex != selectedIndex)
            SelectFromUi(newIndex);
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
            cachedHintContent = new NoireContent();

            if (!binding.IsEmpty)
                cachedHintContent.AddText($"{KeybindsHelper.FormatBinding(binding)} + ");

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
