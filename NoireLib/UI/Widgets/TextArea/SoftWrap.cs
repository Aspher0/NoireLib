using System;
using System.Collections.Generic;
using System.Text;
using NoireLib.Helpers;

namespace NoireLib.UI;

// Wraps text for display with line breaks of its own, kept apart from the text itself. A soft break is an index into
// the displayed text where a break was put in; the text the user owns never holds one.
internal static class SoftWrap
{
    public static (string Display, int[] SoftBreaks) Wrap(string text, float width, Func<char, float> advance)
    {
        var display = new StringBuilder(text.Length + 16);
        var soft = new List<int>();
        var lineWidth = 0f;
        var lastBreak = -1;
        var widthAtBreak = 0f;

        foreach (var character in text)
        {
            if (character == '\n')
            {
                display.Append(character);
                lineWidth = 0f;
                lastBreak = -1;
                continue;
            }

            var step = advance(character);

            // Chinese and Japanese break between characters too, as a space would.
            if (lineWidth > 0f && LineBreakHelper.CanBreakBetween(display[^1], character))
            {
                lastBreak = display.Length;
                widthAtBreak = lineWidth;
            }

            if (lineWidth > 0f && lineWidth + step > width)
            {
                if (lastBreak >= 0 && lastBreak < display.Length)
                {
                    display.Insert(lastBreak, '\n');
                    soft.Add(lastBreak);
                    lineWidth -= widthAtBreak;
                }
                else
                {
                    soft.Add(display.Length);
                    display.Append('\n');
                    lineWidth = 0f;
                }

                lastBreak = -1;
            }

            display.Append(character);
            lineWidth += step;

            if (character == ' ')
            {
                lastBreak = display.Length;
                widthAtBreak = lineWidth;
            }
        }

        return (display.ToString(), soft.ToArray());
    }

    public static string Unwrap(string display, IReadOnlyList<int> softBreaks)
    {
        if (softBreaks.Count == 0)
            return display;

        var text = new StringBuilder(display.Length);
        var next = 0;

        for (var index = 0; index < display.Length; index++)
        {
            if (next < softBreaks.Count && softBreaks[next] == index)
            {
                next++;
                continue;
            }

            text.Append(display[index]);
        }

        return text.ToString();
    }

    // The breaks before the edit keep their place, the ones after it move with the text, the ones inside it are gone.
    public static int[] Carry(string before, IReadOnlyList<int> softBreaks, string after)
    {
        var shortest = Math.Min(before.Length, after.Length);
        var prefix = 0;

        while (prefix < shortest && before[prefix] == after[prefix])
            prefix++;

        var suffix = 0;

        while (suffix < shortest - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            suffix++;

        var delta = after.Length - before.Length;
        var carried = new List<int>(softBreaks.Count);

        foreach (var position in softBreaks)
        {
            var moved = position < prefix ? position : position >= before.Length - suffix ? position + delta : -1;

            if (moved >= 0 && moved < after.Length && after[moved] == '\n')
                carried.Add(moved);
        }

        return carried.ToArray();
    }

    // Removes the character before the cursor, or the one after it when forward, as Backspace and Delete do. A
    // surrogate pair goes as one character.
    public static (string Text, int Cursor) DeleteAcross(string text, int cursor, bool forward)
    {
        if (forward)
        {
            if (cursor >= text.Length)
                return (text, cursor);

            var count = cursor + 1 < text.Length && char.IsSurrogatePair(text[cursor], text[cursor + 1]) ? 2 : 1;
            return (text.Remove(cursor, count), cursor);
        }

        if (cursor <= 0)
            return (text, cursor);

        var back = cursor >= 2 && char.IsSurrogatePair(text[cursor - 2], text[cursor - 1]) ? 2 : 1;
        return (text.Remove(cursor - back, back), cursor - back);
    }

    public static int TextIndex(int displayIndex, IReadOnlyList<int> softBreaks)
    {
        var before = 0;

        foreach (var position in softBreaks)
        {
            if (position < displayIndex)
                before++;
        }

        return displayIndex - before;
    }

    public static int DisplayIndex(int textIndex, IReadOnlyList<int> softBreaks)
    {
        var display = textIndex;

        for (var index = 0; index < softBreaks.Count; index++)
        {
            if (softBreaks[index] - index <= textIndex)
                display++;
        }

        return display;
    }
}
