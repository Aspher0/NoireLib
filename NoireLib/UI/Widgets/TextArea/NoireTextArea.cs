using Dalamud.Bindings.ImGui;
using NoireLib.Internal.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.UI;

/// <summary>
/// A multiline text field whose lines wrap at its width, with a vertical scrollbar and no horizontal one. The value
/// never holds the breaks the wrapping draws.
/// </summary>
[NoireFacade]
public static unsafe class NoireTextArea
{
    private sealed class WrapState
    {
        public string Text = string.Empty;
        public string Display = string.Empty;
        public int[] SoftBreaks = [];
        public float Width = -1f;
        public float FontSize = -1f;
        public bool Active;

        // The marks of the last search, for the display and the search they were measured for.
        public readonly List<Vector4> Marks = [];
        public string? MarkedDisplay;
        public string? MarkedSearch;
        public bool MarkedWholeWord;
    }

    private static readonly Dictionary<string, WrapState> States = [];
    private static readonly Dictionary<char, float> Advances = [];

    private static float advanceFontSize = -1f;
    private static WrapState? editing;

    /// <summary>Draws the field.</summary>
    /// <param name="id">The ImGui id of the field, such as <c>##notes</c>.</param>
    /// <param name="value">The text, updated in place.</param>
    /// <param name="capacity">The most bytes the text may hold.</param>
    /// <param name="size">The size of the field in pixels. A negative width fills the space left.</param>
    /// <returns>True on the frame the text changes.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="id"/> or <paramref name="value"/> is null.</exception>
    public static bool Draw(string id, ref string value, int capacity, Vector2 size)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(value);

        if (!States.TryGetValue(id, out var state))
            States[id] = state = new WrapState();

        var outer = size.X <= 0f ? ImGui.GetContentRegionAvail().X : size.X;
        var width = WrapWidth(outer);
        var fontSize = ImGui.GetFontSize();

        if (!state.Active && (value != state.Text || MathF.Abs(width - state.Width) > 0.5f || fontSize != state.FontSize))
        {
            state.Text = value;
            state.Width = width;
            state.FontSize = fontSize;
            (state.Display, state.SoftBreaks) = SoftWrap.Wrap(value, width, Advance);
        }

        var display = state.Display;
        editing = state;

        var edited = ImGui.InputTextMultiline(
            id,
            ref display,
            capacity + state.SoftBreaks.Length + 1024,
            size,
            ImGuiInputTextFlags.NoHorizontalScroll | ImGuiInputTextFlags.CallbackEdit,
            OnEdit);

        editing = null;
        state.Active = ImGui.IsItemActive();
        state.Display = display;

        if (!edited || state.Text == value)
            return false;

        value = state.Text;
        return true;
    }

    // Draws the field with every occurrence of the search marked behind its text. The marks assume the field shows every
    // line, as a field as tall as HeightFor does.
    internal static bool Draw(string id, ref string value, int capacity, Vector2 size, string search, bool wholeWord, uint markColor)
    {
        if (search.Length == 0)
            return Draw(id, ref value, capacity, size);

        // The field draws no background: it would cover the marks.
        var background = ImGui.GetColorU32(ImGuiCol.FrameBg);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, 0u);
        var edited = Draw(id, ref value, capacity, size);
        ImGui.PopStyleColor();

        var state = States[id];

        if (!string.Equals(state.MarkedDisplay, state.Display, StringComparison.Ordinal)
            || !string.Equals(state.MarkedSearch, search, StringComparison.Ordinal) || state.MarkedWholeWord != wholeWord)
        {
            state.MarkedDisplay = state.Display;
            state.MarkedSearch = search;
            state.MarkedWholeWord = wholeWord;
            Mark(state.Text, state.Display, state.SoftBreaks, search, wholeWord, state.Marks);
        }

        var style = ImGui.GetStyle();
        var min = ImGui.GetItemRectMin();

        using (var draw = UiDraw.BeginWindow())
        {
            if (!draw.List.IsNull)
                draw.List.AddRectFilled(min, ImGui.GetItemRectMax(), background, style.FrameRounding);
        }

        DrawMarks(min + style.FramePadding, state.Marks, markColor);
        return edited;
    }

    // The rectangles, as (left, top, right, bottom) from the top-left of the first line, that cover every occurrence of
    // the search in a text shown wrapped; an occurrence cut by a break gets one rectangle per line.
    internal static void Mark(string text, string display, int[] softBreaks, string search, bool wholeWord, List<Vector4> marks)
    {
        marks.Clear();

        for (var at = TextMatches.Next(text, search, wholeWord, 0); at >= 0; at = TextMatches.Next(text, search, wholeWord, at + search.Length))
        {
            var start = SoftWrap.DisplayIndex(at, softBreaks);
            var end = SoftWrap.DisplayIndex(at + search.Length - 1, softBreaks) + 1;

            while (start < end)
            {
                var lineEnd = display.IndexOf('\n', start, end - start);
                var stop = lineEnd < 0 ? end : lineEnd;

                if (stop > start)
                    marks.Add(MarkOf(display, start, stop));

                start = stop + 1;
            }
        }
    }

    // On the current window: text drawn after them, or inside a child window, stays on top.
    internal static void DrawMarks(Vector2 origin, List<Vector4> marks, uint color)
    {
        if (marks.Count == 0)
            return;

        using var draw = UiDraw.BeginWindow();

        if (draw.List.IsNull)
            return;

        foreach (var mark in marks)
            draw.List.AddRectFilled(origin + new Vector2(mark.X, mark.Y), origin + new Vector2(mark.Z, mark.W), color);
    }

    private static Vector4 MarkOf(string display, int start, int end)
    {
        var lineStart = start == 0 ? 0 : display.LastIndexOf('\n', start - 1) + 1;
        var line = 0;

        for (var index = 0; index < lineStart; index++)
        {
            if (display[index] == '\n')
                line++;
        }

        var lineHeight = ImGui.GetFontSize();
        var left = ImGui.CalcTextSize(display.Substring(lineStart, start - lineStart)).X;
        var right = ImGui.CalcTextSize(display.Substring(lineStart, end - lineStart)).X;
        return new Vector4(left, line * lineHeight, right, (line + 1) * lineHeight);
    }

    // Tall enough for every wrapped line: the field never scrolls.
    internal static float HeightFor(string text, float outerWidth)
    {
        var (display, _) = SoftWrap.Wrap(text, WrapWidth(outerWidth), Advance);
        var lines = 1;

        foreach (var c in display)
        {
            if (c == '\n')
                lines++;
        }

        return (lines * ImGui.GetFontSize()) + (ImGui.GetStyle().FramePadding.Y * 2f) + 2f;
    }

    private static float WrapWidth(float outerWidth)
    {
        var style = ImGui.GetStyle();
        return MathF.Max(1f, outerWidth - (style.FramePadding.X * 2f) - style.ScrollbarSize - 2f);
    }

    private static int OnEdit(ImGuiInputTextCallbackDataPtr data)
    {
        if (editing is not { } state)
            return 0;

        var after = Encoding.UTF8.GetString(data.Buf, data.BufTextLen);
        var carried = SoftWrap.Carry(state.Display, state.SoftBreaks, after);
        var text = SoftWrap.Unwrap(after, carried);
        var cursor = SoftWrap.TextIndex(Encoding.UTF8.GetCharCount(data.Buf, data.CursorPos), carried);

        // Backspace at the start of a wrapped line, or Delete at its end, removes only a break the wrapping drew: the key
        // removes the character on the other side of it instead.
        if (after.Length == state.Display.Length - 1 && text == state.Text)
            (text, cursor) = SoftWrap.DeleteAcross(text, cursor, ImGui.IsKeyDown(ImGuiKey.Delete));

        var (display, softBreaks) = SoftWrap.Wrap(text, state.Width, Advance);

        data.DeleteChars(0, data.BufTextLen);
        data.InsertChars(0, display);

        var cursorChar = Math.Min(display.Length, SoftWrap.DisplayIndex(cursor, softBreaks));
        data.CursorPos = Encoding.UTF8.GetByteCount(display.AsSpan(0, cursorChar));
        data.SelectionStart = data.CursorPos;
        data.SelectionEnd = data.CursorPos;

        state.Text = text;
        state.Display = display;
        state.SoftBreaks = softBreaks;
        return 0;
    }

    internal static float Advance(char character)
    {
        var fontSize = ImGui.GetFontSize();

        if (fontSize != advanceFontSize)
        {
            Advances.Clear();
            advanceFontSize = fontSize;
        }

        if (!Advances.TryGetValue(character, out var width))
            Advances[character] = width = ImGui.CalcTextSize(character.ToString()).X;

        return width;
    }
}
