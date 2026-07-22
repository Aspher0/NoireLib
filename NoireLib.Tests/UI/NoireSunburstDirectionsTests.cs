using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for a sunburst's ray directions, asserted through the pure function rather than by drawing.
/// </summary>
/// <remarks>
/// The directions are now computed once unrotated and turned as a whole on the way out. What has to hold is that
/// turning a cached direction lands where computing it at the rotated angle would have, and that the layout the
/// drawing indexes into is the layout the function writes. Getting the second wrong swaps a ray's edges for its
/// neighbour's, which draws a burst that is subtly wrong rather than obviously broken.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireSunburstDirectionsTests
{
    private static Vector2 Direction(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    /// <summary>
    /// The rotation the drawing applies, reproduced here.
    /// </summary>
    private static Vector2 Turn(Vector2 direction, float radians)
    {
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        return new Vector2((direction.X * cos) - (direction.Y * sin), (direction.X * sin) + (direction.Y * cos));
    }

    [Theory]
    [InlineData(24, 0.5f, 8, 0f)]
    [InlineData(24, 0.5f, 8, 1.2f)]
    [InlineData(6, 0.9f, 1, -0.7f)]
    [InlineData(60, 0.1f, 4, 3.9f)]
    public void SunburstDirections_Turned_MatchComputingThemAtTheRotatedAngle(int rays, float softness, int layers, float rotation)
    {
        const float halfWidth = 0.05f;

        var stride = 1 + (layers * 2);
        var directions = new Vector2[rays * stride];

        NoireShapes.SunburstDirections(directions, rays, halfWidth, softness, layers)
            .Should().Be(rays * stride);

        var slot = MathF.Tau / rays;

        for (var ray = 0; ray < rays; ray++)
        {
            var angle = rotation + (slot * ray);
            var origin = ray * stride;

            Turn(directions[origin], rotation).X.Should().BeApproximately(Direction(angle).X, 1e-5f);
            Turn(directions[origin], rotation).Y.Should().BeApproximately(Direction(angle).Y, 1e-5f);

            for (var layer = 0; layer < layers; layer++)
            {
                var width = layers == 1 ? halfWidth : halfWidth * (1f - (softness * layer / (layers - 1)));

                var left = Turn(directions[origin + 1 + (layer * 2)], rotation);
                var right = Turn(directions[origin + 2 + (layer * 2)], rotation);

                left.X.Should().BeApproximately(Direction(angle - width).X, 1e-5f);
                left.Y.Should().BeApproximately(Direction(angle - width).Y, 1e-5f);
                right.X.Should().BeApproximately(Direction(angle + width).X, 1e-5f);
                right.Y.Should().BeApproximately(Direction(angle + width).Y, 1e-5f);
            }
        }
    }

    [Fact]
    public void SunburstDirections_AreAllUnitLength()
    {
        var directions = new Vector2[24 * 17];

        NoireShapes.SunburstDirections(directions, 24, 0.05f, 0.5f, 8);

        // Scaled by the radius on the way out, so anything other than unit length would draw a ray of the wrong reach.
        foreach (var direction in directions)
            direction.Length().Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void SunburstDirections_WithOneLayer_UseTheFullHalfWidth()
    {
        const float halfWidth = 0.2f;

        var directions = new Vector2[3];

        NoireShapes.SunburstDirections(directions, 1, halfWidth, 0.9f, 1);

        // A single layer has no inward narrowing to apply, so its edges sit at the full width whatever the softness.
        Turn(directions[1], 0f).Should().Be(Direction(-halfWidth));
        Turn(directions[2], 0f).Should().Be(Direction(halfWidth));
    }
}
