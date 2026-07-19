using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the UI scale rule: a pixel value NoireUI ships or a plugin sets is written at 100% and multiplied once, at the
/// point it is resolved.<br/>
/// The failure this guards against is invisible at 100%, which is the only scale a library is ever developed at: a
/// value left unscaled looks perfect until someone else runs the plugin, and a value scaled twice by two call sites
/// each being careful looks perfect for exactly as long.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireUiScaleTests : IDisposable
{
    public void Dispose() => NoireUI.ScaleOverride = null;

    private static void At(float scale) => NoireUI.ScaleOverride = () => scale;

    #region The scale itself

    [Fact]
    public void Scale_IsOne_WhenThereIsNoImGuiContext()
    {
        NoireUI.Scale.Should().Be(1f, "because a scale of zero would collapse every measurement built on it");
    }

    [Theory]
    [InlineData(1f, 400f)]
    [InlineData(1.5f, 600f)]
    [InlineData(2f, 800f)]
    public void Scaled_MultipliesALogicalValue(float scale, float expected)
    {
        At(scale);

        NoireUI.Scaled(400f).Should().Be(expected);
    }

    [Fact]
    public void Scaled_MultipliesBothComponentsOfAPair()
    {
        At(1.5f);

        NoireUI.Scaled(new Vector2(12f, 10f)).Should().Be(new Vector2(18f, 15f));
    }

    #endregion

    #region Theme shape values

    [Fact]
    public void ResolveRounding_ScalesAThemeValue()
    {
        At(2f);
        var theme = new NoireTheme { Rounding = 6f };

        theme.ResolveRounding().Should().Be(12f, "because a rounding set on a theme is written at 100%");
    }

    [Fact]
    public void ResolveFramePadding_ScalesAThemeValue()
    {
        At(1.5f);
        var theme = new NoireTheme { FramePadding = new Vector2(10f, 6f) };

        theme.ResolveFramePadding().Should().Be(new Vector2(15f, 9f));
    }

    [Fact]
    public void ResolveItemSpacing_ScalesAThemeValue()
    {
        At(2f);
        var theme = new NoireTheme { ItemSpacing = new Vector2(8f, 4f) };

        theme.ResolveItemSpacing().Should().Be(new Vector2(16f, 8f));
    }

    [Fact]
    public void ResolveBorderSize_ScalesAThemeValue()
    {
        At(2f);
        var theme = new NoireTheme { BorderSize = 1f };

        theme.ResolveBorderSize().Should().Be(2f);
    }

    [Fact]
    public void ResolveBorderSize_KeepsZeroAtZero()
    {
        At(2f);
        var theme = new NoireTheme { BorderSize = 0f };

        theme.ResolveBorderSize().Should().Be(0f, "because zero is a real value here, and the way to ask for no border");
    }

    #endregion

    #region Style values

    [Fact]
    public void ToastStyle_ScalesItsPixelValues()
    {
        At(2f);
        var style = new ToastStyle();

        style.ScaledPadding.Should().Be(style.Padding * 2f);
        style.ScaledGap.Should().Be(style.Gap * 2f);
        style.ScaledStripeWidth.Should().Be(style.StripeWidth * 2f);
        style.ScaledBorderSize.Should().Be(style.BorderSize * 2f);
        style.ScaledTimerThickness.Should().Be(style.TimerThickness * 2f);
        style.ScaledProgressHeight.Should().Be(style.ProgressHeight * 2f);
        style.ScaledSlideDistance.Should().Be(style.SlideDistance * 2f);
    }

    [Fact]
    public void ButtonStyle_FallsThroughToTheTheme_WhenItSetsNothing()
    {
        At(2f);
        NoireTheme.Current = new NoireTheme { Rounding = 4f, FramePadding = new Vector2(8f, 5f) };

        try
        {
            var style = new ButtonStyle();

            style.ResolveRounding().Should().Be(8f, "because an unset style value resolves through the theme, which scales it once");
            style.ResolvePadding().Should().Be(new Vector2(16f, 10f));
        }
        finally
        {
            NoireTheme.Current = new NoireTheme();
        }
    }

    [Fact]
    public void ButtonStyle_ScalesItsOwnValues_WhenItSetsThem()
    {
        At(1.5f);
        var style = new ButtonStyle { Rounding = 4f, Padding = new Vector2(10f, 6f), BorderSize = 2f, HoldBorderThickness = 3f };

        style.ResolveRounding().Should().Be(6f);
        style.ResolvePadding().Should().Be(new Vector2(15f, 9f));
        style.ResolveBorderSize().Should().Be(3f);
        style.ScaledHoldBorderThickness.Should().Be(4.5f);
    }

    [Fact]
    public void TooltipStyle_ScalesItsOffsets()
    {
        At(2f);
        var style = new TooltipStyle();

        style.ScaledMouseOffset.Should().Be(new Vector2(32f, 32f));
        style.ScaledItemGap.Should().Be(12f);
    }

    [Fact]
    public void TooltipStyle_LeavesAnUnsetValueUnset()
    {
        At(2f);
        var style = new TooltipStyle();

        style.ScaledRounding.Should().BeNull("because an unset value has to stay unset so the ImGui style shows through");
        style.ScaledPadding.Should().BeNull();
        style.ScaledBorderSize.Should().BeNull();
    }

    [Fact]
    public void ModalOptions_ScalesItsWidth()
    {
        At(1.5f);

        new ModalOptions().ScaledWidth.Should().Be(630f);
    }

    #endregion

    #region Positions

    [Fact]
    public void UiPosition_ScalesItsOffset()
    {
        At(2f);
        var position = UiPosition.AtAnchor(UiAnchor.TopLeft, new Vector2(20f, 20f));

        position.Resolve(new Vector2(100f, 50f), Vector2.Zero, new Vector2(1000f, 500f))
            .Should().Be(new Vector2(40f, 40f), "because an anchor offset clears the same margin at every scale");
    }

    [Fact]
    public void UiPosition_ScalesAnAbsolutePosition()
    {
        At(2f);
        var position = UiPosition.AtAbsolute(100f, 50f);

        position.Resolve(new Vector2(10f, 10f), Vector2.Zero, new Vector2(1000f, 500f))
            .Should().Be(new Vector2(200f, 100f));
    }

    [Fact]
    public void UiPosition_LeavesTheElementSizeAlone()
    {
        At(2f);
        var position = UiPosition.AtAnchor(UiAnchor.BottomRight);

        // The element size is measured, so it arrives already at the right scale. Scaling it here would place a
        // bottom-right anchored element off the bottom right of the screen by its own size.
        position.Resolve(new Vector2(100f, 50f), Vector2.Zero, new Vector2(1000f, 500f))
            .Should().Be(new Vector2(900f, 450f));
    }

    #endregion
}
