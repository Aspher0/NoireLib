using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Point tests in two dimensions, for hit-testing what a drawing surface just drew.<br/>
/// Every method is a pure function of its arguments, so it runs on any thread and unit-tests without a game.
/// Coordinates are whatever space the caller drew in, screen pixels being the usual one.
/// </summary>
public static class Geometry2DHelper
{
    /// <summary>
    /// Distance from a point to a finite segment.
    /// </summary>
    /// <param name="p">The point to measure.</param>
    /// <param name="a">Segment start.</param>
    /// <param name="b">Segment end.</param>
    /// <returns>The distance, measured to the nearer endpoint when the segment has no length.</returns>
    public static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq < 1e-6f)
            return Vector2.Distance(p, a);

        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>
    /// Whether a point lies inside a convex quad whose corners are given in order. Winding-agnostic, so a quad
    /// projected from 3D works whichever way it happens to face.
    /// </summary>
    /// <param name="p">The point to test.</param>
    /// <param name="a">First corner.</param>
    /// <param name="b">Second corner.</param>
    /// <param name="c">Third corner.</param>
    /// <param name="d">Fourth corner.</param>
    /// <returns>True when the point is inside or on an edge.</returns>
    public static bool PointInConvexQuad(Vector2 p, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var s0 = Cross(a, b, p);
        var s1 = Cross(b, c, p);
        var s2 = Cross(c, d, p);
        var s3 = Cross(d, a, p);
        var hasNeg = s0 < 0f || s1 < 0f || s2 < 0f || s3 < 0f;
        var hasPos = s0 > 0f || s1 > 0f || s2 > 0f || s3 > 0f;
        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// Twice the signed area of the triangle formed by an edge and a point, the sign telling which side of the edge
    /// the point falls on. The building block of every convex point-in-shape test.
    /// </summary>
    /// <param name="a">Edge start.</param>
    /// <param name="b">Edge end.</param>
    /// <param name="p">The point to place.</param>
    /// <returns>Positive when the point is left of the directed edge, negative when right, zero when collinear.</returns>
    public static float Cross(Vector2 a, Vector2 b, Vector2 p)
        => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
}
