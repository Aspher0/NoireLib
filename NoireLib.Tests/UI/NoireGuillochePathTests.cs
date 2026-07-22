using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the guilloche curve itself, asserted through the pure path function rather than by drawing.
/// <see cref="NoireGuillocheSegmentsTests"/> covers how many points a ring gets; this covers where they go.
/// </summary>
/// <remarks>
/// The curve is now computed once at radius one and scaled on the way out, so the property that has to hold is that
/// scaling a unit curve lands where computing the curve at that radius directly would have. If it does not, a cached
/// rosette is a different shape from the one that shipped, and nothing about the drawing would say so.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireGuillochePathTests
{
    /// <summary>
    /// The curve as it was computed before it was cached: at the ring's own radius, in one pass.
    /// </summary>
    private static Vector2[] DirectlyAtRadius(float radius, int lobes, float depth, int segments)
    {
        var points = new Vector2[segments];

        var rolling = radius / lobes;
        var carrier = radius - rolling;
        var offset = depth * rolling;
        var ratio = carrier / rolling;

        for (var step = 0; step < segments; step++)
        {
            var t = MathF.Tau * step / segments;

            points[step] = new Vector2(
                (carrier * MathF.Cos(t)) + (offset * MathF.Cos(ratio * t)),
                (carrier * MathF.Sin(t)) - (offset * MathF.Sin(ratio * t)));
        }

        return points;
    }

    [Theory]
    [InlineData(120f, 7, 0.6f, 128)]
    [InlineData(40f, 5, 1f, 64)]
    [InlineData(300f, 12, 0.25f, 512)]
    [InlineData(1f, 2, 0f, 32)]
    public void GuillochePath_ScaledToARadius_MatchesComputingItAtThatRadius(float radius, int lobes, float depth, int segments)
    {
        var unit = new Vector2[segments];
        NoireShapes.GuillochePath(unit, lobes, depth, segments);

        var direct = DirectlyAtRadius(radius, lobes, depth, segments);

        for (var step = 0; step < segments; step++)
        {
            // Not bitwise equal, and it cannot be: factoring the radius out of the curve reassociates the multiply, so
            // the two differ in the last bits of a float. The tolerance is scaled to the shape, and a millionth of a
            // radius is several orders of magnitude below a pixel.
            var tolerance = radius * 1e-5f;

            (unit[step].X * radius).Should().BeApproximately(direct[step].X, tolerance);
            (unit[step].Y * radius).Should().BeApproximately(direct[step].Y, tolerance);
        }
    }

    [Fact]
    public void GuillochePath_WritesEveryPointItPromised()
    {
        var points = new Vector2[64];

        NoireShapes.GuillochePath(points, 7, 0.6f, 64).Should().Be(64);

        points.Should().NotContain(Vector2.Zero, "every point of the curve is written, so none is left at its default");
    }

    [Fact]
    public void GuillochePath_IsClosed()
    {
        const int segments = 96;

        var points = new Vector2[segments];
        NoireShapes.GuillochePath(points, 5, 0.7f, segments);

        // The last point steps to just short of a full turn rather than repeating the first, which is what lets the
        // ring be stroked as a closed polyline without a doubled vertex at the seam.
        points[^1].Should().NotBe(points[0]);
        Vector2.Distance(points[^1], points[0]).Should().BeLessThan(0.25f);
    }

    [Fact]
    public void GuillochePath_AtZeroDepth_IsACircle()
    {
        const int segments = 128;

        var points = new Vector2[segments];
        NoireShapes.GuillochePath(points, 7, 0f, segments);

        // With no offset the tracing point sits on the rolling circle's centre, which runs round a plain circle of the
        // carrier radius. This is the degenerate case the lobe count stops mattering in.
        var expected = 1f - (1f / 7f);

        foreach (var point in points)
            point.Length().Should().BeApproximately(expected, 1e-4f);
    }
}
