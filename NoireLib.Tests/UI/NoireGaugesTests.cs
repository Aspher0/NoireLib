using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="NoireGauges"/>: threshold colour resolution, countdown fractions and the vertical range a
/// sparkline plots against.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireGaugesTests
{
    private static readonly Vector4 Base = new(0.1f, 0.2f, 0.3f, 1f);
    private static readonly Vector4 Warning = new(1f, 0.7f, 0f, 1f);
    private static readonly Vector4 Danger = new(1f, 0f, 0f, 1f);

    private static readonly GaugeThreshold[] Thresholds =
    [
        new(0.5f, Warning),
        new(0.25f, Danger),
    ];

    #region Thresholds

    [Fact]
    public void ResolveFillColor_NoThresholds_UsesTheBaseColor()
    {
        NoireGauges.ResolveFillColor(0.1f, null, Base).Should().Be(Base);
    }

    [Fact]
    public void ResolveFillColor_AboveEveryThreshold_UsesTheBaseColor()
    {
        NoireGauges.ResolveFillColor(0.9f, Thresholds, Base).Should().Be(Base);
    }

    [Fact]
    public void ResolveFillColor_BelowTheFirstThreshold_UsesIt()
    {
        NoireGauges.ResolveFillColor(0.4f, Thresholds, Base).Should().Be(Warning);
    }

    [Fact]
    public void ResolveFillColor_BelowBothThresholds_UsesTheLowerOne()
    {
        NoireGauges.ResolveFillColor(0.1f, Thresholds, Base)
            .Should().Be(Danger, "the more urgent band wins when both apply");
    }

    [Fact]
    public void ResolveFillColor_ExactlyOnAThreshold_Applies()
    {
        NoireGauges.ResolveFillColor(0.5f, Thresholds, Base)
            .Should().Be(Warning, "a threshold applies at its value, not only under it");
    }

    [Fact]
    public void ResolveFillColor_ThresholdOrderDoesNotMatter()
    {
        GaugeThreshold[] reversed = [new(0.25f, Danger), new(0.5f, Warning)];

        NoireGauges.ResolveFillColor(0.1f, reversed, Base).Should().Be(Danger);
        NoireGauges.ResolveFillColor(0.4f, reversed, Base).Should().Be(Warning);
    }

    [Fact]
    public void ResolveFillColor_EmptyThresholds_UsesTheBaseColor()
    {
        NoireGauges.ResolveFillColor(0.1f, Array.Empty<GaugeThreshold>(), Base).Should().Be(Base);
    }

    #endregion

    #region Countdowns

    [Fact]
    public void TimerFraction_AtTheStart_IsFull()
    {
        NoireGauges.TimerFraction(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)).Should().Be(1f);
    }

    [Fact]
    public void TimerFraction_Halfway_IsHalf()
    {
        NoireGauges.TimerFraction(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30))
            .Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void TimerFraction_Expired_IsEmpty()
    {
        NoireGauges.TimerFraction(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(30)).Should().Be(0f);
    }

    [Fact]
    public void TimerFraction_Overrun_IsCappedAtFull()
    {
        NoireGauges.TimerFraction(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30)).Should().Be(1f);
    }

    [Fact]
    public void TimerFraction_ZeroTotal_IsEmptyRatherThanInfinite()
    {
        NoireGauges.TimerFraction(TimeSpan.FromSeconds(5), TimeSpan.Zero).Should().Be(0f);
    }

    #endregion

    #region Sparkline bounds

    [Fact]
    public void SparklineBounds_TakesTheRangeFromTheData()
    {
        NoireGauges.SparklineBounds([3f, 7f, 5f], null, null).Should().Be((3f, 7f));
    }

    [Fact]
    public void SparklineBounds_PinnedBounds_Win()
    {
        NoireGauges.SparklineBounds([3f, 7f, 5f], 0f, 10f).Should().Be((0f, 10f));
    }

    [Fact]
    public void SparklineBounds_OnePinnedBound_LeavesTheOtherToTheData()
    {
        NoireGauges.SparklineBounds([3f, 7f, 5f], 0f, null).Should().Be((0f, 7f));
        NoireGauges.SparklineBounds([3f, 7f, 5f], null, 10f).Should().Be((3f, 10f));
    }

    [Fact]
    public void SparklineBounds_FlatSeries_GetsARangeAroundItself()
    {
        var (min, max) = NoireGauges.SparklineBounds([4f, 4f, 4f], null, null);

        min.Should().Be(3.5f);
        max.Should().Be(4.5f);
        (max > min).Should().BeTrue("a zero range would put the whole trace at infinity");
    }

    [Fact]
    public void SparklineBounds_EmptySeries_GetsAUsableRange()
    {
        var (min, max) = NoireGauges.SparklineBounds([], null, null);

        (max > min).Should().BeTrue();
    }

    [Fact]
    public void SparklineBounds_InvertedPinnedBounds_AreCorrected()
    {
        var (min, max) = NoireGauges.SparklineBounds([1f, 2f], 10f, 0f);

        (max > min).Should().BeTrue("a range that crosses itself would mirror the trace rather than fail loudly");
    }

    [Fact]
    public void SparklineBounds_SingleValue_GetsARangeAroundIt()
    {
        NoireGauges.SparklineBounds([2f], null, null).Should().Be((1.5f, 2.5f));
    }

    #endregion
}
