using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks fills, antialiased edges, stroke reach, opacity, holes and gradients of the CPU rasterizer.</summary>
public class NoireRasterTests
{
    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    [Fact]
    public void Render_FilledSquare_CoversItsOutline_AndNothingElse()
    {
        var pixels = NoireRaster.Render([RasterShape.Path("M2 2 H8 V8 H2 z").Filled(Red)], 10, 10, 1f, Vector2.Zero);

        Pixel(pixels, 10, 5, 5).Should().Equal(255, 0, 0, 255);
        Pixel(pixels, 10, 0, 0)[3].Should().Be(0);
        Pixel(pixels, 10, 9, 9)[3].Should().Be(0);
    }

    [Fact]
    public void Render_EdgeThroughAPixel_CoversItPartly()
    {
        var pixels = NoireRaster.Render([RasterShape.Path("M0 0 H4.5 V10 H0 z").Filled(Red)], 10, 10, 1f, Vector2.Zero);

        Pixel(pixels, 10, 4, 5)[3].Should().BeInRange(100, 160, "because the edge splits that pixel in half");
    }

    [Fact]
    public void Render_Stroke_ReachesHalfItsWidthEitherSide()
    {
        var pixels = NoireRaster.Render([RasterShape.Path("M0 5 H10").Stroked(Blue, 2f)], 10, 10, 1f, Vector2.Zero);

        Pixel(pixels, 10, 5, 4)[3].Should().Be(255);
        Pixel(pixels, 10, 5, 5)[3].Should().Be(255);
        Pixel(pixels, 10, 5, 7)[3].Should().Be(0);
    }

    [Fact]
    public void Render_Opacity_ScalesCoverage()
    {
        var pixels = NoireRaster.Render([RasterShape.Path("M0 0 H10 V10 H0 z").Filled(Red) with { Opacity = 0.5f }], 10, 10, 1f, Vector2.Zero);

        Pixel(pixels, 10, 5, 5)[3].Should().BeInRange(127, 128);
    }

    [Fact]
    public void Render_HoleWoundTheOtherWay_StaysEmpty()
    {
        var pixels = NoireRaster.Render([RasterShape.Path("M0 0 H10 V10 H0 z M3 3 V7 H7 V3 z").Filled(Red)], 10, 10, 1f, Vector2.Zero);

        Pixel(pixels, 10, 5, 5)[3].Should().Be(0);
        Pixel(pixels, 10, 1, 1)[3].Should().Be(255);
    }

    [Fact]
    public void Render_ScaleAndOffset_PlaceTheShape()
    {
        var pixels = NoireRaster.Render([RasterShape.Path("M0 0 H2 V2 H0 z").Filled(Red)], 10, 10, 2f, new Vector2(5, 5));

        Pixel(pixels, 10, 7, 7)[3].Should().Be(255);
        Pixel(pixels, 10, 3, 3)[3].Should().Be(0);
    }

    [Fact]
    public void Render_Gradient_FollowsTheBoundingBox()
    {
        var gradient = NoireGradient.Linear(GradientDirection.LeftToRight, Red, Blue);
        var pixels = NoireRaster.Render([RasterShape.Path("M0 0 H20 V2 H0 z").Filled(gradient)], 20, 2, 1f, Vector2.Zero);

        Pixel(pixels, 20, 0, 1)[0].Should().BeGreaterThan(Pixel(pixels, 20, 0, 1)[2]);
        Pixel(pixels, 20, 19, 1)[2].Should().BeGreaterThan(Pixel(pixels, 20, 19, 1)[0]);
    }

    [Fact]
    public void Ellipse_IsClosed_AndStaysOnItsRadii()
    {
        var shape = RasterShape.Ellipse(new Vector2(5, 5), new Vector2(4, 2));

        shape.Subpaths.Should().ContainSingle();
        shape.Subpaths[0].Closed.Should().BeTrue();
        shape.Subpaths[0].Points.Should().OnlyContain(p => System.MathF.Abs((((p.X - 5) / 4) * ((p.X - 5) / 4)) + (((p.Y - 5) / 2) * ((p.Y - 5) / 2)) - 1f) < 1e-4f);
    }

    private static byte[] Pixel(byte[] pixels, int width, int x, int y)
        => pixels[(((y * width) + x) * 4)..((((y * width) + x) * 4) + 4)];
}
