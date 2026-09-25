using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NoireLib.Localizer;

// The {name} placeholders a translation must keep: every one its source has, and no other, since a text fills them by
// name. A translation that renames one, such as {target} into {cible}, would show the name instead of the value.
internal static partial class PlaceholderCheck
{
    [GeneratedRegex(@"\{[^{}\s]+\}")]
    private static partial Regex Placeholder();

    // The placeholders of the source the translation lacks, and those of the translation the source does not have.
    public static (string[] Missing, string[] Unknown) Compare(string source, string translation)
    {
        var expected = Collect(source);
        var found = Collect(translation);
        var missing = new List<string>();
        var unknown = new List<string>();

        foreach (var name in expected)
        {
            if (!found.Contains(name))
                missing.Add(name);
        }

        foreach (var name in found)
        {
            if (!expected.Contains(name))
                unknown.Add(name);
        }

        return ([.. missing], [.. unknown]);
    }

    private static List<string> Collect(string text)
    {
        var names = new List<string>();

        if (text.IndexOf('{') < 0)
            return names;

        foreach (Match match in Placeholder().Matches(text))
        {
            if (!names.Contains(match.Value))
                names.Add(match.Value);
        }

        return names;
    }
}
