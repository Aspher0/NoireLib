using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The overlay button's custom-draw surface. The button itself is a drawable needing an initialized plugin, so what
/// runs headless is the record's parts and the style contract.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireOverlayButtonHookTests : IClassFixture<UiHarness>
{
    private static readonly Vector2 Min = new(100f, 100f);
    private static readonly Vector2 Max = new(180f, 132f);

    private readonly UiHarness harness;

    public NoireOverlayButtonHookTests(UiHarness harness) => this.harness = harness;

    private static UiOverlayButtonDraw Draw(float borderSize)
        => new(
            ImGui.GetWindowDrawList(),
            null!,
            Min,
            Max,
            false,
            false,
            true,
            false,
            new Vector4(0.2f, 0.2f, 0.25f, 1f),
            new Vector4(0.6f, 0.6f, 0.6f, 1f),
            borderSize,
            4f);

    [Fact]
    public void Clone_CarriesTheHook()
    {
        var style = new OverlayButtonStyle { CustomDraw = static _ => { } };

        style.Clone().CustomDraw.Should().BeSameAs(style.CustomDraw);
    }

    [Fact]
    public void Parts_PaintTheBackgroundAndBorder()
    {
        var result = harness.Draw(
            static () =>
            {
                var args = Draw(borderSize: 2f);

                args.DrawBackground();
                args.DrawBorder();
            },
            warmUpFrames: 1);

        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Border_DrawsNothingAtZeroThickness()
    {
        var result = harness.Draw(
            static () => Draw(borderSize: 0f).DrawBorder(),
            warmUpFrames: 1);

        result.TotalVtxCount.Should().Be(0);
    }
}
