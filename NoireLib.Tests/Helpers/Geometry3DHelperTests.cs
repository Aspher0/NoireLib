using FluentAssertions;
using NoireLib.Helpers;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the 3D geometry primitives, in particular the degenerate inputs each guard exists for: a ray parallel to a
/// plane or an axis, a zero-length segment, a zero-area triangle, a ray starting inside a sphere, and an inverted box.
/// </summary>
public class Geometry3DHelperTests
{
    [Fact]
    public void RayPlane_RayParallelToPlane_ReturnsFalseAndLeavesTheOriginAsTheHit()
    {
        var ok = Geometry3DHelper.RayPlane(Vector3.Zero, Vector3.UnitX, new Vector3(0, 5, 0), Vector3.UnitY, out var t, out var hit);

        ok.Should().BeFalse();
        t.Should().Be(0f);
        hit.Should().Be(Vector3.Zero, "a parallel ray has no hit, so the caller gets its own origin back rather than a NaN");
    }

    [Fact]
    public void RayPlane_PlaneBehindTheOrigin_ReturnsNegativeT()
    {
        var ok = Geometry3DHelper.RayPlane(Vector3.Zero, Vector3.UnitY, new Vector3(0, -3, 0), Vector3.UnitY, out var t, out var hit);

        ok.Should().BeTrue();
        t.Should().BeApproximately(-3f, 1e-5f);
        hit.Y.Should().BeApproximately(-3f, 1e-5f);
    }

    [Fact]
    public void RaySphere_OriginInsideTheSphere_TakesTheForwardRoot()
    {
        var ok = Geometry3DHelper.RaySphere(Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 2f, out var t);

        ok.Should().BeTrue();
        t.Should().BeApproximately(2f, 1e-5f, "the near root is behind the origin, so the far one is the answer");
    }

    [Fact]
    public void RaySphere_SphereBehindTheOrigin_ReturnsFalse()
        => Geometry3DHelper.RaySphere(Vector3.Zero, Vector3.UnitZ, new Vector3(0, 0, -10), 1f, out _).Should().BeFalse();

    [Fact]
    public void RaySphere_RayMissesEntirely_ReturnsFalse()
        => Geometry3DHelper.RaySphere(Vector3.Zero, Vector3.UnitZ, new Vector3(5, 0, 10), 1f, out _).Should().BeFalse();

    [Fact]
    public void RayTriangle_DegenerateTriangle_ReturnsFalse()
    {
        var a = new Vector3(0, 0, 5);

        Geometry3DHelper.RayTriangle(Vector3.Zero, Vector3.UnitZ, a, a, a, out _).Should().BeFalse();
    }

    [Fact]
    public void RayTriangle_HitFromBehindTheFace_StillReportsAHit()
    {
        // Wound so the ray arrives on the back face; the picker must accept it or a mesh becomes unpickable from one side.
        var a = new Vector3(-1, -1, 5);
        var b = new Vector3(1, -1, 5);
        var c = new Vector3(0, 1, 5);

        Geometry3DHelper.RayTriangle(Vector3.Zero, Vector3.UnitZ, a, c, b, out var t).Should().BeTrue();
        t.Should().BeApproximately(5f, 1e-5f);
    }

    [Fact]
    public void RayTriangle_TriangleBehindTheOrigin_ReturnsFalse()
    {
        var a = new Vector3(-1, -1, -5);
        var b = new Vector3(1, -1, -5);
        var c = new Vector3(0, 1, -5);

        Geometry3DHelper.RayTriangle(Vector3.Zero, Vector3.UnitZ, a, b, c, out _).Should().BeFalse();
    }

    [Fact]
    public void RayBox_InvertedBox_BehavesAsThoughTheCornersWereSwapped()
    {
        var inv = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 1f);
        var hit = Geometry3DHelper.RayBox(Vector3.Zero, inv, new Vector3(1, 1, 10), new Vector3(-1, -1, 5), 100f);

        hit.Should().BeTrue("each slab takes its own min and max, so swapped corners describe the same box rather than an empty one");
        hit.Should().Be(Geometry3DHelper.RayBox(Vector3.Zero, inv, new Vector3(-1, -1, 5), new Vector3(1, 1, 10), 100f));
    }

    [Fact]
    public void RayBox_RayParallelToTwoAxes_HitsThroughTheInfiniteSlabs()
    {
        // The caller hands infinite reciprocals for the axes the ray does not move along; the slab test must survive them.
        var inv = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 1f);

        Geometry3DHelper.RayBox(Vector3.Zero, inv, new Vector3(-1, -1, 5), new Vector3(1, 1, 10), 100f).Should().BeTrue();
        Geometry3DHelper.RayBox(Vector3.Zero, inv, new Vector3(2, 2, 5), new Vector3(3, 3, 10), 100f).Should().BeFalse();
    }

    [Fact]
    public void RayBox_HitBeyondTMax_ReturnsFalse()
    {
        var inv = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 1f);

        Geometry3DHelper.RayBox(Vector3.Zero, inv, new Vector3(-1, -1, 50), new Vector3(1, 1, 60), 10f).Should().BeFalse();
    }

    [Fact]
    public void RaySegmentDistance_ZeroLengthSegment_MeasuresToThePoint()
    {
        var p = new Vector3(3, 0, 4);

        var d = Geometry3DHelper.RaySegmentDistance(Vector3.Zero, Vector3.UnitZ, p, p, out var rayT);

        rayT.Should().BeApproximately(4f, 1e-5f);
        d.Should().BeApproximately(3f, 1e-5f);
    }

    [Fact]
    public void RaySegmentDistance_ClosestApproachIsPastAnEnd_ClampsToThatEnd()
    {
        var d = Geometry3DHelper.RaySegmentDistance(Vector3.Zero, Vector3.UnitZ, new Vector3(2, 0, 5), new Vector3(2, 0, 6), out _);

        d.Should().BeApproximately(2f, 1e-5f);
    }

    [Fact]
    public void ClosestAxisParam_RayParallelToTheAxis_FallsBackToProjectingTheOrigin()
    {
        var ok = Geometry3DHelper.ClosestAxisParam(new Vector3(0, 3, 7), Vector3.UnitZ, Vector3.Zero, Vector3.UnitZ, out var param);

        ok.Should().BeFalse();
        param.Should().BeApproximately(7f, 1e-5f, "with no unique closest point the origin's own projection is the usable answer");
    }

    [Fact]
    public void ClosestAxisParam_PerpendicularRay_FindsThePointOnTheAxis()
    {
        Geometry3DHelper.ClosestAxisParam(new Vector3(0, 5, 4), -Vector3.UnitY, Vector3.Zero, Vector3.UnitZ, out var param)
            .Should().BeTrue();

        param.Should().BeApproximately(4f, 1e-5f);
    }

    [Fact]
    public void RayRing_PointingThroughTheHole_Misses()
    {
        Geometry3DHelper.RayRing(new Vector3(0, 5, 0), -Vector3.UnitY, Vector3.Zero, Vector3.UnitY, 3f, 0.2f, out _)
            .Should().BeFalse();

        Geometry3DHelper.RayRing(new Vector3(3, 5, 0), -Vector3.UnitY, Vector3.Zero, Vector3.UnitY, 3f, 0.2f, out var t)
            .Should().BeTrue();
        t.Should().BeApproximately(5f, 1e-5f);
    }

    [Fact]
    public void RayRing_RingBehindTheOrigin_ReturnsFalse()
        => Geometry3DHelper.RayRing(new Vector3(3, 5, 0), Vector3.UnitY, Vector3.Zero, Vector3.UnitY, 3f, 0.2f, out _)
            .Should().BeFalse();

    [Fact]
    public void SignedAngleOnPlane_QuarterTurn_IsSignedByTheAxis()
    {
        var from = new Vector3(1, 0, 0);
        var to = new Vector3(0, 0, -1);

        Geometry3DHelper.SignedAngleOnPlane(Vector3.Zero, Vector3.UnitY, from, to)
            .Should().BeApproximately(MathHelper.HalfPi, 1e-4f);

        Geometry3DHelper.SignedAngleOnPlane(Vector3.Zero, -Vector3.UnitY, from, to)
            .Should().BeApproximately(-MathHelper.HalfPi, 1e-4f, "flipping the axis flips the sign");
    }

    [Fact]
    public void SignedAngleOnPlane_VectorCollapsedOntoTheAxis_ReturnsZero()
        => Geometry3DHelper.SignedAngleOnPlane(Vector3.Zero, Vector3.UnitY, new Vector3(0, 4, 0), new Vector3(1, 0, 0))
            .Should().Be(0f);

    [Fact]
    public void SafeNormalize_ZeroVector_ReturnsTheFallback()
    {
        Geometry3DHelper.SafeNormalize(Vector3.Zero, Vector3.UnitZ).Should().Be(Vector3.UnitZ);
        Geometry3DHelper.SafeNormalize(new Vector3(0, 8, 0), Vector3.UnitZ).Should().Be(Vector3.UnitY);
    }

    [Fact]
    public void Snap_NonPositiveStep_PassesTheComponentThrough()
    {
        var snapped = Geometry3DHelper.Snap(new Vector3(1.4f, 2.6f, 3.3f), new Vector3(1f, 0f, -1f));

        snapped.X.Should().BeApproximately(1f, 1e-5f);
        snapped.Y.Should().BeApproximately(2.6f, 1e-5f, "a zero step means no grid on that component");
        snapped.Z.Should().BeApproximately(3.3f, 1e-5f);
    }

    [Fact]
    public void AabbOverlap_TouchingFaces_CountAsOverlapping()
    {
        Geometry3DHelper.AabbOverlap(Vector3.Zero, Vector3.One, Vector3.One, new Vector3(2f)).Should().BeTrue();
        Geometry3DHelper.AabbOverlap(Vector3.Zero, Vector3.One, new Vector3(1.001f), new Vector3(2f)).Should().BeFalse();
    }

    [Fact]
    public void ClipConvexPolygon_SquareCutInHalf_KeepsTheInsideAndTheTwoCrossings()
    {
        List<Vector3> square =
        [
            new(-1, 0, -1),
            new(1, 0, -1),
            new(1, 0, 1),
            new(-1, 0, 1),
        ];
        var result = new List<Vector3>();

        Geometry3DHelper.ClipConvexPolygon(square, result, axis: 0, limit: 0f, keepGreater: false);

        result.Should().HaveCount(4);
        result.Should().OnlyContain(v => v.X <= 1e-6f);
    }

    [Fact]
    public void ClipConvexPolygon_PolygonEntirelyOutside_ClearsTheResult()
    {
        List<Vector3> square = [new(5, 0, 5), new(6, 0, 5), new(6, 0, 6)];
        var result = new List<Vector3> { Vector3.Zero };

        Geometry3DHelper.ClipConvexPolygon(square, result, axis: 0, limit: 0f, keepGreater: false);

        result.Should().BeEmpty("the result is cleared before anything is added, so stale vertices never survive a clip");
    }

    [Fact]
    public void ClipConvexPolygon_EmptyPolygon_ClearsTheResult()
    {
        var result = new List<Vector3> { Vector3.One };

        Geometry3DHelper.ClipConvexPolygon([], result, axis: 1, limit: 0f, keepGreater: true);

        result.Should().BeEmpty();
    }
}
