using FluentAssertions;
using NoireLib.Database;
using NoireLib.HistoryLogger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireHistoryLogger module, locking two contracts of its entry lists.
/// <br/><br/>
/// Ordering: entries are held oldest first, appends go to the tail, and trimming discards the oldest from the front.
/// <br/><br/>
/// The add/remove round trip: the entry AddEntry hands back is the one that was stored, and passing it to RemoveEntry
/// removes it from memory and deletes the database row behind it.
/// <br/><br/>
/// The module owns a window, so it is built against the window system
/// <see cref="WindowedModuleCollection.EnsureWindowSystem"/> installs. Its persistence resolves a SQLite file under
/// the plugin's configuration directory, which does not exist outside the game, so the database-backed tests point
/// the database at a temporary directory through <see cref="NoireDatabase.SetDatabaseDirectoryOverride"/> and drive
/// the real load path from there.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WindowedModuleCollection.Name)]
public class NoireHistoryLoggerTests : IDisposable
{
    #region Helpers

    private readonly string tempDirectory;
    private readonly List<string> databasesToClean = new();
    private readonly List<NoireHistoryLogger> loggersToClean = new();

    public NoireHistoryLoggerTests()
    {
        WindowedModuleCollection.EnsureWindowSystem();

        tempDirectory = Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        foreach (var logger in loggersToClean)
        {
            try
            {
                logger.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        // The SQLite connections are cached per database name and hold the files open, so they are released before
        // the directory holding them is removed.
        NoireDatabase.DisposeAll();

        foreach (var databaseName in databasesToClean)
            NoireDatabase.RemoveDatabaseDirectoryOverride(databaseName);

        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp directory left behind is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Builds an entry stamped at a distinct minute so ordering assertions read unambiguously.
    /// </summary>
    private static HistoryLogEntry Entry(int minute) => new()
    {
        Id = minute,
        Timestamp = new DateTime(2024, 1, 1, 0, minute, 0, DateTimeKind.Utc),
        Category = "General",
        Level = HistoryLogLevel.Info,
        Message = $"Entry {minute}"
    };

    /// <summary>
    /// Creates a logger persisting to a database of its own, held in this test's temporary directory so that the
    /// real SQLite path runs without the plugin configuration directory the game would provide.
    /// </summary>
    /// <returns>The logger, already registered for cleanup.</returns>
    private NoireHistoryLogger CreatePersistingLogger()
    {
        var databaseName = $"NoireLibTests_{Guid.NewGuid():N}";
        NoireDatabase.SetDatabaseDirectoryOverride(databaseName, tempDirectory);
        databasesToClean.Add(databaseName);

        var logger = new NoireHistoryLogger(
            active: false,
            enableLogging: false,
            persistLogs: true,
            databaseName: databaseName);

        loggersToClean.Add(logger);
        return logger;
    }

    /// <summary>
    /// Creates a logger holding its entries in memory only, which is the module's default.
    /// </summary>
    /// <returns>The logger, already registered for cleanup.</returns>
    private NoireHistoryLogger CreateRuntimeLogger()
    {
        var logger = new NoireHistoryLogger(active: false, enableLogging: false);
        loggersToClean.Add(logger);
        return logger;
    }

    private static string FindHistoryLoggerSourceFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NoireLib", "Modules", "HistoryLogger", "NoireHistoryLogger.cs");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate NoireHistoryLogger.cs from the test output path.");
    }

    #endregion

    #region Trimming

    [Fact]
    public void TrimEntries_ShouldDiscardTheOldestEntries_WhenOverTheLimit()
    {
        var entries = new List<HistoryLogEntry> { Entry(1), Entry(2), Entry(3), Entry(4) };

        NoireHistoryLogger.TrimEntries(entries, 2);

        entries.Should().HaveCount(2);
        entries.Select(entry => entry.Message).Should().Equal(new[] { "Entry 3", "Entry 4" },
            "entry lists are ordered oldest first, so trimming from the front must discard the oldest entries and keep the newest");
        entries.Should().BeInAscendingOrder(entry => entry.Timestamp);
    }

    [Fact]
    public void TrimEntries_ShouldLeaveTheListUntouched_WhenWithinTheLimit()
    {
        var entries = new List<HistoryLogEntry> { Entry(1), Entry(2) };

        NoireHistoryLogger.TrimEntries(entries, 2);

        entries.Select(entry => entry.Message).Should().Equal("Entry 1", "Entry 2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TrimEntries_ShouldLeaveTheListUntouched_WhenTheLimitIsNotPositive(int maxEntries)
    {
        var entries = new List<HistoryLogEntry> { Entry(1), Entry(2), Entry(3) };

        NoireHistoryLogger.TrimEntries(entries, maxEntries);

        entries.Should().HaveCount(3, "a non-positive limit means no cap rather than an empty list");
    }

    #endregion

    #region Ordering

    [Fact]
    public void AppendThenTrim_ShouldKeepEntriesOldestFirst_AndDropOnlyTheOldest()
    {
        // Mirrors how AddEntry maintains a list: append the newest at the tail, then trim.
        var entries = new List<HistoryLogEntry>();

        for (var minute = 1; minute <= 5; minute++)
        {
            entries.Add(Entry(minute));
            NoireHistoryLogger.TrimEntries(entries, 3);
        }

        entries.Select(entry => entry.Message).Should().Equal(new[] { "Entry 3", "Entry 4", "Entry 5" },
            "repeated appends past the limit must retain the most recent entries");
        entries.Should().BeInAscendingOrder(entry => entry.Timestamp,
            "the list must stay coherently ordered oldest first across appends");
    }

    [Fact]
    public void LoadedThenAppended_ShouldStayOldestFirst_AcrossBothPaths()
    {
        // The load path fills the list ascending by id; the append path adds newer entries after it. Both must
        // agree on the ordering or the trimmed result interleaves incoherently.
        var loaded = new[] { Entry(1), Entry(2), Entry(3) }.OrderBy(entry => entry.Id).ToList();

        loaded.Add(Entry(4));
        NoireHistoryLogger.TrimEntries(loaded, 3);

        loaded.Select(entry => entry.Message).Should().Equal("Entry 2", "Entry 3", "Entry 4");
        loaded.Should().BeInAscendingOrder(entry => entry.Timestamp);
    }

    /// <summary>
    /// Names the ordering the database-backed tests below prove behaviorally, so that a change to the query reads as
    /// a deliberate one rather than being noticed only through the entries a load happens to keep.
    /// </summary>
    [Fact]
    public void LoadEntriesFromDatabase_ShouldQueryEntriesOldestFirst()
    {
        var source = File.ReadAllText(FindHistoryLoggerSourceFile());

        source.Should().Contain("OrderByAsc(\"id\")",
            "the load path must order ascending so index 0 holds the oldest entry, matching the append path and front-trimming");
        source.Should().NotContain("OrderByDesc(\"id\")",
            "loading newest first would make TrimEntries discard the newest rows and leave the list incoherently ordered");
    }

    #endregion

    #region Database-backed loading

    [Fact]
    public void LoadEntriesFromDatabase_ShouldFillTheListOldestFirst()
    {
        var logger = CreatePersistingLogger();

        for (var minute = 1; minute <= 4; minute++)
            logger.AddEntry($"Entry {minute}");

        logger.LoadEntriesFromDatabase(true);

        logger.GetDatabaseEntriesSnapshot().Select(entry => entry.Message)
            .Should().Equal(new[] { "Entry 1", "Entry 2", "Entry 3", "Entry 4" },
                "the load path must reproduce the order the entries were written in, oldest at index 0");
        logger.GetDatabaseEntriesSnapshot().Should().BeInAscendingOrder(entry => entry.Id);
    }

    [Fact]
    public void LoadEntriesFromDatabase_ShouldKeepTheNewestEntries_WhenOverTheInMemoryLimit()
    {
        // The regression gate, driven end to end: the load path used to query newest first, which left the newest
        // rows at the front of the list and made the front-trimming below discard exactly the entries a user opens
        // the window to see.
        var logger = CreatePersistingLogger();

        for (var minute = 1; minute <= 5; minute++)
            logger.AddEntry($"Entry {minute}");

        logger.MaxInMemoryEntries = 2;
        logger.LoadEntriesFromDatabase(true);

        logger.GetDatabaseEntriesSnapshot().Select(entry => entry.Message)
            .Should().Equal(new[] { "Entry 4", "Entry 5" },
                "a load that overflows the in-memory limit must retain the most recent rows");
    }

    [Fact]
    public void LoadEntriesFromDatabase_ShouldReplaceOrAppend_AsAsked()
    {
        var logger = CreatePersistingLogger();
        logger.AddEntry("Entry 1");

        logger.LoadEntriesFromDatabase(false);

        logger.GetDatabaseEntriesSnapshot().Select(entry => entry.Message)
            .Should().Equal(new[] { "Entry 1", "Entry 1" },
                "an append-mode load adds the stored rows to the entries already held");

        logger.LoadEntriesFromDatabase(true);

        logger.GetDatabaseEntriesSnapshot().Select(entry => entry.Message)
            .Should().Equal(new[] { "Entry 1" }, "a replacing load drops what was held before it");
    }

    [Fact]
    public void AddEntry_ShouldPersistTheEntry_WithAscendingIdsAndItsFieldsIntact()
    {
        var logger = CreatePersistingLogger();

        logger.AddEntry("Entry 1", category: "Persisted", level: HistoryLogLevel.Warning, source: "Tests");
        logger.AddEntry("Entry 2");

        // Reloading discards what is held in memory, so what is asserted below came back out of the database.
        logger.LoadEntriesFromDatabase(true);

        var reloaded = logger.GetDatabaseEntriesSnapshot();
        reloaded.Should().HaveCount(2);
        reloaded.Should().BeInAscendingOrder(entry => entry.Id,
            "ids grow with insertion order, which is what the load path orders by");

        reloaded[0].Message.Should().Be("Entry 1");
        reloaded[0].Category.Should().Be("Persisted");
        reloaded[0].Level.Should().Be(HistoryLogLevel.Warning);
        reloaded[0].Source.Should().Be("Tests");
    }

    #endregion

    #region The add/remove round trip

    /// <summary>
    /// The regression gate for the contract a consumer reaches for first: what AddEntry hands back is what RemoveEntry
    /// takes. AddEntry used to return the entry as it was handed in, stamping the database id onto a separate copy it
    /// kept, which left the caller holding a value that removed neither the row nor the entry in memory.
    /// </summary>
    [Fact]
    public void RemoveEntry_ShouldRemoveTheEntryAddEntryReturned_AndItsRow_WhenPersisting()
    {
        var logger = CreatePersistingLogger();
        logger.AddEntry("Keep me");
        var stored = logger.AddEntry("Remove me");

        stored.Id.Should().NotBeNull("a persisted entry must be handed back carrying the id of the row it was written to");

        logger.RemoveEntry(stored).Should().BeTrue();

        logger.GetDatabaseEntriesSnapshot().Select(entry => entry.Message).Should().Equal("Keep me");
        logger.GetRuntimeEntriesSnapshot().Select(entry => entry.Message).Should().Equal("Keep me");

        var rowsLeft = logger.ExecuteDatabaseQuery(builder => builder.Where("id", stored.Id!.Value).Count());
        rowsLeft.Should().Be(0, "removing an entry the module handed back must delete the row standing behind it");
    }

    [Fact]
    public void RemoveEntry_ShouldRemoveTheEntryAddEntryReturned_WhenNotPersisting()
    {
        var logger = CreateRuntimeLogger();
        logger.AddEntry("Keep me");
        var stored = logger.AddEntry("Remove me");

        stored.Id.Should().BeNull("a runtime-only entry is never written to a database, so nothing assigns it an id");

        logger.RemoveEntry(stored).Should().BeTrue("an entry without an id must still be removable by value");

        logger.GetRuntimeEntriesSnapshot().Select(entry => entry.Message).Should().Equal("Keep me");
        logger.GetEntriesSnapshot().Select(entry => entry.Message).Should().Equal("Keep me");
    }

    [Fact]
    public void AddEntry_ShouldReturnTheStoredEntry_RatherThanTheOneHandedIn()
    {
        var logger = CreatePersistingLogger();

        var handedIn = new HistoryLogEntry { Message = "Entry", Category = "   " };
        var stored = logger.AddEntry(handedIn);

        stored.Id.Should().NotBeNull();
        stored.Category.Should().Be("General", "a blank category is normalized on the way in");
        handedIn.Id.Should().BeNull("the entry handed in is left untouched");

        logger.GetDatabaseEntriesSnapshot().Should().ContainSingle().Which.Should().Be(stored,
            "the value returned must be the one held, or a caller cannot act on what it just added");

        logger.RemoveEntry(handedIn).Should().BeFalse(
            "an entry the module never stored carries neither the normalization nor the id of the one that was");
        logger.RemoveEntry(stored).Should().BeTrue();
    }

    /// <summary>
    /// A reload refills the entry list from the database, so the copies it holds are not the instances AddEntry
    /// returned. The id is what still ties the two together.
    /// </summary>
    [Fact]
    public void RemoveEntry_ShouldRemoveTheEntryAddEntryReturned_AfterAReloadReplacedIt()
    {
        var logger = CreatePersistingLogger();
        logger.AddEntry("Entry 1");
        var stored = logger.AddEntry("Entry 2");

        logger.LoadEntriesFromDatabase(true);

        logger.RemoveEntry(stored).Should().BeTrue(
            "the entry AddEntry returned must keep identifying its row once the list holds reloaded copies");
        logger.GetDatabaseEntriesSnapshot().Select(entry => entry.Message).Should().Equal("Entry 1");

        var rowsLeft = logger.ExecuteDatabaseQuery(builder => builder.Where("id", stored.Id!.Value).Count());
        rowsLeft.Should().Be(0);
    }

    [Fact]
    public void RemoveEntry_ShouldKeepTheRow_WhenClearingTheDatabaseIsNotAllowed()
    {
        var logger = CreatePersistingLogger();
        logger.SetAllowUserClearDatabase(false);

        var stored = logger.AddEntry("Remove me");

        logger.RemoveEntry(stored).Should().BeTrue("the entry is still removed from the runtime list");

        logger.GetDatabaseEntriesSnapshot().Select(entry => entry.Message).Should().Equal("Remove me",
            "the database-backed list is left alone while clearing the database is refused");

        var rowsLeft = logger.ExecuteDatabaseQuery(builder => builder.Where("id", stored.Id!.Value).Count());
        rowsLeft.Should().Be(1, "a refused permission must leave the row in place");
    }

    [Fact]
    public void RemoveEntry_ShouldRemoveNothing_WhenBothClearPermissionsAreRefused()
    {
        var logger = CreateRuntimeLogger();
        logger.SetAllowUserClearInMemory(false);
        logger.SetAllowUserClearDatabase(false);

        var stored = logger.AddEntry("Keep me");

        logger.RemoveEntry(stored).Should().BeFalse();
        logger.GetRuntimeEntriesSnapshot().Select(entry => entry.Message).Should().Equal("Keep me");
    }

    [Fact]
    public void RemoveEntry_ShouldThrow_WhenTheEntryIsNull()
    {
        var logger = CreateRuntimeLogger();

        var act = () => logger.RemoveEntry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveEntry_ShouldBumpTheEntriesVersion_OnlyWhenSomethingWasRemoved()
    {
        var logger = CreateRuntimeLogger();
        var stored = logger.AddEntry("Remove me");

        var versionBeforeRemoval = logger.EntriesVersion;

        logger.RemoveEntry(stored).Should().BeTrue();
        logger.EntriesVersion.Should().NotBe(versionBeforeRemoval,
            "a reader caching a view keyed on this version has to be told the entries changed");

        var versionAfterRemoval = logger.EntriesVersion;

        logger.RemoveEntry(stored).Should().BeFalse("the entry is already gone");
        logger.EntriesVersion.Should().Be(versionAfterRemoval, "a removal that found nothing changes no view");
    }

    #endregion

    #region Rejected arguments

    [Fact]
    public void AddEntry_ShouldThrow_WhenTheEntryIsNull()
    {
        var logger = CreateRuntimeLogger();

        var act = () => logger.AddEntry((HistoryLogEntry)null!);

        act.Should().Throw<ArgumentNullException>("an entry that cannot be normalized has to be named at the call site "
            + "rather than surface as a null dereference inside the module");
    }

    [Fact]
    public void ExecuteDatabaseQuery_ShouldThrow_WhenTheActionIsNull()
    {
        var logger = CreatePersistingLogger();

        var act = () => logger.ExecuteDatabaseQuery((Action<QueryBuilder<HistoryLogEntryModel>>)null!);
        var actWithResult = () => logger.ExecuteDatabaseQuery((Func<QueryBuilder<HistoryLogEntryModel>, int>)null!);

        act.Should().Throw<ArgumentNullException>();
        actWithResult.Should().Throw<ArgumentNullException>("the query builder opens the database and creates the "
            + "table before it reaches the action, so a missing action has to be refused before any of that");
    }

    #endregion

    #region Window state

    /// <summary>
    /// Every module owning a window offers the same read of whether it is open, so the member belongs to the windowed
    /// module base. A copy declared on this module would shadow it, compile, and leave a reader unable to tell which of
    /// the two any given call reaches.
    /// </summary>
    [Fact]
    public void IsWindowOpen_ShouldBeTheBaseMember_RatherThanACopyOnTheModule()
    {
        var declaredOnTheModule = typeof(NoireHistoryLogger).GetProperty(
            nameof(NoireHistoryLogger.IsWindowOpen),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        declaredOnTheModule.Should().BeNull("the module must inherit the member rather than shadow it");

        var inherited = typeof(NoireHistoryLogger).GetProperty(nameof(NoireHistoryLogger.IsWindowOpen));

        inherited.Should().NotBeNull();
        inherited!.DeclaringType!.Name.Should().StartWith("NoireModuleWithWindowBase",
            "the read is promoted to the base so that every windowed module carries it");
    }

    [Fact]
    public void IsWindowOpen_ShouldReflectTheWindowState()
    {
        var logger = CreateRuntimeLogger();

        logger.HasWindow.Should().BeTrue("the module registers its window while initializing");
        logger.IsWindowOpen.Should().BeFalse("a module's window starts closed");

        logger.ShowWindow();
        logger.IsWindowOpen.Should().BeTrue();

        logger.HideWindow();
        logger.IsWindowOpen.Should().BeFalse();

        logger.ToggleWindow();
        logger.IsWindowOpen.Should().BeTrue("a toggle from closed opens the window");
    }

    #endregion
}
