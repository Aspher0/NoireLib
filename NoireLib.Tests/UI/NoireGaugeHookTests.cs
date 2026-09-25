using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The gauges' custom-draw hooks: a set hook replaces the shipped painting, the record's parts reproduce it, and the
/// record carries resolved values rather than raw style ones.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireGaugeHookTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly float[] Series = [1f, 4f, 2f, 8f, 3f, 9f, 5f, 7f];

    private static readonly RingStyle HookedRing = new() { CustomDraw = static args => { args.DrawTrack(); args.DrawFill(); } };
    private static readonly BarStyle HookedBar = new() { CustomDraw = static args => { args.DrawTrack(); args.DrawFill(); args.DrawMarks(); } };
    private static readonly PipStyle HookedPips = new() { CustomDraw = static args => args.DrawPip() };
    private static readonly SparklineStyle HookedSparkline = new() { CustomDraw = static args => { args.DrawArea(); args.DrawLine(); args.DrawMark(); } };

    private readonly UiHarness harness;

    public NoireGaugeHookTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void RingHook_ReplacesTheShippedPainting()
    {
        var calls = 0;
        var style = new RingStyle { CustomDraw = _ => calls++ };

        var result = harness.Draw(() => NoireGauges.Ring(0.5f, style), warmUpFrames: 2);

        // Drawing nothing is a valid hook; the shipped track and fill must not paint underneath it.
        calls.Should().BeGreaterThan(0);
        result.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void RingHook_PartsDrawWhatNoireUiWould()
    {
        var shipped = harness.Draw(static () => NoireGauges.Ring(0.5f), warmUpFrames: 2);
        var hooked = harness.Draw(static () => NoireGauges.Ring(0.5f, HookedRing), warmUpFrames: 2);

        hooked.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void RingHook_ReceivesResolvedState()
    {
        var captured = default(UiRingDraw);
        var style = new RingStyle
        {
            SweepTurns = 0.75f,
            Clockwise = false,
            Thresholds = [new GaugeThreshold(0.5f, new(1f, 0f, 0f, 1f))],
        };
        style.CustomDraw = args => captured = args;

        harness.Draw(() => NoireGauges.Ring(1.5f, style), warmUpFrames: 0);

        captured.Fraction.Should().Be(1f, "the value is clamped before the hook sees it");
        captured.SweepTurns.Should().Be(-0.75f, "a counter-clockwise ring hands the hook a signed sweep");
        captured.OuterRadius.Should().BeGreaterThan(captured.InnerRadius);
    }

    [Fact]
    public void RingHook_UnderAThreshold_ReceivesItsColor()
    {
        var captured = default(UiRingDraw);
        var danger = new Vector4(1f, 0f, 0f, 1f);
        var style = new RingStyle { Thresholds = [new GaugeThreshold(0.5f, danger)] };
        style.CustomDraw = args => captured = args;

        harness.Draw(() => NoireGauges.Ring(0.25f, style), warmUpFrames: 0);

        captured.FillColor.Should().Be(danger);
        captured.LabelColor.Should().Be(danger, "an unset label colour follows the fill");
    }

    [Fact]
    public void RingHook_FromATimer_ReceivesTheCountdownLabel()
    {
        var captured = default(UiRingDraw);
        var style = new RingStyle();
        style.CustomDraw = args => captured = args;

        harness.Draw(
            () => NoireGauges.Timer(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), style),
            warmUpFrames: 0);

        captured.Fraction.Should().BeApproximately(0.5f, 0.0001f);
        captured.Label.Should().NotBeNullOrEmpty("the resolved countdown text reaches the hook");
    }

    [Fact]
    public void BarHook_ReplacesTheShippedPainting()
    {
        var calls = 0;
        var style = new BarStyle { Width = 120f, CustomDraw = _ => calls++ };

        var result = harness.Draw(() => NoireGauges.Bar(0.5f, style), warmUpFrames: 2);

        calls.Should().BeGreaterThan(0);
        result.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void BarHook_PartsDrawWhatNoireUiWould()
    {
        var plain = new BarStyle { Width = 120f, Marks = [0.25f, 0.75f] };
        var hooked = plain.Clone();
        hooked.CustomDraw = HookedBar.CustomDraw;

        var shipped = harness.Draw(() => NoireGauges.Bar(0.5f, plain), warmUpFrames: 2);
        var replaced = harness.Draw(() => NoireGauges.Bar(0.5f, hooked), warmUpFrames: 2);

        replaced.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void BarHook_ReceivesResolvedState()
    {
        var captured = default(UiBarDraw);
        var to = new Vector4(0f, 1f, 0f, 1f);
        var style = new BarStyle { Width = 120f, ColorTo = to, Marks = [0.5f] };
        style.CustomDraw = args => captured = args;

        harness.Draw(() => NoireGauges.Bar(-0.5f, style), warmUpFrames: 0);

        captured.Fraction.Should().Be(0f, "the value is clamped before the hook sees it");
        captured.FillColorTo.Should().Be(to);
        captured.Marks.Should().ContainSingle().Which.Should().Be(0.5f);
        captured.Size.X.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void PipHook_IsCalledOncePerPip()
    {
        var seen = new List<UiPipDraw>();
        var style = new PipStyle { OutlineEmpty = true };
        style.CustomDraw = args => seen.Add(args);

        var result = harness.Draw(() => NoireGauges.Pips(3, 5, style), warmUpFrames: 0);

        seen.Should().HaveCount(5);
        seen.Should().OnlyContain(pip => pip.Total == 5);
        seen.ConvertAll(static pip => pip.Filled).Should().Equal(true, true, true, false, false);
        seen.ConvertAll(static pip => pip.Outlined).Should().Equal(false, false, false, true, true);
        seen.ConvertAll(static pip => pip.Index).Should().Equal(0, 1, 2, 3, 4);
        result.TotalVtxCount.Should().Be(0, "the hook drew nothing, so nothing may paint underneath it");
    }

    [Fact]
    public void PipHook_PartsDrawWhatNoireUiWould()
    {
        var shipped = harness.Draw(static () => NoireGauges.Pips(3, 5), warmUpFrames: 2);
        var hooked = harness.Draw(static () => NoireGauges.Pips(3, 5, HookedPips), warmUpFrames: 2);

        hooked.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void SparklineHook_ReceivesTheProjectedPoints()
    {
        var points = 0;
        var style = new SparklineStyle { Width = 100f };
        style.CustomDraw = args => points = args.Points.Length;

        var result = harness.Draw(() => NoireGauges.Sparkline(Series, style), warmUpFrames: 0);

        points.Should().Be(Series.Length);
        result.TotalVtxCount.Should().Be(0, "no background or baseline is set, and the hook drew nothing");
    }

    [Fact]
    public void SparklineHook_PartsDrawWhatNoireUiWould()
    {
        var plain = new SparklineStyle { Width = 100f };
        var hooked = plain.Clone();
        hooked.CustomDraw = HookedSparkline.CustomDraw;

        var shipped = harness.Draw(() => NoireGauges.Sparkline(Series, plain), warmUpFrames: 2);
        var replaced = harness.Draw(() => NoireGauges.Sparkline(Series, hooked), warmUpFrames: 2);

        replaced.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void SparklineHook_IsNotCalledForATraceTooShortToDraw()
    {
        var calls = 0;
        var style = new SparklineStyle { Width = 100f };
        style.CustomDraw = _ => calls++;

        harness.Draw(() => NoireGauges.Sparkline([5f], style), warmUpFrames: 0);

        calls.Should().Be(0, "the shipped trace needs two points and the hook holds the same bar");
    }

    [Fact]
    public void TimerLabel_ReadsWholeSeconds()
    {
        var captured = default(UiRingDraw);
        var style = new RingStyle();
        style.CustomDraw = args => captured = args;

        harness.Draw(
            () => NoireGauges.Timer(TimeSpan.FromMilliseconds(13_200), TimeSpan.FromSeconds(60), style),
            warmUpFrames: 0);

        // "13s200ms" was too wide and changed every frame: a centred label resized and flickered.
        captured.Label.Should().Be("14s");
    }

    [Fact]
    public void TimerLabel_HoldsStillWithinASecond()
    {
        var seen = new List<string?>();
        var style = new RingStyle();
        style.CustomDraw = args => seen.Add(args.Label);

        foreach (var milliseconds in new[] { 13_999, 13_500, 13_100, 13_001 })
        {
            var remaining = TimeSpan.FromMilliseconds(milliseconds);
            harness.Draw(() => NoireGauges.Timer(remaining, TimeSpan.FromSeconds(60), style), warmUpFrames: 0);
        }

        seen.Distinct().Should().HaveCount(1, "every frame of the same second must read the same, or the label flickers");
    }

    [Fact]
    public void TimerLabel_ShowsTheLastSecondThenZero()
    {
        var style = new RingStyle();
        string? atTheEnd = null;
        string? expired = null;

        style.CustomDraw = args => atTheEnd = args.Label;
        harness.Draw(() => NoireGauges.Timer(TimeSpan.FromMilliseconds(120), TimeSpan.FromSeconds(60), style), warmUpFrames: 0);

        style.CustomDraw = args => expired = args.Label;
        harness.Draw(() => NoireGauges.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(60), style), warmUpFrames: 0);

        atTheEnd.Should().Be("1s", "rounded up, so the last second is shown for its whole duration");
        expired.Should().Be("0s");
    }

    [Fact]
    public void LabelSize_ShrinksToFitTheSpaceGiven()
    {
        var fitted = 0f;
        var measured = 0f;
        const float hole = 22f;

        harness.Draw(
            () =>
            {
                fitted = NoireGauges.FitTextSize("1m30s", 14f, hole);
                measured = NoireText.CalcSize("1m30s", fitted).X;
            },
            warmUpFrames: 2);

        fitted.Should().BeLessThan(14f, "a label wider than the ring's hole has to come down to fit it");

        // Fits, or bottomed out at the readable minimum. Headless, every size shares one stand-in font.
        (measured <= hole || fitted <= NoireGauges.MinFittedLabelSize).Should().BeTrue();
    }

    [Fact]
    public void LabelSize_IsLeftAlone_WhenItAlreadyFits()
    {
        var fitted = 0f;

        harness.Draw(() => fitted = NoireGauges.FitTextSize("7s", 14f, 400f), warmUpFrames: 2);

        fitted.Should().Be(14f);
    }

    [Fact]
    public void HookedGauges_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireGauges.Ring(0.5f, HookedRing);
                    NoireGauges.Bar(0.5f, HookedBar);
                    NoireGauges.Pips(3, 5, HookedPips);
                    NoireGauges.Sparkline(Series, HookedSparkline);
                }
            },
            // A shape is recorded for replay the second frame its size is seen, and this layout settles on the second.
            warmUpFrames: 3);

        // Struct records and preallocated hooks: the hooked path costs nothing either.
        result.AllocatedBytes.Should().Be(0L);
    }
}
