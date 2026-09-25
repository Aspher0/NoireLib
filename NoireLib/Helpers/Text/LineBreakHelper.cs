using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Line breaking for scripts without spaces: Chinese and Japanese break between characters, except before a closing
/// mark or after an opening one. Every other script, Korean included, breaks at spaces.
/// </summary>
public static class LineBreakHelper
{
    private const string NoLineStart = "\uFF0C\u3002\u3001\uFF1A\uFF1B\uFF01\uFF1F\uFF09\u300D\u300F\u3011\u300B\u3009\u201D\u2019\u30FB\u30FC\u2026\uFF5E,.:;!?)]}%";
    private const string NoLineEnd = "\uFF08\u300C\u300E\u3010\u300A\u3008\u201C\u2018([{";

    /// <summary>Whether a text holds a Chinese or Japanese character, whose lines may break between characters.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when the text has a character a line may break beside without a space.</returns>
    public static bool ContainsCjk(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var c in text)
        {
            if (IsCjk(c))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a line may break between two characters with no space between them: beside a Chinese or Japanese
    /// character, unless the second one must not start a line or the first one must not end one.
    /// </summary>
    /// <param name="before">The character that would end the line.</param>
    /// <param name="after">The character that would start the next line.</param>
    /// <returns>True when the break is allowed.</returns>
    public static bool CanBreakBetween(char before, char after)
    {
        if (before == ' ' || after == ' ' || (!IsCjk(before) && !IsCjk(after)))
            return false;

        return !NoLineStart.Contains(after) && !NoLineEnd.Contains(before);
    }

    /// <summary>Cuts a paragraph into lines no wider than <paramref name="maxWidth"/>. A run with no break stays whole.</summary>
    /// <param name="paragraph">The paragraph, without line feeds.</param>
    /// <param name="maxWidth">The widest a line may be, in the unit <paramref name="width"/> measures in.</param>
    /// <param name="width">Measures a piece of the paragraph.</param>
    /// <param name="lines">Receives the lines, trailing spaces removed.</param>
    public static void Break(string paragraph, float maxWidth, Func<string, float> width, List<string> lines)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(width);
        ArgumentNullException.ThrowIfNull(lines);

        var start = 0;

        while (start < paragraph.Length)
        {
            var fits = -1;
            var first = -1;

            for (var at = start + 1; at <= paragraph.Length; at++)
            {
                if (at < paragraph.Length && !CanBreakAt(paragraph, at))
                    continue;

                if (first < 0)
                    first = at;

                if (width(paragraph[start..TrimEnd(paragraph, start, at)]) > maxWidth)
                    break;

                fits = at;
            }

            var stop = fits > start ? fits : first;
            lines.Add(paragraph[start..TrimEnd(paragraph, start, stop)]);
            start = stop;

            while (start < paragraph.Length && paragraph[start] == ' ')
                start++;
        }
    }

    // A break falls after a space, or where CanBreakBetween allows it.
    private static bool CanBreakAt(string text, int at)
        => text[at] != ' ' && (text[at - 1] == ' ' || CanBreakBetween(text[at - 1], text[at]));

    // The CJK radicals to unified ideographs, kana and CJK punctuation included, the compatibility ideographs and the
    // full-width forms.
    private static bool IsCjk(char c)
        => c is (>= '\u2E80' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF') or (>= '\uFF00' and <= '\uFFEF');

    private static int TrimEnd(string text, int start, int end)
    {
        while (end > start && text[end - 1] == ' ')
            end--;

        return end;
    }
}
