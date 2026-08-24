using Dalamud.Bindings.ImGui;
using System;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives <see cref="NoireShapes.FillUnder"/> through real ImGui frames, to show it submits one strip over the samples
/// it was given rather than a fan from the first of them.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireShapesBandTests : IClassFixture<UiHarness>
{
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);

    /// <summary>A concave trace: it rises, dips hard, and rises again.</summary>
    private static readonly Vector2[] Jagged =
    [
        new(100f, 200f), new(120f, 140f), new(140f, 190f), new(160f, 120f),
        new(180f, 205f), new(200f, 150f), new(220f, 195f), new(240f, 130f),
    ];

    private readonly UiHarness harness;

    public NoireShapesBandTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void FillUnder_SubmitsTwoVerticesPerSample()
    {
        var four = Draw(4);
        var eight = Draw(8);

        (eight.TotalVtxCount - four.TotalVtxCount).Should().Be(8, "two vertices per extra sample, and no more");
        (eight.TotalIdxCount - four.TotalIdxCount).Should().Be(24, "six indices per extra segment");
    }

    [Fact]
    public void FillUnder_DrawsTheSameGeometryEveryFrame()
    {
        var first = Draw(8);
        var second = Draw(8);

        first.TotalVtxCount.Should().Be(16);
        second.TotalVtxCount.Should().Be(first.TotalVtxCount);
        second.TotalIdxCount.Should().Be(first.TotalIdxCount);
    }

    [Fact]
    public void FillUnder_DrawsNothing_ForATransparentColor()
    {
        var result = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.FillUnder(Jagged, 240f, new Vector4(1f, 1f, 1f, 0f))));

        result.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void FillUnder_DrawsNothing_ForASingleSample()
    {
        var result = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.FillUnder(Jagged.AsSpan(0, 1), 240f, White)));

        result.TotalVtxCount.Should().Be(0, "a band needs two samples to have any width");
    }

    private UiHarnessResult Draw(int samples)
        => harness.Draw(() => NoireShapes.On(ImGui.GetWindowDrawList(), () =>
            NoireShapes.FillUnder(Jagged.AsSpan(0, samples), 240f, White)));
}
