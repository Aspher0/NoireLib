using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Reading a timing helper's interval never waits on its lock: the framework thread reads it every call.</summary>
public class TimingHelperIntervalReadTests
{
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(2);

    private sealed class LockHoldingThrottler : Throttler
    {
        public LockHoldingThrottler(TimeSpan interval) : base(interval) { }

        public void Hold() => _lock.Wait();

        public void Free() => _lock.Release();
    }

    private sealed class LockHoldingFrameThrottler : FrameThrottler
    {
        public LockHoldingFrameThrottler(long frames) : base(frames) { }

        public void Hold() => _lock.Wait();

        public void Free() => _lock.Release();
    }

    [Fact]
    public void GetDelay_WhileTheLockIsHeld_ReturnsWithoutWaiting()
    {
        using var throttler = new LockHoldingThrottler(TimeSpan.FromMilliseconds(250));
        throttler.Hold();

        try
        {
            var read = Task.Run(() => throttler.GetInterval());

            read.Wait(ReadBudget).Should().BeTrue("reading the interval must not queue behind the lock");
            read.Result.Should().Be(TimeSpan.FromMilliseconds(250));
        }
        finally
        {
            throttler.Free();
        }
    }

    [Fact]
    public void GetFrames_WhileTheLockIsHeld_ReturnsWithoutWaiting()
    {
        using var throttler = new LockHoldingFrameThrottler(12);
        throttler.Hold();

        try
        {
            var read = Task.Run(() => throttler.GetInterval());

            read.Wait(ReadBudget).Should().BeTrue("reading the interval must not queue behind the lock");
            read.Result.Should().Be(12);
        }
        finally
        {
            throttler.Free();
        }
    }

    [Fact]
    public void SetDelay_IsSeenByTheNextRead()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(100));

        throttler.SetInterval(TimeSpan.FromMilliseconds(420));

        throttler.GetInterval().Should().Be(TimeSpan.FromMilliseconds(420));
    }

    [Fact]
    public void SetFrames_IsSeenByTheNextRead()
    {
        using var throttler = new FrameThrottler(5);

        throttler.SetInterval(9);

        throttler.GetInterval().Should().Be(9);
    }
}
