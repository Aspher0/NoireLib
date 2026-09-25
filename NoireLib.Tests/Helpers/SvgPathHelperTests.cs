using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the SVG path reader: absolute and relative commands, the implicit line after a move, a close returning to the
/// subpath's start, the reflected control points of S and T, arcs, and curves staying within the tolerance.
/// </summary>
public class SvgPathHelperTests
{
    [Fact]
    public void Flatten_AbsoluteAndRelativeLines_LandOnTheSamePoints()
    {
        var absolute = SvgPathHelper.Flatten("M1 1 L4 1 L4 5");
        var relative = SvgPathHelper.Flatten("m1 1 l3 0 l0 4");

        absolute.Should().ContainSingle();
        relative[0].Points.Should().Equal(absolute[0].Points);
        absolute[0].Points.Should().Equal(new Vector2(1, 1), new Vector2(4, 1), new Vector2(4, 5));
    }

    [Fact]
    public void Flatten_CoordinatesAfterAMove_AreImplicitLines()
        => SvgPathHelper.Flatten("M0 0 10 0 10 10")[0].Points.Should().Equal(Vector2.Zero, new Vector2(10, 0), new Vector2(10, 10));

    [Fact]
    public void Flatten_HorizontalAndVertical_KeepTheOtherAxis()
        => SvgPathHelper.Flatten("M2 3 H8 v4 h-6")[0].Points
            .Should().Equal(new Vector2(2, 3), new Vector2(8, 3), new Vector2(8, 7), new Vector2(2, 7));

    [Fact]
    public void Flatten_Close_MarksTheSubpathClosed_AndTheNextCommandStartsFromItsStart()
    {
        var subpaths = SvgPathHelper.Flatten("M8 2 l6 11 H2 z l0 -2");

        subpaths.Should().HaveCount(2);
        subpaths[0].Closed.Should().BeTrue();
        subpaths[1].Closed.Should().BeFalse();
        subpaths[1].Points.Should().Equal(new Vector2(8, 2), new Vector2(8, 0));
    }

    [Fact]
    public void Flatten_Cubic_StaysWithinTheTolerance()
    {
        const float tolerance = 0.05f;
        var points = SvgPathHelper.Flatten("M0 0 C0 10 10 10 10 0", tolerance)[0].Points;

        points[0].Should().Be(Vector2.Zero);
        points[^1].X.Should().BeApproximately(10f, 1e-4f);
        points.Max(p => p.Y).Should().BeApproximately(7.5f, tolerance, "because the true curve peaks at three quarters of its control height");
    }

    [Fact]
    public void Flatten_SmoothCubic_ReflectsThePreviousControlPoint()
    {
        var smooth = SvgPathHelper.Flatten("M0 0 C0 10 10 10 10 0 S20 -10 20 0")[0].Points;
        var spelled = SvgPathHelper.Flatten("M0 0 C0 10 10 10 10 0 C10 -10 20 -10 20 0")[0].Points;

        smooth.Should().Equal(spelled);
    }

    [Fact]
    public void Flatten_SmoothQuadratic_ReflectsThePreviousControlPoint()
    {
        var smooth = SvgPathHelper.Flatten("M0 0 Q5 10 10 0 T20 0")[0].Points;
        var spelled = SvgPathHelper.Flatten("M0 0 Q5 10 10 0 Q15 -10 20 0")[0].Points;

        smooth.Should().Equal(spelled);
    }

    [Fact]
    public void Flatten_HalfCircleArc_StaysOnItsRadius()
    {
        var points = SvgPathHelper.Flatten("M0 0 A5 5 0 0 1 10 0")[0].Points;
        var centre = new Vector2(5, 0);

        points[^1].X.Should().BeApproximately(10f, 1e-3f);
        points.Should().OnlyContain(p => MathF.Abs(Vector2.Distance(p, centre) - 5f) < 1e-3f);
        points.Min(p => p.Y).Should().BeApproximately(-5f, 0.05f, "because a clockwise sweep from the left end passes over the top");
    }

    [Fact]
    public void Flatten_CompactNumbers_AreRead()
        => SvgPathHelper.Flatten("M1.5.5L-2e1-.5")[0].Points.Should().Equal(new Vector2(1.5f, 0.5f), new Vector2(-20f, -0.5f));

    [Fact]
    public void Flatten_MalformedData_ReturnsWhatItCouldRead()
    {
        var subpaths = SvgPathHelper.Flatten("M0 0 L5 5 # L9 9");

        subpaths.Should().ContainSingle();
        subpaths[0].Points.Should().Equal(Vector2.Zero, new Vector2(5, 5));
    }
}
