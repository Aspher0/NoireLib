using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// A searchable pick list: the search, the matching items and their scroll. Filtering runs when the search or
/// <see cref="Version"/> changes, never per frame.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class NoirePicker<T>
{
    private readonly Func<IReadOnlyList<T>> source;
    private readonly Func<T, string, bool> matches;
    private readonly List<T> visible = [];
    private string search = string.Empty;
    private string? builtSearch;
    private int builtVersion;

    /// <summary>Creates a picker.</summary>
    /// <param name="source">The items to pick from.</param>
    /// <param name="matches">Whether an item matches a non-empty search.</param>
    public NoirePicker(Func<IReadOnlyList<T>> source, Func<T, string, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(matches);

        this.source = source;
        this.matches = matches;
    }

    /// <summary>Moved by the owner when the source items changed, to filter again.</summary>
    public int Version { get; set; }

    /// <summary>The search text.</summary>
    public string Search
    {
        get => search;
        set => search = value ?? string.Empty;
    }

    /// <summary>The scroll of the result list, back at the top whenever the results change.</summary>
    public NoireListState List { get; } = new();

    /// <summary>Items listed but not pickable again, such as ones already added.</summary>
    public Func<T, bool>? Marked { get; init; }

    /// <summary>The items matching the search, in source order.</summary>
    public IReadOnlyList<T> Visible
    {
        get
        {
            if (builtVersion == Version && string.Equals(builtSearch, search, StringComparison.Ordinal))
                return visible;

            builtVersion = Version;
            builtSearch = search;
            visible.Clear();

            var items = source();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (search.Length == 0 || matches(item, search))
                    visible.Add(item);
            }

            List.Reset();
            return visible;
        }
    }

    /// <summary>Whether an item is marked.</summary>
    /// <param name="item">The item.</param>
    /// <returns>True when it is listed but not pickable.</returns>
    public bool IsMarked(T item) => Marked?.Invoke(item) ?? false;
}
