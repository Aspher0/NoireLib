using Dalamud.Interface.Windowing;
using FluentAssertions;
using NoireLib.HistoryLogger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the surface a plugin's own log window draws from: <see cref="HistoryLogView"/> (search, filters,
/// sorting, paging, counts, caching), the entry formatting, the delete permission rule, and the routing of the window
/// methods to a window registered with SetCustomWindow.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WindowedModuleCollection.Name)]
public class HistoryLogViewTests : IDisposable
{
    private readonly List<NoireHistoryLogger> loggers = new();

    public HistoryLogViewTests()
    {
        WindowedModuleCollection.EnsureWindowSystem();
    }

    public void Dispose()
    {
        foreach (var logger in loggers)
        {
            try
            {
                logger.Dispose();
            }
            catch
            {
            }
        }
    }

    private sealed class PluginWindow : Window
    {
        public PluginWindow() : base("Plugin logs###HistoryLogViewTests") { }

        public override void Draw() { }
    }

    private NoireHistoryLogger CreateLogger()
    {
        var logger = new NoireHistoryLogger(active: false, enableLogging: false);
        loggers.Add(logger);
        return logger;
    }

    private static HistoryLogEntry Entry(int minute, HistoryLogLevel level, string category, string message, string? source = null) => new()
    {
        Timestamp = new DateTime(2024, 1, 1, 0, minute, 0, DateTimeKind.Utc),
        Level = level,
        Category = category,
        Message = message,
        Source = source,
    };

    private NoireHistoryLogger CreateSeededLogger()
    {
        var logger = CreateLogger();
        logger.AddEntry(Entry(1, HistoryLogLevel.Info, "Swap", "Swapped Dance", "Orchestrator"));
        logger.AddEntry(Entry(2, HistoryLogLevel.Warning, "Swap", "Lenient turn match", "Matcher"));
        logger.AddEntry(Entry(3, HistoryLogLevel.Error, "Penumbra", "No answer in time"));
        logger.AddEntry(Entry(4, HistoryLogLevel.Debug, "Sync", "Sync sent", "SyncService"));
        logger.AddEntry(Entry(5, HistoryLogLevel.Info, "Penumbra", "Settings applied", "Caller"));
        return logger;
    }

    [Fact]
    public void Entries_DefaultToEveryEntry_NewestFirst()
    {
        var view = new HistoryLogView(CreateSeededLogger());

        view.Entries.Select(e => e.Message).Should().Equal("Settings applied", "Sync sent", "No answer in time", "Lenient turn match", "Swapped Dance");
        view.TotalCount.Should().Be(5);
        view.PageCount.Should().Be(1);
        view.Page.Should().Be(1);
    }

    [Fact]
    public void SearchText_MatchesMessageCategoryAndSource_IgnoringCaseAndSurroundingSpaces()
    {
        var view = new HistoryLogView(CreateSeededLogger());

        view.SearchText = "  penumbra ";
        view.Entries.Should().HaveCount(2, "the category matches");

        view.SearchText = "syncservice";
        view.Entries.Should().ContainSingle().Which.Message.Should().Be("Sync sent", "the source matches");

        view.SearchText = "DANCE";
        view.Entries.Should().ContainSingle().Which.Message.Should().Be("Swapped Dance");

        view.TotalCount.Should().Be(5, "the total counts entries before any filter");
    }

    [Fact]
    public void CategoryAndLevelFilters_Combine_AndEmptyMeansEverything()
    {
        var view = new HistoryLogView(CreateSeededLogger());

        view.ToggleCategory("Swap");
        view.Entries.Should().HaveCount(2);
        view.IsCategorySelected("Swap").Should().BeTrue();

        view.SetLevelSelected(HistoryLogLevel.Warning, true);
        view.Entries.Should().ContainSingle().Which.Message.Should().Be("Lenient turn match");

        view.ClearCategoryFilter();
        view.ToggleLevel(HistoryLogLevel.Error);
        view.SelectedLevels.Should().BeEquivalentTo(new[] { HistoryLogLevel.Warning, HistoryLogLevel.Error });
        view.Entries.Should().HaveCount(2);

        view.ClearLevelFilter();
        view.Entries.Should().HaveCount(5);
    }

    [Fact]
    public void Categories_AndLevelCounts_CoverEveryEntry_BeforeAnyFilter()
    {
        var view = new HistoryLogView(CreateSeededLogger());
        view.SetLevelSelected(HistoryLogLevel.Error, true);

        view.Categories.Should().Equal("Penumbra", "Swap", "Sync");
        view.CountOf(HistoryLogLevel.Info).Should().Be(2);
        view.CountOf(HistoryLogLevel.Warning).Should().Be(1);
        view.CountOf(HistoryLogLevel.Critical).Should().Be(0);
    }

    [Fact]
    public void Sorting_FollowsTheColumnAndDirection()
    {
        var view = new HistoryLogView(CreateSeededLogger());

        view.SortDescending = false;
        view.Entries.First().Message.Should().Be("Swapped Dance");

        view.SortColumn = HistoryLogSortColumn.Level;
        view.SortDescending = true;
        view.Entries.First().Level.Should().Be(HistoryLogLevel.Error);

        view.SortColumn = HistoryLogSortColumn.Category;
        view.SortDescending = false;
        view.Entries.Select(e => e.Category).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Paging_SlicesTheEntries_ClampsThePage_AndFilterChangesReturnToPageOne()
    {
        var view = new HistoryLogView(CreateSeededLogger()) { ItemsPerPage = 2 };

        view.PageCount.Should().Be(3);
        view.PageEntries.Select(e => e.Message).Should().Equal("Settings applied", "Sync sent");

        view.Page = 3;
        view.PageStartIndex.Should().Be(4);
        view.PageEntries.Should().ContainSingle().Which.Message.Should().Be("Swapped Dance");

        view.Page = 99;
        view.Page.Should().Be(3, "the page is kept within the page count");
        view.Page = -4;
        view.Page.Should().Be(1);

        view.Page = 2;
        view.SortDescending = false;
        view.Page.Should().Be(2, "sorting keeps the page");

        view.SearchText = "a";
        view.Page.Should().Be(1, "a new search returns to the first page");

        view.Page = 2;
        view.ItemsPerPage = 1;
        view.Page.Should().Be(1, "a new page size returns to the first page");
    }

    [Fact]
    public void Results_AreCached_UntilTheEntriesOrSettingsChange()
    {
        var logger = CreateSeededLogger();
        var view = new HistoryLogView(logger);

        var entries = view.Entries;
        var page = view.PageEntries;
        var revision = view.Revision;

        view.Entries.Should().BeSameAs(entries);
        view.PageEntries.Should().BeSameAs(page);
        view.Revision.Should().Be(revision);

        view.SearchText = view.SearchText;
        view.Revision.Should().Be(revision, "setting an unchanged value rebuilds nothing");

        logger.AddEntry("Fresh entry");
        view.Revision.Should().NotBe(revision);
        view.Entries.First().Message.Should().Be("Fresh entry");
        view.PageEntries.Should().NotBeSameAs(page);
    }

    [Fact]
    public void ReadingASteadyView_DoesNotAllocate()
    {
        var view = new HistoryLogView(CreateSeededLogger()) { ItemsPerPage = 2 };

        void ReadAll()
        {
            _ = view.Entries.Count;
            _ = view.PageEntries.Count;
            _ = view.PageCount;
            _ = view.PageStartIndex;
            _ = view.TotalCount;
            _ = view.Categories.Count;
            _ = view.CountOf(HistoryLogLevel.Info);
            _ = view.Revision;
            _ = view.IsLevelSelected(HistoryLogLevel.Info);
        }

        ReadAll();
        ReadAll();

        var before = GC.GetAllocatedBytesForCurrentThread();
        ReadAll();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0, "a window reads the view every frame");
    }

    [Fact]
    public void FormatEntry_WritesOneLine_WithADashForAMissingSource()
    {
        var entry = Entry(7, HistoryLogLevel.Warning, "Swap", "Something happened");

        NoireHistoryLogger.FormatEntry(entry).Should().Be("2024-01-01 00:07:00 | Warning | Swap | Something happened | -");
        NoireHistoryLogger.FormatEntry(entry with { Source = "Matcher" }).Should().EndWith("| Matcher");
        NoireHistoryLogger.FormatEntries(new[] { entry, entry }).Split(Environment.NewLine).Should().HaveCount(2);
    }

    [Fact]
    public void CanUserDeleteEntries_FollowsThePermissionOfTheActiveStore()
    {
        var logger = CreateLogger();

        logger.SetAllowUserClearInMemory(true).SetAllowUserClearDatabase(false);
        logger.CanUserDeleteEntries.Should().BeTrue("in-memory entries are shown while not persisting");

        logger.SetAllowUserClearInMemory(false);
        logger.CanUserDeleteEntries.Should().BeFalse();
    }

    [Fact]
    public void WindowMethods_TargetTheCustomWindow_WhileOneIsSet()
    {
        var logger = CreateLogger();
        var window = new PluginWindow();

        logger.SetCustomWindow(window).Should().BeSameAs(logger);
        logger.ShowWindow();
        window.IsOpen.Should().BeTrue();
        logger.IsWindowOpen.Should().BeTrue();

        logger.SetCustomWindow(null);
        window.IsOpen.Should().BeFalse();
        logger.IsWindowOpen.Should().BeTrue("the built-in window takes the open state back");
        logger.HideWindow();
        logger.IsWindowOpen.Should().BeFalse();
    }
}
