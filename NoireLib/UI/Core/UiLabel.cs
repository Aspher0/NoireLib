using NoireLib.Helpers;
using System;

namespace NoireLib.UI;

// The strings are equal to the substrings each call replaces, since ids travel into NoireUiState keys.
// Draw thread only: the caches are unsynchronised.
internal static class UiLabel
{
    private const int MaxEntries = 4096;

    // A record struct rather than a bare string, since HotPathCache takes a struct key and this keeps a lookup from
    // boxing.
    private readonly record struct Key(string Label);

    private readonly record struct Parts(string Visible, string Id);

    private static readonly HotPathCache<Key, string> Visibles = new(MaxEntries);
    private static readonly HotPathCache<Key, Parts> Stables = new(MaxEntries);

    internal static string Visible(string label)
    {
        if (label == null)
            return string.Empty;

        var marker = label.IndexOf("##", StringComparison.Ordinal);

        // A label with no marker is already its own visible text, so it returns before the cache is consulted: a
        // dictionary lookup to arrive back at the argument would cost more than the substring this exists to avoid.
        if (marker < 0)
            return label;

        var key = new Key(label);

        if (Visibles.TryGet(key, out var cached))
            return cached;

        var visible = label[..marker];
        Visibles.Set(key, visible);

        return visible;
    }

    internal static void Split(string label, out string visible, out string id)
    {
        if (label == null)
        {
            visible = string.Empty;
            id = string.Empty;
            return;
        }

        var marker = label.IndexOf("###", StringComparison.Ordinal);

        if (marker < 0)
        {
            visible = label;
            id = label;
            return;
        }

        var key = new Key(label);

        if (Stables.TryGet(key, out var cached))
        {
            visible = cached.Visible;
            id = cached.Id;
            return;
        }

        visible = label[..marker];
        id = label[(marker + 3)..];

        Stables.Set(key, new Parts(visible, id));
    }
}
