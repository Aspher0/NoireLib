using FluentAssertions;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins the reconnection schedule. The delay doubles to a ceiling, the jitter band is centred on the computed delay,
/// and an attempt limit stops the clock. It never runs forever.
/// </summary>
public sealed class NoireWebsocketBackoffTests
{
    [Fact]
    public void DelayFor_DoublesUntilTheCeiling()
    {
        var policy = NoireRetryPolicy.Default with { Jitter = 0 };

        policy.DelayFor(0).Should().Be(TimeSpan.FromSeconds(1));
        policy.DelayFor(1).Should().Be(TimeSpan.FromSeconds(2));
        policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(4));
        policy.DelayFor(3).Should().Be(TimeSpan.FromSeconds(8));
        policy.DelayFor(4).Should().Be(TimeSpan.FromSeconds(16));
        policy.DelayFor(5).Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayFor(50).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DelayFor_PlacesTheSampleAcrossTheJitterBand()
    {
        var policy = NoireRetryPolicy.Default with { Jitter = 0.25 };

        policy.DelayFor(0, 0).Should().Be(TimeSpan.FromMilliseconds(750));
        policy.DelayFor(0, 0.5).Should().Be(TimeSpan.FromMilliseconds(1000));
        policy.DelayFor(0, 1).Should().Be(TimeSpan.FromMilliseconds(1250));
    }

    [Fact]
    public void DelayFor_ClampsASampleOutsideTheBand()
    {
        var policy = NoireRetryPolicy.Default with { Jitter = 0.25 };

        policy.DelayFor(0, -5).Should().Be(TimeSpan.FromMilliseconds(750));
        policy.DelayFor(0, 5).Should().Be(TimeSpan.FromMilliseconds(1250));
    }

    [Fact]
    public void DelayFor_WithANegativeAttempt_IsTheFirstDelay()
    {
        var policy = NoireRetryPolicy.Default with { Jitter = 0 };

        policy.DelayFor(-3).Should().Be(policy.DelayFor(0));
    }

    [Fact]
    public void ShouldRetry_WithNoLimit_IsAlwaysTrue()
    {
        NoireRetryPolicy.Default.ShouldRetry(0).Should().BeTrue();
        NoireRetryPolicy.Default.ShouldRetry(10_000).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_OnTheNonePolicy_IsFalseFromTheFirstAttempt()
    {
        NoireRetryPolicy.None.ShouldRetry(0).Should().BeFalse();
    }

    [Fact]
    public void Next_AdvancesTheAttemptAndResetTakesItBack()
    {
        var clock = new BackoffClock(NoireRetryPolicy.Default with { Jitter = 0 });

        clock.Attempt.Should().Be(0);
        clock.Next().Should().Be(TimeSpan.FromSeconds(1));
        clock.Next().Should().Be(TimeSpan.FromSeconds(2));
        clock.Attempt.Should().Be(2);

        clock.Reset();

        clock.Attempt.Should().Be(0);
        clock.Next().Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ShouldRetry_StopsOnceTheAttemptLimitIsSpent()
    {
        var clock = new BackoffClock(NoireRetryPolicy.Default with { Jitter = 0, MaxAttempts = 2 });

        clock.ShouldRetry.Should().BeTrue();
        clock.Next();
        clock.ShouldRetry.Should().BeTrue();
        clock.Next();
        clock.ShouldRetry.Should().BeFalse();
    }

    [Fact]
    public void Next_KeepsEveryDelayInsideTheBandItsJitterDescribes()
    {
        var clock = new BackoffClock(NoireRetryPolicy.Default with { Jitter = 0.25 });

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var expected = NoireRetryPolicy.Default.DelayFor(attempt % 6, 0.5).TotalMilliseconds;
            var delay = clock.Next().TotalMilliseconds;

            delay.Should().BeInRange(expected * 0.75, expected * 1.25);

            if (attempt % 6 == 5)
                clock.Reset();
        }
    }
}
