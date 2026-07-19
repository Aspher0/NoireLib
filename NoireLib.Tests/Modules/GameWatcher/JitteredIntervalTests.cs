using FluentAssertions;
using NoireLib.GameWatcher;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for <see cref="JitteredInterval"/>, the shared randomized refresh cadence used by the
/// friend and player-search sources.
/// </summary>
public class JitteredIntervalTests
{
    [Fact]
    public void Next_StaysWithinConfiguredRange()
    {
        var interval = new JitteredInterval(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40));
        var rng = new Random(12345);

        for (var i = 0; i < 1000; i++)
        {
            var picked = interval.Next(rng).TotalSeconds;
            picked.Should().BeGreaterThanOrEqualTo(30d).And.BeLessThanOrEqualTo(40d);
        }
    }

    [Fact]
    public void Next_FloorsEffectiveWaitAtThirtySeconds()
    {
        // A configuration below the floor must still never produce a wait under 30s.
        var interval = new JitteredInterval(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        var rng = new Random(1);

        for (var i = 0; i < 1000; i++)
            interval.Next(rng).TotalSeconds.Should().BeGreaterThanOrEqualTo(JitteredInterval.FloorSeconds);
    }

    [Fact]
    public void Next_DefaultStructYieldsFlooredWait()
    {
        // default(JitteredInterval) has Min = Max = 0; the floor must rescue it to a flat 30s.
        var interval = default(JitteredInterval);

        interval.Next(new Random(7)).TotalSeconds.Should().Be(JitteredInterval.FloorSeconds);
    }

    [Fact]
    public void Default_IsThirtyToForty()
    {
        JitteredInterval.Default.Min.Should().Be(TimeSpan.FromSeconds(30));
        JitteredInterval.Default.Max.Should().Be(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Next_ProducesVariedValues_NotAFixedBeat()
    {
        var interval = new JitteredInterval(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40));
        var rng = new Random(999);

        var first = interval.Next(rng);
        var sawDifferent = false;

        for (var i = 0; i < 50; i++)
        {
            if (interval.Next(rng) != first)
            {
                sawDifferent = true;
                break;
            }
        }

        sawDifferent.Should().BeTrue("the jittered cadence must not collapse to a single fixed value");
    }

    [Fact]
    public void Next_Throws_OnNullRandom()
    {
        var interval = JitteredInterval.Default;
        var act = () => interval.Next(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
