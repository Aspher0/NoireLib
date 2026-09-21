using Dalamud.Interface.Windowing;
using FluentAssertions;
using NoireLib.Changelog;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the surface a plugin's own changelog window draws from: the cached version list, the module-held
/// selection, and the routing of every open and close path to a window registered with SetCustomWindow.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WindowedModuleCollection.Name)]
public class NoireChangelogManagerCustomWindowTests : IDisposable
{
    private readonly List<Action> toDispose = new();

    public NoireChangelogManagerCustomWindowTests()
    {
        WindowedModuleCollection.EnsureWindowSystem();
    }

    public void Dispose()
    {
        foreach (var dispose in toDispose)
        {
            try
            {
                dispose();
            }
            catch
            {
            }
        }
    }

    private sealed class PluginWindow : Window
    {
        public PluginWindow() : base("Plugin changelog###NoireChangelogManagerCustomWindowTests") { }

        public override void Draw() { }
    }

    private static ChangelogVersion MakeVersion(int major)
        => new()
        {
            Version = new Version(major, 0),
            Date = "2025-01-01",
            Entries = new List<ChangelogEntry>(),
        };

    private NoireChangelogManager CreateManager(NoireEventBus? bus = null, params int[] majors)
    {
        var versions = new List<ChangelogVersion>();
        foreach (var major in majors)
            versions.Add(MakeVersion(major));

        var manager = new NoireChangelogManager(active: false, enableLogging: false, versions: versions, eventBus: bus);
        toDispose.Add(manager.Dispose);
        return manager;
    }

    [Fact]
    public void Versions_AreNewestFirst_AndSelectionStartsOnTheLatest()
    {
        var manager = CreateManager(null, 1, 3, 2);

        manager.Versions.Should().HaveCount(3);
        manager.Versions.Should().BeInDescendingOrder(v => v.Version);
        manager.SelectedVersion!.Version.Should().Be(new Version(3, 0, 0, 0));
    }

    [Fact]
    public void Versions_IsCached_UntilTheVersionsChange()
    {
        var manager = CreateManager(null, 1, 2);

        var first = manager.Versions;
        manager.Versions.Should().BeSameAs(first, "a window reads the list every frame");

        manager.AddVersion(MakeVersion(5));
        manager.Versions.Should().NotBeSameAs(first);
        manager.Versions[0].Version.Should().Be(new Version(5, 0, 0, 0));
        manager.SelectedVersion!.Version.Should().Be(new Version(5, 0, 0, 0), "adding a version resets the selection to the latest");
    }

    [Fact]
    public void SelectVersion_ChangesTheSelection_AndAnnouncesOnlyARealChange()
    {
        var bus = new NoireEventBus(active: true, enableLogging: false);
        toDispose.Add(bus.Dispose);
        var changes = new List<ChangelogVersionChangedEvent>();
        bus.Subscribe<ChangelogVersionChangedEvent>(changes.Add);

        var manager = CreateManager(bus, 1, 2);

        manager.SelectVersion(new Version(1, 0, 0, 0)).Should().BeTrue();
        manager.SelectedVersion!.Version.Should().Be(new Version(1, 0, 0, 0));
        manager.SelectVersion(new Version(1, 0, 0, 0)).Should().BeTrue();
        manager.SelectVersion(new Version(9, 0, 0, 0)).Should().BeFalse();
        manager.SelectedVersion!.Version.Should().Be(new Version(1, 0, 0, 0), "an unknown version leaves the selection alone");

        changes.Should().ContainSingle();
        changes[0].OldVersion.Should().Be(new Version(2, 0, 0, 0));
        changes[0].NewVersion.Should().Be(new Version(1, 0, 0, 0));
    }

    [Fact]
    public void ShowWindow_OpensTheCustomWindow_InsteadOfTheBuiltInOne()
    {
        var manager = CreateManager(null, 1);
        var window = new PluginWindow();

        manager.SetCustomWindow(window).Should().BeSameAs(manager);
        manager.CustomWindow.Should().BeSameAs(window);

        manager.ShowWindow();
        window.IsOpen.Should().BeTrue();
        manager.IsWindowOpen.Should().BeTrue();

        manager.ToggleWindow();
        window.IsOpen.Should().BeFalse();
        manager.IsWindowOpen.Should().BeFalse();

        manager.SetShowWindow(true);
        window.IsOpen.Should().BeTrue();
        manager.HideWindow();
        window.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void ShowChangelogForVersion_SelectsTheVersion_OpensTheCustomWindow_AndAnnouncesIt()
    {
        var bus = new NoireEventBus(active: true, enableLogging: false);
        toDispose.Add(bus.Dispose);
        var opened = new List<Version>();
        var closed = 0;
        bus.Subscribe<ChangelogWindowOpenedEvent>(e => opened.Add(e.Version));
        bus.Subscribe<ChangelogWindowClosedEvent>(_ => closed++);

        var manager = CreateManager(bus, 1, 2);
        var window = new PluginWindow();
        manager.SetCustomWindow(window);

        manager.ShowChangelogForVersion(new Version(1, 0, 0, 0));

        window.IsOpen.Should().BeTrue();
        manager.SelectedVersion!.Version.Should().Be(new Version(1, 0, 0, 0));
        opened.Should().Equal(new Version(1, 0, 0, 0));

        manager.ShowChangelogForVersion(new Version(7, 0, 0, 0));
        manager.SelectedVersion!.Version.Should().Be(new Version(2, 0, 0, 0), "an unknown version falls back to the latest");

        manager.CloseWindow();
        window.IsOpen.Should().BeFalse();
        closed.Should().Be(1);

        manager.CloseWindow();
        closed.Should().Be(1, "closing a closed window announces nothing");
    }

    [Fact]
    public void SetCustomWindow_CarriesTheOpenStateAcross_AndNullRestoresTheBuiltInWindow()
    {
        var manager = CreateManager(null, 1);
        var window = new PluginWindow();

        manager.ShowWindow();
        manager.IsWindowOpen.Should().BeTrue("the built-in window opened");

        manager.SetCustomWindow(window);
        window.IsOpen.Should().BeTrue("an open built-in window hands its open state to the custom one");
        manager.IsWindowOpen.Should().BeTrue();

        manager.SetCustomWindow(null);
        manager.CustomWindow.Should().BeNull();
        window.IsOpen.Should().BeFalse();
        manager.IsWindowOpen.Should().BeTrue("the built-in window takes the open state back");

        manager.HideWindow();
        manager.IsWindowOpen.Should().BeFalse();
    }

    [Fact]
    public void ClearVersions_ClosesTheCustomWindow_AndEmptiesTheSelection()
    {
        var manager = CreateManager(null, 1);
        var window = new PluginWindow();
        manager.SetCustomWindow(window);
        manager.ShowWindow();

        manager.ClearVersions();

        window.IsOpen.Should().BeFalse();
        manager.Versions.Should().BeEmpty();
        manager.SelectedVersion.Should().BeNull();
    }
}
