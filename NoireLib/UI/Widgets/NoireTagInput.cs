using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A field that collects short strings as chips: Enter adds one, a separated run adds all of them, and backspace on
/// an empty field takes the last chip back for editing.
/// </summary>
/// <remarks>
/// Separators, duplicates, tag count and tag length are configurable, along with a <see cref="Validate"/> callback,
/// and every refusal is reported as a <see cref="TagRejection"/>. Suggestions are matched with
/// <see cref="FuzzyMatcher"/> and shown under the field while it has focus.
/// </remarks>
/// <example>
/// <code>
/// var tags = new NoireTagInput("tags", config.Tags)
/// {
///     Suggestions = knownTags,
///     Validate = tag =&gt; tag.Contains(' ') ? "Tags cannot contain spaces." : null,
/// };
///
/// if (tags.Draw())
///     config.Tags = tags.Tags.ToArray();
/// </code>
/// </example>
[NoireFacadeFactory]
public sealed class NoireTagInput
{
    /// <summary>The fault message reported when a consumer draw hook throws.</summary>
    private const string CallbackFault = "A tag chip hook threw.";

    private readonly List<string> tags = new();
    private readonly List<string> suggestionMatches = new();

    private string input = string.Empty;
    private bool changedThisFrame;
    private bool focusInput;

    /// <summary>
    /// Creates a tag field.
    /// </summary>
    /// <param name="id">A stable id for the widget. When <see langword="null"/>, a random one is generated.</param>
    /// <param name="tags">The initial tags.</param>
    /// <param name="comparer">How two tags are compared for duplicates. Defaults to case-insensitive.</param>
    public NoireTagInput(string? id = null, IEnumerable<string>? tags = null, StringComparer? comparer = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;
        Comparer = comparer ?? StringComparer.OrdinalIgnoreCase;

        if (tags != null)
            SetTags(tags);
    }

    /// <summary>The unique identifier of this widget, used for the ImGui ids.</summary>
    public string Id { get; }

    /// <summary>
    /// The width of the field. When <see langword="null"/>, the space available is used. In real pixels, not scaled.
    /// </summary>
    public float? Width { get; set; }

    /// <summary>The hint shown in the empty input.</summary>
    public string Hint { get; set; } = "Add a tag...";

    /// <summary>The tags currently held, in the order they were added.</summary>
    public IReadOnlyList<string> Tags => tags;

    /// <summary>Invoked whenever the tags change, with the current list.</summary>
    public Action<IReadOnlyList<string>>? OnChanged { get; set; }

    #region Rules

    /// <summary>How two tags are compared, for duplicate detection.</summary>
    public StringComparer Comparer { get; set; }

    /// <summary>The characters that split a pasted or typed run into several tags.</summary>
    public char[] Separators { get; set; } = [',', ';', '\n', '\r', '\t'];

    /// <summary>Whether the same tag may appear twice. Off by default.</summary>
    public bool AllowDuplicates { get; set; }

    /// <summary>Whether surrounding whitespace is trimmed off a tag. On by default.</summary>
    public bool TrimWhitespace { get; set; } = true;

    /// <summary>The most tags the field accepts. When <see langword="null"/>, there is no limit.</summary>
    public int? MaxTags { get; set; }

    /// <summary>The longest a single tag may be.</summary>
    public int MaxTagLength { get; set; } = 64;

    /// <summary>
    /// Refuses a tag for a reason the field cannot know. Return an error message, or <see langword="null"/> to accept.
    /// </summary>
    public Func<string, string?>? Validate { get; set; }

    /// <summary>Whether a refused tag shakes the field. Honours <see cref="NoireUI.ReducedMotion"/>.</summary>
    public bool ShakeOnReject { get; set; } = true;

    /// <summary>
    /// How the keyboard focus mark looks on this field. When <see langword="null"/>, <see cref="NoireFocus.Style"/>.
    /// </summary>
    /// <remarks>
    /// A style whose <see cref="FocusStyle.Shape"/> is <see cref="FocusShape.None"/> leaves this field unmarked while
    /// the rest of the interface keeps its mark.
    /// </remarks>
    public FocusStyle? FocusStyle { get; set; }

    /// <summary>
    /// Replaces each chip's painting, called once per visible chip, with layout, hit testing, removal and the
    /// off-screen cull still handled by NoireUI.
    /// </summary>
    /// <remarks>A chip's size is measured from the tag and the theme before painting and the hook cannot change it.</remarks>
    public Action<UiTagChipDraw>? ChipDraw { get; set; }

    /// <summary>Whether the reason a tag was refused is shown under the field.</summary>
    public bool ShowErrors { get; set; } = true;

    #endregion

    #region Suggestions

    /// <summary>
    /// The tags offered as suggestions while typing. When <see langword="null"/>, none are.
    /// </summary>
    public IReadOnlyList<string>? Suggestions { get; set; }

    /// <summary>How many suggestions are shown at once.</summary>
    public int MaxSuggestions { get; set; } = 6;

    /// <summary>Whether tags already held are still offered as suggestions. Off by default.</summary>
    public bool SuggestHeldTags { get; set; }

    #endregion

    #region State

    /// <summary>Why the last attempt to add a tag failed, or <see cref="TagRejection.None"/>.</summary>
    public TagRejection LastRejection { get; private set; }

    /// <summary>
    /// The message describing the last refusal, ready to show to a user. Empty when nothing was refused.
    /// </summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>The text currently in the input, before it has been committed.</summary>
    public string PendingText
    {
        get => input;
        set => input = value ?? string.Empty;
    }

    #endregion

    #region Editing

    /// <summary>
    /// Adds a tag, reporting why if it is refused.
    /// </summary>
    /// <param name="tag">The tag to add.</param>
    /// <param name="rejection">Why it was refused, or <see cref="TagRejection.None"/>.</param>
    /// <returns>True when it was added.</returns>
    public bool TryAdd(string? tag, out TagRejection rejection)
    {
        var candidate = Normalize(tag);
        rejection = Evaluate(candidate, out var message);

        LastRejection = rejection;
        LastError = message;

        if (rejection != TagRejection.None)
            return false;

        tags.Add(candidate);
        Notify();
        return true;
    }

    /// <summary>
    /// Adds a tag.
    /// </summary>
    /// <param name="tag">The tag to add.</param>
    /// <returns>True when it was added.</returns>
    public bool Add(string? tag) => TryAdd(tag, out _);

    /// <summary>
    /// Adds every tag in a run of text, splitting it on <see cref="Separators"/>.
    /// </summary>
    /// <param name="text">The text to split and add.</param>
    /// <returns>How many tags were added.</returns>
    public int AddRange(string? text)
    {
        var added = 0;

        foreach (var candidate in Split(text, Separators, TrimWhitespace))
        {
            if (TryAdd(candidate, out _))
                added++;
        }

        return added;
    }

    /// <summary>
    /// Removes the tag at a position.
    /// </summary>
    /// <remarks>
    /// With <see cref="AllowDuplicates"/> on, <see cref="Remove(string)"/> takes the first tag that compares equal
    /// instead of the one at this position.
    /// </remarks>
    /// <param name="index">The position to remove.</param>
    /// <returns>True when there was a tag there.</returns>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= tags.Count)
            return false;

        tags.RemoveAt(index);
        Notify();
        return true;
    }

    /// <summary>
    /// Removes a tag, the first one that matches when duplicates are allowed.
    /// </summary>
    /// <param name="tag">The tag to remove.</param>
    /// <returns>True when it was there.</returns>
    public bool Remove(string? tag)
    {
        if (tag == null)
            return false;

        var index = IndexOf(tag);

        if (index < 0)
            return false;

        tags.RemoveAt(index);
        Notify();
        return true;
    }

    /// <summary>Replaces every tag.</summary>
    /// <param name="values">The tags to hold, with anything the rules refuse dropped.</param>
    public void SetTags(IEnumerable<string>? values)
    {
        tags.Clear();

        if (values != null)
        {
            foreach (var value in values)
            {
                var candidate = Normalize(value);

                if (Evaluate(candidate, out _) == TagRejection.None)
                    tags.Add(candidate);
            }
        }

        Notify();
    }

    /// <summary>Removes every tag.</summary>
    public void Clear()
    {
        if (tags.Count == 0)
            return;

        tags.Clear();
        Notify();
    }

    /// <summary>Takes the last tag back into the input for editing.</summary>
    /// <returns>True when there was a tag to take back.</returns>
    public bool PopLastForEditing()
    {
        if (tags.Count == 0)
            return false;

        input = tags[^1];
        tags.RemoveAt(tags.Count - 1);
        Notify();
        return true;
    }

    #endregion

    #region Rules, as logic

    /// <summary>Splits a run of text into candidate tags.</summary>
    /// <param name="text">The text to split.</param>
    /// <param name="separators">The characters to split on.</param>
    /// <param name="trim">Whether surrounding whitespace is removed from each piece.</param>
    /// <returns>The candidates, in order, with empty pieces dropped.</returns>
    internal static List<string> Split(string? text, char[]? separators, bool trim)
    {
        var result = new List<string>();

        if (string.IsNullOrEmpty(text))
            return result;

        var pieces = separators is { Length: > 0 }
            ? text.Split(separators)
            : [text];

        foreach (var piece in pieces)
        {
            var candidate = trim ? piece.Trim() : piece;

            if (!string.IsNullOrEmpty(candidate))
                result.Add(candidate);
        }

        return result;
    }

    /// <summary>
    /// Decides whether a candidate can be added.
    /// </summary>
    /// <param name="candidate">The normalized tag to test.</param>
    /// <param name="message">The reason for a refusal, empty when accepted.</param>
    /// <returns>The refusal reason, or <see cref="TagRejection.None"/>.</returns>
    internal TagRejection Evaluate(string candidate, out string message)
    {
        message = string.Empty;

        if (string.IsNullOrEmpty(candidate))
            return TagRejection.Empty;

        if (MaxTagLength > 0 && candidate.Length > MaxTagLength)
        {
            message = $"Tags can be at most {MaxTagLength} characters.";
            return TagRejection.TooLong;
        }

        if (!AllowDuplicates && IndexOf(candidate) >= 0)
        {
            message = $"'{candidate}' is already in the list.";
            return TagRejection.Duplicate;
        }

        if (MaxTags.HasValue && tags.Count >= MaxTags.Value)
        {
            message = $"At most {MaxTags.Value} tags.";
            return TagRejection.Full;
        }

        if (Validate is { } validate)
        {
            string? error;

            try
            {
                error = validate(candidate);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"The validation callback of tag field '{Id}' threw an exception.");
                error = "Validation failed.";
            }

            if (!string.IsNullOrEmpty(error))
            {
                message = error;
                return TagRejection.Invalid;
            }
        }

        return TagRejection.None;
    }

    private string Normalize(string? tag)
    {
        var candidate = tag ?? string.Empty;
        return TrimWhitespace ? candidate.Trim() : candidate;
    }

    private int IndexOf(string tag)
    {
        for (var i = 0; i < tags.Count; i++)
        {
            if (Comparer.Equals(tags[i], tag))
                return i;
        }

        return -1;
    }

    private void Notify()
    {
        changedThisFrame = true;

        try
        {
            OnChanged?.Invoke(tags);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"The change callback of tag field '{Id}' threw an exception.");
        }
    }

    #endregion

    #region Drawing

    /// <summary>
    /// Draws the field.
    /// </summary>
    /// <returns>True on the frame the tags change.</returns>
    public bool Draw()
    {
        using var profile = UiProfile.Widget(nameof(NoireTagInput), Id);

        NoireUI.EnsureFrameServices();
        changedThisFrame = false;

        // Not the ImGui content region: it reports the window's right edge, past a narrower centred column.
        var width = Width ?? NoireLayout.ContentWidth();
        var shake = ShakeOnReject ? NoireAnim.Shake(Id, "reject") : 0f;

        ImGui.BeginGroup();

        if (shake != 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + shake);

        DrawChips(width);
        DrawInput(width);
        DrawSuggestions(width);
        DrawError();

        ImGui.EndGroup();

        return changedThisFrame;
    }

    private void DrawChips(float width)
    {
        if (tags.Count == 0)
            return;

        // Each chip needs its index for its ImGui id: with duplicates allowed, two chips of the same text would
        // otherwise share one id and only the first would receive a click.
        var removing = -1;

        // The theme answers the same padding and colours for every chip in a frame, so they are resolved once.
        var theme = NoireTheme.Current;
        var padding = theme.ResolveFramePadding();

        for (var index = 0; index < tags.Count; index++)
        {
            var size = MeasureChip(tags[index], padding);

            NoireLayout.FlowItem(size.X, index == 0, width: width);

            if (DrawChip(tags[index], index, size, theme, padding))
                removing = index;
        }

        ImGui.NewLine();

        // Applied after the row: removing mid-loop shifts later chips onto the index still being drawn.
        if (removing >= 0)
            RemoveAt(removing);
    }

    /// <summary>
    /// The room a chip takes: its label, the padding around it and the space for the cross.
    /// </summary>
    /// <param name="tag">The tag the chip holds.</param>
    /// <param name="padding">The frame padding already resolved from the theme.</param>
    /// <returns>The chip's size in real pixels.</returns>
    private static Vector2 MeasureChip(string tag, Vector2 padding)
        => NoireText.CalcSize(tag) + new Vector2((padding.X * 2f) + NoireUI.Scaled(16f), padding.Y * 2f);

    /// <summary>
    /// Draws one chip, reporting whether its cross was clicked.
    /// </summary>
    /// <remarks>Keyed on the index, since an id built from the text would merge two chips holding the same tag.</remarks>
    /// <param name="tag">The tag the chip holds.</param>
    /// <param name="index">The chip's position, used to build its id.</param>
    /// <param name="size">The chip's size, measured by the caller laying the row out.</param>
    /// <param name="theme">The theme, resolved once for the whole row.</param>
    /// <param name="padding">The frame padding, resolved once for the whole row.</param>
    /// <returns>True on the frame the chip's cross is clicked.</returns>
    private bool DrawChip(string tag, int index, Vector2 size, NoireTheme theme, Vector2 padding)
    {
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(UiIds.For("###NoireTagChip_", Id, index), size);
        var hovered = ImGui.IsItemHovered();

        // Painting a chip outside the clip rect is wasted work, but the layout still has to run for it since a
        // wrapped row cannot place the next chip otherwise.
        if (!ImGui.IsRectVisible(origin, origin + size))
        {
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(size);

            return clicked;
        }

        var accent = theme.Resolve(ThemeColor.Accent);

        // Label and cross share the text's optical centre; the chip's geometric centre sits the label low because
        // the text line reserves room under the baseline.
        var middle = origin.Y + (size.Y * 0.5f);

        // The cross is drawn rather than typed, so it needs no icon font and follows the text size.
        var cross = NoireUI.Scaled(3.5f);
        var crossCentre = new Vector2(origin.X + size.X - padding.X - cross, middle);

        if (ChipDraw != null)
        {
            using var draw = UiDraw.Begin();

            if (!draw.List.IsNull)
            {
                var args = new UiTagChipDraw(
                    draw.List, origin, origin + size, index, tag, hovered, accent,
                    ColorHelper.ScaleAlpha(accent, hovered ? 0.35f : 0.20f),
                    ColorHelper.ScaleAlpha(accent, hovered ? 0.85f : 0.45f),
                    size.Y * 0.5f, padding, crossCentre, cross,
                    ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.Text), hovered ? 0.95f : 0.55f));
                UiHook.Invoke(ChipDraw, args, nameof(NoireTagInput), CallbackFault);
            }
        }
        else
        {
            NoireShapes.Rect(origin, origin + size, ColorHelper.ScaleAlpha(accent, hovered ? 0.35f : 0.20f), CornerShape.Rounded, size.Y * 0.5f);
            NoireShapes.RectOutline(origin, origin + size, ColorHelper.ScaleAlpha(accent, hovered ? 0.85f : 0.45f), 1f, CornerShape.Rounded, size.Y * 0.5f);

            ImGui.SetCursorScreenPos(new Vector2(origin.X + padding.X, middle - NoireText.CenterOffset()));

            // The chip was measured as a single line, so a page-level wrap position must not reach the label.
            ImGui.PushTextWrapPos(-1f);
            NoireText.Draw(tag);
            ImGui.PopTextWrapPos();

            var colour = ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.Text), hovered ? 0.95f : 0.55f);

            Span<Vector2> down = [crossCentre - new Vector2(cross, cross), crossCentre + new Vector2(cross, cross)];
            Span<Vector2> up = [crossCentre + new Vector2(-cross, cross), crossCentre + new Vector2(cross, -cross)];

            NoireShapes.Stroke(down, colour, NoireUI.Scaled(1.4f), closed: false);
            NoireShapes.Stroke(up, colour, NoireUI.Scaled(1.4f), closed: false);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return clicked;
    }

    private void DrawInput(float width)
    {
        if (focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusInput = false;
        }

        ImGui.SetNextItemWidth(width);

        var committed = ImGui.InputTextWithHint(UiIds.For("###NoireTagInput_", Id), Hint, ref input, 256, ImGuiInputTextFlags.EnterReturnsTrue);
        var active = ImGui.IsItemActive();

        NoireFocus.OnLast(FocusStyle);

        // Checked before committing, so a pasted run splits into several tags instead of one tag holding separators.
        if (!committed && input.Length > 0 && ContainsSeparator(input))
        {
            AddRange(input);
            input = string.Empty;
            return;
        }

        if (committed)
        {
            CommitInput();
            return;
        }

        // Backspace on an empty field takes the last tag back for editing; the chip's cross is what deletes.
        if (active && input.Length == 0 && tags.Count > 0 && ImGui.IsKeyPressed(ImGuiKey.Backspace, false))
            PopLastForEditing();
    }

    private void CommitInput()
    {
        if (input.Length == 0)
            return;

        if (ContainsSeparator(input))
        {
            AddRange(input);
            input = string.Empty;
        }
        else if (TryAdd(input, out _))
        {
            input = string.Empty;
        }
        else if (ShakeOnReject)
        {
            NoireAnim.Trigger(Id, "reject");
        }

        // Focus is kept so several tags can be added in a row without clicking back into the field.
        focusInput = true;
    }

    private bool ContainsSeparator(string text)
    {
        if (Separators is not { Length: > 0 })
            return false;

        return text.IndexOfAny(Separators) >= 0;
    }

    private void DrawSuggestions(float width)
    {
        if (Suggestions == null || input.Length == 0)
            return;

        suggestionMatches.Clear();

        foreach (var suggestion in FuzzyMatcher.Rank(Suggestions, input, static text => text))
        {
            if (!SuggestHeldTags && IndexOf(suggestion) >= 0)
                continue;

            suggestionMatches.Add(suggestion);

            if (suggestionMatches.Count >= Math.Max(1, MaxSuggestions))
                break;
        }

        if (suggestionMatches.Count == 0)
            return;

        Span<int> matched = stackalloc int[FuzzyMatcher.MaxQueryLength];

        // A suggestion is one row of a fixed height, so it must not wrap either.
        ImGui.PushTextWrapPos(-1f);

        foreach (var suggestion in suggestionMatches)
        {
            var start = ImGui.GetCursorPos();

            if (ImGui.Selectable(UiIds.For("###NoireTagSuggestion_", Id, suggestion), false, ImGuiSelectableFlags.None, new Vector2(width, NoireText.LineHeight())))
            {
                Add(suggestion);
                input = string.Empty;
                focusInput = true;
            }

            var after = ImGui.GetCursorPos();
            ImGui.SetCursorPos(start);

            if (FuzzyMatcher.TryMatch(suggestion, input, matched, out var match))
                NoireText.Highlighted(suggestion, matched[..match.MatchedCount]);
            else
                NoireText.Draw(suggestion);

            ImGui.SetCursorPos(after);
        }

        ImGui.PopTextWrapPos();
    }

    private void DrawError()
    {
        if (!ShowErrors || LastRejection == TagRejection.None || LastError.Length == 0)
            return;

        NoireText.Colored(NoireTheme.Current.Resolve(ThemeColor.Danger), LastError, TextSize.Caption);
    }

    #endregion
}
