using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.UI;

// Ids are byte-identical to the interpolation each call replaces, since they travel into NoireUiState keys.
// Draw thread only: the cache is unsynchronised.
internal static class UiIds
{
    private enum Shape
    {
        Owner,

        OwnerSuffix,

        OwnerSuffixRaw,

        OwnerIndex,

        LabelOwner,

        LabelOwnerSuffix,

        LabelOwnerSuffixIndex,
    }

    // The shape is part of the key, so two shapes that happen to share their parts cannot return each other's string.
    private readonly record struct Key(Shape Shape, string Prefix, string Owner, string Suffix, string Label, int Index);

    private const int MaxEntries = 4096;

    private static readonly Dictionary<Key, string> Cache = new();

    // {prefix}{owner}
    internal static string For(string prefix, string owner)
        => Resolve(new Key(Shape.Owner, prefix, owner ?? string.Empty, string.Empty, string.Empty, 0));

    // {prefix}{owner}_{index}
    internal static string For(string prefix, string owner, int index)
        => Resolve(new Key(Shape.OwnerIndex, prefix, owner ?? string.Empty, string.Empty, string.Empty, index));

    // {prefix}{owner}_{suffix}
    internal static string For(string prefix, string owner, string suffix)
        => Resolve(new Key(Shape.OwnerSuffix, prefix, owner ?? string.Empty, suffix ?? string.Empty, string.Empty, 0));

    // {prefix}{owner}{suffix}, for a key whose separators are already in the literals.
    internal static string Join(string prefix, string owner, string suffix)
        => Resolve(new Key(Shape.OwnerSuffixRaw, prefix, owner ?? string.Empty, suffix ?? string.Empty, string.Empty, 0));

    // {label}{prefix}{owner}
    internal static string Labelled(string label, string prefix, string owner)
        => Resolve(new Key(Shape.LabelOwner, prefix, owner ?? string.Empty, string.Empty, label ?? string.Empty, 0));

    // {label}{prefix}{owner}_{suffix}
    internal static string Labelled(string label, string prefix, string owner, string suffix)
        => Resolve(new Key(Shape.LabelOwnerSuffix, prefix, owner ?? string.Empty, suffix ?? string.Empty, label ?? string.Empty, 0));

    // {label}{prefix}{owner}{suffix}{index}, with no separators of its own.
    internal static string Labelled(string label, string prefix, string owner, string suffix, int index)
        => Resolve(new Key(Shape.LabelOwnerSuffixIndex, prefix, owner ?? string.Empty, suffix ?? string.Empty, label ?? string.Empty, index));

    private static string Resolve(Key key)
    {
        if (Cache.TryGetValue(key, out var existing))
            return existing;

        var index = key.Index.ToString(CultureInfo.InvariantCulture);

        var built = key.Shape switch
        {
            Shape.Owner => key.Prefix + key.Owner,
            Shape.OwnerSuffix => key.Prefix + key.Owner + "_" + key.Suffix,
            Shape.OwnerSuffixRaw => key.Prefix + key.Owner + key.Suffix,
            Shape.OwnerIndex => key.Prefix + key.Owner + "_" + index,
            Shape.LabelOwner => key.Label + key.Prefix + key.Owner,
            Shape.LabelOwnerSuffix => key.Label + key.Prefix + key.Owner + "_" + key.Suffix,
            _ => key.Label + key.Prefix + key.Owner + key.Suffix + index,
        };

        if (Cache.Count >= MaxEntries)
            Cache.Clear();

        Cache[key] = built;
        return built;
    }
}
