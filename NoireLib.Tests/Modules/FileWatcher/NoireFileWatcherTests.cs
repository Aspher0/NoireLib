using FluentAssertions;
using NoireLib.EventBus;
using NoireLib.FileWatcher;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireFileWatcher module, locking its delivery contract: notifications are marshaled
/// through a bounded queue that is drained one delivery at a time, every notification surviving duplicate
/// suppression is delivered (the queue does not coalesce), delivery falls back to inline when NoireLib is not
/// initialized, every EventBus event the module publishes goes through that same queue rather than running on the
/// caller's thread, drops are counted rather than only logged, and no consumer callback runs once the module is
/// disposed.<br/>
/// Also locks the watch lifecycle: a bulk enable/disable reports the same per-watch state-changed event a single
/// toggle does and reports only the watches that actually changed, a removed watch's disposed FileSystemWatcher is
/// unreachable rather than merely unlikely to be touched, and a registration racing disposal is abandoned instead of
/// outliving the module.<br/>
/// And the key index: a key resolves to the watch that registered it last, and retiring a watch only ever retires the
/// key that still resolves to it, so no removal can leave a registered, running watch that the key no longer reaches.
/// </summary>
public class NoireFileWatcherTests : IDisposable
{
    #region Helpers

    private readonly List<NoireFileWatcher> watchersToClean = new();
    private readonly List<NoireEventBus> busesToClean = new();
    private readonly List<string> directoriesToClean = new();

    public void Dispose()
    {
        foreach (var watcher in watchersToClean)
        {
            try
            {
                watcher.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        foreach (var bus in busesToClean)
        {
            try
            {
                bus.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        foreach (var directory in directoriesToClean)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private NoireFileWatcher CreateWatcher(bool active = false, bool forceQueuedDelivery = false)
    {
        var watcher = new NoireFileWatcher(active: active, enableLogging: false);
        watcher.ForceQueuedDelivery = forceQueuedDelivery;
        watchersToClean.Add(watcher);
        return watcher;
    }

    private NoireEventBus CreateEventBus()
    {
        var bus = new NoireEventBus(active: true, enableLogging: false);
        busesToClean.Add(bus);
        return bus;
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        directoriesToClean.Add(directory);
        return directory;
    }

    private static FileWatchNotification MakeNotification(
        string fullPath,
        FileWatchEventType eventType = FileWatchEventType.Changed,
        string watchId = "watch",
        DateTimeOffset? occurredAtUtc = null)
        => new(
            WatchId: watchId,
            RootPath: Path.GetDirectoryName(fullPath) ?? fullPath,
            TargetType: FileWatchTargetType.File,
            FullPath: fullPath,
            Name: Path.GetFileName(fullPath),
            EventType: eventType,
            OccurredAtUtc: occurredAtUtc ?? DateTimeOffset.UtcNow,
            NativeChangeType: WatcherChangeTypes.Changed);

    /// <summary>
    /// Spins until the condition holds or the timeout elapses. Real filesystem events arrive on their own
    /// schedule, so an end-to-end assertion has to wait for one rather than assume it has already landed.
    /// </summary>
    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return true;

            Thread.Sleep(10);
        }

        return condition();
    }

    #endregion

    #region Inline Fallback

    [Fact]
    public void Delivery_WithoutInitializedNoireLib_RunsInlineWithoutADrain()
    {
        var watcher = CreateWatcher();
        var ran = 0;

        watcher.PostDelivery(() => ran++);

        ran.Should().Be(1, because: "with no framework thread to marshal onto, delivery must run inline on the posting thread");
    }

    [Fact]
    public void RealFileEvent_WithoutInitializedNoireLib_ReachesCallbackInline()
    {
        var directory = CreateTempDirectory();
        var watcher = CreateWatcher(active: true);
        var received = new ConcurrentQueue<FileWatchNotification>();

        watcher.WatchDirectory(directory, callback: received.Enqueue);
        File.WriteAllText(Path.Combine(directory, "created.txt"), "content");

        WaitUntil(() => received.Any(n => n.EventType == FileWatchEventType.Created))
            .Should().BeTrue(because: "an uninitialized NoireLib falls back to inline delivery, so no drain is needed for the callback to run");
    }

    [Fact]
    public void RealFileEvent_AfterDispose_DoesNotReachCallback()
    {
        var directory = CreateTempDirectory();
        var watcher = CreateWatcher(active: true);
        var received = 0;

        watcher.WatchDirectory(directory, callback: _ => Interlocked.Increment(ref received));
        watcher.Dispose();

        File.WriteAllText(Path.Combine(directory, "after-dispose.txt"), "content");
        Thread.Sleep(200);

        received.Should().Be(0, because: "disposal must retire the watchers and the delivery path together");
    }

    #endregion

    #region Coalescing Decision

    [Fact]
    public void IsSuppressedDuplicate_RepeatedEventForSamePathWithinWindow_Suppresses()
    {
        var watcher = CreateWatcher();
        watcher.DuplicateNotificationWindow = TimeSpan.FromSeconds(10);

        var now = DateTimeOffset.UtcNow;
        var first = MakeNotification(@"C:\data\file.txt", occurredAtUtc: now);
        var second = MakeNotification(@"C:\data\file.txt", occurredAtUtc: now.AddMilliseconds(5));

        watcher.IsSuppressedDuplicate(first).Should().BeFalse(because: "the first notification of a burst is the one that gets delivered");
        watcher.IsSuppressedDuplicate(second).Should().BeTrue(because: "a file write arrives as a burst of identical events, which collapses inside the window");
    }

    [Fact]
    public void IsSuppressedDuplicate_SamePathOutsideWindow_DoesNotSuppress()
    {
        var watcher = CreateWatcher();
        watcher.DuplicateNotificationWindow = TimeSpan.FromMilliseconds(50);

        var now = DateTimeOffset.UtcNow;

        watcher.IsSuppressedDuplicate(MakeNotification(@"C:\data\file.txt", occurredAtUtc: now)).Should().BeFalse();
        watcher.IsSuppressedDuplicate(MakeNotification(@"C:\data\file.txt", occurredAtUtc: now.AddMilliseconds(500)))
            .Should().BeFalse(because: "a genuinely separate write past the window is a distinct notification, not a duplicate");
    }

    [Fact]
    public void IsSuppressedDuplicate_DifferentPathsOrEventTypes_DoNotSuppressEachOther()
    {
        var watcher = CreateWatcher();
        watcher.DuplicateNotificationWindow = TimeSpan.FromSeconds(10);

        var now = DateTimeOffset.UtcNow;

        watcher.IsSuppressedDuplicate(MakeNotification(@"C:\data\a.txt", occurredAtUtc: now)).Should().BeFalse();
        watcher.IsSuppressedDuplicate(MakeNotification(@"C:\data\b.txt", occurredAtUtc: now))
            .Should().BeFalse(because: "suppression is per path, so an unrelated file must not be collapsed into another's burst");
        watcher.IsSuppressedDuplicate(MakeNotification(@"C:\data\a.txt", FileWatchEventType.Deleted, occurredAtUtc: now))
            .Should().BeFalse(because: "suppression is per event type, so a delete must not be collapsed into a change burst");
    }

    [Fact]
    public void DrainDeliveryQueue_ManyDeliveriesForOnePath_DeliversEveryOne()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var delivered = 0;

        for (var i = 0; i < 25; i++)
            watcher.PostDelivery(() => delivered++);

        watcher.DrainDeliveryQueue();

        delivered.Should().Be(25, because: "the queue must not coalesce; collapsing bursts is duplicate suppression's job, and tying delivery count to the frame rate would drop events consumers asked to see");
    }

    #endregion

    #region Bounded Queue

    [Fact]
    public void PostDelivery_BeyondCapacity_DropsOldestAndKeepsQueueBounded()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var overflow = 10;
        var delivered = new List<int>();

        for (var i = 0; i < NoireFileWatcher.DeliveryQueueCapacity + overflow; i++)
        {
            var index = i;
            watcher.PostDelivery(() => delivered.Add(index));
        }

        watcher.DrainDeliveryQueue();

        delivered.Should().HaveCount(NoireFileWatcher.DeliveryQueueCapacity, because: "an event storm must be bounded rather than grow memory");
        delivered.Should().NotContain(0, because: "the oldest deliveries are the ones dropped");
        delivered.Last().Should().Be(NoireFileWatcher.DeliveryQueueCapacity + overflow - 1, because: "the newest notification describes the current state of a path and must survive");
    }

    [Fact]
    public void DrainDeliveryQueue_DeliversInPostOrder()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var delivered = new List<int>();

        for (var i = 0; i < 10; i++)
        {
            var index = i;
            watcher.PostDelivery(() => delivered.Add(index));
        }

        watcher.DrainDeliveryQueue();

        delivered.Should().Equal(Enumerable.Range(0, 10), because: "deliveries must reach consumers in the order the events were observed");
    }

    [Fact]
    public void DrainDeliveryQueue_DeliveryThatPostsMore_DoesNotStarveTheDrain()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var delivered = 0;

        watcher.PostDelivery(() =>
        {
            delivered++;
            watcher.PostDelivery(() => delivered++);
        });

        watcher.DrainDeliveryQueue();

        delivered.Should().Be(1, because: "a drain only runs what was queued at entry, so work a callback posts waits for the next frame instead of looping forever");

        watcher.DrainDeliveryQueue();

        delivered.Should().Be(2, because: "the deferred delivery must still run on the following drain");
    }

    [Fact]
    public void DrainDeliveryQueue_ConcurrentPosts_RunSerially()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var concurrent = 0;
        var maxConcurrent = 0;
        var posted = 200;

        Parallel.For(0, posted, _ =>
        {
            watcher.PostDelivery(() =>
            {
                var running = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, running);
                Thread.Sleep(1);
                Interlocked.Decrement(ref concurrent);
            });
        });

        watcher.DrainDeliveryQueue();

        maxConcurrent.Should().Be(1, because: "the underlying watchers raise events concurrently on several thread pool threads, and the drain is what stops consumer callbacks from overlapping");
    }

    [Fact]
    public void PostDelivery_WhenQueued_DoesNotRunBeforeADrain()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var ran = false;

        watcher.PostDelivery(() => ran = true);

        ran.Should().BeFalse(because: "a queued delivery waits for the framework thread rather than running on the observing thread");

        watcher.DrainDeliveryQueue();

        ran.Should().BeTrue();
    }

    #endregion

    #region Disposal

    [Fact]
    public void DrainDeliveryQueue_AfterDispose_RunsNothingPending()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var ran = 0;

        watcher.PostDelivery(() => ran++);
        watcher.PostDelivery(() => ran++);

        watcher.Dispose();
        watcher.DrainDeliveryQueue();

        ran.Should().Be(0, because: "a notification already in flight when Dispose runs must never reach a consumer callback");
    }

    [Fact]
    public void PostDelivery_AfterDispose_IsDiscarded()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var ran = 0;

        watcher.Dispose();
        watcher.PostDelivery(() => ran++);
        watcher.DrainDeliveryQueue();

        ran.Should().Be(0, because: "a watcher event racing disposal must not resurrect the delivery path");
    }

    [Fact]
    public void Dispose_DuringADrain_StopsRemainingDeliveries()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var ran = 0;

        watcher.PostDelivery(() =>
        {
            ran++;
            watcher.Dispose();
        });
        watcher.PostDelivery(() => ran++);
        watcher.PostDelivery(() => ran++);

        watcher.DrainDeliveryQueue();

        ran.Should().Be(1, because: "disposing from inside a callback must stop the rest of that drain, not deliver into a torn-down module");
    }

    [Fact]
    public void Dispose_InlineMode_IsIdempotent()
    {
        var watcher = CreateWatcher();

        var act = () =>
        {
            watcher.Dispose();
            watcher.Dispose();
        };

        act.Should().NotThrow(because: "module teardown is orchestrated externally and may run more than once");
    }

    [Fact]
    public void Dispose_InlineMode_WaitsForADeliveryThatIsAlreadyRunning()
    {
        var watcher = CreateWatcher();
        using var deliveryEntered = new ManualResetEventSlim(false);
        using var releaseDelivery = new ManualResetEventSlim(false);
        var deliveryFinished = false;
        var disposeReturned = false;

        var delivering = Task.Run(() => watcher.PostDelivery(() =>
        {
            deliveryEntered.Set();
            releaseDelivery.Wait();
            Volatile.Write(ref deliveryFinished, true);
        }));

        deliveryEntered.Wait(5000).Should().BeTrue(because: "inline delivery runs on the posting thread, so the callback must already be running");

        var disposing = Task.Run(() =>
        {
            watcher.Dispose();
            Volatile.Write(ref disposeReturned, true);
        });

        disposing.Wait(200).Should().BeFalse(because: "a callback that is already past the disposal latch cannot be called off, so Dispose has to wait for it instead of returning while it still runs");
        Volatile.Read(ref disposeReturned).Should().BeFalse();

        releaseDelivery.Set();

        disposing.Wait(5000).Should().BeTrue(because: "Dispose must return once the in-flight delivery has finished");
        delivering.Wait(5000).Should().BeTrue();
        Volatile.Read(ref deliveryFinished).Should().BeTrue();
    }

    [Fact]
    public void Dispose_FromInsideAnInlineDelivery_DoesNotDeadlock()
    {
        var watcher = CreateWatcher();

        var disposingFromCallback = Task.Run(() => watcher.PostDelivery(watcher.Dispose));

        disposingFromCallback.Wait(5000)
            .Should().BeTrue(because: "a callback may dispose the module that invoked it, so the wait for in-flight deliveries must not count the disposing thread's own delivery");
    }

    [Fact]
    public void PostDelivery_InlineMode_AfterDispose_DoesNotRunTheDelivery()
    {
        var watcher = CreateWatcher();
        var ran = 0;

        watcher.Dispose();
        watcher.PostDelivery(() => ran++);

        ran.Should().Be(0, because: "inline delivery is gated by the same disposal latch as the queued path");
    }

    #endregion

    #region Dropped Delivery Accounting

    [Fact]
    public void GetStatistics_AfterQueueOverflow_CountsTheDroppedDeliveries()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        var overflow = 10;

        for (var i = 0; i < NoireFileWatcher.DeliveryQueueCapacity + overflow; i++)
            watcher.PostDelivery(() => { });

        watcher.GetStatistics().TotalDeliveriesDropped
            .Should().Be(overflow, because: "dropping under overflow is only logged otherwise, leaving a consumer no programmatic way to learn that notifications never reached a handler");
    }

    [Fact]
    public void GetStatistics_WithoutOverflow_ReportsNoDroppedDeliveries()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);

        watcher.PostDelivery(() => { });
        watcher.DrainDeliveryQueue();

        watcher.GetStatistics().TotalDeliveriesDropped.Should().Be(0, because: "a delivery that reached its drain was not dropped");
    }

    [Fact]
    public void FileWatcherStatistics_StillDeconstructsIntoItsPositionalMembers()
    {
        var watcher = CreateWatcher(forceQueuedDelivery: true);

        // Compiling at all is the assertion: the drop counter is an init-only property in the record body rather than
        // a new positional parameter, precisely so that it leaves the primary constructor and the generated
        // Deconstruct alone and cannot break a consumer already deconstructing this record.
        var (registeredWatches, enabledWatches, totalRegistrations, totalRemoved, observed, dispatched, errors, duplicates, callbackExceptions)
            = watcher.GetStatistics();

        registeredWatches.Should().Be(0);
        enabledWatches.Should().Be(0);
        totalRegistrations.Should().Be(0);
        totalRemoved.Should().Be(0);
        observed.Should().Be(0);
        dispatched.Should().Be(0);
        errors.Should().Be(0);
        duplicates.Should().Be(0);
        callbackExceptions.Should().Be(0);

        watcher.GetStatistics().TotalDeliveriesDropped.Should().Be(0, because: "the drop counter is readable as a property even though it is not part of the deconstruction");
    }

    #endregion

    #region EventBus Thread Contract

    [Fact]
    public void Watch_RegisteredEvent_IsPublishedThroughTheQueueRatherThanOnTheCallersThread()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        var published = 0;
        bus.Subscribe<FileWatchRegisteredEvent>(_ => published++);

        watcher.WatchDirectory(CreateTempDirectory());

        published.Should().Be(0, because: "NoireEventBus invokes sync handlers inline, so publishing from Watch would run a subscriber on whichever thread the caller of Watch happened to use");

        watcher.DrainDeliveryQueue();

        published.Should().Be(1, because: "the registration event is delivered on the framework thread like every other event this module publishes");
    }

    [Fact]
    public void LifecycleEvents_AreDeliveredThroughTheQueueInTheOrderTheyWereCaused()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        var published = new List<string>();
        bus.Subscribe<FileWatchRegisteredEvent>(_ => published.Add("registered"));
        bus.Subscribe<FileWatchStateChangedEvent>(_ => published.Add("stateChanged"));
        bus.Subscribe<FileWatchRemovedEvent>(_ => published.Add("removed"));
        bus.Subscribe<FileWatchesClearedEvent>(_ => published.Add("cleared"));

        var watchId = watcher.WatchDirectory(CreateTempDirectory());
        watcher.SetWatchEnabled(watchId, false);
        watcher.ClearAllWatches();

        published.Should().BeEmpty(because: "no lifecycle event may run on the thread that called the module");

        watcher.DrainDeliveryQueue();

        published.Should().Equal(new[] { "registered", "stateChanged", "removed", "cleared" }, because: "the queue preserves the order the calls were made in, and ClearAllWatches reports each removal before reporting the clear");
    }

    [Fact]
    public void Dispose_PublishesNoLifecycleEventForTheWatchesItRemoves()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        watcher.WatchDirectory(CreateTempDirectory());
        watcher.DrainDeliveryQueue();

        var published = 0;
        bus.Subscribe<FileWatchRemovedEvent>(_ => published++);
        bus.Subscribe<FileWatchesClearedEvent>(_ => published++);

        watcher.Dispose();
        watcher.DrainDeliveryQueue();

        published.Should().Be(0, because: "a module being torn down must not call into the subscribers of a plugin that is unloading");
    }

    #endregion

    #region Bulk State Changes

    [Fact]
    public void DisableAllWatches_PublishesOneStateChangedEventPerWatchThatChanged()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        var firstId = watcher.WatchDirectory(CreateTempDirectory());
        var secondId = watcher.WatchDirectory(CreateTempDirectory());
        watcher.DrainDeliveryQueue();

        var published = new List<(string WatchId, bool Enabled)>();
        bus.Subscribe<FileWatchStateChangedEvent>(e => published.Add((e.WatchId, e.Enabled)));

        watcher.DisableAllWatches();

        published.Should().BeEmpty(because: "a bulk state change is queued like every other event rather than running on the caller's thread");

        watcher.DrainDeliveryQueue();

        // Asserted as a set rather than a sequence: the module orders deliveries by the calls that caused them, and
        // makes no promise about the order of the watches within one bulk call. Pinning that here would be pinning
        // the registration index's enumeration order, which is not part of the contract.
        published.Should().BeEquivalentTo(new[] { (firstId, false), (secondId, false) },
            because: "a subscriber tracking watch state must be told about a bulk change too, or its view is silently wrong afterwards");
    }

    [Fact]
    public void EnableAllWatches_OverAlreadyEnabledWatches_PublishesNothing()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        watcher.WatchDirectory(CreateTempDirectory());
        watcher.WatchDirectory(CreateTempDirectory());
        watcher.DrainDeliveryQueue();

        var published = 0;
        bus.Subscribe<FileWatchStateChangedEvent>(_ => published++);

        watcher.EnableAllWatches();
        watcher.DrainDeliveryQueue();

        published.Should().Be(0, because: "watches start enabled, so a bulk enable changed nothing and reporting a change would be a lie");
    }

    [Fact]
    public void EnableAllWatches_PublishesOnlyForTheWatchesThatActuallyChanged()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        var enabledId = watcher.WatchDirectory(CreateTempDirectory());
        var disabledId = watcher.WatchDirectory(CreateTempDirectory());
        watcher.SetWatchEnabled(disabledId, false);
        watcher.DrainDeliveryQueue();

        var published = new List<(string WatchId, bool Enabled)>();
        bus.Subscribe<FileWatchStateChangedEvent>(e => published.Add((e.WatchId, e.Enabled)));

        watcher.EnableAllWatches();
        watcher.DrainDeliveryQueue();

        published.Should().Equal(new[] { (disabledId, true) },
            because: "only the watch whose state actually flipped changed, so the already-enabled one must stay silent");
        published.Should().NotContain(e => e.WatchId == enabledId);
    }

    [Fact]
    public void BulkAndSingleStateChanges_AreDeliveredInTheOrderTheyWereCaused()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        var watchId = watcher.WatchDirectory(CreateTempDirectory());
        watcher.DrainDeliveryQueue();

        var published = new List<bool>();
        bus.Subscribe<FileWatchStateChangedEvent>(e => published.Add(e.Enabled));

        watcher.DisableAllWatches();
        watcher.SetWatchEnabled(watchId, true);
        watcher.DisableAllWatches();

        watcher.DrainDeliveryQueue();

        published.Should().Equal(new[] { false, true, false },
            because: "a state subscriber replays these in order to track state, so a bulk change must not jump ahead of or behind a single one");
    }

    [Fact]
    public void DisableAllWatches_WithNoWatches_PublishesNothingAndChains()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(forceQueuedDelivery: true);
        watcher.EventBus = bus;

        var published = 0;
        bus.Subscribe<FileWatchStateChangedEvent>(_ => published++);

        watcher.DisableAllWatches().Should().BeSameAs(watcher, because: "the bulk methods stay fluent");
        watcher.DrainDeliveryQueue();

        published.Should().Be(0);
    }

    #endregion

    #region Watcher Lifecycle

    [Fact]
    public void SetWatchEnabled_ForARemovedWatch_ReportsMissingRatherThanTouchingItsDisposedWatcher()
    {
        var watcher = CreateWatcher(active: true);
        var watchId = watcher.WatchDirectory(CreateTempDirectory());

        watcher.RemoveWatch(watchId).Should().BeTrue();

        var act = () => watcher.SetWatchEnabled(watchId, true);

        act.Should().NotThrow<ObjectDisposedException>(because: "removal unindexes the registration under the lock before disposing its watcher, so no later lookup can reach the disposed instance");
        watcher.SetWatchEnabled(watchId, true).Should().BeFalse(because: "a removed watch is simply gone");
    }

    [Fact]
    public void BulkStateChange_AfterAWatchWasRemoved_DoesNotTouchTheRemovedWatcher()
    {
        var watcher = CreateWatcher(active: true);
        var removedId = watcher.WatchDirectory(CreateTempDirectory());
        var liveId = watcher.WatchDirectory(CreateTempDirectory());

        watcher.RemoveWatch(removedId);

        var act = () =>
        {
            watcher.DisableAllWatches();
            watcher.EnableAllWatches();
        };

        act.Should().NotThrow<ObjectDisposedException>(because: "a bulk change iterates the registration index, which the removal already left");
        watcher.GetWatches().Should().ContainSingle().Which.WatchId.Should().Be(liveId);
    }

    [Fact]
    public void RemoveWatch_RacingConcurrentStateChanges_NeverSurfacesObjectDisposedException()
    {
        // The registration index is the retirement marker: every site looks a registration up and touches its
        // FileSystemWatcher inside one lock acquisition, so a watcher can only be disposed once it is unreachable.
        // This exercises that invariant under contention. It cannot fail unless the invariant is broken, so it is a
        // gate rather than a timing assertion, though it samples interleavings rather than proving their absence.
        var watcher = CreateWatcher(active: true);
        var watchIds = new ConcurrentQueue<string>();

        for (var i = 0; i < 24; i++)
            watchIds.Enqueue(watcher.WatchDirectory(CreateTempDirectory()));

        var failures = new ConcurrentQueue<Exception>();

        var act = () => Parallel.ForEach(watchIds, watchId =>
        {
            try
            {
                Parallel.Invoke(
                    () => watcher.RemoveWatch(watchId),
                    () => watcher.SetWatchEnabled(watchId, false),
                    () => watcher.SetWatchEnabled(watchId, true),
                    () => watcher.DisableAllWatches(),
                    () => watcher.EnableAllWatches(),
                    () => watcher.GetWatches());
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        act.Should().NotThrow();
        failures.Should().BeEmpty(because: "a watch removed while another call touches it must not surface a disposed watcher to the consumer");
        watcher.GetWatches().Should().BeEmpty(because: "every watch was removed exactly once");
    }

    [Fact]
    public void Watch_AfterDispose_RegistersNothingAndLeavesNoLiveWatcherBehind()
    {
        var watcher = CreateWatcher(active: true);
        var directory = CreateTempDirectory();

        watcher.Dispose();

        var watchId = watcher.Watch(new FileWatchRegistrationOptions
        {
            Path = directory,
            TargetType = FileWatchTargetType.Directory,
        });

        watcher.GetWatch(watchId).Should().BeNull(because: "disposal removes every watch it can see and then stops looking, so a registration landing after that sweep must take itself back out rather than keep a live FileSystemWatcher the module will never dispose");
        watcher.GetWatches().Should().BeEmpty();
    }

    [Fact]
    public void Watch_AfterDispose_DeliversNothingForTheAbandonedRegistration()
    {
        var directory = CreateTempDirectory();
        var watcher = CreateWatcher(active: true);
        var received = 0;

        watcher.Dispose();
        watcher.WatchDirectory(directory, callback: _ => Interlocked.Increment(ref received));

        File.WriteAllText(Path.Combine(directory, "after-dispose.txt"), "content");
        Thread.Sleep(200);

        received.Should().Be(0, because: "an abandoned registration raises no events, and the delivery latch would turn them away regardless");
    }

    #endregion

    #region Key Index

    [Fact]
    public void Watch_WithAKeyAnExistingWatchHolds_ReplacesThatWatch()
    {
        var watcher = CreateWatcher(active: true);
        var directory = CreateTempDirectory();

        var firstId = watcher.Watch(new FileWatchRegistrationOptions { Path = directory, Key = "shared" });
        var secondId = watcher.Watch(new FileWatchRegistrationOptions { Path = directory, Key = "shared" });

        watcher.GetWatch(firstId).Should().BeNull(because: "registering a key resolves the watch already holding it and removes it, so a consumer can replace a watch without removing it first");
        watcher.GetWatchByKey("shared")!.WatchId.Should().Be(secondId);
        watcher.GetWatches().Should().ContainSingle().Which.WatchId.Should().Be(secondId);
    }

    [Fact]
    public void RemoveWatch_ForTheWatchAKeyResolvesTo_RetiresTheKey()
    {
        var watcher = CreateWatcher(active: true);
        var watchId = watcher.Watch(new FileWatchRegistrationOptions { Path = CreateTempDirectory(), Key = "only" });

        watcher.GetWatchByKey("only").Should().NotBeNull();

        watcher.RemoveWatch(watchId).Should().BeTrue();

        watcher.GetWatchByKey("only").Should().BeNull(because: "a key must not outlive the registration it resolves to");
        watcher.RemoveWatchByKey("only").Should().BeFalse();
    }

    /// <summary>
    /// A key identifies at most one watch: registering a key resolves and retires whichever watch already holds
    /// it, inside the same lock acquisition that inserts the new one. Two registrations of one key must never both
    /// stay live with the index resolving to only one of them; the earlier watch is removed outright, so the index
    /// always resolves to exactly one live watch or to nothing.
    /// </summary>
    [Fact]
    public void Watch_UnderAKeyAlreadyHeld_RemovesThePreviousWatchEntirely()
    {
        var watcher = CreateWatcher(active: true);
        var directory = CreateTempDirectory();

        var firstId = watcher.Watch(new FileWatchRegistrationOptions { Path = directory, Key = "shared" });
        var secondId = watcher.Watch(new FileWatchRegistrationOptions { Path = directory, Key = "shared" });

        secondId.Should().NotBe(firstId);
        watcher.GetWatch(firstId).Should().BeNull(
            because: "registering a key removes the watch that held it rather than leaving it live but unreachable by key");
        watcher.GetWatch(secondId).Should().NotBeNull();
        watcher.GetWatchByKey("shared")!.WatchId.Should().Be(secondId);
        watcher.GetWatches().Should().ContainSingle(because: "a key identifies at most one live watch");
    }

    [Fact]
    public void Watch_UnderAKeyAlreadyHeld_AnnouncesThePreviousWatchAsRemoved()
    {
        var bus = CreateEventBus();
        var watcher = CreateWatcher(active: true);
        watcher.EventBus = bus;
        var directory = CreateTempDirectory();

        var firstId = watcher.Watch(new FileWatchRegistrationOptions { Path = directory, Key = "shared" });

        var removed = new List<string>();
        bus.Subscribe<FileWatchRemovedEvent>(e => removed.Add(e.WatchId));

        watcher.Watch(new FileWatchRegistrationOptions { Path = directory, Key = "shared" });

        removed.Should().ContainSingle(because: "the watch a key registration displaces is announced as removed")
            .Which.Should().Be(firstId);

        watcher.RemoveWatchByKey("shared").Should().BeTrue(
            because: "the key resolves to the surviving watch, so removing by key reaches it");
        watcher.GetWatches().Should().BeEmpty();
    }

    #endregion

    #region Event Type Gating

    [Fact]
    public void RealFileEvent_WithErrorNotificationsDisabled_StillReachesTheCallback()
    {
        var directory = CreateTempDirectory();
        var watcher = CreateWatcher(active: true);
        var received = new ConcurrentQueue<FileWatchNotification>();

        watcher.Watch(new FileWatchRegistrationOptions
        {
            Path = directory,
            TargetType = FileWatchTargetType.Directory,
            NotifyOnError = false,
        }, callback: received.Enqueue);

        File.WriteAllText(Path.Combine(directory, "created.txt"), "content");

        WaitUntil(() => received.Any(n => n.EventType == FileWatchEventType.Created))
            .Should().BeTrue(because: "a watcher error is not a notification and is routed separately, so NotifyOnError must not gate a Created event");
        received.Should().NotContain(n => n.EventType == FileWatchEventType.Error, because: "the notification path never produces an Error event type");
    }

    [Fact]
    public void RealRenameEvent_WithRenamedNotificationsDisabled_DoesNotReachTheCallback()
    {
        var directory = CreateTempDirectory();
        var original = Path.Combine(directory, "original.txt");
        File.WriteAllText(original, "content");

        var watcher = CreateWatcher(active: true);
        var received = new ConcurrentQueue<FileWatchNotification>();

        watcher.Watch(new FileWatchRegistrationOptions
        {
            Path = directory,
            TargetType = FileWatchTargetType.Directory,
            NotifyOnRenamed = false,
        }, callback: received.Enqueue);

        File.Move(original, Path.Combine(directory, "renamed.txt"));

        // The sentinel is written after the rename, so its arrival proves the watch is live and that the rename's own
        // events have had their chance to land. Without it the assertion below would pass on a watch that saw nothing.
        File.WriteAllText(Path.Combine(directory, "sentinel.txt"), "content");

        WaitUntil(() => received.Any(n => n.Name == "sentinel.txt"))
            .Should().BeTrue(because: "the watch must still observe the event types it was left enabled for");
        received.Should().NotContain(n => n.EventType == FileWatchEventType.Renamed, because: "NotifyOnRenamed false gates the rename out of the notification path");
    }

    #endregion
}
