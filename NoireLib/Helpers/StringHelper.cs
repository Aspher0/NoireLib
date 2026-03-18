using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NoireLib.Helpers;

/// <summary>
/// Helper class with various string utility methods.
/// </summary>
public static class StringHelper
{
    /// <summary>
    /// Returns a shortened version of a long string, showing the beginning and end with ellipsis in between.
    /// </summary>
    /// <param name="longString">The original string to shorten.</param>
    /// <param name="charsToShow">The number of characters to show from the start and end of the string combined (excluding the ellipsis).</param>
    /// <returns>A shortned string. For example: "ARandomLongString" with <paramref name="charsToShow"/> set to 8 will return "ARan...ring".</returns>
    public static string ShortenString(this string longString, int charsToShow = 8)
    {
        if (longString.Length <= charsToShow)
            return longString;

        int frontChars = (int)Math.Ceiling(charsToShow / 2.0);
        int backChars = (int)Math.Floor(charsToShow / 2.0);
        return longString.Substring(0, frontChars) + "..." + longString.Substring(longString.Length - backChars);
    }

    /// <summary>
    /// Truncates a string to a specified maximum length, optionally adding an ellipsis.
    /// </summary>
    /// <param name="value">The string to truncate.</param>
    /// <param name="maxLength">The maximum length of the resulting string.</param>
    /// <param name="addEllipsis">Whether to add "..." at the end if truncated.</param>
    /// <returns>The truncated string.</returns>
    public static string Truncate(this string value, int maxLength, bool addEllipsis = true)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        if (addEllipsis && maxLength > 3)
            return value.Substring(0, maxLength - 3) + "...";

        return value.Substring(0, maxLength);
    }

    /// <summary>
    /// Removes all whitespace from a string.
    /// </summary>
    /// <param name="value">The string to process.</param>
    /// <returns>The string with all whitespace removed.</returns>
    public static string RemoveWhitespace(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    /// <summary>
    /// Converts a string to title case (first letter of each word capitalized).
    /// </summary>
    /// <param name="value">The string to convert.</param>
    /// <returns>The string in title case.</returns>
    public static string ToTitleCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLower());
    }

    /// <summary>
    /// Checks if a string contains another string, ignoring case.
    /// </summary>
    /// <param name="source">The source string to search in.</param>
    /// <param name="value">The value to search for.</param>
    /// <returns>True if the source contains the value (case-insensitive), false otherwise.</returns>
    public static bool ContainsIgnoreCase(this string source, string value)
    {
        if (source == null || value == null)
            return false;

        return source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a string equals another string, ignoring case.
    /// </summary>
    /// <param name="source">The source string.</param>
    /// <param name="value">The value to compare.</param>
    /// <returns>True if the strings are equal (case-insensitive), false otherwise.</returns>
    public static bool EqualsIgnoreCase(this string? source, string? value)
    {
        return string.Equals(source, value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reverses a string.
    /// </summary>
    /// <param name="value">The string to reverse.</param>
    /// <returns>The reversed string.</returns>
    public static string Reverse(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        char[] charArray = value.ToCharArray();
        Array.Reverse(charArray);
        return new string(charArray);
    }

    /// <summary>
    /// Counts the number of occurrences of a substring in a string.
    /// </summary>
    /// <param name="source">The source string to search in.</param>
    /// <param name="value">The substring to count.</param>
    /// <param name="ignoreCase">Whether to ignore case when counting.</param>
    /// <returns>The number of occurrences found.</returns>
    public static int CountOccurrences(this string source, string value, bool ignoreCase = false)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            return 0;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int count = 0;
        int index = 0;

        while ((index = source.IndexOf(value, index, comparison)) != -1)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    /// <summary>
    /// Removes all non-alphanumeric characters from a string.
    /// </summary>
    /// <param name="value">The string to process.</param>
    /// <returns>The string with only alphanumeric characters.</returns>
    public static string RemoveNonAlphanumeric(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return Regex.Replace(value, @"[^a-zA-Z0-9]", string.Empty);
    }

    /// <summary>
    /// Ensures a string ends with a specific suffix.
    /// </summary>
    /// <param name="value">The string to process.</param>
    /// <param name="suffix">The suffix to ensure.</param>
    /// <returns>The string with the suffix guaranteed to be at the end.</returns>
    public static string EnsureEndsWith(this string value, string suffix)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(suffix))
            return value;

        return value.EndsWith(suffix) ? value : value + suffix;
    }

    /// <summary>
    /// Ensures a string starts with a specific prefix.
    /// </summary>
    /// <param name="value">The string to process.</param>
    /// <param name="prefix">The prefix to ensure.</param>
    /// <returns>The string with the prefix guaranteed to be at the start.</returns>
    public static string EnsureStartsWith(this string value, string prefix)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(prefix))
            return value;

        return value.StartsWith(prefix) ? value : prefix + value;
    }

    /// <summary>
    /// Repeats a string a specified number of times.
    /// </summary>
    /// <param name="value">The string to repeat.</param>
    /// <param name="count">The number of times to repeat.</param>
    /// <returns>The repeated string.</returns>
    public static string Repeat(this string value, int count)
    {
        if (string.IsNullOrEmpty(value) || count <= 0)
            return string.Empty;

        var sb = new StringBuilder(value.Length * count);
        for (int i = 0; i < count; i++)
            sb.Append(value);

        return sb.ToString();
    }

    /// <summary>
    /// Removes diacritics (accents) from a string.
    /// </summary>
    /// <param name="value">The string to process.</param>
    /// <returns>The string without diacritics.</returns>
    public static string RemoveDiacritics(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var normalizedString = value.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(c);
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Removes all newline characters from a string.
    /// </summary>
    /// <param name="value">The string to process.</param>
    /// <returns>The string with all newline characters removed.</returns>
    public static string RemoveNewlines(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.ReplaceLineEndings(string.Empty);
    }

    /// <summary>
    /// Determines whether the content of two strings is equal, optionally ignoring case differences.<br/>
    /// The strings are stripped off of all whitespace characters before the comparison, so only the actual content of the strings is compared.
    /// </summary>
    /// <param name="value1">The first string to compare.</param>
    /// <param name="value2">The second string to compare.</param>
    /// <param name="ignoreCase">true to ignore case during the comparison; otherwise, false.</param>
    /// <returns>true if the content of both strings is equal according to the specified case sensitivity; otherwise, false.</returns>
    public static bool EqualsContentOnly(this string value1, string value2, bool ignoreCase = false)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var content1 = GetContentOnly(value1);
        var content2 = GetContentOnly(value2);

        return string.Equals(content1, content2, comparison);
    }

    /// <summary>
    /// Returns a new string containing only the non-whitespace characters from the input string.
    /// </summary>
    /// <param name="value">The string from which to remove all whitespace characters.</param>
    /// <returns>A string consisting of the non-whitespace characters from the input string.<br/>
    /// If the input is null or empty, the original value is returned.</returns>
    public static string GetContentOnly(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var sb = new StringBuilder();
        foreach (char c in value)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }
        return sb.ToString();
    }
}
