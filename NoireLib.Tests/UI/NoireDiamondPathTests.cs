using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="NoireShapes.DiamondPath"/>, the mark the deco language is built from.
/// </summary>
/// <remarks>
/// Winding is the property worth locking. <see cref="NoireShapes.Fill"/> needs the path convex and
/// <see cref="NoireShapes.GlowPath"/> needs it clockwise to know which way an edge faces, and neither fails loudly: a
/// counter-clockwise diamond fills correctly and then grows its halo inwards, which reads as the glow simply not
/// working rather than as a winding bug.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireDiamondPathTests
{
    /// <summary>
    /// Twice the signed area. Positive is clockwise in screen space, where y grows downwards.
    /// </summary>
    private static float Winding(ReadOnlySpan<Vector2> points)
    {
        var sum = 0f;

        for (var i = 0; i < points.Length; i++)
        {
            var from = points[i];
            var to = points[(i + 1) % points.Length];
            sum += (from.X * to.Y) - (to.X * from.Y);
        }

        return sum;
    }

    [Fact]
    public void DiamondPath_WritesFourCornersFromTheTop()
    {
        Span<Vector2> points = stackalloc Vector2[4];
        var count = NoireShapes.DiamondPath(new Vector2(100f, 50f), 10f, points);

        count.Should().Be(4);
        points[0].Should().Be(new Vector2(100f, 40f));
        points[1].Should().Be(new Vector2(110f, 50f));
        points[2].Should().Be(new Vector2(100f, 60f));
        points[3].Should().Be(new Vector2(90f, 50f));
    }

    [Fact]
    public void DiamondPath_WindsClockwise()
    {
        Span<Vector2> points = stackalloc Vector2[4];
        var count = NoireShapes.DiamondPath(new Vector2(0f, 0f), 6f, points);

        Winding(points[..count]).Should().BePositive("because the glow reads which way an edge faces from the winding");
    }

    [Fact]
    public void DiamondPath_RefusesABufferItWouldOverrun()
    {
        Span<Vector2> points = stackalloc Vector2[3];

        NoireShapes.DiamondPath(Vector2.Zero, 5f, points)
            .Should().Be(0, "a short buffer is refused rather than partly written, which would draw a triangle");
    }

    [Fact]
    public void DiamondPath_NeverEmitsCoincidentPoints()
    {
        // The defect class this library has shipped three times: a zero-length edge has no direction to build a join
        // from, and renders as a spike across the shape.
        for (var radius = 0.5f; radius < 40f; radius += 0.5f)
        {
            Span<Vector2> points = stackalloc Vector2[4];
            var count = NoireShapes.DiamondPath(new Vector2(13.5f, 7.25f), radius, points);

            for (var i = 0; i < count; i++)
                points[i].Should().NotBe(points[(i + 1) % count]);
        }
    }
}
