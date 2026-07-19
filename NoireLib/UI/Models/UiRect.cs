using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// An axis-aligned rectangle in screen pixels, used wherever the library has to talk about a region rather than a
/// point: the bounds of a game addon, the area an element occupies, the box a label is projected into.
/// </summary>
/// <remarks>
/// Coordinates are real pixels, never logical ones. A rectangle here is measured or read from the game rather than
/// written by hand, so it is already at the scale the screen is at. See <see cref="NoireUI.Scale"/>.
/// </remarks>
/// <param name="Position">The top left corner.</param>
/// <param name="Size">The width and height.</param>
public readonly record struct UiRect(Vector2 Position, Vector2 Size)
{
    /// <summary>
    /// Creates a rectangle from its four edges.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public UiRect(float x, float y, float width, float height)
        : this(new Vector2(x, y), new Vector2(width, height))
    {
    }

    /// <summary>A rectangle at the origin with no size.</summary>
    public static UiRect Empty => default;

    /// <summary>The left edge.</summary>
    public float Left => Position.X;

    /// <summary>The top edge.</summary>
    public float Top => Position.Y;

    /// <summary>The right edge.</summary>
    public float Right => Position.X + Size.X;

    /// <summary>The bottom edge.</summary>
    public float Bottom => Position.Y + Size.Y;

    /// <summary>The bottom right corner.</summary>
    public Vector2 Max => Position + Size;

    /// <summary>The centre point.</summary>
    public Vector2 Center => Position + (Size * 0.5f);

    /// <summary>Whether the rectangle has no area, which is what an unresolved target reads as.</summary>
    public bool IsEmpty => Size.X <= 0f || Size.Y <= 0f;

    /// <summary>
    /// Creates a rectangle from two opposite corners, in either order.
    /// </summary>
    /// <param name="min">One corner.</param>
    /// <param name="max">The opposite corner.</param>
    /// <returns>The rectangle spanning both.</returns>
    public static UiRect FromBounds(Vector2 min, Vector2 max)
        => new(Vector2.Min(min, max), Vector2.Abs(max - min));

    /// <summary>
    /// The point at a normalized position inside the rectangle, from (0, 0) (top left) to (1, 1) (bottom right).
    /// </summary>
    /// <param name="ratio">The normalized position.</param>
    /// <returns>The point in screen pixels.</returns>
    public Vector2 PointAt(Vector2 ratio) => Position + (ratio * Size);

    /// <summary>
    /// The point at one of the nine anchors of the rectangle.
    /// </summary>
    /// <param name="anchor">The anchor to resolve.</param>
    /// <returns>The point in screen pixels.</returns>
    public Vector2 PointAt(UiAnchor anchor) => PointAt(UiPosition.GetAnchorRatio(anchor));

    /// <summary>
    /// Whether a point lies inside the rectangle, left and top edges included.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns>True when the point is inside.</returns>
    public bool Contains(Vector2 point)
        => point.X >= Left && point.Y >= Top && point.X < Right && point.Y < Bottom;

    /// <summary>
    /// Whether this rectangle overlaps another.
    /// </summary>
    /// <param name="other">The rectangle to test against.</param>
    /// <returns>True when the two overlap.</returns>
    public bool Intersects(UiRect other)
        => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    /// <summary>
    /// Grows the rectangle by an amount on every side, or shrinks it when the amount is negative.
    /// </summary>
    /// <param name="amount">The number of pixels to grow by.</param>
    /// <returns>The expanded rectangle, never smaller than zero in either axis.</returns>
    public UiRect Expand(float amount)
    {
        var size = Size + new Vector2(amount * 2f);
        return new UiRect(Position - new Vector2(amount), Vector2.Max(size, Vector2.Zero));
    }

    /// <summary>
    /// Clamps a rectangle of the given size so it stays fully inside this one.
    /// </summary>
    /// <remarks>
    /// An element larger than the bounds is pinned to the top left rather than pushed off the other side, which is the
    /// only choice that keeps the part a user reads first on screen.
    /// </remarks>
    /// <param name="position">The top left corner of the element.</param>
    /// <param name="size">The size of the element.</param>
    /// <returns>The clamped top left corner.</returns>
    public Vector2 Clamp(Vector2 position, Vector2 size)
    {
        var max = Max - size;
        return new Vector2(
            MathF.Max(Left, MathF.Min(position.X, max.X)),
            MathF.Max(Top, MathF.Min(position.Y, max.Y)));
    }
}
