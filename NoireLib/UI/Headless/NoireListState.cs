using System;

namespace NoireLib.UI;

/// <summary>Scroll and virtualization of a list whose rows share one height. No drawing.</summary>
public sealed class NoireListState
{
    private const float ScrollSeconds = 0.12f;

    private float target;
    private int count;
    private float rowHeight = 1f;
    private float viewport;
    private int reveal = -1;

    /// <summary>The scroll offset in pixels, always inside the content.</summary>
    public float Scroll { get; private set; }

    /// <summary>The first row to draw.</summary>
    public int First { get; private set; }

    /// <summary>One past the last row to draw.</summary>
    public int Last { get; private set; }

    /// <summary>The height of every row together.</summary>
    public float ContentHeight => count * rowHeight;

    /// <summary>The largest scroll offset.</summary>
    public float MaxScroll => MathF.Max(0f, ContentHeight - viewport);

    /// <summary>Where a row starts, relative to the top of the viewport.</summary>
    /// <param name="index">The row.</param>
    /// <returns>The offset in pixels.</returns>
    public float RowTop(int index) => (index * rowHeight) - Scroll;

    /// <summary>Lays the list out for this frame, clamping the scroll when the list got shorter.</summary>
    /// <param name="rowCount">How many rows the list has.</param>
    /// <param name="height">The height of one row, in pixels.</param>
    /// <param name="viewportHeight">The visible height, in pixels.</param>
    public void Layout(int rowCount, float height, float viewportHeight)
    {
        count = Math.Max(0, rowCount);
        rowHeight = MathF.Max(1f, height);
        viewport = MathF.Max(0f, viewportHeight);

        if (reveal >= 0)
        {
            var top = Math.Min(reveal, Math.Max(0, count - 1)) * rowHeight;

            if (top < target)
                target = top;
            else if (top + rowHeight > target + viewport)
                target = top + rowHeight - viewport;

            reveal = -1;
        }

        target = Math.Clamp(target, 0f, MaxScroll);

        Scroll = NoireUI.ReducedMotion
            ? target
            : Scroll + ((target - Scroll) * MathF.Min(1f, NoireUI.DeltaTime / ScrollSeconds));

        if (MathF.Abs(target - Scroll) < 0.5f)
            Scroll = target;

        First = Math.Min(count, Math.Max(0, (int)(Scroll / rowHeight)));
        Last = Math.Min(count, (int)MathF.Ceiling((Scroll + viewport) / rowHeight) + 1);
    }

    /// <summary>Scrolls to an offset at once, for a dragged scrollbar thumb.</summary>
    /// <param name="offset">The offset in pixels.</param>
    public void ScrollTo(float offset) => Scroll = target = Math.Clamp(offset, 0f, MaxScroll);

    /// <summary>Scrolls so a row is visible on the next layout.</summary>
    /// <param name="index">The row.</param>
    public void Reveal(int index) => reveal = Math.Max(0, index);

    /// <summary>Returns to the top at once.</summary>
    public void Reset()
    {
        Scroll = target = 0f;
        reveal = -1;
    }
}
