using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the frame clock's headless behaviour: with no game update behind it the count stands still unless a test
/// advances it, which is what lets the frame-based helpers be driven without a game. The count is process-global,
/// so every assertion here is relative rather than absolute.
/// </summary>
public class FrameClockTests
{
    [Fact]
    public void IsRunning_WithoutAGame_IsFalse()
        => FrameClock.IsRunning.Should().BeFalse("nothing is here to raise the game's update");

    [Fact]
    public void Current_NeverMovesOnItsOwn()
    {
        var first = FrameClock.Current;
        var second = FrameClock.Current;

        second.Should().Be(first);
    }

    [Fact]
    public void Advance_MovesTheCountForward()
    {
        var before = FrameClock.Current;

        FrameClock.Advance(5);

        FrameClock.Current.Should().BeGreaterThanOrEqualTo(before + 5);
    }
}
