using System;

namespace NoireLib.Internal.Helpers;

// Whole words only matches occurrences no letter or digit touches: "Replace" finds "Replace an emote", not "Replacement".
internal static class TextMatches
{
    public static bool Any(string text, string search, bool wholeWord) => Next(text, search, wholeWord, 0) >= 0;

    // The start of the first occurrence at or after the given index, or -1. The occurrence is as long as the search.
    public static int Next(string text, string search, bool wholeWord, int from)
    {
        if (search.Length == 0 || from > text.Length)
            return -1;

        for (var at = text.IndexOf(search, from, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(search, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = at + search.Length;

            if (!wholeWord || ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end]))))
                return at;
        }

        return -1;
    }
}
