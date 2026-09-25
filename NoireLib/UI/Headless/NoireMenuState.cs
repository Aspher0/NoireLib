using System.Numerics;
using System.Threading;

namespace NoireLib.UI;

/// <summary>Whether a context menu is open, where, and when it closes. Drawn through <see cref="NoireUI.Menu{T}"/>.</summary>
public sealed class NoireMenuState
{
    private static int created;

    private int openedFrame = int.MinValue;

    // One ImGui window per menu, named once.
    internal string WindowName { get; } = "##noire.menu." + Interlocked.Increment(ref created);

    /// <summary>Whether the menu is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Where the menu opened, in screen pixels.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>The top left of the area the menu was drawn in last frame.</summary>
    public Vector2 Min { get; internal set; }

    /// <summary>The bottom right of that area.</summary>
    public Vector2 Max { get; internal set; }

    /// <summary>Opens the menu.</summary>
    /// <param name="at">Where, in screen pixels.</param>
    public void Open(Vector2 at)
    {
        Position = at;
        Min = Max = at;
        IsOpen = true;
        openedFrame = NoireUI.FrameCount;
    }

    /// <summary>Closes the menu.</summary>
    public void Close() => IsOpen = false;

    /// <summary>Whether a point is over the open menu.</summary>
    /// <param name="point">The point, in screen pixels.</param>
    /// <returns>True when it is.</returns>
    public bool Contains(Vector2 point)
        => IsOpen && point.X >= Min.X && point.Y >= Min.Y && point.X < Max.X && point.Y < Max.Y;

    // The press that opened the menu never closes it.
    internal void AfterDraw(bool itemClicked, bool dismissed)
    {
        if (itemClicked)
        {
            IsOpen = false;
            return;
        }

        if (NoireUI.FrameCount != openedFrame && dismissed)
            IsOpen = false;
    }
}
