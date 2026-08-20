using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The toast chrome's custom-draw surface. The shipped painting is the record's own parts, so there is no parity to
/// assert; what is asserted is that the parts paint, that they respect their guards, and that the style's clone
/// carries the new members.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireToastHookTests : IClassFixture<UiHarness>
{
    private static readonly Vector2 Min = new(100f, 100f);
    private static readonly Vector2 Max = new(380f, 180f);

    private readonly UiHarness harness;

    public NoireToastHookTests(UiHarness harness) => this.harness = harness;

    /// <summary>
    /// A chrome record over the current window's list, with every colour opaque so anything drawn shows as vertices.
    /// </summary>
    private static UiToastDraw Chrome(
        ToastTimerMode timerMode = ToastTimerMode.BottomBar,
        float timerFraction = 0.5f,
        float stripeWidth = 3f,
        float borderSize = 1f)
        => new(
            ImGui.GetWindowDrawList(),
            null!,
            Min,
            Max,
            Min,
            Max,
            new Vector4(1f, 0.5f, 0f, 1f),
            1f,
            false,
            4f,
            new Vector4(0.1f, 0.1f, 0.1f, 1f),
            new Vector4(1f, 0.5f, 0f, 1f),
            stripeWidth,
            new Vector4(0.5f, 0.5f, 0.5f, 1f),
            borderSize,
            timerMode,
            timerFraction,
            new Vector4(1f, 0.5f, 0f, 1f),
            2f,
            0.16f);

    [Fact]
    public void Clone_CarriesTheChromeHookAndTheButtonStyles()
    {
        var style = new ToastStyle
        {
            CustomDraw = static _ => { },
            CloseButtonStyle = new ButtonStyle { Tone = ButtonTone.Neutral },
            ActionButtonStyle = new ButtonStyle { Tone = ButtonTone.Ghost },
        };

        var clone = style.Clone();

        clone.CustomDraw.Should().BeSameAs(style.CustomDraw);
        clone.CloseButtonStyle.Should().BeSameAs(style.CloseButtonStyle);
        clone.ActionButtonStyle.Should().BeSameAs(style.ActionButtonStyle);
    }

    [Fact]
    public void Parts_PaintTheChrome()
    {
        var result = harness.Draw(
            static () =>
            {
                var chrome = Chrome();

                chrome.DrawBackground();
                chrome.DrawStripe();
                chrome.DrawBorder();
                chrome.DrawTimer();
            },
            warmUpFrames: 1);

        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Parts_RespectTheirGuards()
    {
        var result = harness.Draw(
            static () =>
            {
                var chrome = Chrome(timerMode: ToastTimerMode.None, stripeWidth: 0f, borderSize: 0f);

                chrome.DrawStripe();
                chrome.DrawBorder();
                chrome.DrawTimer();
            },
            warmUpFrames: 1);

        result.TotalVtxCount.Should().Be(0, "a removed stripe, border and countdown draw nothing");
    }

    [Fact]
    public void Timer_DrawsNothingWhenThereIsNothingToCount()
    {
        var result = harness.Draw(
            static () =>
            {
                var chrome = Chrome(timerFraction: 0f);
                chrome.DrawTimer();
            },
            warmUpFrames: 1);

        result.TotalVtxCount.Should().Be(0);
    }

    [Theory]
    [InlineData(ToastTimerMode.BottomBar)]
    [InlineData(ToastTimerMode.TopBar)]
    [InlineData(ToastTimerMode.Stripe)]
    [InlineData(ToastTimerMode.Border)]
    [InlineData(ToastTimerMode.TintLeftToRight)]
    [InlineData(ToastTimerMode.TintRightToLeft)]
    public void Timer_PaintsEveryMode(ToastTimerMode mode)
    {
        var result = harness.Draw(
            () =>
            {
                var chrome = Chrome(timerMode: mode);
                chrome.DrawTimer();
            },
            warmUpFrames: 1);

        result.TotalVtxCount.Should().BeGreaterThan(0);
    }
}
