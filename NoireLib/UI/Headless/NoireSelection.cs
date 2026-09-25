using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>Which items of a list are selected, with the click, Ctrl and Shift rules of a file explorer.</summary>
/// <typeparam name="TKey">What identifies an item.</typeparam>
public sealed class NoireSelection<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> selected = new();
    private TKey? anchor;
    private bool hasAnchor;

    /// <summary>The selected keys.</summary>
    public IReadOnlyCollection<TKey> Selected => selected;

    /// <summary>How many items are selected.</summary>
    public int Count => selected.Count;

    /// <summary>Whether an item is selected.</summary>
    /// <param name="key">The item.</param>
    /// <returns>True when it is.</returns>
    public bool IsSelected(TKey key) => selected.Contains(key);

    /// <summary>Applies a click: alone it selects one item, Ctrl toggles one, Shift selects the range from the last click.</summary>
    /// <param name="index">The clicked item's position in <paramref name="ordered"/>.</param>
    /// <param name="ordered">The items as they are listed.</param>
    /// <param name="ctrl">Whether Ctrl is held.</param>
    /// <param name="shift">Whether Shift is held.</param>
    public void Click(int index, IReadOnlyList<TKey> ordered, bool ctrl, bool shift)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        if ((uint)index >= (uint)ordered.Count)
            return;

        var from = shift ? IndexOfAnchor(ordered) : -1;

        if (from >= 0)
        {
            if (!ctrl)
                selected.Clear();

            var first = Math.Min(from, index);
            var last = Math.Max(from, index);

            for (var i = first; i <= last; i++)
                selected.Add(ordered[i]);

            return;
        }

        var key = ordered[index];

        if (ctrl)
        {
            if (!selected.Remove(key))
                selected.Add(key);
        }
        else
        {
            selected.Clear();
            selected.Add(key);
        }

        SetAnchor(key);
    }

    /// <summary>Selects only one item unless it is already selected, as a right click does before its menu.</summary>
    /// <param name="key">The item.</param>
    public void Focus(TKey key)
    {
        if (selected.Contains(key))
            return;

        selected.Clear();
        selected.Add(key);
        SetAnchor(key);
    }

    /// <summary>Drops the keys no longer in the list.</summary>
    /// <param name="present">The keys the list holds now.</param>
    public void Retain(IReadOnlyCollection<TKey> present)
    {
        ArgumentNullException.ThrowIfNull(present);

        if (hasAnchor && !Contains(present, anchor!))
            ClearAnchor();

        if (selected.Count == 0)
            return;

        List<TKey>? gone = null;

        foreach (var key in selected)
        {
            if (!Contains(present, key))
                (gone ??= []).Add(key);
        }

        if (gone == null)
            return;

        foreach (var key in gone)
            selected.Remove(key);
    }

    /// <summary>Clears the selection.</summary>
    public void Clear()
    {
        selected.Clear();
        ClearAnchor();
    }

    private void SetAnchor(TKey key)
    {
        anchor = key;
        hasAnchor = true;
    }

    private void ClearAnchor()
    {
        anchor = default;
        hasAnchor = false;
    }

    private int IndexOfAnchor(IReadOnlyList<TKey> ordered)
    {
        if (!hasAnchor)
            return -1;

        var comparer = EqualityComparer<TKey>.Default;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (comparer.Equals(ordered[i], anchor!))
                return i;
        }

        return -1;
    }

    private static bool Contains(IReadOnlyCollection<TKey> collection, TKey key)
    {
        if (collection is ICollection<TKey> fast)
            return fast.Contains(key);

        var comparer = EqualityComparer<TKey>.Default;

        foreach (var item in collection)
        {
            if (comparer.Equals(item, key))
                return true;
        }

        return false;
    }
}
