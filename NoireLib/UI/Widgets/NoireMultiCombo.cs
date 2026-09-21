using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace NoireLib.UI;

/// <summary>A dropdown of checkboxes that selects several items by value and stays open while picking.</summary>
/// <typeparam name="T">The type of the items.</typeparam>
[NoireFacadeFactory]
public sealed class NoireMultiCombo<T>
{
    private readonly List<T> items = new();
    private readonly List<int> filteredIndices = new();
    private readonly List<(int Index, int Score)> scored = new();
    private readonly HashSet<T> selected;

    private readonly StringBuilder previewBuilder = new(64);

    private string filterText = string.Empty;
    private string previewText = string.Empty;
    private int highlightIndex = -1;
    private bool scrollToHighlight;
    private bool changedThisFrame;
    private bool showMatches;

    /// <summary>
    /// Creates a multi-select combo.
    /// </summary>
    /// <param name="id">A stable id for the widget, or <see langword="null"/> for a generated one.</param>
    /// <param name="items">The initial options.</param>
    /// <param name="displayFunc">How an item is converted to its display text.</param>
    /// <param name="comparer">How two items are compared for selection.</param>
    public NoireMultiCombo(string? id = null, IEnumerable<T>? items = null, Func<T, string>? displayFunc = null, IEqualityComparer<T>? comparer = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;
        DisplayFunc = displayFunc;
        selected = new HashSet<T>(comparer ?? EqualityComparer<T>.Default);

        if (items != null)
            this.items.AddRange(items);
    }

    /// <summary>The unique identifier of this widget, used for the ImGui ids.</summary>
    public string Id { get; }

    /// <summary>The label displayed next to the widget. When <see langword="null"/> or empty, none is displayed.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// The width of the widget, in real pixels. When <see langword="null"/>, the default ImGui item width is used.
    /// </summary>
    public float? Width { get; set; }

    /// <summary>How an item is converted to its display text. When <see langword="null"/>, <c>ToString()</c> is used.</summary>
    public Func<T, string>? DisplayFunc { get; set; }

    /// <summary>The options, in the order they were given.</summary>
    public IReadOnlyList<T> Items => items;

    /// <summary>Invoked when the selection changes, with the items now selected.</summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; set; }

    #region Preview

    /// <summary>The text shown when nothing is selected.</summary>
    public string PreviewPlaceholder { get; set; } = "None selected";

    /// <summary>
    /// How many items are named in the preview before it summarises the rest.
    /// </summary>
    public int PreviewMaxItems { get; set; } = 3;

    /// <summary>
    /// The summary appended once more than <see cref="PreviewMaxItems"/> are selected, where <c>{0}</c> is how many
    /// more.
    /// </summary>
    public string PreviewOverflowFormat { get; set; } = "+{0} more";

    /// <summary>
    /// Builds the preview text yourself. When <see langword="null"/>, the items are named up to
    /// <see cref="PreviewMaxItems"/> and the rest summarised.
    /// </summary>
    public Func<IReadOnlyList<T>, string>? PreviewFunc { get; set; }

    #endregion

    #region Options

    /// <summary>Whether the dropdown shows a filter text input at the top.</summary>
    public bool FilterEnabled { get; set; } = true;

    /// <summary>The hint text of the filter input.</summary>
    public string FilterHint { get; set; } = "Filter...";

    /// <summary>Whether the filter input is automatically focused when the dropdown opens.</summary>
    public bool FilterAutoFocus { get; set; } = true;

    /// <summary>Whether the filter text is cleared every time the dropdown opens.</summary>
    public bool ClearFilterOnOpen { get; set; } = true;

    /// <summary>
    /// Whether the filter matches fuzzily and orders the options by how well they matched.
    /// </summary>
    public bool FilterFuzzy { get; set; } = true;

    /// <summary>Whether the characters the filter matched are picked out in the option list.</summary>
    public bool FilterHighlight { get; set; } = true;

    /// <summary>The color the matched characters are drawn in. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? FilterHighlightColor { get; set; }

    /// <summary>The text displayed in the dropdown when no option matches the filter.</summary>
    public string NoResultsText { get; set; } = "No results";

    /// <summary>Whether the "all" and "none" shortcuts are shown above the options.</summary>
    public bool ShowSelectAll { get; set; } = true;

    /// <summary>The label of the shortcut selecting every option the filter currently shows.</summary>
    public string SelectAllText { get; set; } = "All";

    /// <summary>The label of the shortcut clearing the selection.</summary>
    public string SelectNoneText { get; set; } = "None";

    /// <summary>Whether picking an option closes the dropdown.</summary>
    public bool CloseOnSelect { get; set; }

    /// <summary>How many options the dropdown is sized to hold.</summary>
    public int VisibleItemCount { get; set; } = 10;

    /// <summary>
    /// Whether the option list is drawn through a clipper. When <see langword="null"/>, past
    /// <see cref="VirtualizeThreshold"/> options.
    /// </summary>
    public bool? Virtualize { get; set; }

    /// <summary>How many options it takes before <see cref="Virtualize"/> turns itself on.</summary>
    public int VirtualizeThreshold { get; set; } = 100;

    /// <summary>
    /// Draws each option yourself, tick box included.<br/>
    /// Set <see cref="ItemHeight"/> as well when the rows are taller than one line.
    /// </summary>
    public Action<UiMultiComboItemDraw<T>>? ItemRenderer { get; set; }

    /// <summary>The height of one option at 100%. When <see langword="null"/>, one line of text.</summary>
    public float? ItemHeight { get; set; }

    #endregion

    #region Selection

    /// <summary>The selected items, in the order the options were given.</summary>
    public IReadOnlyList<T> Selected
    {
        get
        {
            var result = new List<T>();

            foreach (var item in items)
            {
                if (selected.Contains(item))
                    result.Add(item);
            }

            return result;
        }
    }

    /// <summary>How many options are selected.</summary>
    public int SelectedCount => selected.Count;

    /// <summary>Whether an item is selected.</summary>
    /// <param name="item">The item to test.</param>
    /// <returns>True when it is selected.</returns>
    public bool IsSelected(T item) => selected.Contains(item);

    /// <summary>
    /// Selects or deselects an item.
    /// </summary>
    /// <param name="item">The item to change.</param>
    /// <param name="isSelected">Whether it should be selected.</param>
    /// <returns>True when the selection actually changed.</returns>
    public bool Set(T item, bool isSelected)
    {
        var changed = isSelected ? selected.Add(item) : selected.Remove(item);

        if (changed)
            Notify();

        return changed;
    }

    /// <summary>
    /// Flips whether an item is selected.
    /// </summary>
    /// <param name="item">The item to toggle.</param>
    /// <returns>True when it is now selected.</returns>
    public bool Toggle(T item)
    {
        var isSelected = !selected.Contains(item);
        Set(item, isSelected);
        return isSelected;
    }

    /// <summary>Selects every option.</summary>
    public void SelectAll()
    {
        var changed = false;

        foreach (var item in items)
            changed |= selected.Add(item);

        if (changed)
            Notify();
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        if (selected.Count == 0)
            return;

        selected.Clear();
        Notify();
    }

    /// <summary>
    /// Replaces the selection outright, for restoring a persisted set.
    /// </summary>
    /// <param name="values">The items to select.</param>
    public void SetSelection(IEnumerable<T>? values)
    {
        selected.Clear();

        if (values != null)
        {
            foreach (var value in values)
                selected.Add(value);
        }

        Notify();
    }

    /// <summary>Replaces the options, keeping whatever selection still applies.</summary>
    /// <param name="values">The new options.</param>
    public void SetItems(IEnumerable<T>? values)
    {
        items.Clear();

        if (values != null)
            items.AddRange(values);

        selected.RemoveWhere(item => !items.Contains(item));

        RebuildFilteredIndices();
    }

    private void Notify()
    {
        changedThisFrame = true;

        try
        {
            OnSelectionChanged?.Invoke(Selected);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"The selection callback of multi-combo '{Id}' threw an exception.");
        }
    }

    #endregion

    #region Drawing

    /// <summary>
    /// Draws the widget.
    /// </summary>
    /// <returns>True on the frame the selection changes.</returns>
    public bool Draw()
    {
        using var profile = UiProfile.Widget(nameof(NoireMultiCombo<T>), Id);

        NoireUI.EnsureFrameServices();
        changedThisFrame = false;

        if (Width.HasValue)
            ImGui.SetNextItemWidth(Width.Value);

        var label = string.IsNullOrEmpty(Label) ? string.Empty : Label;

        // ImGui's own popup cap is about eight rows. The list child and the popup would each grow a scrollbar.
        // ImGui floors a constrained window's size but tests the scrollbar against unfloored padding.
        var hostPadding = ImGui.GetStyle().WindowPadding;
        using var padding = UiPush.Style(ImGuiStyleVar.WindowPadding, new Vector2(MathF.Round(hostPadding.X), MathF.Round(hostPadding.Y)));

        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, MeasureMaxPopupHeight()));

        // Inside the popup the current window is the popup.
        var ownerInFront = UiWindowOrder.InTopLayer;

        if (!ImGui.BeginCombo(UiIds.Labelled(label, "###NoireMultiCombo_", Id), BuildPreview()))
            return changedThisFrame;

        try
        {
            if (ownerInFront)
                UiWindowOrder.KeepInFront();

            DrawDropdown();
        }
        finally
        {
            ImGui.EndCombo();
        }

        return changedThisFrame;
    }

    private void DrawDropdown()
    {
        var appearing = ImGui.IsWindowAppearing();

        if (appearing)
        {
            if (ClearFilterOnOpen)
                filterText = string.Empty;

            highlightIndex = -1;
            RebuildFilteredIndices();
        }

        if (FilterEnabled)
        {
            if (appearing && FilterAutoFocus)
                ImGui.SetKeyboardFocusHere();

            ImGui.SetNextItemWidth(-1f);

            if (ImGui.InputTextWithHint(UiIds.For("###NoireMultiComboFilter_", Id), FilterHint, ref filterText, 256))
            {
                highlightIndex = -1;
                RebuildFilteredIndices();
            }
        }

        if (ShowSelectAll)
            DrawSelectAllRow();

        HandleKeyboard();
        DrawItemRows();
    }

    private void DrawSelectAllRow()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = (ImGui.GetContentRegionAvail().X - spacing) * 0.5f;

        if (ImGui.Button(UiIds.Labelled(SelectAllText, "###NoireMultiComboAll_", Id), new Vector2(width, 0f)))
        {
            var changed = false;

            foreach (var index in filteredIndices)
            {
                if (index < items.Count)
                    changed |= selected.Add(items[index]);
            }

            if (changed)
                Notify();
        }

        ImGui.SameLine();

        if (ImGui.Button(UiIds.Labelled(SelectNoneText, "###NoireMultiComboNone_", Id), new Vector2(width, 0f)))
            ClearSelection();

        ImGui.Separator();
    }

    private void HandleKeyboard()
    {
        if (filteredIndices.Count == 0)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
        {
            highlightIndex = NoireComboBox<T>.ComputeCycledIndex(highlightIndex, 1, filteredIndices.Count, true);
            scrollToHighlight = true;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
        {
            highlightIndex = NoireComboBox<T>.ComputeCycledIndex(highlightIndex, -1, filteredIndices.Count, true);
            scrollToHighlight = true;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) && highlightIndex >= 0 && highlightIndex < filteredIndices.Count)
        {
            var index = filteredIndices[highlightIndex];

            if (index < items.Count)
                Toggle(items[index]);
        }
    }

    private void DrawItemRows()
    {
        var visibleCount = Math.Max(1, Math.Min(VisibleItemCount, Math.Max(filteredIndices.Count, 1)));
        var height = (visibleCount * ResolveRowStep()) - ImGui.GetStyle().ItemSpacing.Y;

        using var child = ImRaii.Child(UiIds.For("###NoireMultiComboItems_", Id), new Vector2(0f, height), false, ImGuiWindowFlags.NoBackground);
        if (!child)
            return;

        if (filteredIndices.Count == 0)
        {
            ImGui.TextDisabled(NoResultsText);
            return;
        }

        showMatches = FilterHighlight && FilterFuzzy && FilterEnabled && filterText.Length > 0;

        if (!(Virtualize ?? filteredIndices.Count >= VirtualizeThreshold))
        {
            for (var position = 0; position < filteredIndices.Count; position++)
                DrawItemRow(position);

            return;
        }

        var clipper = new ImGuiListClipper();
        clipper.Begin(filteredIndices.Count, ResolveRowStep());

        if (highlightIndex >= 0 && highlightIndex < filteredIndices.Count)
            clipper.ForceDisplayRangeByIndices(highlightIndex, highlightIndex + 1);

        while (clipper.Step())
        {
            for (var position = clipper.DisplayStart; position < clipper.DisplayEnd; position++)
                DrawItemRow(position);
        }

        clipper.End();
    }

    private void DrawItemRow(int position)
    {
        var itemIndex = filteredIndices[position];

        if (itemIndex >= items.Count)
            return;

        var item = items[itemIndex];
        var isSelected = selected.Contains(item);
        var isHighlighted = position == highlightIndex;
        var start = ImGui.GetCursorPos();

        // ImGui only closes a popup from a selectable in the popup window itself. These rows live in a child.
        if (ImGui.Selectable(UiIds.For("###NoireMultiComboItem_", Id, itemIndex), isSelected || isHighlighted, ImGuiSelectableFlags.DontClosePopups, new Vector2(0f, ResolveItemHeight())))
        {
            Toggle(item);

            if (CloseOnSelect)
                ImGui.CloseCurrentPopup();
        }

        if (isHighlighted && scrollToHighlight)
        {
            ImGui.SetScrollHereY(0.5f);
            scrollToHighlight = false;
        }

        var after = ImGui.GetCursorPos();
        ImGui.SetCursorPos(start);

        if (ItemRenderer is { } renderer)
        {
            try
            {
                renderer(new UiMultiComboItemDraw<T>(this, item, itemIndex, DisplayOf(item), isSelected, isHighlighted));
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"The item renderer of multi-combo '{Id}' threw an exception.");
            }
        }
        else
        {
            DrawItemCheckbox(isSelected);
            DrawItemLabel(DisplayOf(item));
        }

        ImGui.SetCursorPos(after);
    }

    internal void DrawItemCheckbox(bool isSelected) => DrawCheckbox(isSelected);

    private static void DrawCheckbox(bool isSelected)
    {
        var theme = NoireTheme.Current;
        var side = NoireText.LineHeight() * 0.72f;

        // Centred on the label. A box centred on the row sits above the words.
        var origin = ImGui.GetCursorScreenPos() + new Vector2(0f, NoireText.CenterOffset() - (side * 0.5f));
        var box = origin + new Vector2(side, side);

        NoireShapes.Rect(origin, box, theme.Resolve(ThemeColor.SurfaceSunken), CornerShape.Rounded, side * 0.2f);
        NoireShapes.RectOutline(origin, box, theme.Resolve(ThemeColor.Border), 1f, CornerShape.Rounded, side * 0.2f);

        if (isSelected)
        {
            Span<Vector2> tick =
            [
                origin + new Vector2(side * 0.22f, side * 0.52f),
                origin + new Vector2(side * 0.42f, side * 0.72f),
                origin + new Vector2(side * 0.78f, side * 0.28f),
            ];

            NoireShapes.Stroke(tick, theme.Resolve(ThemeColor.Accent), MathF.Max(1.5f, side * 0.14f), closed: false);
        }

        ImGui.Dummy(new Vector2(side, NoireText.LineHeight()));
        ImGui.SameLine(0f, NoireUI.Scaled(7f));
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

    internal string BuildPreview()
    {
        if (selected.Count == 0)
            return PreviewPlaceholder;

        if (PreviewFunc is { } custom)
        {
            try
            {
                return custom(Selected) ?? string.Empty;
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"The preview callback of multi-combo '{Id}' threw an exception.");
            }
        }

        var named = Math.Min(Math.Max(PreviewMaxItems, 1), selected.Count);
        var written = 0;
        var remaining = 0;

        previewBuilder.Clear();

        foreach (var item in items)
        {
            if (!selected.Contains(item))
                continue;

            if (written >= named)
            {
                remaining++;
                continue;
            }

            if (written > 0)
                previewBuilder.Append(", ");

            previewBuilder.Append(DisplayOf(item));
            written++;
        }

        if (remaining > 0)
            previewBuilder.Append("  ").Append(OverflowText(PreviewOverflowFormat, remaining));

        if (!previewBuilder.Equals(previewText.AsSpan()))
            previewText = previewBuilder.ToString();

        return previewText;
    }

    private static string OverflowText(string format, int remaining)
    {
        var key = new OverflowKey(format, remaining);

        if (Overflows.TryGet(key, out var cached))
            return cached;

        var text = string.Format(CultureInfo.CurrentCulture, format, remaining);
        Overflows.Set(key, text);

        return text;
    }

    private readonly record struct OverflowKey(string Format, int Remaining);

    private static readonly HotPathCache<OverflowKey, string> Overflows = new(256);

    private float MeasureMaxPopupHeight()
    {
        var visibleCount = Math.Max(1, VisibleItemCount);

        // Uncapped while everything fits. A cap one pixel under the content is a scrollbar.
        if (filteredIndices.Count <= visibleCount)
            return float.MaxValue;

        var style = ImGui.GetStyle();
        var height = (visibleCount * ResolveRowStep()) - style.ItemSpacing.Y + (style.WindowPadding.Y * 2f);

        if (FilterEnabled)
            height += ImGui.GetFrameHeight() + style.ItemSpacing.Y;

        if (ShowSelectAll)
        {
            height += ImGui.GetFrameHeight() + style.ItemSpacing.Y;
            height += (style.ItemSpacing.Y * 2f) + 1f;
        }

        // A hair of slack against rounding.
        return height + style.ItemSpacing.Y;
    }

    private float ResolveItemHeight()
        => ItemHeight.HasValue ? NoireUI.Scaled(ItemHeight.Value) : NoireText.LineHeight();

    private float ResolveRowStep() => ResolveItemHeight() + ImGui.GetStyle().ItemSpacing.Y;

    private string DisplayOf(T item) => DisplayFunc?.Invoke(item) ?? item?.ToString() ?? string.Empty;

    #endregion

    #region Internal logic

    internal void RebuildFilteredIndices()
    {
        filteredIndices.Clear();

        if (!FilterEnabled || string.IsNullOrEmpty(filterText))
        {
            for (var i = 0; i < items.Count; i++)
                filteredIndices.Add(i);

            return;
        }

        if (!FilterFuzzy)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (NoireComboBox<T>.DefaultFilterMatch(DisplayOf(items[i]), filterText))
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

        scored.Sort(static (left, right) => right.Score != left.Score
            ? right.Score.CompareTo(left.Score)
            : left.Index.CompareTo(right.Index));

        foreach (var entry in scored)
            filteredIndices.Add(entry.Index);
    }

    internal IReadOnlyList<int> FilteredIndices => filteredIndices;

    internal string FilterText
    {
        get => filterText;
        set => filterText = value ?? string.Empty;
    }

    #endregion
}
