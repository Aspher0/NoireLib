using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the frame throttle, above all the sentinel: subtracting a never-run field directly overflows negative and
/// reads as "still throttled", which wedges the throttled work off for the whole session.
/// </summary>
public class FrameThrottleTests
{
    [Fact]
    public void HasElapsed_NeverRun_IsDueAtAnyFrameNumber()
    {
        FrameThrottle.HasElapsed(0, FrameThrottle.Never, 30).Should().BeTrue();
        FrameThrottle.HasElapsed(5000, FrameThrottle.Never, 30).Should().BeTrue();
        FrameThrottle.HasElapsed(long.MaxValue, FrameThrottle.Never, 30).Should().BeTrue();
    }

    [Fact]
    public void HasElapsed_WithinTheInterval_IsNotDue()
    {
        FrameThrottle.HasElapsed(100, 90, 30).Should().BeFalse("10 frames have passed of the 30 required");
        FrameThrottle.HasElapsed(119, 90, 30).Should().BeFalse("29 frames is still one short");
        FrameThrottle.HasElapsed(120, 90, 30).Should().BeTrue();
        FrameThrottle.HasElapsed(500, 90, 30).Should().BeTrue();
    }

    [Fact]
    public void HasElapsed_ZeroInterval_IsAlwaysDue()
        => FrameThrottle.HasElapsed(90, 90, 0).Should().BeTrue();

    [Fact]
    public void TryPass_FirstCall_PassesAndAdoptsTheCurrentFrame()
    {
        var last = FrameThrottle.Never;

        FrameThrottle.TryPass(7, ref last, 30).Should().BeTrue();
        last.Should().Be(7);
    }

    [Fact]
    public void TryPass_WithinTheInterval_DoesNotMoveTheField()
    {
        var last = 90L;

        FrameThrottle.TryPass(100, ref last, 30).Should().BeFalse();
        last.Should().Be(90, "a refused pass must not restart the interval, or a busy caller would never run");
    }

    [Fact]
    public void TryPass_AcrossTheInterval_RunsOncePerWindow()
    {
        var last = FrameThrottle.Never;
        var runs = 0;

        for (var frame = 0L; frame < 100; frame++)
        {
            if (FrameThrottle.TryPass(frame, ref last, 30))
                runs++;
        }

        runs.Should().Be(4, "frames 0, 30, 60 and 90");
    }
}
