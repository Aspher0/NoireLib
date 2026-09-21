using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the window menu at zero allocation per frame in both of its looks, and pins its settings model.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireWindowMenuTests : IClassFixture<UiHarness>
{
    private static readonly WindowMenuSettings Settings = new() { ClickThrough = true };

    private static readonly WindowMenuStyle Styled = new() { Note = "Shared by every window." };

    private static readonly WindowMenuStyle Delegated = new()
    {
        Width = 0f,
        MinWidth = 280f,
        OpacitySlider = new SliderStyle(),
        TextStepSlider = new SliderStyle(),
        ToggleOn = new ButtonStyle(),
        ToggleOff = new ButtonStyle(),
        TextStepNames = ["Small", "Medium", "Big"],
    };

    private static readonly ToggleStyle Curved = new() { AnimationCurve = UiCubicBezier.EaseOut.Curve };

    private static bool sawOpen;

    private readonly UiHarness harness;

    public NoireWindowMenuTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Closed_AllocatesNothing()
    {
        var result = harness.Draw(
            static () => NoireWindowMenu.Draw("alloc_closed", Vector2.Zero, new Vector2(20f, 20f), Settings, Styled),
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Open_BuiltInLook_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                if (!NoireWindowMenu.IsOpen("alloc_open"))
                    NoireWindowMenu.Toggle("alloc_open");

                NoireWindowMenu.Draw("alloc_open", new Vector2(400f, 10f), new Vector2(420f, 30f), Settings, Styled);
                sawOpen = NoireWindowMenu.IsOpen("alloc_open");
            },
            warmUpFrames: 4);

        sawOpen.Should().BeTrue("the measurement is only worth anything with the menu actually drawn");
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Open_DelegatedLook_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                if (!NoireWindowMenu.IsOpen("alloc_delegated"))
                    NoireWindowMenu.Toggle("alloc_delegated");

                NoireWindowMenu.Draw("alloc_delegated", new Vector2(400f, 10f), new Vector2(420f, 30f), Settings, Delegated);
                sawOpen = NoireWindowMenu.IsOpen("alloc_delegated");
            },
            warmUpFrames: 4);

        sawOpen.Should().BeTrue("the measurement is only worth anything with the menu actually drawn");
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Toggle_WithACurve_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var on = true;
                NoireButtons.Toggle("##alloc_curved_toggle", ref on, Curved);
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void ToggleStyle_Clone_KeepsTheCurve()
        => Curved.Clone().AnimationCurve.Should().BeSameAs(Curved.AnimationCurve);

    [Fact]
    public void Settings_GetAndSet_ReachEverySwitch()
    {
        var settings = new WindowMenuSettings();

        foreach (var toggle in Enum.GetValues<WindowMenuToggle>())
        {
            settings.Get(toggle).Should().BeFalse();
            settings.Set(toggle, true);
            settings.Get(toggle).Should().BeTrue();
        }
    }

    [Fact]
    public void ChangeOf_MatchesTheNamedFlags()
    {
        WindowMenuSettings.ChangeOf(WindowMenuToggle.AlwaysOnTop).Should().Be(WindowMenuChange.AlwaysOnTop);
        WindowMenuSettings.ChangeOf(WindowMenuToggle.LockHeight).Should().Be(WindowMenuChange.LockHeight);
        WindowMenuSettings.ChangeOf(WindowMenuToggle.StayInGpose).Should().Be(WindowMenuChange.StayInGpose);
        WindowMenuSettings.ChangeOf(WindowMenuToggle.StayAutoHide).Should().Be(WindowMenuChange.StayAutoHide);
    }

    [Fact]
    public void Visibility_LeavesAutoHideToTheHost()
    {
        var settings = new WindowMenuSettings { StayInGpose = true, StayInCutscenes = true, StayAutoHide = true };

        settings.Visibility.Should().Be(UiVisibility.InGpose | UiVisibility.InCutscenes);
    }

    [Fact]
    public void StyleClone_OwnsItsLabels()
    {
        var copy = Styled.Clone();
        copy.SetLabel(WindowMenuToggle.LockWidth, "Pin width");

        Styled.GetLabel(WindowMenuToggle.LockWidth).Should().Be("Lock width");
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(0.5f, 0.8024f)]
    [InlineData(0.25f, 0.4094f)]
    public void CubicBezier_Ease_MatchesCss(float t, float expected)
        => UiCubicBezier.Ease.Evaluate(t).Should().BeApproximately(expected, 0.002f);

    [Fact]
    public void CubicBezier_Linear_IsTheIdentity()
    {
        var linear = new UiCubicBezier(0f, 0f, 1f, 1f);

        for (var t = 0f; t <= 1f; t += 0.1f)
            linear.Evaluate(t).Should().BeApproximately(t, 0.001f);
    }

    [Fact]
    public void CubicBezier_Overshoot_LeavesTheRange()
    {
        var back = new UiCubicBezier(0.34f, 1.56f, 0.64f, 1f);

        back.Evaluate(0.6f).Should().BeGreaterThan(1f);
    }
}
