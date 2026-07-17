using Dalamud.Interface.Windowing;
using FluentAssertions;
using NoireLib.Changelog;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireChangelogManager module, locking the invariant that the documented
/// <c>versions</c> constructor parameter works: the versions a caller hands to the constructor are accepted and
/// present afterwards, rather than reaching the window before it exists.<br/>
/// Also covers version ordering, the latest-version lookup, and the fact that a version added while an event bus
/// is attached is announced on it.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WindowedModuleCollection.Name)]
public class NoireChangelogManagerTests : IDisposable
{
    #region Helpers

    private readonly List<NoireChangelogManager> managersToClean = new();

    public NoireChangelogManagerTests()
    {
        WindowedModuleCollection.EnsureWindowSystem();
    }

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

    private NoireChangelogManager Track(NoireChangelogManager manager)
    {
        managersToClean.Add(manager);
        return manager;
    }

    private static ChangelogVersion MakeVersion(int major, int minor = 0, string title = "Some Title")
        => new()
        {
            Version = new Version(major, minor),
            Date = "2025-01-01",
            Title = title,
            Entries = new List<ChangelogEntry>(),
        };

    #endregion

    #region The versions constructor parameter

    [Fact]
    public void Constructor_WithVersions_DoesNotThrowAndKeepsThem()
    {
        // The regression gate: unpacking the versions argument before the window exists made every non-null
        // value of the documented parameter throw out of the constructor.
        var versions = new List<ChangelogVersion> { MakeVersion(1), MakeVersion(2) };

        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: versions));

        manager.GetAllVersions().Should().HaveCount(2);
        manager.GetVersion(new Version(1, 0, 0, 0)).Should().NotBeNull();
        manager.GetVersion(new Version(2, 0, 0, 0)).Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEmptyVersionList_DoesNotThrow()
    {
        // An empty list took the same path and threw for the same reason, without ever reaching a version.
        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: new List<ChangelogVersion>()));

        manager.GetAllVersions().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullVersions_DoesNotThrow()
    {
        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: null));

        manager.GetAllVersions().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithVersionsAndEventBus_AnnouncesEveryVersion()
    {
        // The event bus is attached before the versions are unpacked, so the events a constructor-supplied
        // list produces are delivered rather than dropped into a bus that is not set yet.
        var bus = new NoireEventBus(active: true, enableLogging: false);
        var announced = new List<Version>();
        bus.Subscribe<ChangelogVersionAddedEvent>(evt => announced.Add(evt.Version));

        var versions = new List<ChangelogVersion> { MakeVersion(1), MakeVersion(2) };
        Track(new NoireChangelogManager(active: false, enableLogging: false, versions: versions, eventBus: bus));

        announced.Should().HaveCount(2, "a version added through the constructor is announced like any other");
        bus.Dispose();
    }

    #endregion

    #region Version management

    [Fact]
    public void GetAllVersions_OrdersNewestFirst()
    {
        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: new List<ChangelogVersion>
        {
            MakeVersion(1),
            MakeVersion(3),
            MakeVersion(2),
        }));

        manager.GetAllVersions().Should().BeInDescendingOrder(v => v.Version);
    }

    [Fact]
    public void GetLatestVersion_ReturnsHighest_AndNullWhenEmpty()
    {
        var empty = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: null));
        empty.GetLatestVersion().Should().BeNull();

        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: new List<ChangelogVersion>
        {
            MakeVersion(1),
            MakeVersion(3),
            MakeVersion(2, 5),
        }));

        manager.GetLatestVersion().Should().Be(new Version(3, 0, 0, 0));
    }

    [Fact]
    public void AddVersions_AddsEveryVersion_AndAddVersionKeepsWorking()
    {
        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: null));

        manager.AddVersions(new List<ChangelogVersion> { MakeVersion(1), MakeVersion(2) });
        manager.GetAllVersions().Should().HaveCount(2);

        manager.AddVersion(MakeVersion(3));
        manager.GetAllVersions().Should().HaveCount(3);
        manager.GetLatestVersion().Should().Be(new Version(3, 0, 0, 0));
    }

    [Fact]
    public void AddVersion_WithSameVersion_ReplacesRatherThanDuplicates()
    {
        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: null));

        manager.AddVersion(MakeVersion(1, 0, "First"));
        manager.AddVersion(MakeVersion(1, 0, "Second"));

        manager.GetAllVersions().Should().ContainSingle();
        manager.GetVersion(new Version(1, 0, 0, 0))!.Title.Should().Be("Second");
    }

    [Fact]
    public void RemoveAndClearVersions_EmptyTheManager()
    {
        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: new List<ChangelogVersion>
        {
            MakeVersion(1),
            MakeVersion(2),
        }));

        manager.RemoveVersion(new Version(1, 0, 0, 0)).Should().BeTrue();
        manager.RemoveVersion(new Version(9, 0, 0, 0)).Should().BeFalse();
        manager.GetAllVersions().Should().ContainSingle();

        manager.ClearVersions();
        manager.GetAllVersions().Should().BeEmpty();
        manager.GetLatestVersion().Should().BeNull();
    }

    #endregion

    #region Window state

    /// <summary>
    /// The read of whether a module's window is open comes from the windowed module base, so a module that declares
    /// nothing of its own still offers it. The window itself is protected, which leaves a consumer no other way to ask.
    /// </summary>
    [Fact]
    public void IsWindowOpen_ReflectsTheWindowState()
    {
        var manager = Track(new NoireChangelogManager(active: false, enableLogging: false, versions: null));

        manager.HasWindow.Should().BeTrue("the module registers its window while initializing");
        manager.IsWindowOpen.Should().BeFalse("a module's window starts closed");

        manager.ShowWindow();
        manager.IsWindowOpen.Should().BeTrue();

        manager.HideWindow();
        manager.IsWindowOpen.Should().BeFalse();

        manager.SetShowWindow(null);
        manager.IsWindowOpen.Should().BeTrue("a null passed to SetShowWindow toggles the window");
    }

    #endregion
}

/// <summary>
/// Groups the test classes that construct modules owning a window. They share the process-wide window system
/// that <see cref="WindowedModuleCollection.EnsureWindowSystem"/> installs, and a module unregisters its window
/// from whichever system is installed at disposal, so they must not run at the same time.
/// </summary>
[CollectionDefinition(Name)]
public class WindowedModuleCollection
{
    public const string Name = "NoireLib windowed modules";

    private static readonly object InstallLock = new();

    /// <summary>
    /// Installs a window system into <see cref="NoireService"/> so that modules owning a window can be built
    /// outside a running game.<br/>
    /// <see cref="NoireModuleWithWindowBase{TModule, TWindow}"/> refuses to register a window without one, and
    /// NoireService only ever creates one while initializing against a real plugin interface, so the property is
    /// written directly here.
    /// </summary>
    public static void EnsureWindowSystem()
    {
        lock (InstallLock)
        {
            if (NoireService.NoireWindowSystem != null)
                return;

            var setter = typeof(NoireService)
                .GetProperty(nameof(NoireService.NoireWindowSystem), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetSetMethod(nonPublic: true)!;

            setter.Invoke(null, new object?[] { new WindowSystem("NoireLib_WindowSystem_For_Tests") });
        }
    }
}
