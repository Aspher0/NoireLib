using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the 2D point tests, including the zero-length segment and the winding-agnostic quad test that lets a quad
/// projected from 3D be hit-tested whichever way it happens to face, and the curve samplers' point counts and endpoints.
/// </summary>
public class Geometry2DHelperTests
{
    [Fact]
    public void PointToSegmentDistance_ZeroLengthSegment_MeasuresToThePoint()
        => Geometry2DHelper.PointToSegmentDistance(new Vector2(3, 4), Vector2.Zero, Vector2.Zero)
            .Should().BeApproximately(5f, 1e-5f);

    [Fact]
    public void PointToSegmentDistance_PastAnEnd_ClampsToThatEnd()
    {
        var a = Vector2.Zero;
        var b = new Vector2(10, 0);

        Geometry2DHelper.PointToSegmentDistance(new Vector2(-3, 4), a, b).Should().BeApproximately(5f, 1e-5f);
        Geometry2DHelper.PointToSegmentDistance(new Vector2(5, 4), a, b).Should().BeApproximately(4f, 1e-5f);
    }

    [Fact]
    public void PointInConvexQuad_EitherWinding_AgreesOnTheSamePoints()
    {
        var a = new Vector2(0, 0);
        var b = new Vector2(10, 0);
        var c = new Vector2(10, 10);
        var d = new Vector2(0, 10);
        var inside = new Vector2(5, 5);
        var outside = new Vector2(11, 5);

        Geometry2DHelper.PointInConvexQuad(inside, a, b, c, d).Should().BeTrue();
        Geometry2DHelper.PointInConvexQuad(inside, d, c, b, a).Should().BeTrue("reversing the winding must not change the answer");
        Geometry2DHelper.PointInConvexQuad(outside, a, b, c, d).Should().BeFalse();
        Geometry2DHelper.PointInConvexQuad(outside, d, c, b, a).Should().BeFalse();
    }

    [Fact]
    public void PointInConvexQuad_PointOnAnEdge_CountsAsInside()
        => Geometry2DHelper.PointInConvexQuad(new Vector2(5, 0), new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10))
            .Should().BeTrue();

    [Fact]
    public void Cross_PlacesAPointRelativeToTheDirectedEdge()
    {
        var a = Vector2.Zero;
        var b = new Vector2(1, 0);

        Geometry2DHelper.Cross(a, b, new Vector2(0, 1)).Should().BePositive();
        Geometry2DHelper.Cross(a, b, new Vector2(0, -1)).Should().BeNegative();
        Geometry2DHelper.Cross(a, b, new Vector2(2, 0)).Should().Be(0f);
    }

    [Fact]
    public void PointToSegmentDistanceSquared_IsTheSquareOfTheDistance()
        => Geometry2DHelper.PointToSegmentDistanceSquared(new Vector2(-3, 4), Vector2.Zero, new Vector2(10, 0))
            .Should().BeApproximately(25f, 1e-4f);

    [Fact]
    public void WindingNumber_CountsTheTurnsAroundAPoint_SignedByDirection()
    {
        Vector2[] square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Vector2[] reversed = [new(0, 10), new(10, 10), new(10, 0), new(0, 0)];

        Geometry2DHelper.WindingNumber(square, new Vector2(5, 5)).Should().NotBe(0);
        Geometry2DHelper.WindingNumber(reversed, new Vector2(5, 5)).Should().Be(-Geometry2DHelper.WindingNumber(square, new Vector2(5, 5)));
        Geometry2DHelper.WindingNumber(square, new Vector2(15, 5)).Should().Be(0);
    }

    [Fact]
    public void SampleCubic_StartsAndEndsOnItsEndpoints()
    {
        Span<Vector2> points = stackalloc Vector2[16];
        var start = new Vector2(0, 0);
        var end = new Vector2(10, 0);

        var count = Geometry2DHelper.SampleCubic(points, start, new Vector2(0, 5), new Vector2(10, 5), end, 8);

        count.Should().Be(9);
        points[0].Should().Be(start);
        points[8].Should().Be(end);
        points[4].Y.Should().BeApproximately(3.75f, 1e-4f, "because a symmetric cubic peaks at three quarters of its control height");
    }

    [Fact]
    public void SampleQuadratic_WithoutItsStart_WritesOnlyTheNewPoints()
    {
        Span<Vector2> points = stackalloc Vector2[16];

        var count = Geometry2DHelper.SampleQuadratic(points, Vector2.Zero, new Vector2(5, 10), new Vector2(10, 0), 4, includeStart: false);

        count.Should().Be(4);
        points[3].Should().Be(new Vector2(10, 0));
    }

    [Fact]
    public void SampleCubic_StopsWhenTheOutputIsFull()
    {
        Span<Vector2> points = stackalloc Vector2[3];

        Geometry2DHelper.SampleCubic(points, Vector2.Zero, Vector2.One, Vector2.One, new Vector2(2, 2), 10).Should().Be(3);
    }

    [Fact]
    public void SampleCatmullRom_PassesThroughEveryPoint()
    {
        Vector2[] through = [new(0, 0), new(5, 5), new(10, 0)];
        Span<Vector2> points = stackalloc Vector2[32];

        var count = Geometry2DHelper.SampleCatmullRom(points, through, 6);

        count.Should().Be(13);
        points[0].Should().Be(through[0]);
        points[6].Should().Be(through[1]);
        points[12].Should().Be(through[2]);
    }
}
