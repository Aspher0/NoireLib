using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The arithmetic behind a world-anchored element: how far away is too far, how much smaller it gets with distance,
/// and where it goes once the point it follows leaves the screen.
/// </summary>
/// <remarks>
/// Kept apart from the drawing so all of it can be reasoned about, and tested, without a camera or a game running.
/// The projection itself is the game's own, through <c>IGameGui.WorldToScreen</c>; everything after it is here.<br/>
/// One property of that projection shapes everything below. It divides by the absolute value of the clip-space w
/// rather than by w itself, so a point behind the camera comes back already reflected through the centre of the
/// screen: its direction from the centre is the true one, and it stays continuous as a point crosses the camera
/// plane. Only the magnitude is meaningless there, which is why an off-screen marker is placed from the direction
/// alone and never from the projected coordinate.
/// </remarks>
public static class UiWorldProjection
{
    /// <summary>
    /// How far from the centre a projected point has to be, in pixels, before the direction to it is worth reading.
    /// </summary>
    /// <remarks>
    /// A point almost exactly behind the camera projects onto the centre, where the direction is whatever the last bit
    /// of floating-point noise said. Below this it is treated as having no direction at all rather than one that spins.
    /// </remarks>
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
    /// <remarks>
    /// The scale is the reference distance over the actual one, so an element is exactly its authored size at the
    /// reference and half of it at twice that, clamped at both ends. Clamping is what stops a marker underfoot from
    /// filling the screen and one across the zone from becoming a single unreadable pixel.
    /// </remarks>
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
    /// How large an element is at a given distance, ramping between two distances the way the distance fade does.
    /// </summary>
    /// <remarks>
    /// The alternative to <see cref="DistanceScale"/>, and the one to reach for when the two distances that matter are
    /// the ones you can name. Perspective shrinking is authored by a reference distance and a pair of clamps, which is
    /// physically right but answers "where does it stop shrinking" only indirectly; this states both ends outright, and
    /// reads as the same pair of numbers as <see cref="DistanceAlpha"/>.<br/>
    /// A range that does not run forwards is treated as a hard change at <paramref name="from"/> rather than as an
    /// error, which is what a slider dragged past its partner produces.
    /// </remarks>
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
    /// <remarks>
    /// This exists for text. Drawing at a size the glyphs were not rasterized at is the blur <see cref="NoireText"/>
    /// exists to avoid, and asking for a real font at every distance instead would be a full glyph atlas per pixel of
    /// distance. Stepped, the whole range costs a handful of sizes, each of them sharp.
    /// </remarks>
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
    /// <remarks>
    /// This is the only thing an off-screen marker may read off the projected coordinate. For a point behind the camera
    /// the game's projection returns a direction that is already correct but a distance that is not, and for a point far
    /// off the side of the screen the distance has been blown up by the perspective divide. The direction survives both.
    /// <br/>
    /// A point almost exactly behind the camera projects onto the centre and has no direction at all. Straight down is
    /// the answer then: it is where every marker for something directly behind belongs, and it holds still instead of
    /// spinning with the noise.
    /// </remarks>
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
    /// <remarks>
    /// This is a ray out of the centre to the boundary, not a clamp of the projected point. Clamping cannot do this job:
    /// a point behind the camera can project to anywhere at all, the centre of the screen included, and clamping a point
    /// that is already inside the viewport leaves it exactly where it was. The result is a marker for something behind
    /// you sitting in the middle of the screen instead of on an edge.<br/>
    /// The answer is where the <em>centre</em> of the element goes, so it is placed with a centred pivot. The element's
    /// own size and the margin are taken out of the box it travels in, which is what keeps the whole element inside the
    /// margin rather than hanging over the edge by half of itself.
    /// </remarks>
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
    /// <remarks>
    /// Multiples rather than pixels, so the result multiplies the direction back without normalizing it first. Used to
    /// pin an element to a viewport edge and to stand an arrow off the element it belongs to.
    /// </remarks>
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
