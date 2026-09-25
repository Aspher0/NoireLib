using FluentAssertions;
using NoireLib.Core.Subscriptions;
using NoireLib.EventBus;
using NoireLib.GameWatcher;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireGameWatcher core: steady-state allocations of the tick and dispatch paths, demand
/// activation under concurrent interest changes, and the EventBus mirror.
/// </summary>
public class NoireGameWatcherCoreTests
{
    private sealed record CoreEvent(int Value);

    private static NoireGameWatcher MakeWatcher(NoireEventBus? bus = null)
        => new(new GameWatcherOptions { EventBus = bus }, active: false, enableLogging: false);

    #region Allocations

    [Fact]
    public void TickSources_WarmTick_AllocatesNothing()
    {
        var watcher = MakeWatcher();
        using var watch = watcher.WatchValue(static () => 1, static (_, _) => { });

        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
            watcher.TickSources(now);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
            watcher.TickSources(now);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        watcher.Dispose();
    }

    [Fact]
    public void Publish_WarmDispatchWithMirror_AllocatesNothing()
    {
        var bus = new NoireEventBus(active: true, enableLogging: false);
        var watcher = MakeWatcher(bus);
        var evt = new CoreEvent(1);

        using var first = watcher.Subscribe<CoreEvent>(static _ => { });
        using var second = watcher.Subscribe<CoreEvent>(static _ => { }, new NoireSubscriptionOptions<CoreEvent> { Priority = 5 });
        using var mirror = watcher.PublishToEventBus<CoreEvent>(static e => e.Value < 0);

        // Past the diagnostics log capacity: its queue is at its steady size.
        for (var i = 0; i < 200; i++)
            watcher.Publish(evt);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
            watcher.Publish(evt);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        watcher.Dispose();
        bus.Dispose();
    }

    #endregion

    #region Demand activation

    private sealed class ProbeSource : GameWatcherSource
    {
        private int live;
        private int overlaps;

        public ProbeSource(NoireGameWatcher owner) : base(owner, SourceKind.Session) { }

        public int Overlaps => Volatile.Read(ref overlaps);

        public override bool IsPolling => false;

        protected override void OnActivate()
        {
            if (Interlocked.Increment(ref live) != 1)
                Interlocked.Increment(ref overlaps);

            Thread.SpinWait(500);
        }

        protected override void OnDeactivate()
        {
            if (Interlocked.Decrement(ref live) != 0)
                Interlocked.Increment(ref overlaps);

            Thread.SpinWait(500);
        }
    }

    private static void RunConcurrently(int threads, Action body)
    {
        using var barrier = new Barrier(threads);
        var workers = new Thread[threads];

        for (var i = 0; i < threads; i++)
        {
            workers[i] = new Thread(() =>
            {
                barrier.SignalAndWait();
                body();
            });

            workers[i].Start();
        }

        foreach (var worker in workers)
            worker.Join();
    }

    private static (NoireGameWatcher Watcher, ProbeSource Probe) MakeProbedWatcher()
    {
        var watcher = MakeWatcher();
        var probe = new ProbeSource(watcher);

        watcher.ReplaceSource(probe);
        watcher.AllowSourceActivationWithoutGame = true;
        watcher.SetActive(true);

        return (watcher, probe);
    }

    [Fact]
    public void Interest_ConcurrentAddAndRelease_NeverOverlapsActivationAndEndsStopped()
    {
        var (watcher, probe) = MakeProbedWatcher();

        RunConcurrently(8, () =>
        {
            for (var i = 0; i < 400; i++)
            {
                watcher.AddInterest(SourceKind.Session);
                watcher.ReleaseInterest(SourceKind.Session);
            }
        });

        probe.Overlaps.Should().Be(0);
        probe.RefCount.Should().Be(0);
        probe.IsRunning.Should().BeFalse();
        watcher.Dispose();
    }

    [Fact]
    public void Interest_ConcurrentHolders_LeaveTheSourceRunningUntilTheLastRelease()
    {
        var (watcher, probe) = MakeProbedWatcher();

        RunConcurrently(8, () =>
        {
            for (var i = 0; i < 200; i++)
            {
                watcher.AddInterest(SourceKind.Session);
                watcher.ReleaseInterest(SourceKind.Session);
            }

            watcher.AddInterest(SourceKind.Session);
        });

        probe.Overlaps.Should().Be(0);
        probe.RefCount.Should().Be(8);
        probe.IsRunning.Should().BeTrue();

        RunConcurrently(8, () => watcher.ReleaseInterest(SourceKind.Session));

        probe.RefCount.Should().Be(0);
        probe.IsRunning.Should().BeFalse();
        watcher.Dispose();
    }

    #endregion

    #region EventBus mirror

    [Fact]
    public void PublishToEventBus_WithoutBus_ReturnsAnInactiveToken()
    {
        var watcher = MakeWatcher();

        var token = watcher.PublishToEventBus<CoreEvent>();

        token.IsActive.Should().BeFalse();
        watcher.Dispose();
    }

    [Fact]
    public void PublishToEventBus_DisposingTheToken_StopsMirroringAndDeactivatesIt()
    {
        var bus = new NoireEventBus(active: true, enableLogging: false);
        var watcher = MakeWatcher(bus);
        var received = new List<int>();
        bus.Subscribe<CoreEvent>(e => received.Add(e.Value));

        var token = watcher.PublishToEventBus<CoreEvent>();
        watcher.Publish(new CoreEvent(1));

        token.IsActive.Should().BeTrue();
        token.Dispose();
        watcher.Publish(new CoreEvent(2));

        token.IsActive.Should().BeFalse();
        received.Should().Equal(1);
        watcher.Dispose();
        bus.Dispose();
    }

    [Fact]
    public void PublishToEventBus_TwoMirrors_EachFilterReachesTheBusAndAnOverlapPublishesOnce()
    {
        var bus = new NoireEventBus(active: true, enableLogging: false);
        var watcher = MakeWatcher(bus);
        var received = new List<int>();
        bus.Subscribe<CoreEvent>(e => received.Add(e.Value));

        var low = watcher.PublishToEventBus<CoreEvent>(e => e.Value < 10);
        using var high = watcher.PublishToEventBus<CoreEvent>(e => e.Value >= 5);

        watcher.Publish(new CoreEvent(1));
        watcher.Publish(new CoreEvent(20));
        watcher.Publish(new CoreEvent(7));

        received.Should().Equal(1, 20, 7);

        low.Dispose();
        watcher.Publish(new CoreEvent(2));
        watcher.Publish(new CoreEvent(30));

        received.Should().Equal(1, 20, 7, 30);
        watcher.Dispose();
        bus.Dispose();
    }

    [Fact]
    public void Dispose_InvalidatesEventBusMirrorTokens()
    {
        var bus = new NoireEventBus(active: true, enableLogging: false);
        var watcher = MakeWatcher(bus);

        var token = watcher.PublishToEventBus<CoreEvent>();
        watcher.Dispose();

        token.IsActive.Should().BeFalse();
        bus.Dispose();
    }

    #endregion
}
