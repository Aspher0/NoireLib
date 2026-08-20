using FluentAssertions;
using System;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the frame throttler, above all the sentinel: subtracting a never-run field directly overflows negative and
/// reads as "still throttled", which wedges the throttled work off for the whole session. The instance half is
/// driven by advancing <see cref="FrameClock"/> by hand, since no game update runs here.
/// </summary>
public class FrameThrottlerTests
{
    [Fact]
    public void HasElapsed_NeverRun_IsDueAtAnyFrameNumber()
    {
        FrameThrottler.HasElapsed(0, FrameThrottler.Never, 30).Should().BeTrue();
        FrameThrottler.HasElapsed(5000, FrameThrottler.Never, 30).Should().BeTrue();
        FrameThrottler.HasElapsed(long.MaxValue, FrameThrottler.Never, 30).Should().BeTrue();
    }

    [Fact]
    public void HasElapsed_WithinTheInterval_IsNotDue()
    {
        FrameThrottler.HasElapsed(100, 90, 30).Should().BeFalse("10 frames have passed of the 30 required");
        FrameThrottler.HasElapsed(119, 90, 30).Should().BeFalse("29 frames is still one short");
        FrameThrottler.HasElapsed(120, 90, 30).Should().BeTrue();
        FrameThrottler.HasElapsed(500, 90, 30).Should().BeTrue();
    }

    [Fact]
    public void HasElapsed_ZeroInterval_IsAlwaysDue()
        => FrameThrottler.HasElapsed(90, 90, 0).Should().BeTrue();

    [Fact]
    public void TryPass_FirstCall_PassesAndAdoptsTheCurrentFrame()
    {
        var last = FrameThrottler.Never;

        FrameThrottler.TryPass(7, ref last, 30).Should().BeTrue();
        last.Should().Be(7);
    }

    [Fact]
    public void TryPass_WithinTheInterval_DoesNotMoveTheField()
    {
        var last = 90L;

        FrameThrottler.TryPass(100, ref last, 30).Should().BeFalse();
        last.Should().Be(90, "a refused pass must not restart the interval, or a busy caller would never run");
    }

    [Fact]
    public void TryPass_AcrossTheInterval_RunsOncePerWindow()
    {
        var last = FrameThrottler.Never;
        var runs = 0;

        for (var frame = 0L; frame < 100; frame++)
        {
            if (FrameThrottler.TryPass(frame, ref last, 30))
                runs++;
        }

        runs.Should().Be(4, "frames 0, 30, 60 and 90");
    }

    [Fact]
    public void Throttle_FirstCall_Runs()
    {
        using var throttler = new FrameThrottler(30);
        var runs = 0;

        throttler.Throttle(() => { runs++; }).Should().BeTrue();
        runs.Should().Be(1);
    }

    [Fact]
    public void Throttle_WithinTheInterval_IsRefused()
    {
        using var throttler = new FrameThrottler(30);
        var runs = 0;

        throttler.Throttle(() => { runs++; });
        FrameClock.Advance(10);

        throttler.Throttle(() => { runs++; }).Should().BeFalse();
        runs.Should().Be(1, "only ten of the thirty frames have passed");
    }

    [Fact]
    public void Throttle_OnceTheIntervalHasPassed_RunsAgain()
    {
        using var throttler = new FrameThrottler(30);
        var runs = 0;

        throttler.Throttle(() => { runs++; });
        FrameClock.Advance(30);

        throttler.Throttle(() => { runs++; }).Should().BeTrue();
        runs.Should().Be(2);
    }

    [Fact]
    public void Throttle_Func_ReturnsTheDefaultWhileThrottled()
    {
        using var throttler = new FrameThrottler(30);

        throttler.Throttle(() => 7, -1).Should().Be(7);
        throttler.Throttle(() => 7, -1).Should().Be(-1);
    }

    [Fact]
    public void Reset_AllowsTheNextCallThrough()
    {
        using var throttler = new FrameThrottler(30);
        var runs = 0;

        throttler.Throttle(() => { runs++; });
        throttler.Reset();

        throttler.Throttle(() => { runs++; }).Should().BeTrue();
        runs.Should().Be(2);
    }

    [Fact]
    public void GetRemainingFrames_BeforeTheFirstRun_ReportsAvailable()
    {
        using var throttler = new FrameThrottler(30);

        throttler.GetRemainingFrames().Should().Be(0);
        throttler.IsAvailable().Should().BeTrue();
    }

    [Fact]
    public void GetRemainingFrames_CountsDownWithTheClock()
    {
        using var throttler = new FrameThrottler(30);

        throttler.Throttle(() => { });
        throttler.GetRemainingFrames().Should().Be(30);

        FrameClock.Advance(12);
        throttler.GetRemainingFrames().Should().Be(18);

        FrameClock.Advance(18);
        throttler.GetRemainingFrames().Should().Be(0);
        throttler.IsAvailable().Should().BeTrue();
    }

    [Fact]
    public void SetInterval_ChangesWhatTheNextCallWaitsFor()
    {
        using var throttler = new FrameThrottler(30);

        throttler.GetInterval().Should().Be(30);
        throttler.SetInterval(5);
        throttler.GetInterval().Should().Be(5);
    }

    [Fact]
    public void Constructor_RejectsAnIntervalBelowOneFrame()
    {
        var construct = () => new FrameThrottler(0);

        construct.Should().Throw<ArgumentException>("an interval of zero frames would throttle nothing");
    }

    [Fact]
    public void Throttle_AfterDispose_Throws()
    {
        var throttler = new FrameThrottler(30);
        throttler.Dispose();

        var call = () => throttler.Throttle(() => { });

        call.Should().Throw<ObjectDisposedException>();
    }
}
