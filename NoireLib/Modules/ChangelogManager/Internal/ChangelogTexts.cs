using NoireLib.Helpers;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.Changelog;

// Each changelog text is a NoireString keyed by its version and a checksum of the text: an edited text asks for a
// new translation, while added, removed or reordered entries keep theirs.
internal sealed class ChangelogTexts
{
    private readonly Dictionary<string, NoireString> texts = new(StringComparer.Ordinal);
    private ChangelogVersion[] shown = [];
    private ChangelogVersion[]? shownFrom;
    private int shownRevision = -1;

    // The version with its texts in the active language; declares them on first sight.
    public ChangelogVersion Localize(ChangelogVersion version)
    {
        var prefix = "changelog." + version.Version + ".";
        var entries = new List<ChangelogEntry>(version.Entries.Count);

        foreach (var entry in version.Entries)
            entries.Add(entry with { Text = Text(prefix, entry.Text), ButtonText = Text(prefix, entry.ButtonText) });

        return version with
        {
            Title = Text(prefix, version.Title),
            Description = Text(prefix, version.Description),
            Entries = entries,
        };
    }

    // The same instances until the versions or the language change: callers may compare by reference.
    public ChangelogVersion[] Shown(ChangelogVersion[] sorted)
    {
        if (ReferenceEquals(shownFrom, sorted) && shownRevision == NoireLanguages.Revision)
            return shown;

        shownFrom = sorted;
        shownRevision = NoireLanguages.Revision;
        shown = new ChangelogVersion[sorted.Length];

        for (var index = 0; index < sorted.Length; index++)
            shown[index] = Localize(sorted[index]);

        return shown;
    }

    public ChangelogVersion? ShownOf(ChangelogVersion[] sorted, ChangelogVersion? source)
    {
        if (source == null)
            return null;

        var copies = Shown(sorted);

        for (var index = 0; index < sorted.Length; index++)
        {
            if (ReferenceEquals(sorted[index], source))
                return copies[index];
        }

        return Localize(source);
    }

    // The same text twice in one version shares one key, and so one translation.
    private string? Text(string prefix, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source;

        var key = prefix + Crc32Helper.Compute(Encoding.UTF8.GetBytes(source)).ToString("x8");

        if (!texts.TryGetValue(key, out var text))
            texts[key] = text = new NoireString(key, source);

        return text.Text;
    }
}
