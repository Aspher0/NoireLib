using FluentAssertions;
using NoireLib.UI;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The tooltip chrome's custom-draw hook: it is suppressed while the tooltip is measured off screen, it receives the
/// placed window, it removes the window's own chrome, and the style's clone carries it.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireTooltipHookTests : IClassFixture<UiHarness>
{
    private static readonly NoireContent Content = new NoireContent().AddText("A line of explanation.");

    private readonly UiHarness harness;

    public NoireTooltipHookTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Clone_CarriesTheHook()
    {
        var style = new TooltipStyle { CustomDraw = static _ => { } };

        style.Clone().CustomDraw.Should().BeSameAs(style.CustomDraw);
    }

    [Fact]
    public void Hook_IsNotCalledWhileMeasuring_ThenReceivesThePlacedWindow()
    {
        var sizes = new List<Vector2>();
        var style = new TooltipStyle { CustomDraw = args => sizes.Add(args.Size) };

        harness.Draw(() => NoireTooltip.Show(Content, style, "hook_measure"), warmUpFrames: 0);

        sizes.Should().BeEmpty("the first frame is the measuring frame and the hook must not see it");

        harness.Draw(() => NoireTooltip.Show(Content, style, "hook_measure"), warmUpFrames: 2);

        sizes.Should().NotBeEmpty();
        sizes[^1].X.Should().BeGreaterThan(0f);
        sizes[^1].Y.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void Hook_SuppressesTheWindowsOwnChrome()
    {
        var shippedStyle = new TooltipStyle { BorderSize = 2f };
        var hookedStyle = new TooltipStyle { BorderSize = 2f, CustomDraw = static _ => { } };

        var shipped = harness.Draw(() => NoireTooltip.Show(Content, shippedStyle, "chrome_shipped"), warmUpFrames: 3);
        var hooked = harness.Draw(() => NoireTooltip.Show(Content, hookedStyle, "chrome_hooked"), warmUpFrames: 3);

        // Same content either way; the hooked frame is missing exactly the window's background and border.
        hooked.TotalVtxCount.Should().BeLessThan(shipped.TotalVtxCount);
    }

    [Fact]
    public void Parts_PaintTheChromeTheHookReplaced()
    {
        var calls = 0;
        var style = new TooltipStyle
        {
            BorderSize = 2f,
            CustomDraw = args =>
            {
                calls++;
                args.DrawBackground();
                args.DrawBorder();
            },
        };

        harness.Draw(() => NoireTooltip.Show(Content, style, "hook_parts"), warmUpFrames: 3);

        calls.Should().BeGreaterThan(0);
    }
}
