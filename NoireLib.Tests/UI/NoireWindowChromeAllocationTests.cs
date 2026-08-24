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

        // Was a whole PlateStyle per frame, for every window with its opacity turned down.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Fading_LeavesTheCallersStyleAlone()
    {
        var plate = new PlateStyle { Fill = new(1f, 1f, 1f, 1f) };
        var style = new WindowChromeStyle { Opacity = 0.25f, Plate = plate };

        harness.Draw(() => NoireWindowChrome.Draw(static () => { }, style), warmUpFrames: 2);

        // The reason fading copies at all: the plate is usually a shared static, and a window fading itself must not
        // fade everything else drawn from the same style with it.
        plate.Fill!.Value.W.Should().Be(1f);
    }
}
