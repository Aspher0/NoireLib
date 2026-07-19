using FluentAssertions;
using NoireLib.UI;
using System;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the geometry every shape in <see cref="NoireShapes"/> is drawn from, and the scale rule its styles follow.
/// <br/>
/// The path is worth testing on its own because three separate things depend on properties of it that nothing else
/// checks: filling needs it convex, beveling needs it wound clockwise so an edge can tell which way it faces, and both
/// need it to stay inside the rectangle it was asked for. A path that quietly loses one of those does not fail, it
/// draws something slightly wrong.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireShapesTests : IDisposable
{
    public void Dispose() => NoireUI.ScaleOverride = null;

    private static readonly Vector2 Min = new(10f, 20f);
    private static readonly Vector2 Max = new(110f, 80f);

    private static Span<Vector2> Buffer => new Vector2[NoireShapes.MaxRectPathPoints];

    /// <summary>
    /// Twice the signed area. Positive is clockwise on screen, because the draw list's y axis grows downwards.
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

    #region Square corners

    [Fact]
    public void RectPath_WritesTheFourCornersClockwise_WhenNothingIsCut()
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Square, 0f);

        count.Should().Be(4);
        points[0].Should().Be(Min, "because a path starts at the top left");
        points[1].Should().Be(new Vector2(Max.X, Min.Y));
        points[2].Should().Be(Max);
        points[3].Should().Be(new Vector2(Min.X, Max.Y));
    }

    [Theory]
    [InlineData(CornerShape.Rounded)]
    [InlineData(CornerShape.Notched)]
    public void RectPath_LeavesTheRectangleSquare_WhenTheCutIsZero(CornerShape shape)
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, shape, 0f);

        count.Should().Be(4, "because a cut of no depth is not a cut");
    }

    [Fact]
    public void RectPath_LeavesTheRectangleSquare_WhenNoCornerIsSelected()
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Notched, 12f, RectCorners.None);

        count.Should().Be(4);
    }

    #endregion

    #region Cut corners

    [Fact]
    public void RectPath_ReplacesEachCornerWithTwoPoints_WhenNotched()
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Notched, 10f);

        count.Should().Be(8, "because a chamfer turns one corner point into the two ends of the cut");
        points.ToArray().Take(count).Should().NotContain(Min, "because the corner itself is what was cut away");
    }

    [Fact]
    public void RectPath_CutsOnlyTheSelectedCorners()
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Notched, 10f, RectCorners.Diagonal);

        count.Should().Be(6, "because two corners become two points each and two stay a single point");

        var written = points.ToArray().Take(count).ToArray();
        written.Should().Contain(new Vector2(Max.X, Min.Y), "because the top right corner was left square");
        written.Should().Contain(new Vector2(Min.X, Max.Y), "because the bottom left corner was left square");
    }

    [Fact]
    public void RectPath_UsesMoreThanTwoPointsPerCorner_WhenRounded()
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Rounded, 12f);

        count.Should().BeGreaterThan(8, "because an arc is not a chamfer");
    }

    [Theory]
    [InlineData(CornerShape.Notched)]
    [InlineData(CornerShape.Rounded)]
    public void RectPath_ClampsTheCut_WhenItIsDeeperThanHalfTheShortestSide(CornerShape shape)
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, shape, 500f);

        count.Should().BeGreaterThan(0);

        foreach (var point in points[..count])
        {
            point.X.Should().BeInRange(Min.X, Max.X, "because a cut deeper than the rectangle would turn it inside out");
            point.Y.Should().BeInRange(Min.Y, Max.Y);
        }
    }

    #endregion

    #region Pills

    [Fact]
    public void RectPath_DoesNotRepeatItsFirstPoint_WhenTheCutMakesAPill()
    {
        var points = Buffer;

        // A corner radius of half the height is a fully rounded chip, which is what any pill-shaped tag asks for. Both
        // left-hand arcs then centre on the vertical middle and meet at the same point on the left edge.
        var height = Max.Y - Min.Y;
        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Rounded, height * 0.5f);

        count.Should().BeGreaterThan(3);
        points[count - 1].Should().NotBe(points[0],
            "because the closing edge is added by the stroke, and a repeated first point leaves it zero length, which "
            + "has no direction to build a join from and draws as a spike straight across the shape");
    }

    /// <summary>
    /// The invariant rather than the instance. Every coincident pair is a zero-length edge, a join built from no
    /// direction at all, and a spike across the shape; checking only the one that was reported is how the second one
    /// survived the first fix.
    /// </summary>
    [Theory]
    [InlineData(CornerShape.Rounded, 1f)]
    [InlineData(CornerShape.Rounded, 0.5f)]
    [InlineData(CornerShape.Rounded, 0.25f)]
    [InlineData(CornerShape.Notched, 1f)]
    [InlineData(CornerShape.Notched, 0.5f)]
    [InlineData(CornerShape.Square, 1f)]
    public void RectPath_NeverRepeatsAPoint(CornerShape shape, float fractionOfHalfHeight)
    {
        var points = Buffer;
        var cornerSize = (Max.Y - Min.Y) * 0.5f * fractionOfHalfHeight;

        var count = NoireShapes.RectPath(points, Min, Max, shape, cornerSize);

        for (var i = 0; i < count; i++)
        {
            var next = points[(i + 1) % count];

            points[i].Should().NotBe(next, $"because point {i} and the one after it would be a zero-length edge");
        }
    }

    [Fact]
    public void RectPath_NeverRepeatsAPoint_OnASquashedRectangle()
    {
        var points = Buffer;

        // Short enough that the corner cuts from both ends meet in the middle of every side at once.
        var count = NoireShapes.RectPath(points, Min, Min + new Vector2(24f, 24f), CornerShape.Rounded, 12f);

        for (var i = 0; i < count; i++)
            points[i].Should().NotBe(points[(i + 1) % count]);
    }

    [Fact]
    public void RectPath_KeepsTheClosingEdgeTheSameLengthAsItsNeighbours_OnAPill()
    {
        var points = Buffer;
        var height = Max.Y - Min.Y;

        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Rounded, height * 0.5f);

        var closing = (points[0] - points[count - 1]).Length();
        var neighbour = (points[count - 1] - points[count - 2]).Length();

        closing.Should().BeApproximately(neighbour, 0.5f, "because the seam should be an ordinary segment, not a degenerate one");
    }

    #endregion

    #region Properties everything drawn from the path depends on

    [Theory]
    [InlineData(CornerShape.Square, 0f)]
    [InlineData(CornerShape.Notched, 12f)]
    [InlineData(CornerShape.Rounded, 12f)]
    public void RectPath_StaysInsideTheRectangle(CornerShape shape, float cornerSize)
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, shape, cornerSize);

        foreach (var point in points[..count])
        {
            point.X.Should().BeInRange(Min.X, Max.X);
            point.Y.Should().BeInRange(Min.Y, Max.Y);
        }
    }

    [Theory]
    [InlineData(CornerShape.Square, 0f)]
    [InlineData(CornerShape.Notched, 12f)]
    [InlineData(CornerShape.Rounded, 12f)]
    public void RectPath_WindsClockwise(CornerShape shape, float cornerSize)
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Max, shape, cornerSize);

        Winding(points[..count]).Should().BePositive("because Bevel reads which way an edge faces from the winding");
    }

    #endregion

    #region Refusals

    [Theory]
    [InlineData(0f, 60f)]
    [InlineData(100f, 0f)]
    [InlineData(-10f, 60f)]
    public void RectPath_WritesNothing_WhenTheRectangleIsEmpty(float width, float height)
    {
        var points = Buffer;

        var count = NoireShapes.RectPath(points, Min, Min + new Vector2(width, height), CornerShape.Notched, 5f);

        count.Should().Be(0);
    }

    [Fact]
    public void RectPath_WritesNothing_WhenTheBufferIsTooSmall()
    {
        Span<Vector2> points = new Vector2[3];

        var count = NoireShapes.RectPath(points, Min, Max, CornerShape.Square, 0f);

        count.Should().Be(0, "because a partly written path would draw a shape nobody asked for");
    }

    #endregion

    #region Arc paths

    private static Span<Vector2> ArcBuffer => new Vector2[NoireShapes.MaxArcPathPoints];

    [Fact]
    public void ArcPath_DoesNotRepeatItsFirstPoint_WhenTheSweepIsAFullTurn()
    {
        var points = ArcBuffer;

        var count = NoireShapes.ArcPath(points, Vector2.Zero, 60f, 0f, 1f, out var closed);

        closed.Should().BeTrue();
        count.Should().BeGreaterThan(3);
        points[count - 1].Should().NotBe(points[0],
            "because the closing edge is added by the stroke, and repeating the first point leaves that edge zero "
            + "length, which has no direction to build a join from and draws as a spike at the seam");
    }

    [Fact]
    public void ArcPath_EndsWhereItWasAskedTo_WhenTheSweepIsPartial()
    {
        var points = ArcBuffer;

        var count = NoireShapes.ArcPath(points, Vector2.Zero, 60f, 0f, 0.25f, out var closed);

        closed.Should().BeFalse();
        points[0].Y.Should().BeApproximately(-60f, 0.01f, "because zero turns is twelve o'clock");
        points[count - 1].X.Should().BeApproximately(60f, 0.01f, "because a quarter turn clockwise is three o'clock");
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    public void ArcPath_WritesNothing_WhenTheSweepIsEmpty(float fromTurns, float toTurns)
    {
        var points = ArcBuffer;

        var count = NoireShapes.ArcPath(points, Vector2.Zero, 60f, fromTurns, toTurns, out var closed);

        count.Should().Be(0, "because a gauge reading zero draws nothing at all, not a hairline at its start");
        closed.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.75f)]
    [InlineData(1f)]
    public void ArcPath_KeepsEveryPointOnTheCircle(float sweep)
    {
        var points = ArcBuffer;

        var count = NoireShapes.ArcPath(points, new Vector2(100f, 50f), 60f, 0f, sweep, out _);

        foreach (var point in points[..count])
            (point - new Vector2(100f, 50f)).Length().Should().BeApproximately(60f, 0.01f);
    }

    [Fact]
    public void ArcPath_WritesNothing_WhenTheBufferIsTooSmall()
    {
        Span<Vector2> points = new Vector2[2];

        var count = NoireShapes.ArcPath(points, Vector2.Zero, 60f, 0f, 1f, out _);

        count.Should().Be(0);
    }

    #endregion

    #region Scopes

    [Fact]
    public void Gradient_StillRunsItsBody_WhenThereIsNoImGuiContext()
    {
        var ran = false;

        NoireShapes.Gradient(Min, Max, GradientAxis.Vertical, Vector4.One, Vector4.Zero, () => ran = true);

        ran.Should().BeTrue("because a scope that swallows its body turns a missing context into missing drawing");
    }

    [Fact]
    public void On_RestoresThePreviousTarget_WhenTheBodyThrows()
    {
        var act = () => NoireShapes.On(default, static () => throw new InvalidOperationException("boom"));

        act.Should().Throw<InvalidOperationException>("because a body's exception is a bug and stays visible");
        NoireShapes.DrawList.IsNull.Should().BeTrue("because the redirect must not outlive the scope that set it");
    }

    [Theory]
    [InlineData(null)]
    public void Gradient_RefusesANullBody(Action? body)
    {
        var act = () => NoireShapes.Gradient(Min, Max, GradientAxis.Vertical, Vector4.One, Vector4.Zero, body!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Styles follow the scale rule

    [Theory]
    [InlineData(1f, 14f)]
    [InlineData(2f, 28f)]
    public void PlateStyle_ScalesTheValuesItShipsADefaultFor(float scale, float expected)
    {
        NoireUI.ScaleOverride = () => scale;

        var style = new PlateStyle { CornerSize = 14f, BorderSize = 14f, BevelSize = 14f, GlowSpread = 14f };

        style.ResolveCornerSize().Should().Be(expected);
        style.ResolveBorderSize().Should().Be(expected);
        style.ScaledBevelSize.Should().Be(expected);
        style.ScaledGlowSpread.Should().Be(expected);
    }

    [Theory]
    [InlineData(1f, 6f)]
    [InlineData(1.5f, 9f)]
    public void FrameStyle_ScalesTheValuesItShipsADefaultFor(float scale, float expected)
    {
        NoireUI.ScaleOverride = () => scale;

        var style = new FrameStyle { Thickness = 6f, Inset = 6f, DoubleGap = 6f, TickLength = 6f, TickInset = 6f };

        style.ScaledThickness.Should().Be(expected);
        style.ScaledInset.Should().Be(expected);
        style.ScaledDoubleGap.Should().Be(expected);
        style.ScaledTickLength.Should().Be(expected);
        style.ScaledTickInset.Should().Be(expected);
        style.ResolveTickThickness().Should().Be(expected, "because an unset tick thickness follows the line thickness");
    }

    [Fact]
    public void PlateStyle_DerivesTheBevelFromTheFill_WhenItWasNotGiven()
    {
        var fill = new Vector4(0.2f, 0.2f, 0.25f, 1f);
        var style = new PlateStyle { Fill = fill };

        style.ResolveBevelLight().Should().NotBe(fill, "because a bevel that matches its fill is not a bevel");
        style.ResolveBevelShadow().Should().NotBe(fill);
    }

    [Fact]
    public void Clone_LeavesTheOriginalAlone()
    {
        var style = new PlateStyle { CornerSize = 8f };

        var clone = style.Clone();
        clone.CornerSize = 20f;

        style.CornerSize.Should().Be(8f);
    }

    #endregion
}
