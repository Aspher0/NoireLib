using NoireLib.Core.Modules;
using NoireLib.Database;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.HistoryLogger;

/// <summary>
/// A module for logging historical events with optional database persistence and a built-in UI for viewing and managing logs.
/// </summary>
public class NoireHistoryLogger : NoireModuleWithWindowBase<NoireHistoryLogger, HistoryLoggerWindow, HistoryLoggerConfigInstance>
{
    internal const string DefaultDatabaseName = "NoireHistoryLogger";

    private readonly List<HistoryLogEntry> runtimeEntries = new();
    private readonly List<HistoryLogEntry> databaseEntries = new();
    private readonly Dictionary<Type, string?> autoLogTypes = new();
    private readonly object entryLock = new();

    private bool persistLogs;
    private string databaseName = DefaultDatabaseName;
    private int entriesVersion;

    // UI Control flags
    private bool allowUserTogglePersistence = false;
    private bool allowUserClearInMemory = true;
    private bool allowUserClearDatabase = true;
    private bool allowManualEntryCreation = false;

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireHistoryLogger() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireHistoryLogger"/> module.
    /// </summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="persistLogs">Whether to persist logs to a database.</param>
    /// <param name="databaseName">Optional database name override.</param>
    /// <param name="allowUserTogglePersistence">Whether the user can toggle database persistence in the UI.</param>
    /// <param name="allowUserClearInMemory">Whether the user can clear in-memory entries in the UI.</param>
    /// <param name="allowUserClearDatabase">Whether the user can clear database entries in the UI.</param>
    /// <param name="allowManualEntryCreation">Whether the user can create manual entries in the UI.</param>
    public NoireHistoryLogger(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        bool persistLogs = false,
        string? databaseName = null,
        bool allowUserTogglePersistence = false,
        bool allowUserClearInMemory = true,
        bool allowUserClearDatabase = true,
        bool allowManualEntryCreation = false)
            : base(moduleId, active, enableLogging, persistLogs, databaseName, allowUserTogglePersistence, allowUserClearInMemory, allowUserClearDatabase, allowManualEntryCreation) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireHistoryLogger(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    /// <summary>
    /// Gets whether the module persists logs to a database.
    /// </summary>
    public bool PersistLogs => persistLogs;

    /// <summary>
    /// Gets the database name used for persistent logs.
    /// </summary>
    public string DatabaseName => databaseName;

    /// <summary>
    /// Gets or sets the maximum number of entries to keep in memory.
    /// </summary>
    public int MaxInMemoryEntries { get; set; } = 2000;

    /// <summary>
    /// Gets or sets whether the user can toggle database persistence in the UI.
    /// </summary>
    public bool AllowUserTogglePersistence
    {
        get => allowUserTogglePersistence;
        set => allowUserTogglePersistence = value;
    }

    /// <summary>
    /// Gets or sets whether the user can clear in-memory entries in the UI.
    /// </summary>
    public bool AllowUserClearInMemory
    {
        get => allowUserClearInMemory;
        set => allowUserClearInMemory = value;
    }

    /// <summary>
    /// Gets or sets whether the user can clear database entries in the UI.
    /// </summary>
    public bool AllowUserClearDatabase
    {
        get => allowUserClearDatabase;
        set => allowUserClearDatabase = value;
    }

    /// <summary>
    /// Gets or sets whether the user can create manual entries in the UI.
    /// </summary>
    public bool AllowManualEntryCreation
    {
        get => allowManualEntryCreation;
        set => allowManualEntryCreation = value;
    }

    /// <summary>
    /// A counter bumped whenever the entries returned by <see cref="GetEntriesSnapshot"/> change.<br/>
    /// Lets a reader tell that its cached view is still current without taking a snapshot to compare against.
    /// </summary>
    internal int EntriesVersion
    {
        get
        {
            lock (entryLock)
                return entriesVersion;
        }
    }

    /// <summary>
    /// Gets a snapshot of runtime-only log entries.
    /// </summary>
    public IReadOnlyList<HistoryLogEntry> GetRuntimeEntriesSnapshot()
    {
        lock (entryLock)
            return runtimeEntries.ToList();
    }

    /// <summary>
    /// Gets a snapshot of database-backed log entries.
    /// </summary>
    public IReadOnlyList<HistoryLogEntry> GetDatabaseEntriesSnapshot()
    {
        lock (entryLock)
            return databaseEntries.ToList();
    }

    /// <summary>
    /// Removes a log entry from memory, and from the database when it is persisted.<br/>
    /// Respects the <see cref="AllowUserClearInMemory"/> and <see cref="AllowUserClearDatabase"/> permissions.<br/>
    /// Pass the entry <see cref="AddEntry(HistoryLogEntry)"/> returned, or one read from a snapshot. An entry built by
    /// hand is not the one the module stored: persistence stamps the stored copy with a database id, and normalization
    /// can rewrite its fields, so whether it matches anything is not something to rely on.
    /// </summary>
    /// <param name="entry">The entry to remove.</param>
    /// <returns><see langword="true"/> if an entry was removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is <see langword="null"/>.</exception>
    public bool RemoveEntry(HistoryLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        bool removed;
        bool deleteFromDatabase;

        lock (entryLock)
        {
            removed = allowUserClearInMemory && RemoveMatchingEntry(runtimeEntries, entry);

            if (allowUserClearDatabase)
                removed |= RemoveMatchingEntry(databaseEntries, entry);

            // The permission and the persistence flag are read once, alongside the removal they gate, so that the
            // delete below cannot run against a decision the in-memory removal was not made from.
            deleteFromDatabase = allowUserClearDatabase && persistLogs;

            // Bumping alongside the mutation keeps a reader from observing changed entries under an unchanged version
            // and caching that view for as long as nothing else changes.
            if (removed)
                entriesVersion++;
        }

        // The delete opens the database and blocks for the whole round trip, so it runs outside the lock the window
        // takes to read the entries and their version on every frame it draws. It leaves no in-memory state to update.
        if (deleteFromDatabase && entry.Id is long id)
        {
            var deleted = ExecuteDatabaseQuery(builder => builder.Where("id", id).Delete());
            removed |= deleted > 0;
        }

        return removed;
    }

    /// <summary>
    /// Executes a query against the current history log database.
    /// </summary>
    /// <param name="action">The action to perform with the query builder.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public void ExecuteDatabaseQuery(Action<QueryBuilder<HistoryLogEntryModel>> action)
    {
        // Checked before the call rather than by the query builder, which opens the database and creates the table
        // before it ever reaches the action.
        ArgumentNullException.ThrowIfNull(action);

        HistoryLogEntryModel.ExecuteQuery(databaseName, action);
    }

    /// <summary>
    /// Executes a query against the current history log database and returns a result.
    /// </summary>
    /// <param name="action">The query builder action.</param>
    /// <returns>The action result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public TResult ExecuteDatabaseQuery<TResult>(Func<QueryBuilder<HistoryLogEntryModel>, TResult> action)
    {
        // Checked before the call rather than by the query builder, which opens the database and creates the table
        // before it ever reaches the action.
        ArgumentNullException.ThrowIfNull(action);

        return HistoryLogEntryModel.ExecuteQuery(databaseName, action);
    }

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters.</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is bool shouldPersist)
            persistLogs = shouldPersist;

        if (args.Length > 1 && args[1] is string dbName && !string.IsNullOrWhiteSpace(dbName))
            databaseName = dbName;

        if (args.Length > 2 && args[2] is bool togglePersistence)
            allowUserTogglePersistence = togglePersistence;

        if (args.Length > 3 && args[3] is bool clearInMemory)
            allowUserClearInMemory = clearInMemory;

        if (args.Length > 4 && args[4] is bool clearDatabase)
            allowUserClearDatabase = clearDatabase;

        if (args.Length > 5 && args[5] is bool allowManualEntry)
            allowManualEntryCreation = allowManualEntry;

        RegisterWindow(new HistoryLoggerWindow(this));

        if (persistLogs)
            LoadEntriesFromDatabase(true);

        if (EnableLogging)
            NoireLogger.LogInfo(this, "History Logger initialized.");
    }

    /// <summary>
    /// Called when the module is activated.
    /// </summary>
    protected override void OnActivated()
    {
        if (EnableLogging)
            NoireLogger.LogInfo(this, "History Logger activated.");
    }

    /// <summary>
    /// Called when the module is deactivated.
    /// </summary>
    protected override void OnDeactivated()
    {
        if (ModuleWindow?.IsOpen == true)
            ModuleWindow.IsOpen = false;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "History Logger deactivated.");
    }

    /// <summary>
    /// Disposes the module and associated resources.
    /// </summary>
    protected override void DisposeInternal()
    {
        lock (entryLock)
        {
            runtimeEntries.Clear();
            databaseEntries.Clear();
            autoLogTypes.Clear();
            entriesVersion++;
        }
    }

    /// <summary>
    /// Toggles whether logs are persisted to the database.
    /// </summary>
    /// <param name="persist">Whether to persist logs.</param>
    /// <param name="loadExisting">Whether to load existing database logs into memory.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHistoryLogger SetPersistLogs(bool persist, bool loadExisting = true)
    {
        if (persistLogs == persist)
            return this;

        persistLogs = persist;

        // Toggling persistence swaps which list GetEntriesSnapshot reads from, so the snapshot changes even
        // though no entry was touched.
        lock (entryLock)
            entriesVersion++;

        if (persistLogs && loadExisting)
            LoadEntriesFromDatabase(true);

        return this;
    }

    /// <summary>
    /// Sets whether the user can toggle database persistence in the UI.
    /// </summary>
    /// <param name="allow">Whether to allow the user to toggle persistence.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHistoryLogger SetAllowUserTogglePersistence(bool allow)
    {
        allowUserTogglePersistence = allow;
        return this;
    }

    /// <summary>
    /// Sets whether the user can clear in-memory entries in the UI.
    /// </summary>
    /// <param name="allow">Whether to allow the user to clear in-memory entries.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHistoryLogger SetAllowUserClearInMemory(bool allow)
    {
        allowUserClearInMemory = allow;
        return this;
    }

    /// <summary>
    /// Sets whether the user can clear database entries in the UI.
    /// </summary>
    /// <param name="allow">Whether to allow the user to clear database entries.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHistoryLogger SetAllowUserClearDatabase(bool allow)
    {
        allowUserClearDatabase = allow;
        return this;
    }

    /// <summary>
    /// Sets whether the user can create manual entries in the UI.
    /// </summary>
    /// <param name="allow">Whether to allow manual entry creation.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHistoryLogger SetAllowManualEntryCreation(bool allow)
    {
        allowManualEntryCreation = allow;
        return this;
    }

    /// <summary>
    /// Overrides the database name used for persistent logs.
    /// </summary>
    /// <param name="name">The new database name.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHistoryLogger SetDatabaseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Database name cannot be null or empty.", nameof(name));

        databaseName = name;

        if (persistLogs)
            LoadEntriesFromDatabase(true);

        return this;
    }

    /// <summary>
    /// Adds a new log entry.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="category">Optional category.</param>
    /// <param name="level">Optional severity level.</param>
    /// <param name="source">Optional source name.</param>
    /// <returns>The entry as it is stored, which is what <see cref="RemoveEntry"/> accepts.</returns>
    public HistoryLogEntry AddEntry(string message, string? category = null, HistoryLogLevel level = HistoryLogLevel.Info, string? source = null)
    {
        var entry = new HistoryLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category,
            Level = level,
            Message = message,
            Source = source
        };

        return AddEntry(entry);
    }

    /// <summary>
    /// Adds a log entry and persists it if configured.
    /// </summary>
    /// <param name="entry">The entry to add.</param>
    /// <returns>
    /// The entry as it is stored: normalized, and carrying the database <see cref="HistoryLogEntry.Id"/> persistence
    /// assigned it. It differs from <paramref name="entry"/>, which stays untouched, so keep this value rather than the
    /// one handed in for a later <see cref="RemoveEntry"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is <see langword="null"/>.</exception>
    public HistoryLogEntry AddEntry(HistoryLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var normalized = NormalizeEntry(entry);

        if (persistLogs)
        {
            var model = HistoryLogEntryModel.Create(databaseName);
            model.Timestamp = normalized.Timestamp;
            model.Category = normalized.Category;
            model.Level = normalized.Level.ToString();
            model.Message = normalized.Message;
            model.Source = normalized.Source;

            if (model.Save())
                normalized = normalized with { Id = model.Id };
        }

        lock (entryLock)
        {
            runtimeEntries.Add(normalized);
            TrimEntries(runtimeEntries, MaxInMemoryEntries);

            if (persistLogs)
            {
                databaseEntries.Add(normalized);
                TrimEntries(databaseEntries, MaxInMemoryEntries);
            }

            entriesVersion++;
        }

        return normalized;
    }

    /// <summary>
    /// Clears stored log entries.
    /// </summary>
    public void ClearEntries()
    {
        lock (entryLock)
        {
            runtimeEntries.Clear();
            entriesVersion++;
        }
    }

    /// <summary>
    /// Clears stored database log entries.
    /// </summary>
    public void ClearDatabaseEntries()
    {
        lock (entryLock)
        {
            databaseEntries.Clear();
            entriesVersion++;
        }

        if (persistLogs)
            HistoryLogEntryModel.ExecuteQuery(databaseName, builder => builder.Delete());
    }

    /// <summary>
    /// Retrieves a snapshot of current log entries.
    /// </summary>
    public IReadOnlyList<HistoryLogEntry> GetEntriesSnapshot()
    {
        lock (entryLock)
            return persistLogs ? databaseEntries.ToList() : runtimeEntries.ToList();
    }

    /// <summary>
    /// Retrieves the list of distinct categories currently stored.
    /// </summary>
    public IReadOnlyList<string> GetCategories()
    {
        lock (entryLock)
        {
            var source = persistLogs ? databaseEntries : runtimeEntries;
            return source.Select(entry => entry.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(category => category).ToList();
        }
    }

    /// <summary>
    /// Reloads entries from the database into memory.<br/>
    /// Entries are loaded oldest first, matching the order in which <see cref="AddEntry(HistoryLogEntry)"/> appends them.
    /// </summary>
    /// <param name="replaceExisting">Whether to replace existing entries.</param>
    public void LoadEntriesFromDatabase(bool replaceExisting)
    {
        // Ascending by id keeps the oldest entry at index 0, which is what both the append path and the
        // front-trimming in TrimEntries assume. Loading newest first would make trimming drop the newest rows.
        var models = HistoryLogEntryModel.ExecuteQuery(databaseName, builder => builder.OrderByAsc("id").Get());
        var loadedEntries = models.Select(ToEntryFromModel).Where(entry => entry != null).Cast<HistoryLogEntry>().ToList();

        lock (entryLock)
        {
            if (replaceExisting)
                databaseEntries.Clear();

            databaseEntries.AddRange(loadedEntries);
            TrimEntries(databaseEntries, MaxInMemoryEntries);
            entriesVersion++;
        }
    }

    /// <summary>
    /// Registers a type for automatic logging of all methods.
    /// </summary>
    public void RegisterTypeForAutoLogging<T>(string? category = null) where T : class
    {
        lock (entryLock)
            autoLogTypes[typeof(T)] = category;
    }

    /// <summary>
    /// Clears automatic logging registrations.
    /// </summary>
    public void ClearAutoLoggingRegistrations()
    {
        lock (entryLock)
            autoLogTypes.Clear();
    }

    /// <summary>
    /// Creates a proxy around an instance to log method calls.
    /// </summary>
    public T CreateLoggedProxy<T>(T instance, bool? logAllMethods = null, string? category = null) where T : class
    {
        var (logAll, resolvedCategory) = ResolveProxySettings(typeof(T), logAllMethods, category);
        return NoireHistoryLogProxy.Create(instance, this, logAll, resolvedCategory);
    }

    /// <summary>
    /// Creates a proxy around a new instance to log method calls.
    /// </summary>
    public T CreateLoggedProxy<T>(bool? logAllMethods = null, string? category = null) where T : class, new()
    {
        var (logAll, resolvedCategory) = ResolveProxySettings(typeof(T), logAllMethods, category);
        return NoireHistoryLogProxy.Create<T>(this, logAll, resolvedCategory);
    }

    private (bool LogAll, string? Category) ResolveProxySettings(Type type, bool? logAllMethods, string? category)
    {
        if (logAllMethods.HasValue)
            return (logAllMethods.Value, category);

        lock (entryLock)
        {
            if (autoLogTypes.TryGetValue(type, out var defaultCategory))
                return (true, category ?? defaultCategory);
        }

        return (false, category);
    }

    /// <summary>
    /// Removes the entry matching <paramref name="entry"/> from <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The entry list to remove from.</param>
    /// <param name="entry">The entry to look for.</param>
    /// <returns><see langword="true"/> if an entry was removed.</returns>
    private static bool RemoveMatchingEntry(List<HistoryLogEntry> target, HistoryLogEntry entry)
    {
        // A persisted entry is matched on its database id rather than by value. The id names the row exactly, whereas
        // value equality of the record cannot be relied on across storage: a timestamp written to the database and read
        // back goes through text and is reinterpreted on the way in, so a reloaded entry is not dependably equal to the
        // one that was written, and a list refilled by a reload would not yield to a Remove by value.
        if (entry.Id is not long id)
            return target.Remove(entry);

        var index = target.FindIndex(candidate => candidate.Id == id);
        if (index < 0)
            return false;

        target.RemoveAt(index);
        return true;
    }

    private static HistoryLogEntry NormalizeEntry(HistoryLogEntry entry)
    {
        var category = string.IsNullOrWhiteSpace(entry.Category) ? "General" : entry.Category;
        var message = entry.Message ?? string.Empty;
        return entry with { Category = category, Message = message };
    }

    /// <summary>
    /// Drops the oldest entries from <paramref name="target"/> until it holds at most <paramref name="maxEntries"/> of them.<br/>
    /// Entry lists are ordered oldest first, so the excess is removed from the front.
    /// </summary>
    /// <param name="target">The entry list to trim in place.</param>
    /// <param name="maxEntries">The maximum number of entries to retain. Zero or less leaves the list untouched.</param>
    internal static void TrimEntries(List<HistoryLogEntry> target, int maxEntries)
    {
        if (maxEntries <= 0)
            return;

        if (target.Count <= maxEntries)
            return;

        var excess = target.Count - maxEntries;
        target.RemoveRange(0, excess);
    }

    private static HistoryLogEntry? ToEntryFromModel(HistoryLogEntryModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Message))
            return null;

        if (!Enum.TryParse(model.Level, out HistoryLogLevel level))
            level = HistoryLogLevel.Info;

        return new HistoryLogEntry
        {
            Id = model.Id,
            Timestamp = model.Timestamp,
            Category = string.IsNullOrWhiteSpace(model.Category) ? "General" : model.Category,
            Level = level,
            Message = model.Message,
            Source = model.Source
        };
    }
}
