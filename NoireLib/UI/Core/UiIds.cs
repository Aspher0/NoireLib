using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.UI;

/// <summary>
/// Builds the ImGui ids the widgets draw with, once each, and hands the same string back on every later frame.<br/>
/// The strings are byte-identical to the interpolation each call replaces, since ids travel into
/// <see cref="NoireUiState"/> keys. Draw thread only: the cache is unsynchronised.
/// </summary>
internal static class UiIds
{
    /// <summary>
    /// Which parts an id is assembled from, and in what order.
    /// </summary>
    private enum Shape
    {
        /// <summary><c>{prefix}{owner}</c></summary>
        Owner,

        /// <summary><c>{prefix}{owner}_{suffix}</c></summary>
        OwnerSuffix,

        /// <summary><c>{prefix}{owner}{suffix}</c>, with no separator of its own.</summary>
        OwnerSuffixRaw,

        /// <summary><c>{prefix}{owner}_{index}</c></summary>
        OwnerIndex,

        /// <summary><c>{label}{prefix}{owner}</c></summary>
        LabelOwner,

        /// <summary><c>{label}{prefix}{owner}_{suffix}</c></summary>
        LabelOwnerSuffix,

        /// <summary><c>{label}{prefix}{owner}{suffix}{index}</c></summary>
        LabelOwnerSuffixIndex,
    }

    /// <summary>
    /// Everything an id is built from. The shape is part of the key, so two shapes that happen to share their parts
    /// cannot return each other's string.
    /// </summary>
    private readonly record struct Key(Shape Shape, string Prefix, string Owner, string Suffix, string Label, int Index);

    /// <summary>
    /// How many ids are kept before the cache starts over.
    /// </summary>
    private const int MaxEntries = 4096;

    private static readonly Dictionary<Key, string> Cache = new();

    /// <summary>
    /// An id of the form <c>{prefix}{owner}</c>.
    /// </summary>
    /// <param name="prefix">The widget's constant id prefix.</param>
    /// <param name="owner">The widget's own id.</param>
    /// <returns>The cached id string.</returns>
    internal static string For(string prefix, string owner)
        => Resolve(new Key(Shape.Owner, prefix, owner ?? string.Empty, string.Empty, string.Empty, 0));

    /// <summary>
    /// An id of the form <c>{prefix}{owner}_{index}</c>, for one row of a collection.
    /// </summary>
    /// <param name="prefix">The widget's constant id prefix.</param>
    /// <param name="owner">The widget's own id.</param>
    /// <param name="index">The row's position.</param>
    /// <returns>The cached id string.</returns>
    internal static string For(string prefix, string owner, int index)
        => Resolve(new Key(Shape.OwnerIndex, prefix, owner ?? string.Empty, string.Empty, string.Empty, index));

    /// <summary>
    /// An id of the form <c>{prefix}{owner}_{suffix}</c>, for a row identified by a value rather than a position.
    /// </summary>
    /// <param name="prefix">The widget's constant id prefix.</param>
    /// <param name="owner">The widget's own id.</param>
    /// <param name="suffix">The value identifying the row.</param>
    /// <returns>The cached id string.</returns>
    internal static string For(string prefix, string owner, string suffix)
        => Resolve(new Key(Shape.OwnerSuffix, prefix, owner ?? string.Empty, suffix ?? string.Empty, string.Empty, 0));

    /// <summary>
    /// An id of the form <c>{prefix}{owner}{suffix}</c>, for a key whose separators are already in the literals.
    /// </summary>
    /// <param name="prefix">The leading literal.</param>
    /// <param name="owner">The widget's own id.</param>
    /// <param name="suffix">The trailing literal.</param>
    /// <returns>The cached id string.</returns>
    internal static string Join(string prefix, string owner, string suffix)
        => Resolve(new Key(Shape.OwnerSuffixRaw, prefix, owner ?? string.Empty, suffix ?? string.Empty, string.Empty, 0));

    /// <summary>
    /// An id of the form <c>{label}{prefix}{owner}</c>, for a widget whose visible label is part of the id string.
    /// </summary>
    /// <param name="label">The visible label.</param>
    /// <param name="prefix">The widget's constant id prefix.</param>
    /// <param name="owner">The widget's own id.</param>
    /// <returns>The cached id string.</returns>
    internal static string Labelled(string label, string prefix, string owner)
        => Resolve(new Key(Shape.LabelOwner, prefix, owner ?? string.Empty, string.Empty, label ?? string.Empty, 0));

    /// <summary>
    /// An id of the form <c>{label}{prefix}{owner}_{suffix}</c>.
    /// </summary>
    /// <param name="label">The visible label.</param>
    /// <param name="prefix">The widget's constant id prefix.</param>
    /// <param name="owner">The widget's own id.</param>
    /// <param name="suffix">The value identifying the row.</param>
    /// <returns>The cached id string.</returns>
    internal static string Labelled(string label, string prefix, string owner, string suffix)
        => Resolve(new Key(Shape.LabelOwnerSuffix, prefix, owner ?? string.Empty, suffix ?? string.Empty, label ?? string.Empty, 0));

    /// <summary>
    /// An id of the form <c>{label}{prefix}{owner}{suffix}{index}</c>, with no separators of its own.
    /// </summary>
    /// <param name="label">The visible label.</param>
    /// <param name="prefix">The widget's constant id prefix.</param>
    /// <param name="owner">The widget's own id.</param>
    /// <param name="suffix">The literal that separates the owner from the position.</param>
    /// <param name="index">The row's position.</param>
    /// <returns>The cached id string.</returns>
    internal static string Labelled(string label, string prefix, string owner, string suffix, int index)
        => Resolve(new Key(Shape.LabelOwnerSuffixIndex, prefix, owner ?? string.Empty, suffix ?? string.Empty, label ?? string.Empty, index));

    /// <summary>
    /// Returns the id for a key, building it on the first ask.
    /// </summary>
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
