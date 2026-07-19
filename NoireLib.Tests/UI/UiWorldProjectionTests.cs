using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="UiWorldProjection"/>: distance fade and scale, the behind-camera correction, edge pinning
/// and the arrow geometry.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class UiWorldProjectionTests
{
    private static readonly UiRect Viewport = new(0f, 0f, 1000f, 500f);

    #region Distance fade

    [Fact]
    public void DistanceAlpha_NoMaximum_NeverFades()
    {
        UiWorldProjection.DistanceAlpha(9999f, 10f, 0f).Should().Be(1f);
    }

    [Fact]
    public void DistanceAlpha_InsideFadeStart_IsOpaque()
    {
        UiWorldProjection.DistanceAlpha(10f, 20f, 40f).Should().Be(1f);
    }

    [Fact]
    public void DistanceAlpha_AtMaximum_IsGone()
    {
        UiWorldProjection.DistanceAlpha(40f, 20f, 40f).Should().Be(0f);
    }

    [Fact]
    public void DistanceAlpha_Midway_IsHalf()
    {
        UiWorldProjection.DistanceAlpha(30f, 20f, 40f).Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void DistanceAlpha_FadeStartBeyondMaximum_CutsOffWithoutFading()
    {
        UiWorldProjection.DistanceAlpha(39f, 100f, 40f).Should().Be(1f);
        UiWorldProjection.DistanceAlpha(41f, 100f, 40f).Should().Be(0f);
    }

    #endregion

    #region Distance scale

    [Fact]
    public void DistanceScale_AtReference_IsUnchanged()
    {
        UiWorldProjection.DistanceScale(20f, 20f, 0.5f, 2f).Should().Be(1f);
    }

    [Fact]
    public void DistanceScale_TwiceTheReference_IsHalf()
    {
        UiWorldProjection.DistanceScale(40f, 20f, 0.1f, 2f).Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void DistanceScale_VeryClose_IsCappedAtTheMaximum()
    {
        UiWorldProjection.DistanceScale(0.1f, 20f, 0.5f, 1.5f).Should().Be(1.5f);
    }

    [Fact]
    public void DistanceScale_VeryFar_IsFlooredAtTheMinimum()
    {
        UiWorldProjection.DistanceScale(5000f, 20f, 0.5f, 1.5f).Should().Be(0.5f);
    }

    [Fact]
    public void DistanceScale_AtZeroDistance_IsTheMaximum()
    {
        UiWorldProjection.DistanceScale(0f, 20f, 0.5f, 1.5f)
            .Should().Be(1.5f, "standing on the point is as close as it gets, not a division by zero");
    }

    [Fact]
    public void DistanceScale_SwappedBounds_AreCorrected()
    {
        UiWorldProjection.DistanceScale(5000f, 20f, 1.5f, 0.5f).Should().Be(0.5f);
    }

    #endregion

    #region Ramp scale

    [Fact]
    public void RampScale_BeforeTheStart_IsTheLargest()
    {
        UiWorldProjection.RampScale(5f, 10f, 60f, 0.5f, 1.5f).Should().Be(1.5f);
    }

    [Fact]
    public void RampScale_PastTheEnd_IsTheSmallest()
    {
        UiWorldProjection.RampScale(200f, 10f, 60f, 0.5f, 1.5f).Should().Be(0.5f);
    }

    [Fact]
    public void RampScale_Midway_IsHalfway()
    {
        UiWorldProjection.RampScale(35f, 10f, 60f, 0.5f, 1.5f).Should().BeApproximately(1f, 0.0001f);
    }

    [Fact]
    public void RampScale_SwappedBounds_AreCorrected()
    {
        UiWorldProjection.RampScale(200f, 10f, 60f, 1.5f, 0.5f).Should().Be(0.5f);
    }

    [Fact]
    public void RampScale_RangeRunningBackwards_CutsOverAtTheStart()
    {
        UiWorldProjection.RampScale(9f, 10f, 5f, 0.5f, 1.5f)
            .Should().Be(1.5f, "a slider dragged past its partner is not a division by zero");

        UiWorldProjection.RampScale(11f, 10f, 5f, 0.5f, 1.5f).Should().Be(0.5f);
    }

    [Fact]
    public void RampScale_ReadsLikeTheDistanceFadeDoes()
    {
        // The point of the mode: the same pair of distances, behaving the same way at both ends.
        const float from = 20f;
        const float to = 40f;

        UiWorldProjection.RampScale(from, from, to, 0f, 1f)
            .Should().Be(UiWorldProjection.DistanceAlpha(from, from, to));

        UiWorldProjection.RampScale(30f, from, to, 0f, 1f)
            .Should().BeApproximately(UiWorldProjection.DistanceAlpha(30f, from, to), 0.0001f);
    }

    #endregion

    #region Scale stepping

    [Fact]
    public void QuantizeScale_RoundsToTheStep()
    {
        UiWorldProjection.QuantizeScale(1.13f, 0.25f).Should().BeApproximately(1.25f, 0.0001f);
        UiWorldProjection.QuantizeScale(1.12f, 0.25f).Should().BeApproximately(1f, 0.0001f);
    }

    [Fact]
    public void QuantizeScale_NoStep_IsUntouched()
    {
        UiWorldProjection.QuantizeScale(1.13f, 0f).Should().Be(1.13f);
    }

    [Fact]
    public void QuantizeScale_NeverRoundsAwayToNothing()
    {
        UiWorldProjection.QuantizeScale(0.05f, 0.25f)
            .Should().Be(0.25f, "a label rounded to a scale of zero would vanish rather than shrink");
    }

    [Fact]
    public void QuantizeScale_ARangeCostsAFewDistinctSizes()
    {
        var seen = new HashSet<float>();

        for (var scale = 0.6f; scale <= 1.4f; scale += 0.01f)
            seen.Add(UiWorldProjection.QuantizeScale(scale, 0.25f));

        seen.Count.Should().BeLessThanOrEqualTo(
            5, "the whole scale range has to fit in the font cache several times over");
    }

    #endregion

    #region Off-screen direction

    [Fact]
    public void OffScreenDirection_ReadsStraightOffTheProjectedPoint()
    {
        UiWorldProjection.OffScreenDirection(new Vector2(900f, 250f), Viewport)
            .Should().Be(new Vector2(400f, 0f), "the game's projection already un-mirrors a point behind the camera");
    }

    [Fact]
    public void OffScreenDirection_AtTheCentre_PointsDown()
    {
        UiWorldProjection.OffScreenDirection(Viewport.Center, Viewport)
            .Should().Be(new Vector2(0f, 1f), "something exactly behind the camera projects onto the centre");
    }

    [Fact]
    public void OffScreenDirection_JustOffTheCentre_DoesNotSpin()
    {
        UiWorldProjection.OffScreenDirection(Viewport.Center + new Vector2(0.2f, -0.1f), Viewport)
            .Should().Be(new Vector2(0f, 1f), "a fraction of a pixel is noise, not a direction");
    }

    #endregion

    #region Edge pinning

    [Fact]
    public void PinToEdge_PointingRight_LandsOnTheRightEdge()
    {
        var pinned = UiWorldProjection.PinToEdge(Viewport, new Vector2(1f, 0f), new Vector2(80f, 20f), 10f);

        pinned.Should().Be(
            new Vector2(950f, 250f), "the right edge less the margin and half the element, vertically centred");
    }

    [Fact]
    public void PinToEdge_PointingDown_LandsOnTheBottomEdge()
    {
        var pinned = UiWorldProjection.PinToEdge(Viewport, new Vector2(0f, 1f), new Vector2(80f, 20f), 10f);

        pinned.Should().Be(new Vector2(500f, 480f));
    }

    [Fact]
    public void PinToEdge_IgnoresHowFarTheProjectedPointWas()
    {
        var near = UiWorldProjection.PinToEdge(Viewport, new Vector2(1f, 0f), new Vector2(80f, 20f), 10f);
        var far = UiWorldProjection.PinToEdge(Viewport, new Vector2(9000f, 0f), new Vector2(80f, 20f), 10f);

        far.Should().Be(near, "only the direction survives the projection, so only the direction may be read");
    }

    [Fact]
    public void PinToEdge_AlwaysReachesAnEdge()
    {
        // The bug this replaces: a point behind the camera projects near the centre, and clamping a point already
        // inside the viewport left the marker sitting in the middle of the screen.
        var inset = Viewport.Expand(-10f);

        for (var degrees = 0; degrees < 360; degrees += 15)
        {
            var radians = degrees * MathF.PI / 180f;
            var direction = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
            var pinned = UiWorldProjection.PinToEdge(Viewport, direction, new Vector2(80f, 20f), 10f);

            var onAnEdge =
                MathF.Abs(pinned.X - (inset.Left + 40f)) < 0.01f ||
                MathF.Abs(pinned.X - (inset.Right - 40f)) < 0.01f ||
                MathF.Abs(pinned.Y - (inset.Top + 10f)) < 0.01f ||
                MathF.Abs(pinned.Y - (inset.Bottom - 10f)) < 0.01f;

            onAnEdge.Should().BeTrue($"a marker pinned along {degrees} degrees belongs on an edge, not in the middle");
        }
    }

    [Fact]
    public void PinToEdge_ElementLargerThanTheViewport_StaysCentred()
    {
        UiWorldProjection.PinToEdge(Viewport, new Vector2(1f, 0f), new Vector2(4000f, 4000f), 10f)
            .Should().Be(Viewport.Center, "there is nowhere to travel, so it does not travel out of the screen");
    }

    #endregion

    #region Edge distance

    [Fact]
    public void EdgeDistance_AlongAnAxis_IsHalfTheBox()
    {
        UiWorldProjection.EdgeDistance(new Vector2(80f, 20f), new Vector2(1f, 0f)).Should().Be(40f);
        UiWorldProjection.EdgeDistance(new Vector2(80f, 20f), new Vector2(0f, 1f)).Should().Be(10f);
    }

    [Fact]
    public void EdgeDistance_Diagonally_TakesTheNearerEdge()
    {
        UiWorldProjection.EdgeDistance(new Vector2(80f, 20f), new Vector2(1f, 1f))
            .Should().Be(10f, "the short axis runs out first");
    }

    [Fact]
    public void EdgeDistance_NoBox_HasNoEdgeToReach()
    {
        float.IsInfinity(UiWorldProjection.EdgeDistance(Vector2.Zero, new Vector2(1f, 0f))).Should().BeTrue();
    }

    #endregion

    #region Arrow

    [Fact]
    public void ArrowAngle_PointingRight_IsZero()
    {
        UiWorldProjection.ArrowAngle(Vector2.Zero, new Vector2(10f, 0f)).Should().Be(0f);
    }

    [Fact]
    public void ArrowAngle_PointingDown_IsAQuarterTurn()
    {
        UiWorldProjection.ArrowAngle(Vector2.Zero, new Vector2(0f, 10f))
            .Should().BeApproximately(MathF.PI / 2f, 0.0001f);
    }

    [Fact]
    public void ArrowAngle_CoincidentPoints_IsZero()
    {
        UiWorldProjection.ArrowAngle(new Vector2(5f, 5f), new Vector2(5f, 5f)).Should().Be(0f);
    }

    [Fact]
    public void ArrowAngle_FromADirection_PointsTheSameWayAsFromTwoPoints()
    {
        UiWorldProjection.ArrowAngle(new Vector2(3f, 4f))
            .Should().Be(UiWorldProjection.ArrowAngle(new Vector2(10f, 10f), new Vector2(13f, 14f)));
    }

    [Fact]
    public void ArrowAngle_PinnedBelowTheCentre_PointsDownAndNotBackUp()
    {
        // The arrow follows the direction the label was pinned along, so a marker for something behind the camera
        // points off the bottom of the screen rather than back into the middle of it.
        var direction = UiWorldProjection.OffScreenDirection(Viewport.Center, Viewport);

        UiWorldProjection.ArrowAngle(direction)
            .Should().BeApproximately(MathF.PI / 2f, 0.0001f, "screen y grows downward");
    }

    [Fact]
    public void ArrowPoints_PointingRight_HasTheTipAheadAndTheBaseBehind()
    {
        Span<Vector2> points = stackalloc Vector2[3];
        UiWorldProjection.ArrowPoints(new Vector2(100f, 50f), 0f, 20f, points);

        points[0].Should().Be(new Vector2(100f, 50f));
        points[1].X.Should().BeApproximately(80f, 0.0001f);
        points[2].X.Should().BeApproximately(80f, 0.0001f);
        (points[1].Y - points[2].Y).Should().BeApproximately(20f, 0.0001f, "the base spans the arrow's own width");
    }

    [Fact]
    public void ArrowPoints_TooSmallASpan_IsRefused()
    {
        var act = () =>
        {
            var points = new Vector2[2];
            UiWorldProjection.ArrowPoints(Vector2.Zero, 0f, 10f, points);
        };

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region UiRect

    [Fact]
    public void UiRect_PointAt_ResolvesTheNineAnchors()
    {
        var rect = new UiRect(10f, 20f, 100f, 50f);

        rect.PointAt(UiAnchor.TopLeft).Should().Be(new Vector2(10f, 20f));
        rect.PointAt(UiAnchor.MiddleCenter).Should().Be(new Vector2(60f, 45f));
        rect.PointAt(UiAnchor.BottomRight).Should().Be(new Vector2(110f, 70f));
    }

    [Fact]
    public void UiRect_Expand_GrowsOnEverySide()
    {
        new UiRect(10f, 10f, 20f, 20f).Expand(5f).Should().Be(new UiRect(5f, 5f, 30f, 30f));
    }

    [Fact]
    public void UiRect_Expand_NeverShrinksBelowNothing()
    {
        new UiRect(10f, 10f, 20f, 20f).Expand(-50f).Size.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void UiRect_FromBounds_AcceptsCornersInEitherOrder()
    {
        UiRect.FromBounds(new Vector2(100f, 80f), new Vector2(20f, 10f))
            .Should().Be(new UiRect(20f, 10f, 80f, 70f));
    }

    [Fact]
    public void UiRect_Contains_ExcludesTheFarEdges()
    {
        var rect = new UiRect(0f, 0f, 10f, 10f);

        rect.Contains(new Vector2(0f, 0f)).Should().BeTrue();
        rect.Contains(new Vector2(10f, 5f)).Should().BeFalse();
    }

    [Fact]
    public void UiRect_Intersects_IsTrueOnlyWhenAreasOverlap()
    {
        var rect = new UiRect(0f, 0f, 10f, 10f);

        rect.Intersects(new UiRect(5f, 5f, 10f, 10f)).Should().BeTrue();
        rect.Intersects(new UiRect(10f, 0f, 10f, 10f)).Should().BeFalse("touching edges do not overlap");
    }

    [Fact]
    public void UiRect_IsEmpty_ReportsAZeroSizedRect()
    {
        UiRect.Empty.IsEmpty.Should().BeTrue();
        new UiRect(0f, 0f, 1f, 1f).IsEmpty.Should().BeFalse();
    }

    #endregion
}
