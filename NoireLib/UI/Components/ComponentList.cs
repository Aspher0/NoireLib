using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// A component whose children are one row per item of a list, named <c>row[key]</c> by the item's key. A row keeps its
/// id and path while its item stays in the list, whatever its position, and is disposed when its item leaves.
/// </summary>
/// <typeparam name="TItem">The item type. Rows are matched to items by equality.</typeparam>
/// <typeparam name="TRow">The row component type.</typeparam>
public sealed class ComponentList<TItem, TRow> : Component
    where TItem : notnull
    where TRow : Component
{
    private readonly Func<TItem, string> key;
    private readonly Func<TItem, TRow> create;
    private readonly Dictionary<TItem, TRow> rows;
    private readonly List<TRow> ordered = [];
    private readonly List<TItem> stale = [];
    private int stamp;

    /// <summary>Creates the list.</summary>
    /// <param name="key">An item's key, read once when its row is created; unique among the items.</param>
    /// <param name="create">Creates the row of an item.</param>
    /// <param name="comparer">How items are matched to rows, or <see langword="null"/> for the default equality.</param>
    public ComponentList(Func<TItem, string> key, Func<TItem, TRow> create, IEqualityComparer<TItem>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(create);

        this.key = key;
        this.create = create;
        rows = new Dictionary<TItem, TRow>(comparer);
    }

    /// <summary>The rows, in the order of the items last synced.</summary>
    public IReadOnlyList<TRow> Rows => ordered;

    /// <summary>Matches the rows to the items: creates the rows of new items, disposes those of items that left, and orders the rest.</summary>
    /// <param name="items">The items, in display order.</param>
    /// <exception cref="ArgumentException">Thrown when two items share a key, or an item appears twice.</exception>
    public void Sync(IReadOnlyList<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        stamp++;
        var reordered = items.Count != ordered.Count;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (!rows.TryGetValue(item, out var row))
            {
                row = create(item);
                AddChild("row[" + key(item) + "]", row);
                rows[item] = row;
            }

            if (row.SyncStamp == stamp)
                throw new ArgumentException($"The item of '{row.Id}' appears twice.", nameof(items));

            row.SyncStamp = stamp;

            if (i >= ordered.Count)
            {
                ordered.Add(row);
                reordered = true;
            }
            else if (!ReferenceEquals(ordered[i], row))
            {
                ordered[i] = row;
                reordered = true;
            }
        }

        if (ordered.Count > items.Count)
            ordered.RemoveRange(items.Count, ordered.Count - items.Count);

        if (rows.Count != items.Count)
        {
            foreach (var (item, row) in rows)
            {
                if (row.SyncStamp != stamp)
                    stale.Add(item);
            }

            foreach (var item in stale)
            {
                var row = rows[item];
                rows.Remove(item);
                Remove(row);
            }

            stale.Clear();
            reordered = true;
        }

        if (reordered)
            ReorderChildren(ordered);
    }

    /// <inheritdoc/>
    protected override void OnDisposed()
    {
        rows.Clear();
        ordered.Clear();
    }
}
