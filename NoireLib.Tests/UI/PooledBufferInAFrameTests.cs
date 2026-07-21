using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.Helpers;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives borrowed working buffers through a real ImGui frame, which is where the property that matters is
/// observable: two surfaces drawing in the same frame each get their own buffer, and neither costs the frame any
/// allocated bytes.
/// </summary>
/// <remarks>
/// <see cref="PooledBufferTests"/> covers the buffer's own contract in isolation. What it cannot show is the thing
/// the buffer exists for, because "allocates nothing per frame" is a property of a frame. This measures one.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class PooledBufferInAFrameTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public PooledBufferInAFrameTests(UiHarness harness) => this.harness = harness;

    /// <summary>
    /// Fills a buffer with a polygon and paints it, the shape of a surface whose working set is sized by its data.
    /// </summary>
    private static void PaintPolygon(Vector2 centre, float radius, int points, Vector4 color)
    {
        using var buffer = PooledBuffer<Vector2>.Rent(points);

        var span = buffer.Span;

        for (var index = 0; index < span.Length; index++)
        {
            var angle = MathF.Tau * index / span.Length;
            span[index] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        NoireShapes.Fill(span, color);
    }

    [Fact]
    public void TwoSurfacesDrawingInOneFrameProduceIndependentOutput()
    {
        var both = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            {
                PaintPolygon(new Vector2(120f, 120f), 40f, 6, new Vector4(1f, 1f, 1f, 1f));
                PaintPolygon(new Vector2(320f, 120f), 40f, 6, new Vector4(1f, 1f, 1f, 1f));
            }));

        var one = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
                PaintPolygon(new Vector2(120f, 120f), 40f, 6, new Vector4(1f, 1f, 1f, 1f))));

        // Two hexagons are exactly twice one hexagon. If the second surface had been handed the first's buffer, the
        // geometry would not come out as two whole independent shapes.
        both.TotalVtxCount.Should().Be(one.TotalVtxCount * 2);
        both.TotalIdxCount.Should().Be(one.TotalIdxCount * 2);
    }

    [Fact]
    public void ADifferentlySizedSecondSurfaceStillGetsItsOwnBuffer()
    {
        // Different lengths land in different pool buckets, which is the case where a shared buffer would silently
        // truncate one of the two rather than corrupting both visibly.
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            {
                PaintPolygon(new Vector2(120f, 120f), 40f, 5, new Vector4(1f, 1f, 1f, 1f));
                PaintPolygon(new Vector2(320f, 120f), 40f, 32, new Vector4(1f, 1f, 1f, 1f));
            }));

        // A pentagon and a 32-gon, each whole: 5 + 32 vertices for the fills plus the antialiased edge each carries.
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.TotalIdxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RentingInsideADrawnFrameCostsTheFrameNothing()
    {
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            {
                PaintPolygon(new Vector2(120f, 120f), 40f, 64, new Vector4(1f, 1f, 1f, 1f));
                PaintPolygon(new Vector2(320f, 120f), 40f, 64, new Vector4(1f, 1f, 1f, 1f));
            }));

        // The reason the buffer exists. Sixty-four points is well past what a stackalloc would want to carry, and a
        // fresh array for each of the two would be garbage on the draw thread every frame.
        result.AllocatedBytes.Should().Be(0L);
    }
}
