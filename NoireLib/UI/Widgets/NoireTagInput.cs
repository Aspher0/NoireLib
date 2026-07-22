using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A field that collects short strings as chips: tags, filters, names, whitelist entries.<br/>
/// Typing and pressing Enter adds one, pasting a comma-separated list adds all of them, backspace on an empty field
/// takes the last one back for editing rather than destroying it, and anything refused says why instead of vanishing.
/// </summary>
/// <remarks>
/// The rules are all yours: what separates a pasted list, whether duplicates are allowed, how many tags fit, how long
/// one may be, and a <see cref="Validate"/> callback for anything the field cannot know. Every refusal comes back as a
/// <see cref="TagRejection"/> rather than as silence.<br/>
/// Suggestions are matched with <see cref="FuzzyMatcher"/> and shown under the field while it has focus.
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
    /// The width of the field. When <see langword="null"/>, the space available is used.<br/>
    /// In real pixels, not scaled. See <see cref="NoireUI.Scale"/>.
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

    /// <summary>
    /// The characters that split a pasted or typed run into several tags.
    /// </summary>
    /// <remarks>
    /// This is what makes pasting a list work, and it is why the field is worth having over a plain text box: people
    /// paste comma-separated lists constantly, and a field that swallows one as a single tag is the thing they then
    /// have to undo by hand.
    /// </remarks>
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
    /// The per-widget override. A style whose <see cref="FocusStyle.Shape"/> is <see cref="FocusShape.None"/> leaves
    /// this field unmarked while the rest of the interface keeps its mark.
    /// </remarks>
    public FocusStyle? FocusStyle { get; set; }

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
    /// The one that removes the right chip when <see cref="AllowDuplicates"/> is on: <see cref="Remove(string)"/>
    /// takes the first tag that compares equal, which is not the one the user clicked.
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

    /// <summary>Replaces every tag, for restoring a persisted list.</summary>
    /// <param name="values">The tags to hold. Anything the rules refuse is dropped.</param>
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

    /// <summary>
    /// Takes the last tag back into the input for editing, which is what backspace on an empty field does.
    /// </summary>
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

    /// <summary>
    /// Splits a run of text into candidate tags.
    /// </summary>
    /// <remarks>
    /// Separated out because it is the part worth being sure about: pasting is the reason this widget exists, and a
    /// splitter that keeps empty pieces or trims the wrong thing turns one paste into a field full of rubbish.
    /// </remarks>
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
    /// Decides whether a candidate can be added, and why not when it cannot.
    /// </summary>
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

        // Not the content region: that reports the window's right edge, so a field inside a page that centres its
        // content in a narrower column would lay its chips out past the end of it.
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

        // Laid out with the row primitive rather than handed to Flow, because a chip needs its index: with duplicates
        // allowed, two chips carrying the same text would otherwise share one ImGui id and only the first of them
        // would ever receive a click.
        var removing = -1;

        // Resolved once for the row rather than per chip: the theme answers the same padding and the same colours for
        // every chip in a frame, and a field can hold a great many of them.
        var theme = NoireTheme.Current;
        var padding = theme.ResolveFramePadding();

        for (var index = 0; index < tags.Count; index++)
        {
            // Measured once and handed on. The measurement is cached, so the second one was a dictionary lookup rather
            // than a walk over the glyphs, but it was still two lookups per chip per frame for one answer.
            var size = MeasureChip(tags[index], padding);

            NoireLayout.FlowItem(size.X, index == 0, width: width);

            if (DrawChip(tags[index], index, size, theme, padding))
                removing = index;
        }

        ImGui.NewLine();

        // Applied after the row, since removing a tag mid-loop shifts every chip after it onto the index of the one
        // that is still being drawn.
        if (removing >= 0)
            RemoveAt(removing);
    }

    /// <summary>
    /// How much room a chip takes: its label, plus the padding around it and the room the cross sits in.
    /// </summary>
    /// <param name="tag">The tag the chip holds.</param>
    /// <param name="padding">The frame padding already resolved from the theme.</param>
    /// <returns>The chip's size in real pixels.</returns>
    private static Vector2 MeasureChip(string tag, Vector2 padding)
        => NoireText.CalcSize(tag) + new Vector2((padding.X * 2f) + NoireUI.Scaled(16f), padding.Y * 2f);

    /// <summary>
    /// Draws one chip, reporting whether its cross was clicked.
    /// </summary>
    /// <remarks>
    /// Keyed on the index rather than the text: two chips holding the same tag are two different chips, and an id
    /// built from the text would make them one, so only the first would be clickable.
    /// </remarks>
    /// <param name="tag">The tag the chip holds.</param>
    /// <param name="index">The chip's position, which is what its id is built from.</param>
    /// <param name="size">The chip's size, measured by the caller laying the row out.</param>
    /// <param name="theme">The theme, resolved once for the whole row.</param>
    /// <param name="padding">The frame padding, resolved once for the whole row.</param>
    /// <returns>True on the frame the chip's cross is clicked.</returns>
    private bool DrawChip(string tag, int index, Vector2 size, NoireTheme theme, Vector2 padding)
    {
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(UiIds.For("###NoireTagChip_", Id, index), size);
        var hovered = ImGui.IsItemHovered();

        // A chip scrolled out of the page still costs two rounded rects, a label and a cross, all of which ImGui then
        // throws away against the clip rect. The layout still has to run for every chip, since a wrapped row does not
        // know where the next one lands until this one has been placed, but the painting does not.
        if (!ImGui.IsRectVisible(origin, origin + size))
        {
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(size);

            return clicked;
        }

        var accent = theme.Resolve(ThemeColor.Accent);

        NoireShapes.Rect(origin, origin + size, ColorHelper.ScaleAlpha(accent, hovered ? 0.35f : 0.20f), CornerShape.Rounded, size.Y * 0.5f);
        NoireShapes.RectOutline(origin, origin + size, ColorHelper.ScaleAlpha(accent, hovered ? 0.85f : 0.45f), 1f, CornerShape.Rounded, size.Y * 0.5f);

        // Both the label and the cross hang off the same line, which is the text's optical centre rather than the
        // chip's geometric one. Centring the label on the chip would put it a couple of pixels low, because its line
        // reserves room under the baseline that a tag rarely uses, and the cross would then sit above the letters.
        var middle = origin.Y + (size.Y * 0.5f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + padding.X, middle - NoireText.CenterOffset()));

        // Wrapping is disabled for the label, because the chip was measured on the assumption that it is one line. A
        // page that sets a wrap position for its prose would otherwise wrap the last chip on a row character by
        // character, inside a pill drawn at the full width of the word.
        ImGui.PushTextWrapPos(-1f);
        NoireText.Draw(tag);
        ImGui.PopTextWrapPos();

        // A cross drawn rather than typed, so it needs no icon font and lines up with the chip whatever the text size.
        var cross = NoireUI.Scaled(3.5f);
        var centre = new Vector2(origin.X + size.X - padding.X - cross, middle);
        var colour = ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.Text), hovered ? 0.95f : 0.55f);

        Span<Vector2> down = [centre - new Vector2(cross, cross), centre + new Vector2(cross, cross)];
        Span<Vector2> up = [centre + new Vector2(-cross, cross), centre + new Vector2(cross, -cross)];

        NoireShapes.Stroke(down, colour, NoireUI.Scaled(1.4f), closed: false);
        NoireShapes.Stroke(up, colour, NoireUI.Scaled(1.4f), closed: false);

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

        // Checked before committing, because a separator typed or pasted mid-run is what turns one paste into the
        // whole list rather than a single tag holding commas.
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

        // Backspace on an empty field takes the last tag back rather than deleting it outright. Deleting is what the
        // chip's own cross is for; this is for fixing a typo in something already committed.
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

        // Committing should not cost the field its focus, or adding several tags in a row means clicking back into it
        // between each one.
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

        // A suggestion is one row of a list and never wraps, for the same reason a chip's label does not.
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
