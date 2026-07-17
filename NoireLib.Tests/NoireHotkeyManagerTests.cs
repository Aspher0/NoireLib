using FluentAssertions;
using Newtonsoft.Json;
using NoireLib.EventBus;
using NoireLib.HotkeyManager;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireHotkeyManager module: the queue that carries detected triggers from the
/// detection timer to the framework thread and its deliberate refusal to coalesce, the guarantee that neither
/// a disposed module nor an unregistered hotkey can reach a consumer callback, the rule that no consumer visible
/// surface is ever invoked while the manager holds its lock, the rebind capture session that a reader always
/// sees whole rather than as a mixture of two, the rebind report the binding UI consumes exactly once, the
/// single case insensitive rule that every comparison of a hotkey id follows, the stored keybinds that every
/// instance of the module shares, activation while NoireLib is not initialized, and the case insensitive
/// comparer that a load from disk restores without writing anything back.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireHotkeyManagerTests : IDisposable
{
    #region Helpers

    private readonly List<NoireHotkeyManager> managersToClean = new();
    private readonly List<NoireEventBus> busesToClean = new();

    public void Dispose()
    {
        foreach (var manager in managersToClean)
        {
            try
            {
                manager.Dispose();
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

        // The stored keybinds are a process wide singleton that outlives a test, so a binding one test persists
        // would otherwise be restored over the binding of the next test that registers the same id.
        HotkeyManagerConfig.Keybinds.Clear();
    }

    private NoireHotkeyManager MakeManager()
    {
        // shouldSaveKeybinds stays off so that registration never reaches the configuration system.
        var manager = new NoireHotkeyManager(moduleId: null, active: false, enableLogging: false, shouldSaveKeybinds: false);
        managersToClean.Add(manager);
        return manager;
    }

    private NoireEventBus MakeEventBus()
    {
        // Publishing is refused by an inactive bus, so these have to be active to observe anything.
        var bus = new NoireEventBus(active: true, enableLogging: false);
        busesToClean.Add(bus);
        return bus;
    }

    private NoireHotkeyManager MakeManagerWithBus(NoireEventBus bus)
    {
        var manager = new NoireHotkeyManager(moduleId: null, active: false, enableLogging: false, shouldSaveKeybinds: false, eventBus: bus);
        managersToClean.Add(manager);
        return manager;
    }

    /// <summary>
    /// Creates a manager that persists its bindings, for the tests that cover the stored keybinds.<br/>
    /// This stays game-free: with NoireLib uninitialized the configuration resolves no file path, so it keeps
    /// the bindings in memory and writes nothing. That in-memory dictionary holds exactly what a real save
    /// would serialize, which is what these tests assert against.
    /// </summary>
    private NoireHotkeyManager MakePersistingManager()
    {
        var manager = new NoireHotkeyManager(moduleId: null, active: false, enableLogging: false, shouldSaveKeybinds: true);
        managersToClean.Add(manager);
        return manager;
    }

    private static HotkeyEntry MakeEntry(string id, Action callback)
        => new(id, id, new HotkeyBinding(65), callback, true, HotkeyActivationMode.Pressed);

    private static HotkeyEntry MakeEntryWithDisplayName(string id, string displayName)
        => new(id, displayName, new HotkeyBinding(65), () => { }, true, HotkeyActivationMode.Pressed);

    /// <summary>
    /// A configuration that counts saves and exposes the serializer settings the configuration system
    /// really loads with, so the comparer tests exercise the actual load boundary rather than a friendlier
    /// approximation of it.
    /// </summary>
    private sealed class ProbeConfig : HotkeyManagerConfigInstance
    {
        public int SaveCount { get; private set; }

        public static JsonSerializerSettings LoadSettings => JsonSettings;

        public override bool Save()
        {
            SaveCount++;
            return true;
        }
    }

    private const string ConfigJson = """
        {
          "Version": 1,
          "Keybinds": {
            "My.Hotkey": { "VkCode": 65, "Ctrl": true, "Shift": false, "Alt": false, "GamepadButton": null }
          }
        }
        """;

    #endregion

    #region Trigger queue and the no-coalescing contract

    [Fact]
    public void QueueTrigger_DoesNotInvokeCallback_UntilDrained()
    {
        var manager = MakeManager();
        var fired = 0;

        manager.QueueTrigger(MakeEntry("deferred.hotkey", () => fired++));

        fired.Should().Be(0, "detection runs on a system timer, and consumer callbacks may touch game state, which is only safe on the framework thread");

        manager.DrainPendingTriggers();

        fired.Should().Be(1, "the drain is what delivers the trigger");
    }

    [Fact]
    public void QueueTrigger_TwiceBeforeADrain_DeliversBothInsteadOfCoalescing()
    {
        var manager = MakeManager();
        var fired = 0;
        var entry = MakeEntry("repeat.hotkey", () => fired++);

        // A Repeat hotkey at its 80ms default outruns the frame loop below roughly 12 FPS, so the same entry
        // legitimately triggers more than once between two frames.
        manager.QueueTrigger(entry);
        manager.QueueTrigger(entry);
        manager.DrainPendingTriggers();

        fired.Should().Be(2, "coalescing would reimpose the frame rate ceiling that detecting on a 16ms system timer exists to escape");
    }

    [Fact]
    public void DrainPendingTriggers_DeliversInDetectionOrder()
    {
        var manager = MakeManager();
        var order = new List<string>();

        manager.QueueTrigger(MakeEntry("first", () => order.Add("first")));
        manager.QueueTrigger(MakeEntry("second", () => order.Add("second")));
        manager.DrainPendingTriggers();

        order.Should().Equal("first", "second");
    }

    [Fact]
    public void DrainPendingTriggers_WithNothingQueued_IsANoOp()
    {
        var manager = MakeManager();
        var fired = 0;

        manager.QueueTrigger(MakeEntry("once.hotkey", () => fired++));
        manager.DrainPendingTriggers();
        manager.DrainPendingTriggers();

        fired.Should().Be(1, "a drained trigger is not delivered again on the next frame");
    }

    [Fact]
    public void TriggerHotkey_ThrowingCallback_DoesNotStopTheRestOfTheDrain()
    {
        var manager = MakeManager();
        var fired = 0;

        manager.QueueTrigger(MakeEntry("throwing.hotkey", () => throw new InvalidOperationException("consumer bug")));
        manager.QueueTrigger(MakeEntry("healthy.hotkey", () => fired++));

        var act = () => manager.DrainPendingTriggers();

        act.Should().NotThrow("a consumer exception must not escape into the framework update");
        fired.Should().Be(1, "one misbehaving hotkey must not swallow the triggers queued behind it");
    }

    [Fact]
    public void QueueTrigger_BeyondCapacity_DropsTheOldestInsteadOfGrowing()
    {
        var manager = MakeManager();
        var delivered = new List<int>();

        for (var i = 0; i <= NoireHotkeyManager.MaxPendingTriggers; i++)
        {
            var index = i;
            manager.QueueTrigger(MakeEntry($"hotkey.{index}", () => delivered.Add(index)));
        }

        manager.DrainPendingTriggers();

        delivered.Should().HaveCount(NoireHotkeyManager.MaxPendingTriggers, "a framework thread that stops pumping must not grow the queue without bound");
        delivered.Should().NotContain(0, "the oldest trigger is the one dropped");
        delivered[0].Should().Be(1);
    }

    #endregion

    #region Unregistering between detection and delivery

    [Fact]
    public void DrainPendingTriggers_ForAHotkeyUnregisteredAfterTheTrigger_DoesNotInvokeTheCallback()
    {
        var manager = MakeManager();
        var fired = 0;
        var entry = MakeEntry("doomed.hotkey", () => fired++);
        manager.RegisterHotkey(entry).Should().BeTrue();

        // Detection queues a trigger a frame before the drain delivers it, so a hotkey really can be removed
        // while one of its triggers is still waiting.
        manager.QueueTrigger(entry);
        manager.UnregisterHotkey("doomed.hotkey").Should().BeTrue();
        manager.DrainPendingTriggers();

        fired.Should().Be(0, "a callback the consumer has retired must not run after the hotkey is gone");
    }

    [Fact]
    public void DrainPendingTriggers_WithOneHotkeyUnregistered_StillDeliversTheOthersInOrder()
    {
        var manager = MakeManager();
        var order = new List<string>();

        var first = MakeEntry("first", () => order.Add("first"));
        var doomed = MakeEntry("doomed", () => order.Add("doomed"));
        var last = MakeEntry("last", () => order.Add("last"));

        manager.RegisterHotkey(first).Should().BeTrue();
        manager.RegisterHotkey(doomed).Should().BeTrue();
        manager.RegisterHotkey(last).Should().BeTrue();

        manager.QueueTrigger(first);
        manager.QueueTrigger(doomed);
        manager.QueueTrigger(last);
        manager.UnregisterHotkey("doomed").Should().BeTrue();
        manager.DrainPendingTriggers();

        order.Should().Equal(new[] { "first", "last" }, "discarding one trigger must not drop or reorder the triggers queued around it");
    }

    [Fact]
    public void RegisterHotkey_AfterAnUnregister_MakesTheEntryDeliverableAgain()
    {
        var manager = MakeManager();
        var fired = 0;
        var entry = MakeEntry("revived.hotkey", () => fired++);

        manager.RegisterHotkey(entry).Should().BeTrue();
        manager.UnregisterHotkey("revived.hotkey").Should().BeTrue();
        manager.RegisterHotkey(entry).Should().BeTrue();

        manager.QueueTrigger(entry);
        manager.DrainPendingTriggers();

        fired.Should().Be(1, "an entry that is registered again is live again, and must not stay silenced by its earlier removal");
    }

    #endregion

    #region Disposal

    [Fact]
    public void Dispose_DiscardsQueuedTriggers_SoNoCallbackRunsAfterwards()
    {
        var manager = MakeManager();
        var fired = 0;

        manager.QueueTrigger(MakeEntry("pending.hotkey", () => fired++));
        manager.Dispose();

        // A framework update can still be in flight while the module is torn down, so a drain after the
        // dispose must find nothing to deliver.
        manager.DrainPendingTriggers();

        fired.Should().Be(0, "a callback delivered after Dispose would run into a plugin that is unloading");
    }

    [Fact]
    public void QueueTrigger_AfterDispose_IsIgnored()
    {
        var manager = MakeManager();
        var fired = 0;

        manager.Dispose();
        manager.QueueTrigger(MakeEntry("late.hotkey", () => fired++));
        manager.DrainPendingTriggers();

        fired.Should().Be(0, "a tick still running when the module is disposed must not be able to queue new work");
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var manager = MakeManager();
        manager.Dispose();

        var act = () => manager.Dispose();

        act.Should().NotThrow("teardown is reached on plugin unload and must tolerate running twice");
    }

    [Fact]
    public void Dispose_ClearsRegisteredHotkeys()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("registered.hotkey", () => { })).Should().BeTrue();

        manager.Dispose();

        manager.GetHotkeys().Should().BeEmpty();
        manager.TryGetHotkey("registered.hotkey", out _).Should().BeFalse();
    }

    #endregion

    #region Registration

    [Fact]
    public void RegisterHotkey_MatchesIdsIgnoringCase()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("My.Hotkey", () => { })).Should().BeTrue();

        manager.RegisterHotkey(MakeEntry("my.hotkey", () => { })).Should().BeFalse("hotkey ids are compared case insensitively");
        manager.TryGetHotkey("MY.HOTKEY", out var entry).Should().BeTrue();
        entry.Id.Should().Be("My.Hotkey");
    }

    [Fact]
    public void UnregisterHotkey_RemovesTheEntry()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("temp.hotkey", () => { }));

        manager.UnregisterHotkey("TEMP.HOTKEY").Should().BeTrue();
        manager.UnregisterHotkey("temp.hotkey").Should().BeFalse("the entry is already gone");
        manager.GetHotkeys().Should().BeEmpty();
    }

    [Fact]
    public void RegisterHotkey_WithAnEmptyDisplayName_FallsBackToTheId()
    {
        var manager = MakeManager();
        var entry = MakeEntryWithDisplayName("blank.hotkey", string.Empty);

        manager.RegisterHotkey(entry).Should().BeTrue();

        entry.DisplayName.Should().Be("blank.hotkey", "the binding UI labels its button with the display name, and the id is the only name a hotkey is guaranteed to have");
    }

    [Fact]
    public void RegisterHotkey_WithAWhitespaceDisplayName_FallsBackToTheId()
    {
        var manager = MakeManager();
        var entry = MakeEntryWithDisplayName("spaced.hotkey", "   ");

        manager.RegisterHotkey(entry).Should().BeTrue();

        entry.DisplayName.Should().Be("spaced.hotkey", "a display name of nothing but spaces labels the button no better than an empty one does");
    }

    [Fact]
    public void RegisterHotkey_WithADisplayName_KeepsIt()
    {
        var manager = MakeManager();
        var entry = MakeEntryWithDisplayName("named.hotkey", "Toggle Borderless");

        manager.RegisterHotkey(entry).Should().BeTrue();

        entry.DisplayName.Should().Be("Toggle Borderless", "the fallback applies only when the caller supplied no name of its own");
    }

    #endregion

    #region Notifying binding changes without holding the lock

    [Fact]
    public void SetHotkeyBinding_NotifiesWithTheEntryAlreadyCarryingTheNewBinding()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("changed.hotkey", () => { })).Should().BeTrue();

        var seen = new List<int>();
        manager.OnHotkeyChanged += entry => seen.Add(entry.Binding.VkCode);

        manager.SetHotkeyBinding("changed.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        seen.Should().Equal(new[] { 66 }, "the binding is written before consumers are notified, so a handler that reads it back sees the change that notified it");
    }

    [Fact]
    public void SetHotkeyBinding_WhileAHandlerRuns_LeavesTheManagerReachableFromOtherThreads()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("shared.hotkey", () => { })).Should().BeTrue();

        var otherThreadFinished = false;

        manager.OnHotkeyChanged += _ =>
        {
            // A handler is consumer code of unknown duration. Invoking it under the manager's lock would stall
            // every other thread that needs a hotkey for as long as the handler ran, and the detection timer
            // takes that lock every 16ms.
            var otherThread = Task.Run(() => manager.GetHotkeys());
            otherThreadFinished = otherThread.Wait(TimeSpan.FromSeconds(5));
        };

        manager.SetHotkeyBinding("shared.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        otherThreadFinished.Should().BeTrue("a thread that needs the manager must not block behind a consumer handler");
    }

    [Fact]
    public void SetHotkeyBinding_HandlerCallingBackIntoTheManager_IsApplied()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("primary.hotkey", () => { })).Should().BeTrue();
        manager.RegisterHotkey(MakeEntry("mirror.hotkey", () => { })).Should().BeTrue();

        manager.OnHotkeyChanged += entry =>
        {
            if (entry.Id == "primary.hotkey")
                manager.SetHotkeyBinding("mirror.hotkey", entry.Binding);
        };

        manager.SetHotkeyBinding("primary.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        manager.TryGetHotkey("mirror.hotkey", out var mirror).Should().BeTrue();
        mirror.Binding.VkCode.Should().Be(66, "a handler may drive the manager, and the lock is released before it runs rather than silently re-entered by it");
    }

    [Fact]
    public void SetHotkeyBinding_HandlerUnregisteringTheHotkey_DoesNotResurrectItsStoredBind()
    {
        var manager = MakePersistingManager();
        manager.RegisterHotkey(MakeEntry("retired.hotkey", () => { })).Should().BeTrue();

        manager.OnHotkeyChanged += entry => manager.UnregisterHotkey(entry.Id);

        manager.SetHotkeyBinding("retired.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        manager.TryGetHotkey("retired.hotkey", out _).Should().BeFalse("the handler unregistered it");
        HotkeyManagerConfig.Keybinds.Should().NotContainKey(
            "retired.hotkey",
            "the binding is stored before consumers are notified, so a handler that retires the hotkey has the last word on the stored bind rather than having its removal written straight back over");
    }

    [Fact]
    public void SetHotkeyBinding_ThrowingHandler_DoesNotEscapeAndKeepsTheBinding()
    {
        var manager = MakePersistingManager();
        manager.RegisterHotkey(MakeEntry("brittle.hotkey", () => { })).Should().BeTrue();

        manager.OnHotkeyChanged += _ => throw new InvalidOperationException("consumer bug");

        var act = () => manager.SetHotkeyBinding("brittle.hotkey", new HotkeyBinding(66));

        act.Should().NotThrow("a rebind is captured on the detection timer, and a consumer handler that throws must not surface there");

        manager.TryGetHotkey("brittle.hotkey", out var entry).Should().BeTrue();
        entry.Binding.VkCode.Should().Be(66, "the binding is committed before consumers are notified, so a throwing handler cannot undo it");
        HotkeyManagerConfig.Keybinds["brittle.hotkey"].VkCode.Should().Be(66, "and cannot prevent it from being persisted either");
    }

    [Fact]
    public void SetHotkeyBinding_PublishesBindingChanged_ReportingWhetherTheBindingDiffered()
    {
        var bus = MakeEventBus();
        var manager = MakeManagerWithBus(bus);
        manager.RegisterHotkey(MakeEntry("watched.hotkey", () => { })).Should().BeTrue();

        var reported = new List<bool>();
        bus.Subscribe<HotkeyBindingChangedEvent>(evt => reported.Add(evt.IsNewBinding));

        manager.SetHotkeyBinding("watched.hotkey", new HotkeyBinding(66)).Should().BeTrue();
        manager.SetHotkeyBinding("watched.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        reported.Should().Equal(
            new[] { true, false },
            "whether the binding differed can only be known when it is written, so it is captured there and carried to the subscriber rather than recomputed later");
    }

    [Fact]
    public void SetHotkeyBinding_SubscriberCallingBackIntoTheManager_DoesNotDeadlock()
    {
        var bus = MakeEventBus();
        var manager = MakeManagerWithBus(bus);
        manager.RegisterHotkey(MakeEntry("subscribed.hotkey", () => { })).Should().BeTrue();

        var observed = 0;

        // The EventBus invokes its synchronous subscribers inline on whichever thread publishes, so a subscriber
        // is consumer code reached straight from the manager and must be treated as such.
        bus.Subscribe<HotkeyBindingChangedEvent>(evt =>
        {
            manager.SetHotkeyEnabled(evt.Hotkey.Id, false);
            manager.GetHotkeys();
            observed++;
        });

        manager.SetHotkeyBinding("subscribed.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        observed.Should().Be(1);
        manager.TryGetHotkey("subscribed.hotkey", out var entry).Should().BeTrue();
        entry.Enabled.Should().BeFalse("a subscriber may reach back into the manager, which means the publication cannot happen under its lock");
    }

    [Fact]
    public void SetHotkeyBinding_ForAnUnknownHotkey_NotifiesNothing()
    {
        var manager = MakeManager();
        var notified = 0;
        manager.OnHotkeyChanged += _ => notified++;

        manager.SetHotkeyBinding("missing.hotkey", new HotkeyBinding(66)).Should().BeFalse();

        notified.Should().Be(0, "nothing changed, so there is nothing to announce");
    }

    [Fact]
    public void ClearHotkeyBinding_NotifiesWithAnEmptyBinding()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("cleared.hotkey", () => { })).Should().BeTrue();

        HotkeyBinding? seen = null;
        manager.OnHotkeyChanged += entry => seen = entry.Binding;

        manager.ClearHotkeyBinding("cleared.hotkey").Should().BeTrue();

        seen.HasValue.Should().BeTrue("clearing a binding notifies like any other binding change");
        seen!.Value.IsEmpty.Should().BeTrue("the entry handed to the handler already carries the cleared binding");
    }

    #endregion

    #region Notifying listening state without holding the lock

    [Fact]
    public void StartAndStopListening_PublishTheirEvents_ToASubscriberThatMayCallBack()
    {
        var bus = MakeEventBus();
        var manager = MakeManagerWithBus(bus);
        manager.RegisterHotkey(MakeEntry("listen.hotkey", () => { })).Should().BeTrue();

        var started = new List<string>();
        var stopped = new List<bool>();

        bus.Subscribe<HotkeyListeningStartedEvent>(evt =>
        {
            started.Add(evt.HotkeyId);
            manager.GetHotkeys();
        });
        bus.Subscribe<HotkeyListeningStoppedEvent>(evt =>
        {
            stopped.Add(evt.WasCancelled);
            manager.GetHotkeys();
        });

        manager.StartListening("listen.hotkey").Should().BeTrue();
        manager.IsListening.Should().BeTrue();
        manager.StopListening(false);

        started.Should().Equal("listen.hotkey");
        stopped.Should().Equal(new[] { false }, "detection stops listening from its own timer thread once it captures a binding, and a subscriber reached from there must still be able to call back into the manager");
        manager.IsListening.Should().BeFalse();
    }

    [Fact]
    public void StopListening_WhenNotListening_PublishesNothing()
    {
        var bus = MakeEventBus();
        var manager = MakeManagerWithBus(bus);

        var stopped = 0;
        bus.Subscribe<HotkeyListeningStoppedEvent>(_ => stopped++);

        manager.StopListening();

        stopped.Should().Be(0, "there was no listening session to announce the end of");
    }

    [Fact]
    public void Dispose_WhileListening_PublishesNoListeningStoppedEvent()
    {
        var bus = MakeEventBus();
        var manager = MakeManagerWithBus(bus);
        manager.RegisterHotkey(MakeEntry("abandoned.hotkey", () => { })).Should().BeTrue();

        var stopped = 0;
        bus.Subscribe<HotkeyListeningStoppedEvent>(_ => stopped++);

        manager.StartListening("abandoned.hotkey").Should().BeTrue();
        manager.Dispose();

        stopped.Should().Be(0, "a disposed module delivers nothing, since a subscriber reached during teardown runs inside a plugin that is unloading");
    }

    #endregion

    #region The rebind capture session as one unit

    [Fact]
    public void StartListening_ExposesTheHotkeyIdAndModeAsOneSession()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("pad.hotkey", () => { })).Should().BeTrue();

        manager.StartListening("pad.hotkey", HotkeyListenMode.Gamepad).Should().BeTrue();

        var session = manager.CurrentListeningSession;
        session.Should().NotBeNull();
        session!.HotkeyId.Should().Be("pad.hotkey");
        session.Mode.Should().Be(HotkeyListenMode.Gamepad, "the input source a session was started with belongs to that session and is read together with its hotkey");
        session.ModifierState.Should().BeNull("a session that has only just started has captured nothing yet");
        session.WaitingForModifierRelease.Should().BeFalse();
    }

    [Fact]
    public void StartListening_ForAnUnknownHotkey_StartsNoSession()
    {
        var manager = MakeManager();

        manager.StartListening("missing.hotkey").Should().BeFalse();

        manager.CurrentListeningSession.Should().BeNull("there is no hotkey to rebind");
        manager.IsListening.Should().BeFalse();
    }

    [Fact]
    public void StartListening_WhileAlreadyListening_ReplacesTheSessionWhole()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("keyboard.hotkey", () => { })).Should().BeTrue();
        manager.RegisterHotkey(MakeEntry("gamepad.hotkey", () => { })).Should().BeTrue();

        manager.StartListening("keyboard.hotkey", HotkeyListenMode.Keyboard).Should().BeTrue();
        manager.StartListening("gamepad.hotkey", HotkeyListenMode.Gamepad).Should().BeTrue();

        var session = manager.CurrentListeningSession;
        session!.HotkeyId.Should().Be("gamepad.hotkey");
        session.Mode.Should().Be(HotkeyListenMode.Gamepad, "a session replaces the previous one entirely, so its hotkey never survives paired with the mode of the one before it");
    }

    [Fact]
    public void StopListening_EndsTheSessionEntirely()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("ended.hotkey", () => { })).Should().BeTrue();
        manager.StartListening("ended.hotkey", HotkeyListenMode.Gamepad).Should().BeTrue();

        manager.StopListening();

        manager.CurrentListeningSession.Should().BeNull();
        manager.IsListening.Should().BeFalse();
        manager.ListeningHotkeyId.Should().BeNull();
    }

    [Fact]
    public void Deactivate_WhileListening_EndsTheSession()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("interrupted.hotkey", () => { })).Should().BeTrue();
        manager.Activate();
        manager.StartListening("interrupted.hotkey").Should().BeTrue();

        manager.Deactivate();

        manager.IsListening.Should().BeFalse("detection is stopped, so nothing is left to capture the binding the session was waiting for");
        manager.CurrentListeningSession.Should().BeNull();
    }

    [Fact]
    public void CurrentListeningSession_WhileSessionsAreReplaced_NeverPairsOneSessionsHotkeyWithAnothersMode()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("keyboard.hotkey", () => { })).Should().BeTrue();
        manager.RegisterHotkey(MakeEntry("gamepad.hotkey", () => { })).Should().BeTrue();

        var mismatches = 0;
        var reading = true;

        // The framework thread reads the session every frame to decide what the binding UI draws and which
        // inputs to swallow, while a consumer starts and replaces sessions from its own thread. Both of those
        // answers come from the hotkey and the mode together, so a reader able to pair the hotkey of one session
        // with the mode of another would act on a session that never existed.
        var reader = Task.Run(() =>
        {
            while (Volatile.Read(ref reading))
            {
                var session = manager.CurrentListeningSession;
                if (session == null)
                    continue;

                var expectedMode = session.HotkeyId == "gamepad.hotkey"
                    ? HotkeyListenMode.Gamepad
                    : HotkeyListenMode.Keyboard;

                if (session.Mode != expectedMode)
                    Interlocked.Increment(ref mismatches);
            }
        });

        for (var i = 0; i < 20_000; i++)
        {
            manager.StartListening("keyboard.hotkey", HotkeyListenMode.Keyboard);
            manager.StartListening("gamepad.hotkey", HotkeyListenMode.Gamepad);
        }

        Volatile.Write(ref reading, false);
        reader.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

        mismatches.Should().Be(0, "the hotkey being rebound and the input source being watched are one session, and a single read of a single reference is what hands a reader both");
    }

    #endregion

    #region The rebind report the binding UI consumes

    [Fact]
    public void TryConsumeBindingChanged_AfterARebind_ReportsItOnce()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("reported.hotkey", () => { })).Should().BeTrue();

        manager.SetHotkeyBinding("reported.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        manager.TryConsumeBindingChanged("reported.hotkey").Should().BeTrue("the binding UI reports a rebind on the frame it lands");
        manager.TryConsumeBindingChanged("reported.hotkey").Should().BeFalse("the report is consumed by the frame that saw it, so the next frame does not repeat it");
    }

    [Fact]
    public void TryConsumeBindingChanged_WithoutARebind_ReportsNothing()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("quiet.hotkey", () => { })).Should().BeTrue();

        manager.TryConsumeBindingChanged("quiet.hotkey").Should().BeFalse("registering a hotkey is not a rebind of it");
    }

    [Fact]
    public void TryConsumeBindingChanged_ForAnotherHotkey_LeavesTheReportForItsOwn()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("changed.hotkey", () => { })).Should().BeTrue();
        manager.RegisterHotkey(MakeEntry("other.hotkey", () => { })).Should().BeTrue();

        manager.SetHotkeyBinding("changed.hotkey", new HotkeyBinding(66)).Should().BeTrue();

        manager.TryConsumeBindingChanged("other.hotkey").Should().BeFalse("the rebind was not this hotkey's");
        manager.TryConsumeBindingChanged("changed.hotkey").Should().BeTrue("a button drawn for one hotkey must not consume the report belonging to another");
    }

    [Fact]
    public void TryConsumeBindingChanged_AfterEachOfTwoRebinds_ReportsBoth()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("twice.hotkey", () => { })).Should().BeTrue();

        manager.SetHotkeyBinding("twice.hotkey", new HotkeyBinding(66)).Should().BeTrue();
        manager.TryConsumeBindingChanged("twice.hotkey").Should().BeTrue();

        manager.SetHotkeyBinding("twice.hotkey", new HotkeyBinding(67)).Should().BeTrue();
        manager.TryConsumeBindingChanged("twice.hotkey").Should().BeTrue("consuming one report must not silence the rebind that follows it");
    }

    [Fact]
    public void TryConsumeBindingChanged_ForAnUnknownHotkey_ReportsNothing()
    {
        var manager = MakeManager();

        manager.SetHotkeyBinding("missing.hotkey", new HotkeyBinding(66)).Should().BeFalse();

        manager.TryConsumeBindingChanged("missing.hotkey").Should().BeFalse("nothing changed, so there is nothing to report");
    }

    [Fact]
    public void TryConsumeBindingChanged_WhileAnotherHotkeyIsReboundConcurrently_DoesNotSwallowTheOtherReport()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("consumed.hotkey", () => { })).Should().BeTrue();
        manager.RegisterHotkey(MakeEntry("rebound.hotkey", () => { })).Should().BeTrue();

        var swallowed = 0;
        var consuming = true;

        // A button drawn for one hotkey consumes its report on the framework thread while the detection timer
        // captures a rebind of a different hotkey from its own thread, so a rebind really does land between that
        // button's read of the report and its clearing of it. The consuming side asks in a different case from
        // the one the rebind was made in, so that the id rule and the conditional clear are exercised together:
        // deciding the report is this caller's is a question about identity, while clearing it is a question
        // about whether the record is still the one that was read.
        var consumer = Task.Run(() =>
        {
            while (Volatile.Read(ref consuming))
                manager.TryConsumeBindingChanged("CONSUMED.HOTKEY");
        });

        for (var i = 0; i < 20_000; i++)
        {
            manager.SetHotkeyBinding("consumed.hotkey", new HotkeyBinding(66 + (i % 2)));
            manager.SetHotkeyBinding("rebound.hotkey", new HotkeyBinding(66 + (i % 2)));

            if (!manager.TryConsumeBindingChanged("rebound.hotkey"))
                swallowed++;
        }

        Volatile.Write(ref consuming, false);
        consumer.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

        swallowed.Should().Be(
            0,
            "the report is cleared only while it is still the one that was read, so a rebind of another hotkey landing after that read survives to reach its own button rather than being wiped out by a button that was never told about it");
    }

    #endregion

    #region Hotkey ids naming one hotkey whatever case they are written in

    [Fact]
    public void TryConsumeBindingChanged_WithADifferentlyCasedId_ReportsTheRebindOnce()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("my.hotkey", () => { })).Should().BeTrue();

        manager.SetHotkeyBinding("MY.HOTKEY", new HotkeyBinding(66)).Should().BeTrue();

        manager.TryConsumeBindingChanged("My.Hotkey").Should().BeTrue("a rebind belongs to the hotkey it was made on, and an id names that hotkey whatever case it is written in");
        manager.TryConsumeBindingChanged("my.hotkey").Should().BeFalse("the report is consumed by whichever spelling of the id asked first, since they are all the same hotkey");
    }

    [Fact]
    public void IsListeningFor_WithADifferentlyCasedId_ReportsTheCapture()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("my.hotkey", () => { })).Should().BeTrue();

        manager.StartListening("MY.HOTKEY").Should().BeTrue();

        manager.IsListeningFor("My.Hotkey").Should().BeTrue("the binding UI has to show as listening even when it draws the id in a different case from the one the capture was started in");
        manager.IsListeningFor("my.hotkey").Should().BeTrue();
    }

    [Fact]
    public void IsListeningFor_ForAnotherHotkey_ReportsNothing()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("listening.hotkey", () => { })).Should().BeTrue();
        manager.RegisterHotkey(MakeEntry("idle.hotkey", () => { })).Should().BeTrue();

        manager.StartListening("listening.hotkey").Should().BeTrue();

        manager.IsListeningFor("idle.hotkey").Should().BeFalse("ignoring case is not ignoring the id, and a different hotkey is still a different hotkey");
    }

    [Fact]
    public void IsListeningFor_WhenNothingIsBeingRebound_ReportsNothing()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("quiet.hotkey", () => { })).Should().BeTrue();

        manager.IsListeningFor("quiet.hotkey").Should().BeFalse("no capture is in progress");
    }

    [Fact]
    public void ARebind_RegisteredDrawnAndConsumedInDifferentCases_IsOneHotkeyThroughout()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("my.hotkey", () => { })).Should().BeTrue();

        // The whole path a binding button walks, spelled a different way at every step. Registering under one
        // case and drawing under another must not silently produce a button that never shows as listening and
        // never receives the rebind it captured, while the rest of the module treats the two ids as one hotkey.
        manager.StartListening("MY.HOTKEY").Should().BeTrue();
        manager.IsListeningFor("My.Hotkey").Should().BeTrue();

        manager.SetHotkeyBinding("mY.hOtKeY", new HotkeyBinding(66)).Should().BeTrue();
        manager.StopListening(false);

        manager.TryConsumeBindingChanged("My.Hotkey").Should().BeTrue();
        manager.TryGetHotkey("MY.hotkey", out var entry).Should().BeTrue();
        entry.Binding.VkCode.Should().Be(66);
        entry.Id.Should().Be("my.hotkey", "the entry keeps the id it was registered with, since matching ignoring case is not rewriting what the consumer supplied");
    }

    [Fact]
    public void StartListening_WithADifferentlyCasedId_StartsASessionForTheRegisteredHotkey()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("my.hotkey", () => { })).Should().BeTrue();

        manager.StartListening("MY.HOTKEY", HotkeyListenMode.Gamepad).Should().BeTrue();

        manager.CurrentListeningSession!.HotkeyId.Should().Be("MY.HOTKEY", "the session carries the id as the caller wrote it, and every comparison against it ignores case rather than the id being rewritten");
        manager.IsListeningFor("my.hotkey").Should().BeTrue();
    }

    #endregion

    #region Persistence of the shared stored keybinds

    [Fact]
    public void SaveAllKeybinds_FromOneInstance_KeepsTheBindsOfAnotherInstance()
    {
        var first = MakePersistingManager();
        var second = MakePersistingManager();

        first.RegisterHotkey(MakeEntry("first.instance.hotkey", () => { })).Should().BeTrue();
        second.RegisterHotkey(MakeEntry("second.instance.hotkey", () => { })).Should().BeTrue();

        // Turning persistence back on is what saves every bind the instance holds.
        first.SetShouldSaveKeybinds(false).SetShouldSaveKeybinds(true);

        HotkeyManagerConfig.Keybinds.Should().ContainKey(
            "second.instance.hotkey",
            "the stored keybinds are keyed by id alone and shared by every instance of the module, so one instance saving its own binds must not erase another's");
        HotkeyManagerConfig.Keybinds.Should().ContainKey("first.instance.hotkey", "the instance still stores what it holds");
    }

    [Fact]
    public void SaveAllKeybinds_KeepsStoredBindsThatNoRegisteredHotkeyOwns()
    {
        var manager = MakePersistingManager();
        HotkeyManagerConfig.Keybinds["not.registered.yet"] = new HotkeyBinding(66);

        manager.RegisterHotkey(MakeEntry("registered.hotkey", () => { })).Should().BeTrue();
        manager.SetShouldSaveKeybinds(false).SetShouldSaveKeybinds(true);

        HotkeyManagerConfig.Keybinds.Should().ContainKey(
            "not.registered.yet",
            "a stored id can belong to a hotkey registered later or to another instance, so a save has no way to tell that it is stale");
    }

    [Fact]
    public void SaveAllKeybinds_StoresTheBindsTheInstanceAlreadyHolds()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("saved.later.hotkey", () => { })).Should().BeTrue();

        HotkeyManagerConfig.Keybinds.Should().NotContainKey("saved.later.hotkey", "persistence was off while the hotkey was registered");

        manager.SetShouldSaveKeybinds(true);

        HotkeyManagerConfig.Keybinds.Should().ContainKey("saved.later.hotkey", "turning persistence on stores the binds the instance is already holding");
    }

    [Fact]
    public void UnregisterHotkey_RemovesOnlyItsOwnStoredBind()
    {
        var manager = MakePersistingManager();
        manager.RegisterHotkey(MakeEntry("removed.hotkey", () => { })).Should().BeTrue();
        HotkeyManagerConfig.Keybinds["kept.hotkey"] = new HotkeyBinding(66);

        manager.UnregisterHotkey("removed.hotkey").Should().BeTrue();

        HotkeyManagerConfig.Keybinds.Should().NotContainKey("removed.hotkey", "unregistering a hotkey is the one path that deletes a stored bind");
        HotkeyManagerConfig.Keybinds.Should().ContainKey("kept.hotkey", "it deletes the single id it is retiring and nothing else");
    }

    #endregion

    #region Activation while NoireLib is not initialized

    [Fact]
    public void Activate_WithoutNoireLibInitialized_DoesNotThrow()
    {
        var manager = MakeManager();

        var act = () => manager.Activate();

        act.Should().NotThrow("a plugin can construct and activate this module before NoireLib is initialized, and detection simply cannot be wired up yet");
        manager.IsActive.Should().BeTrue("the active state is recorded even though nothing is wired");
    }

    [Fact]
    public void Deactivate_WithoutNoireLibInitialized_DoesNotThrow()
    {
        var manager = MakeManager();
        manager.Activate();

        var act = () => manager.Deactivate();

        act.Should().NotThrow("there is no framework to detach a handler from that was never attached");
        manager.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_DiscardsQueuedTriggers()
    {
        var manager = MakeManager();
        var fired = 0;
        var entry = MakeEntry("stopped.hotkey", () => fired++);
        manager.RegisterHotkey(entry).Should().BeTrue();

        manager.Activate();
        manager.QueueTrigger(entry);
        manager.Deactivate();
        manager.DrainPendingTriggers();

        fired.Should().Be(0, "a trigger detected before the module stopped listening must not reach a consumer afterwards");
    }

    #endregion

    #region Configuration comparer at the load boundary

    [Fact]
    public void Config_NewInstance_UsesCaseInsensitiveComparer()
    {
        new HotkeyManagerConfigInstance().Keybinds.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Config_Deserialized_RestoresCaseInsensitiveComparer()
    {
        var config = JsonConvert.DeserializeObject<HotkeyManagerConfigInstance>(ConfigJson, ProbeConfig.LoadSettings);

        config.Should().NotBeNull();
        config!.Keybinds.Comparer.Should().BeSameAs(
            StringComparer.OrdinalIgnoreCase,
            "deserialization rebuilds the dictionary with the default ordinal comparer, so the load boundary has to restore the case insensitive one");
    }

    [Fact]
    public void Config_Deserialized_ResolvesKeybindsIgnoringCase()
    {
        var config = JsonConvert.DeserializeObject<HotkeyManagerConfigInstance>(ConfigJson, ProbeConfig.LoadSettings);

        config!.Keybinds.TryGetValue("my.hotkey", out var binding).Should().BeTrue("a persisted binding must resolve regardless of the case its id was written in");
        binding.VkCode.Should().Be(65);
        binding.Ctrl.Should().BeTrue();
    }

    [Fact]
    public void Config_Deserialized_DoesNotSave()
    {
        var config = JsonConvert.DeserializeObject<ProbeConfig>(ConfigJson, ProbeConfig.LoadSettings);

        config!.Keybinds.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
        config.SaveCount.Should().Be(0, "normalizing the comparer must never write the configuration back to disk");
    }

    [Fact]
    public void Config_Deserialized_WithNullKeybinds_YieldsEmptyCaseInsensitiveDictionary()
    {
        var config = JsonConvert.DeserializeObject<HotkeyManagerConfigInstance>("""{ "Version": 1, "Keybinds": null }""", ProbeConfig.LoadSettings);

        config!.Keybinds.Should().NotBeNull("a hand edited file must not leave the property null for every later read to dereference");
        config.Keybinds.Should().BeEmpty();
        config.Keybinds.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}
