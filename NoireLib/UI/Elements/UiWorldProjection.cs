using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The arithmetic behind a world-anchored element: how far away is too far, how much smaller it gets with distance,
/// and where it goes once the point it follows leaves the screen.
/// </summary>
public static class UiWorldProjection
{
    /// <summary>
    /// How far from the centre a projected point has to be, in pixels, before its direction counts.
    /// </summary>
    private const float DirectionThreshold = 1f;

    /// <summary>
    /// How visible an element is at a given distance, fading out between the fade distance and the maximum.
    /// </summary>
    /// <param name="distance">The distance to the element, in yalms.</param>
    /// <param name="fadeStart">Where fading begins. At or below it the element is fully opaque.</param>
    /// <param name="maxDistance">Where the element is gone entirely. Zero or less means no limit and no fade.</param>
    /// <returns>An alpha multiplier from 0 to 1.</returns>
    public static float DistanceAlpha(float distance, float fadeStart, float maxDistance)
    {
        if (maxDistance <= 0f)
            return 1f;

        if (distance >= maxDistance)
            return 0f;

        if (distance <= fadeStart || fadeStart >= maxDistance)
            return 1f;

        return Math.Clamp((maxDistance - distance) / (maxDistance - fadeStart), 0f, 1f);
    }

    /// <summary>
    /// How large an element is at a given distance, shrinking as it recedes the way the world does.
    /// </summary>
    /// <param name="distance">The distance to the element, in yalms.</param>
    /// <param name="reference">The distance at which the element is drawn at its authored size.</param>
    /// <param name="minScale">The smallest it may become.</param>
    /// <param name="maxScale">The largest it may become.</param>
    /// <returns>A scale multiplier.</returns>
    public static float DistanceScale(float distance, float reference, float minScale, float maxScale)
    {
        if (maxScale < minScale)
            (minScale, maxScale) = (maxScale, minScale);

        if (reference <= 0f || distance <= 0f)
            return maxScale;

        return Math.Clamp(reference / distance, minScale, maxScale);
    }

    /// <summary>
    /// How large an element is at a given distance, ramping between two distances the way the distance fade does.<br/>
    /// A range that does not run forwards is treated as a hard change at <paramref name="from"/> rather than as an error.
    /// </summary>
    /// <param name="distance">The distance to the element, in yalms.</param>
    /// <param name="from">Where shrinking begins. At or below it the element is at <paramref name="maxScale"/>.</param>
    /// <param name="to">Where shrinking ends. At or beyond it the element is at <paramref name="minScale"/>.</param>
    /// <param name="minScale">The smallest it may become.</param>
    /// <param name="maxScale">The largest it may become.</param>
    /// <returns>A scale multiplier.</returns>
    public static float RampScale(float distance, float from, float to, float minScale, float maxScale)
    {
        if (maxScale < minScale)
            (minScale, maxScale) = (maxScale, minScale);

        if (distance <= from)
            return maxScale;

        if (distance >= to)
            return minScale;

        return maxScale - ((maxScale - minScale) * ((distance - from) / (to - from)));
    }

    /// <summary>
    /// Rounds a scale to a multiple of a step, so a value that varies continuously takes a small number of distinct
    /// values instead.
    /// </summary>
    /// <param name="scale">The scale to round.</param>
    /// <param name="step">The step to round to. Zero or less leaves the scale untouched.</param>
    /// <returns>The stepped scale, never zero or negative.</returns>
    public static float QuantizeScale(float scale, float step)
    {
        if (step <= 0f)
            return scale;

        return MathF.Max(step, MathF.Round(scale / step) * step);
    }

    /// <summary>
    /// The direction from the centre of the viewport toward a projected point, for placing a marker that has to sit on
    /// an edge rather than on the point itself.
    /// </summary>
    /// <param name="screen">The projected point.</param>
    /// <param name="viewport">The viewport it was projected into.</param>
    /// <returns>The direction, which is not normalized.</returns>
    public static Vector2 OffScreenDirection(Vector2 screen, UiRect viewport)
    {
        var delta = screen - viewport.Center;

        return delta.LengthSquared() > DirectionThreshold * DirectionThreshold ? delta : new Vector2(0f, 1f);
    }

    /// <summary>
    /// Places an element against the edge of the viewport, along a direction from the centre.
    /// </summary>
    /// <param name="viewport">The viewport to stay inside.</param>
    /// <param name="direction">The direction from the centre, from <see cref="OffScreenDirection"/>. Need not be normalized.</param>
    /// <param name="size">The size of the element being placed.</param>
    /// <param name="margin">How far to stay clear of the edges.</param>
    /// <returns>Where the centre of the element goes.</returns>
    public static Vector2 PinToEdge(UiRect viewport, Vector2 direction, Vector2 size, float margin)
    {
        var inset = viewport.Expand(-margin);
        var center = inset.Center;

        // How far the centre of the element may travel before the element itself touches an edge. Never negative, so an
        // element too large for the viewport stops moving on that axis instead of being pushed out the other side.
        var reach = Vector2.Max((inset.Size - size) * 0.5f, Vector2.Zero);
        var travel = EdgeDistance(reach * 2f, direction);

        if (float.IsInfinity(travel))
            return center;

        // Clamped as well as scaled, so the degenerate cases (an element wider than the screen, a direction along an
        // axis with no room left) stay inside the box rather than leaving it along the axis that still had room.
        return center + Vector2.Clamp(direction * travel, -reach, reach);
    }

    /// <summary>
    /// How far it is from the centre of a box to its edge along a direction, measured in multiples of that direction.
    /// </summary>
    /// <param name="size">The size of the box.</param>
    /// <param name="direction">The direction from the centre. Need not be normalized.</param>
    /// <returns>The multiple of <paramref name="direction"/> that reaches the edge, or infinity when there is no edge to reach.</returns>
    public static float EdgeDistance(Vector2 size, Vector2 direction)
    {
        var half = size * 0.5f;
        var distance = float.PositiveInfinity;

        if (half.X > 0f && MathF.Abs(direction.X) > float.Epsilon)
            distance = MathF.Min(distance, half.X / MathF.Abs(direction.X));

        if (half.Y > 0f && MathF.Abs(direction.Y) > float.Epsilon)
            distance = MathF.Min(distance, half.Y / MathF.Abs(direction.Y));

        return distance;
    }

    /// <summary>
    /// The angle an edge arrow points at, in radians, with zero pointing right.
    /// </summary>
    /// <param name="direction">The direction to point. Need not be normalized.</param>
    /// <returns>The angle in radians, or zero when the direction is empty.</returns>
    public static float ArrowAngle(Vector2 direction)
        => direction.LengthSquared() <= float.Epsilon ? 0f : MathF.Atan2(direction.Y, direction.X);

    /// <summary>
    /// The angle an edge arrow points at, in radians, with zero pointing right.
    /// </summary>
    /// <param name="from">Where the arrow is drawn.</param>
    /// <param name="to">What it points at.</param>
    /// <returns>The angle in radians, or zero when the two coincide.</returns>
    public static float ArrowAngle(Vector2 from, Vector2 to) => ArrowAngle(to - from);

    /// <summary>
    /// The three points of a triangular arrow of the given size, pointing along an angle.
    /// </summary>
    /// <param name="tip">Where the point of the arrow sits.</param>
    /// <param name="angle">The direction it points, in radians.</param>
    /// <param name="size">The length of the arrow from tip to base.</param>
    /// <param name="points">Receives the three corners.</param>
    public static void ArrowPoints(Vector2 tip, float angle, float size, Span<Vector2> points)
    {
        if (points.Length < 3)
            throw new ArgumentException("An arrow needs room for three points.", nameof(points));

        var forward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var side = new Vector2(-forward.Y, forward.X) * (size * 0.5f);
        var back = tip - (forward * size);

        points[0] = tip;
        points[1] = back + side;
        points[2] = back - side;
    }
}
