using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives the two cached ornaments through real ImGui frames, to show that caching their geometry did not change what
/// reaches the screen.
/// </summary>
/// <remarks>
/// The pure path tests assert the curve and the ray directions. What they cannot show is that the drawing still submits
/// the same geometry from them, which is where a wrong index or a lost transform would appear. The first frame computes
/// a shape and the second is served from the cache, so a cache that changed the shape would change the vertex count
/// between two otherwise identical frames.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireShapesGeometryCacheTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public NoireShapesGeometryCacheTests(UiHarness harness) => this.harness = harness;

    private static readonly Vector2 Centre = new(300f, 300f);

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);

    [Fact]
    public void Guilloche_ServedFromTheCache_DrawsTheSameGeometry()
    {
        var first = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 120f, White)));

        var second = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 120f, White)));

        first.TotalVtxCount.Should().BeGreaterThan(0);
        second.TotalVtxCount.Should().Be(first.TotalVtxCount);
        second.TotalIdxCount.Should().Be(first.TotalIdxCount);
    }

    [Fact]
    public void Sunburst_ServedFromTheCache_DrawsTheSameGeometry()
    {
        var first = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Sunburst(Centre, 120f, White)));

        var second = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Sunburst(Centre, 120f, White)));

        first.TotalVtxCount.Should().BeGreaterThan(0);
        second.TotalVtxCount.Should().Be(first.TotalVtxCount);
        second.TotalIdxCount.Should().Be(first.TotalIdxCount);
    }

    [Fact]
    public void Guilloche_AtADifferentRadius_ReusesTheSameCurve()
    {
        // The curve is held at radius one and scaled on the way out, so a second radius is a cache hit rather than a
        // second entry. A larger radius still draws more geometry, because it is given more segments to draw with.
        var small = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 40f, White)));

        var large = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 200f, White)));

        small.TotalVtxCount.Should().BeGreaterThan(0);
        large.TotalVtxCount.Should().BeGreaterThan(small.TotalVtxCount);
    }

    [Fact]
    public void Guilloche_Rotated_DrawsTheSameAmountOfGeometry()
    {
        var upright = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 120f, White, new GuillocheStyle())));

        var turned = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 120f, White, new GuillocheStyle { RotationTurns = 0.3f })));

        // Rotation is applied at submission and is not part of the key, which is what lets a turning ornament keep
        // hitting the cache. The same curve turned is still the same number of points.
        turned.TotalVtxCount.Should().Be(upright.TotalVtxCount);
    }

    [Fact]
    public void Sunburst_Rotated_DrawsTheSameAmountOfGeometry()
    {
        var upright = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Sunburst(Centre, 120f, White, new SunburstStyle())));

        var turned = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Sunburst(Centre, 120f, White, new SunburstStyle { RotationTurns = 0.3f })));

        turned.TotalVtxCount.Should().Be(upright.TotalVtxCount);
    }

    [Fact]
    public void Guilloche_DrawnAgain_AllocatesNothing()
    {
        // Settle the first draw, which computes the curve and puts it in the cache.
        harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 120f, White)));

        var result = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Guilloche(Centre, 120f, White)));

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Sunburst_DrawnAgain_AllocatesNothing()
    {
        harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Sunburst(Centre, 120f, White)));

        var result = harness.Draw(static () => NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            NoireShapes.Sunburst(Centre, 120f, White)));

        result.AllocatedBytes.Should().Be(0L);
    }
}
