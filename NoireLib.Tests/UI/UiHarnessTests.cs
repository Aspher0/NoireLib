using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Proves the harness runs a real ImGui frame and can tell drawing from nothing.
/// </summary>
/// <remarks>
/// These assert the harness itself rather than any widget. A widget test that fails is a widget problem; one of these
/// failing means no drawing test anywhere can be trusted.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class UiHarnessTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public UiHarnessTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Draw_WithAnEmptyBody_ProducesNoVertices()
    {
        var result = harness.Draw(static () => { });

        result.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void Draw_WithAFilledRectangle_ReachesTheDrawData()
    {
        var result = harness.Draw(static () =>
            ImGui.GetWindowDrawList().AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu));

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.TotalIdxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Draw_WithARedirectedShape_ProducesVertices()
    {
        // Driven through NoireShapes.On rather than by calling the shape directly. NoireShapes resolves its draw list
        // through NoireService when nothing has redirected it, and NoireService reports uninitialized outside a plugin,
        // so an undirected shape would paint into a null list and report zero for reasons that have nothing to do with
        // the shape.
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
                NoireShapes.Sunburst(new Vector2(200f, 200f), 80f, Vector4.One)));

        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Draw_WithAnAllocatingBody_ReportsThoseBytes()
    {
        const int size = 10_000;

        byte[]? held = null;

        var result = harness.Draw(() => held = new byte[size]);

        held!.Length.Should().Be(size);

        result.AllocatedBytes.Should().BeGreaterThanOrEqualTo(size);
        result.AllocatedBytes.Should().BeLessThan(size + 512);
    }

    [Fact]
    public void Draw_WithANonAllocatingBody_ReportsZeroBytes()
    {
        // Drawing a rectangle into an existing draw list is real work that reaches the screen, and it must cost no
        // garbage at all.
        var result = harness.Draw(static () =>
            ImGui.GetWindowDrawList().AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu));

        result.TotalVtxCount.Should().BeGreaterThan(0, "the draw must actually have happened for zero bytes to mean anything");
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Draw_WithAnInstrumentedSurface_ListsItsScope()
    {
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
                NoireShapes.Sunburst(new Vector2(200f, 200f), 80f, Vector4.One)));

        result.HasScope("NoireShapes.Sunburst").Should().BeTrue();
    }

    [Fact]
    public void Draw_WithAnUninstrumentedBlock_ListsNoScope()
    {
        var result = harness.Draw(static () =>
            ImGui.GetWindowDrawList().AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu));

        result.TotalVtxCount.Should().BeGreaterThan(0);

        // Drawing straight into the list opens nothing: real cost, no row in the profiler, and the time landing on
        // whatever encloses it. This is what obtaining a list through UiDraw prevents.
        result.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void Draw_WithWarmUpFrames_ReportsBytesForTheMeasuredFrameOnly()
    {
        const int size = 10_000;

        byte[]? held = null;

        var afterWarmUps = harness.Draw(() => held = new byte[size], warmUpFrames: 5);

        held!.Length.Should().Be(size);

        // Six frames each allocated the array; only the last one is reported.
        afterWarmUps.AllocatedBytes.Should().BeLessThan(size + 512);
    }

    [Fact]
    public void Draw_WithWarmUpFrames_ReportsVerticesForTheMeasuredFrameOnly()
    {
        var once = harness.Draw(static () =>
            ImGui.GetWindowDrawList().AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu));

        var afterWarmUps = harness.Draw(
            static () => ImGui.GetWindowDrawList().AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu),
            warmUpFrames: 5);

        afterWarmUps.TotalVtxCount.Should().Be(once.TotalVtxCount);
    }
}
