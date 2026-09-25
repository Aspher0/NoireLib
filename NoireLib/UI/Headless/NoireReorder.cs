using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>Reordering a list by dragging one of its rows. The drawing reports the row under the mouse; the list is the caller's.</summary>
public sealed class NoireReorder
{
    /// <summary>The row being moved, or -1.</summary>
    public int From { get; private set; } = -1;

    /// <summary>Where it would land, or -1.</summary>
    public int To { get; private set; } = -1;

    /// <summary>Whether a row is being moved.</summary>
    public bool Active => From >= 0;

    /// <summary>Starts moving a row.</summary>
    /// <param name="index">The row.</param>
    public void Begin(int index)
    {
        From = index;
        To = index;
    }

    /// <summary>Reports the row under the mouse while moving.</summary>
    /// <param name="index">The row.</param>
    public void Over(int index)
    {
        if (Active)
            To = index;
    }

    /// <summary>Ends the move.</summary>
    /// <param name="from">The row that moved.</param>
    /// <param name="to">Where it lands.</param>
    /// <returns>True when the row changed place.</returns>
    public bool End(out int from, out int to)
    {
        from = From;
        to = To;
        From = To = -1;
        return from >= 0 && to >= 0 && from != to;
    }

    /// <summary>Applies a finished move to a list. A target past either end lands at that end.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="list">The list.</param>
    /// <param name="from">The row that moved.</param>
    /// <param name="to">Where it lands.</param>
    /// <returns>True when the list changed.</returns>
    public static bool Apply<T>(IList<T> list, int from, int to) => NoireReorderableList<T>.MoveItem(list, from, to);
}
