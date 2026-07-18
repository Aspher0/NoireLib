using FluentAssertions;
using NoireLib.HotkeyManager;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for reconfiguring a hotkey at runtime through the live entry returned by
/// <see cref="NoireHotkeyManager.TryGetHotkey"/>: assigning an option takes effect for the next evaluation and,
/// when the manager persists, is written to the stored set; assigning <see cref="HotkeyEntry.Binding"/> routes
/// through <see cref="NoireHotkeyManager.SetHotkeyBinding"/>; and an entry with no owning manager is a plain value
/// that neither throws nor persists.<br/>
/// With NoireLib uninitialized the manager persists an option change inline rather than through the debouncer
/// (which requires an initialized library), so the write is observable here without a running game. The coalescing
/// of a burst of writes is the in-game path and is the owner's to confirm.<br/>
/// Shares the stored-hotkeys collection with <see cref="NoireHotkeyManagerTests"/> so the two never run in
/// parallel: both mutate the process-wide <see cref="HotkeyManagerConfig.Hotkeys"/> singleton, and one clearing it
/// between the other's steps would fail spuriously.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("HotkeyManagerStoredHotkeys")]
public class NoireHotkeyManagerLiveMutationTests : IDisposable
{
    private readonly List<NoireHotkeyManager> managersToClean = new();

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

        // The stored hotkeys are a process wide singleton that outlives a test.
        HotkeyManagerConfig.Hotkeys.Clear();
    }

    private NoireHotkeyManager MakeManager()
    {
        var manager = new NoireHotkeyManager(moduleId: null, active: false, enableLogging: false, shouldSaveKeybinds: false);
        managersToClean.Add(manager);
        return manager;
    }

    private NoireHotkeyManager MakePersistingManager()
    {
        var manager = new NoireHotkeyManager(moduleId: null, active: false, enableLogging: false, shouldSaveKeybinds: true);
        managersToClean.Add(manager);
        return manager;
    }

    private static HotkeyEntry MakeEntry(string id)
        => new(id, id, new HotkeyBinding(65), () => { }, true, HotkeyActivationMode.Pressed);

    [Fact]
    public void SettingOptionsThroughTheEntry_OnAPersistingManager_PersistsThem()
    {
        var manager = MakePersistingManager();
        var entry = MakeEntry("live.options");
        manager.RegisterHotkey(entry).Should().BeTrue();

        entry.ActivationMode = HotkeyActivationMode.HoldAndRepeat;
        entry.HoldDelay = TimeSpan.FromMilliseconds(250);
        entry.BlockGameInput = true;
        entry.RequireGameFocus = false;
        entry.DisplayName = "Renamed";

        var stored = HotkeyManagerConfig.Hotkeys["live.options"];
        stored.ActivationMode.Should().Be(HotkeyActivationMode.HoldAndRepeat, "an option set on the live entry is written to the stored record");
        stored.HoldDelay.Should().Be(TimeSpan.FromMilliseconds(250));
        stored.BlockGameInput.Should().BeTrue();
        stored.RequireGameFocus.Should().BeFalse();
        stored.DisplayName.Should().Be("Renamed");
    }

    [Fact]
    public void SettingAnOptionThroughTheEntry_OnANonPersistingManager_DoesNotPersist()
    {
        var manager = MakeManager();
        var entry = MakeEntry("unsaved.option");
        manager.RegisterHotkey(entry).Should().BeTrue();

        entry.BlockGameInput = true;

        entry.BlockGameInput.Should().BeTrue("the option still takes effect on the entry itself");
        HotkeyManagerConfig.Hotkeys.Should().NotContainKey("unsaved.option", "persistence is off, so a change is not written");
    }

    [Fact]
    public void SettingAnOption_TakesEffectOnTheNextEvaluation()
    {
        var manager = MakeManager();
        var entry = MakeEntry("live.mode");
        manager.RegisterHotkey(entry).Should().BeTrue();

        // Registered as Pressed, so a press would fire immediately. Switching to Released live must change that.
        entry.ActivationMode = HotkeyActivationMode.Released;

        manager.EvaluateActivation(entry, combinationActive: true, mainKeyPhysicallyDown: true, nowMs: 0)
            .Should().BeFalse("the mode was switched to Released at runtime, so a press no longer triggers");
        manager.EvaluateActivation(entry, combinationActive: false, mainKeyPhysicallyDown: false, nowMs: 16)
            .Should().BeTrue("and the release now does");
    }

    [Fact]
    public void SettingTheBindingThroughTheEntry_BehavesLikeSetHotkeyBinding()
    {
        var manager = MakePersistingManager();
        var entry = MakeEntry("bind.through.entry");
        manager.RegisterHotkey(entry).Should().BeTrue();

        var notified = new List<int>();
        manager.OnHotkeyChanged += changed => notified.Add(changed.Binding.VkCode);

        entry.Binding = new HotkeyBinding(66);

        notified.Should().Equal(new[] { 66 }, "assigning Binding on a registered entry raises the binding-changed notification like SetHotkeyBinding does");
        manager.TryConsumeBindingChanged("bind.through.entry").Should().BeTrue("and reports the rebind to the binding UI");
        HotkeyManagerConfig.Hotkeys["bind.through.entry"].Binding.VkCode.Should().Be(66, "and persists it");
    }

    [Fact]
    public void SettingTheBindingThroughAnUnownedEntry_IsAPlainWrite()
    {
        var entry = MakeEntry("orphan.binding");

        var act = () => entry.Binding = new HotkeyBinding(66);

        act.Should().NotThrow("an entry not held by a manager has no routed path, so assigning its binding is a plain field write");
        entry.Binding.VkCode.Should().Be(66);
        HotkeyManagerConfig.Hotkeys.Should().NotContainKey("orphan.binding", "an entry with no owner persists nothing");
    }

    [Fact]
    public void SettingAnOptionOnAnEntryWithNoOwner_DoesNotThrowOrPersist()
    {
        var entry = MakeEntry("orphan.option");

        var act = () => entry.BlockGameInput = true;

        act.Should().NotThrow();
        entry.BlockGameInput.Should().BeTrue();
        HotkeyManagerConfig.Hotkeys.Should().NotContainKey("orphan.option");
    }

    [Fact]
    public void SettingAnOptionAfterUnregister_DoesNotResurrectTheStoredHotkey()
    {
        var manager = MakePersistingManager();
        var entry = MakeEntry("retired.option");
        manager.RegisterHotkey(entry).Should().BeTrue();
        HotkeyManagerConfig.Hotkeys.Should().ContainKey("retired.option", "registration persisted it");

        manager.UnregisterHotkey("retired.option").Should().BeTrue();
        HotkeyManagerConfig.Hotkeys.Should().NotContainKey("retired.option", "unregistering removed the stored hotkey");

        entry.BlockGameInput = true;

        HotkeyManagerConfig.Hotkeys.Should().NotContainKey(
            "retired.option",
            "the entry's owner was cleared on unregister, so a later option set on it cannot write its removed hotkey back");
    }

    [Fact]
    public void SettingAnOptionAfterDispose_DoesNotPersist()
    {
        var manager = MakePersistingManager();
        var entry = MakeEntry("disposed.option");
        manager.RegisterHotkey(entry).Should().BeTrue();

        manager.Dispose();
        HotkeyManagerConfig.Hotkeys.Clear();

        entry.BlockGameInput = true;

        HotkeyManagerConfig.Hotkeys.Should().NotContainKey(
            "disposed.option",
            "teardown cleared the entry's owner, so an option set on it after dispose persists nothing");
    }

    [Fact]
    public void SettingAnOptionToItsCurrentValue_WritesNothing()
    {
        var manager = MakePersistingManager();
        var entry = MakeEntry("noop.option");
        manager.RegisterHotkey(entry).Should().BeTrue();

        HotkeyManagerConfig.Hotkeys.Remove("noop.option");

        // BlockGameInput already defaults to false; assigning false again must not reach the persist path.
        entry.BlockGameInput = false;

        HotkeyManagerConfig.Hotkeys.Should().NotContainKey(
            "noop.option",
            "the notifying setter only persists when the value actually changes, so writing the current value is a no-op");
    }
}
