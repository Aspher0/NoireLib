using System;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.Localizer;

// "key = text" per line, "#" comments, "@credits = A, B" names the translators, and "# Source: text" is the source a
// translation was made from. Other comments belong to the key under them. The writer's own notes are rewritten.
internal static class LanguageFile
{
    internal const string CreditsKey = "@credits";

    private const string SourcePrefix = "# Source: ";
    private const string ChangedPrefix = "# Outdated, the source is now: ";
    private const string UnusedNote = "# Unused: the plugin no longer has this text.";
    private const string MissingTagsPrefix = "# Missing tags: ";
    private const string UnknownTagsPrefix = "# Unknown tags: ";

    // The names a credits line lists, in order, without blanks.
    internal static string[] Credits(string? line)
        => string.IsNullOrWhiteSpace(line) ? [] : line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // basis, when given, receives the source each translation was made from; notes receives the other comments above each
    // key, as written, a key without a translation included.
    internal static Dictionary<string, string> Parse(string text, Dictionary<string, string>? basis = null, Dictionary<string, string>? notes = null)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var span = text.AsSpan();
        string? source = null;
        var pending = new StringBuilder();

        if (span.Length > 0 && span[0] == '\uFEFF')
            span = span[1..];

        while (span.Length > 0)
        {
            var end = span.IndexOf('\n');
            var line = end < 0 ? span : span[..end];
            span = end < 0 ? ReadOnlySpan<char>.Empty : span[(end + 1)..];

            line = line.Trim();

            if (line.Length == 0)
                continue;

            if (line[0] == '#')
            {
                if (line.StartsWith(SourcePrefix))
                    source = Unescape(line[SourcePrefix.Length..]);
                else if (!line.StartsWith(ChangedPrefix) && !line.SequenceEqual(UnusedNote) && !line.StartsWith(MissingTagsPrefix)
                    && !line.StartsWith(UnknownTagsPrefix))
                    pending.Append(line).Append('\n');

                continue;
            }

            var equals = line.IndexOf('=');

            if (equals <= 0)
                continue;

            var key = line[..equals].Trim().ToString();
            var value = line[(equals + 1)..].Trim();

            if (key.Length > 0)
            {
                if (value.Length > 0)
                {
                    table[key] = Unescape(value);

                    if (basis != null && source != null)
                        basis[key] = source;
                }

                if (notes != null && pending.Length > 0)
                    notes[key] = pending.ToString();
            }

            source = null;
            pending.Clear();
        }

        return table;
    }

    // Each key under its notes and the source its translation was made from: basisOf's answer, else the current source.
    // A translation made from another source gets a note with the current one; a key no longer declared gets one too.
    internal static string Write(IReadOnlyDictionary<string, string> table, Func<string, string?> sourceOf, Func<string, string?>? basisOf = null,
        Func<string, string?>? notesOf = null)
    {
        var keys = new List<string>(table.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder(keys.Count * 48);

        foreach (var key in keys)
        {
            var value = table[key];

            if (notesOf?.Invoke(key) is { Length: > 0 } notes)
                builder.Append(notes);

            if (key.StartsWith('@'))
            {
                builder.Append(key).Append(" = ").Append(Escape(value)).Append('\n');
                continue;
            }

            var source = sourceOf(key);
            var basis = value.Length > 0 ? basisOf?.Invoke(key) ?? source : source;

            if (basis != null)
                builder.Append(SourcePrefix).Append(Escape(basis)).Append('\n');

            if (source == null)
                builder.Append(UnusedNote).Append('\n');
            else if (basis != null && !string.Equals(basis, source, StringComparison.Ordinal))
                builder.Append(ChangedPrefix).Append(Escape(source)).Append('\n');

            // A translation must keep the source's {name} placeholders as they are, since the text fills them by name.
            if (source != null && value.Length > 0)
            {
                var (missing, unknown) = PlaceholderCheck.Compare(source, value);

                if (missing.Length > 0)
                    builder.Append(MissingTagsPrefix).AppendJoin(", ", missing).Append('\n');

                if (unknown.Length > 0)
                    builder.Append(UnknownTagsPrefix).AppendJoin(", ", unknown).Append('\n');
            }

            builder.Append(key).Append(" = ").Append(Escape(value)).Append('\n');
        }

        return builder.ToString();
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r", string.Empty).Replace("\n", "\\n");

    private static string Unescape(ReadOnlySpan<char> text)
    {
        if (text.IndexOf('\\') < 0)
            return text.ToString();

        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];

                if (next == 'n')
                {
                    builder.Append('\n');
                    i++;
                    continue;
                }

                if (next == '\\')
                {
                    builder.Append('\\');
                    i++;
                    continue;
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
