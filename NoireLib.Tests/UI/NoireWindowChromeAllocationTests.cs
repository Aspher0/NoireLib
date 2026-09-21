using Dalamud.Bindings.ImGui;
using System.Numerics;
using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the window chrome at zero allocation per frame, including the path a window below full opacity takes.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireWindowChromeAllocationTests : IClassFixture<UiHarness>
{
    private static readonly WindowChromeStyle Opaque = new() { Opacity = 1f };

    private static readonly WindowChromeStyle Faded = new() { Opacity = 0.5f };

    private readonly UiHarness harness;

    public NoireWindowChromeAllocationTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Chrome_AtFullOpacity_AllocatesNothing()
    {
        var result = harness.Draw(static () => NoireWindowChrome.Draw(static () => { }, Opaque), warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Chrome_BelowFullOpacity_AllocatesNothing()
    {
        var result = harness.Draw(static () => NoireWindowChrome.Draw(static () => { }, Faded), warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void BodyDrag_AllocatesNothing()
    {
        var result = harness.Draw(static () => NoireWindowChrome.DragFromBody(), warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void BodyDrag_WithACursorOfItsOwn_AllocatesNothing()
    {
        var result = harness.Draw(static () => NoireWindowChrome.DragFromBody(ImGuiMouseCursor.Arrow), warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void ContinueDrag_AllocatesNothing()
    {
        var result = harness.Draw(static () => NoireWindowChrome.ContinueDrag(), warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void HandleDoubleClick_AllocatesNothing()
    {
        var result = harness.Draw(
            static () => NoireWindowChrome.DoubleClickFrom(Vector2.Zero, new Vector2(400f, 50f)),
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Theory]
    [InlineData(true, true, false, false, true, true)]
    [InlineData(false, true, false, false, true, false)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, false, false, false, false)]
    public void ShouldStart_LetsEveryItemKeepItsPress(bool inside, bool windowHovered, bool itemHovered, bool itemActive, bool pressed, bool expected)
    {
        NoireWindowChrome.ShouldStart(inside, windowHovered, itemHovered, itemActive, pressed).Should().Be(expected);
    }

    [Fact]
    public void Fading_LeavesTheCallersStyleAlone()
    {
        var plate = new PlateStyle { Fill = new(1f, 1f, 1f, 1f) };
        var style = new WindowChromeStyle { Opacity = 0.25f, Plate = plate };

        harness.Draw(() => NoireWindowChrome.Draw(static () => { }, style), warmUpFrames: 2);

        // The plate is usually a shared static. Fading must not change it for every other window.
        plate.Fill!.Value.W.Should().Be(1f);
    }
}
