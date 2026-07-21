using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.UI;

/// <summary>
/// Builds the ImGui ids the widgets draw with, once each, and hands the same string back on every later frame.
/// </summary>
/// <remarks>
/// An id like <c>###NoireComboItem_myCombo_42</c> is a constant for the life of the widget, but written as an
/// interpolated string it is rebuilt on every frame it is drawn. A list of two hundred rows at sixty frames a second is
/// twelve thousand short-lived strings a second, for a set of values that never changed. That is not a large amount of
/// memory, but it is a steady stream of garbage in the one place a plugin cannot afford a collection: the draw thread.
/// <br/>
/// The strings this returns are byte-identical to the interpolation each call replaces, which is not a detail: widget
/// ids travel into <see cref="NoireUiState"/> keys, so an id that changed shape would silently orphan every value a
/// user had saved under the old one.<br/>
/// Reached only from the draw thread, so the dictionary needs no lock.
/// </remarks>
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
    /// <remarks>
    /// Ids are per widget and per row, so a plugin reaches a stable set almost immediately and never grows again. The
    /// bound is there for the case that does grow: an id built from a value rather than from a position, such as a tag
    /// suggestion keyed on the tag itself.
    /// </remarks>
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
