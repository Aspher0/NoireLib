using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the frame debouncer's contract that holds without a game: the frame count it is configured with, the
/// argument guards, and the disposal rules. With no game update behind the clock the wait resolves inline, so the
/// supersede behaviour itself is exercised in game rather than here.
/// </summary>
public class FrameDebouncerTests
{
    [Fact]
    public void Constructor_RejectsAFrameCountBelowOne()
    {
        var construct = () => new FrameDebouncer(0);

        construct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetFrames_ReportsWhatItWasBuiltWith()
    {
        using var debouncer = new FrameDebouncer(12);

        debouncer.GetFrames().Should().Be(12);
    }

    [Fact]
    public void SetFrames_ChangesTheInterval()
    {
        using var debouncer = new FrameDebouncer(12);

        debouncer.SetFrames(3);

        debouncer.GetFrames().Should().Be(3);
    }

    [Fact]
    public void IsPending_WithNothingScheduled_IsFalse()
    {
        using var debouncer = new FrameDebouncer(12);

        debouncer.IsPending().Should().BeFalse();
        debouncer.GetRemainingFrames().Should().Be(0);
    }

    [Fact]
    public async Task DebounceAsync_RejectsANullAction()
    {
        using var debouncer = new FrameDebouncer(12);

        var call = async () => await debouncer.DebounceAsync((Action)null!);

        await call.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DebounceAsync_WithoutAGameClock_RunsInlineRatherThanHanging()
    {
        using var debouncer = new FrameDebouncer(12);
        var ran = false;

        await debouncer.DebounceAsync(() => { ran = true; });

        ran.Should().BeTrue("a wait on a clock that never advances would otherwise never return");
    }

    [Fact]
    public void Cancel_WithNothingScheduled_IsSafe()
    {
        using var debouncer = new FrameDebouncer(12);

        var call = () => debouncer.Cancel();

        call.Should().NotThrow();
    }

    [Fact]
    public async Task DebounceAsync_AfterDispose_Throws()
    {
        var debouncer = new FrameDebouncer(12);
        debouncer.Dispose();

        var call = async () => await debouncer.DebounceAsync(() => { });

        await call.Should().ThrowAsync<ObjectDisposedException>();
    }
}
