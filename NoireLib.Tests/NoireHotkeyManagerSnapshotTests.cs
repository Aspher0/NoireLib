using FluentAssertions;
using NoireLib.HotkeyManager;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the lock-free entries snapshot the detection tick and the framework input blocker read.
/// The snapshot itself is internal, but <see cref="NoireHotkeyManager.GetHotkeys"/> is served from it, so these
/// assert through that surface that a structural change (register, unregister, teardown) is reflected and that the
/// returned collection is a copy the caller owns.<br/>
/// These managers do not persist, so they never touch the process-wide stored hotkeys and need no shared
/// collection.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireHotkeyManagerSnapshotTests : IDisposable
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
    }

    private NoireHotkeyManager MakeManager()
    {
        var manager = new NoireHotkeyManager(moduleId: null, active: false, enableLogging: false, shouldSaveKeybinds: false);
        managersToClean.Add(manager);
        return manager;
    }

    private static HotkeyEntry MakeEntry(string id)
        => new(id, id, new HotkeyBinding(65), () => { }, true, HotkeyActivationMode.Pressed);

    [Fact]
    public void GetHotkeys_ReflectsRegistrationsAndRemovals()
    {
        var manager = MakeManager();

        manager.GetHotkeys().Should().BeEmpty("nothing is registered yet");

        manager.RegisterHotkey(MakeEntry("one")).Should().BeTrue();
        manager.RegisterHotkey(MakeEntry("two")).Should().BeTrue();

        manager.GetHotkeys().Should().HaveCount(2, "the snapshot is rebuilt on every registration");

        manager.UnregisterHotkey("one").Should().BeTrue();

        var remaining = manager.GetHotkeys();
        remaining.Should().HaveCount(1, "unregistering rebuilds the snapshot without the removed entry");
        remaining.Should().OnlyContain(entry => entry.Id == "two");
    }

    [Fact]
    public void GetHotkeys_ReturnsACopyTheCallerOwns()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("owned")).Should().BeTrue();

        var first = (List<HotkeyEntry>)manager.GetHotkeys();
        first.Clear();

        manager.GetHotkeys().Should().HaveCount(1, "the returned list is a copy, so a caller emptying it does not touch the manager");
    }

    [Fact]
    public void GetHotkeys_AfterDispose_IsEmpty()
    {
        var manager = MakeManager();
        manager.RegisterHotkey(MakeEntry("gone")).Should().BeTrue();

        manager.Dispose();

        manager.GetHotkeys().Should().BeEmpty("teardown clears the entries and rebuilds the snapshot empty");
    }
}
