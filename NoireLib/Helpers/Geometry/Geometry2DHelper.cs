using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>Point tests and curve sampling in two dimensions. Pure functions: any thread, testable without a game.</summary>
public static class Geometry2DHelper
{
    /// <summary>Distance from a point to a finite segment.</summary>
    /// <param name="p">The point to measure.</param>
    /// <param name="a">Segment start.</param>
    /// <param name="b">Segment end.</param>
    /// <returns>The distance, measured to the nearer endpoint when the segment has no length.</returns>
    public static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        => MathF.Sqrt(PointToSegmentDistanceSquared(p, a, b));

    /// <summary>
    /// Squared distance from a point to a finite segment, to compare against a squared limit without a square root.
    /// </summary>
    /// <param name="p">The point to measure.</param>
    /// <param name="a">Segment start.</param>
    /// <param name="b">Segment end.</param>
    /// <returns>The squared distance, measured to the nearer endpoint when the segment has no length.</returns>
    public static float PointToSegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq < 1e-6f)
            return Vector2.DistanceSquared(p, a);

        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.DistanceSquared(p, a + ab * t);
    }

    /// <summary>How many times a closed polygon winds around a point. Non-zero is inside under the non-zero rule, as SVG fills.</summary>
    /// <param name="polygon">The outline, in order. The last point joins back to the first.</param>
    /// <param name="p">The point to test.</param>
    /// <returns>The winding number, zero outside or for fewer than three points.</returns>
    public static int WindingNumber(ReadOnlySpan<Vector2> polygon, Vector2 p)
    {
        var count = polygon.Length;

        if (count < 3)
            return 0;

        var winding = 0;

        for (var i = 0; i < count; i++)
        {
            var a = polygon[i];
            var b = polygon[i + 1 == count ? 0 : i + 1];

            if (a.Y <= p.Y)
            {
                if (b.Y > p.Y && Cross(a, b, p) > 0f)
                    winding++;
            }
            else if (b.Y <= p.Y && Cross(a, b, p) < 0f)
            {
                winding--;
            }
        }

        return winding;
    }

    /// <summary>Samples a quadratic Bezier curve at evenly spaced parameters.</summary>
    /// <param name="output">Receives the points. Sampling stops when it is full.</param>
    /// <param name="start">The curve's start.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The curve's end.</param>
    /// <param name="segments">How many straight segments approximate the curve.</param>
    /// <param name="includeStart">Whether to write <paramref name="start"/> itself, false when it ends the previous piece.</param>
    /// <returns>How many points were written.</returns>
    public static int SampleQuadratic(Span<Vector2> output, Vector2 start, Vector2 control, Vector2 end, int segments, bool includeStart = true)
    {
        segments = Math.Max(1, segments);
        var written = 0;

        for (var i = includeStart ? 0 : 1; i <= segments && written < output.Length; i++)
        {
            var t = i / (float)segments;
            var u = 1f - t;
            output[written++] = (u * u * start) + (2f * u * t * control) + (t * t * end);
        }

        return written;
    }

    /// <summary>Samples a cubic Bezier curve at evenly spaced parameters.</summary>
    /// <param name="output">Receives the points. Sampling stops when it is full.</param>
    /// <param name="start">The curve's start.</param>
    /// <param name="control1">The control point leaving <paramref name="start"/>.</param>
    /// <param name="control2">The control point arriving at <paramref name="end"/>.</param>
    /// <param name="end">The curve's end.</param>
    /// <param name="segments">How many straight segments approximate the curve.</param>
    /// <param name="includeStart">Whether to write <paramref name="start"/> itself, false when it ends the previous piece.</param>
    /// <returns>How many points were written.</returns>
    public static int SampleCubic(Span<Vector2> output, Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, int segments,
        bool includeStart = true)
    {
        segments = Math.Max(1, segments);
        var written = 0;

        for (var i = includeStart ? 0 : 1; i <= segments && written < output.Length; i++)
        {
            var t = i / (float)segments;
            var u = 1f - t;
            output[written++] = (u * u * u * start) + (3f * u * u * t * control1) + (3f * u * t * t * control2) + (t * t * t * end);
        }

        return written;
    }

    /// <summary>
    /// Samples a uniform Catmull-Rom spline that passes through every point, its end tangents taken from the end points.
    /// </summary>
    /// <param name="output">Receives the points, <c>(points.Length - 1) * segmentsPerSpan + 1</c> of them. Sampling stops when it is full.</param>
    /// <param name="points">The points the spline passes through.</param>
    /// <param name="segmentsPerSpan">How many straight segments approximate the curve between two neighbouring points.</param>
    /// <returns>How many points were written.</returns>
    public static int SampleCatmullRom(Span<Vector2> output, ReadOnlySpan<Vector2> points, int segmentsPerSpan)
    {
        if (points.Length < 2)
        {
            points[..Math.Min(points.Length, output.Length)].CopyTo(output);
            return Math.Min(points.Length, output.Length);
        }

        segmentsPerSpan = Math.Max(1, segmentsPerSpan);
        var written = 0;

        for (var span = 0; span < points.Length - 1; span++)
        {
            var p0 = points[Math.Max(0, span - 1)];
            var p1 = points[span];
            var p2 = points[span + 1];
            var p3 = points[Math.Min(points.Length - 1, span + 2)];

            for (var k = 0; k < segmentsPerSpan && written < output.Length; k++)
            {
                var u = k / (float)segmentsPerSpan;
                var u2 = u * u;
                var u3 = u2 * u;
                output[written++] = 0.5f * ((2f * p1) + ((-p0 + p2) * u) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * u2)
                    + ((-p0 + (3f * p1) - (3f * p2) + p3) * u3));
            }
        }

        if (written < output.Length)
            output[written++] = points[^1];

        return written;
    }

    /// <summary>Whether a point lies inside a convex quad, whichever way its corners wind.</summary>
    /// <param name="p">The point to test.</param>
    /// <param name="a">First corner.</param>
    /// <param name="b">Second corner.</param>
    /// <param name="c">Third corner.</param>
    /// <param name="d">Fourth corner.</param>
    /// <returns>Whether the point is inside or on an edge.</returns>
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

    /// <summary>Twice the signed area of an edge and a point: which side of the edge the point falls on.</summary>
    /// <param name="a">Edge start.</param>
    /// <param name="b">Edge end.</param>
    /// <param name="p">The point to place.</param>
    /// <returns>Positive left of the directed edge, negative right, zero when collinear.</returns>
    public static float Cross(Vector2 a, Vector2 b, Vector2 p)
        => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
}
