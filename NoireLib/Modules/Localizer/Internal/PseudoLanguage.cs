using System;
using System.Text;

namespace NoireLib.Localizer;

// Accents mark localized text, brackets show clipping, padding reaches German length. The long form writes the text
// twice: a layout that clips or overlaps a long translation shows it.
internal static class PseudoLanguage
{
    private const string Plain = "aeiouyAEIOUYcnCN";
    private const string Accented = "áéíóúýÁÉÍÓÚÝçñÇÑ";

    internal static string Transform(string source, bool longForm = false)
    {
        var builder = new StringBuilder((source.Length * 2) + 3);
        builder.Append('[');
        Accent(source, builder);

        if (longForm && source.Length > 0)
        {
            builder.Append(' ');
            Accent(source, builder);
        }
        else
        {
            builder.Append(' ', Math.Max(1, source.Length * 3 / 10));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static void Accent(string source, StringBuilder builder)
    {
        var inPlaceholder = false;

        foreach (var c in source)
        {
            if (c == '{')
                inPlaceholder = true;
            else if (c == '}')
                inPlaceholder = false;

            var index = inPlaceholder ? -1 : Plain.IndexOf(c);
            builder.Append(index < 0 ? c : Accented[index]);
        }
    }
}
