using FluentAssertions;
using NoireLib.Helpers;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the 2D point tests, including the zero-length segment and the winding-agnostic quad test that lets a quad
/// projected from 3D be hit-tested whichever way it happens to face.
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
}
