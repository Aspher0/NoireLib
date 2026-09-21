
# Module Documentation : NoireHistoryLogger

You are reading the documentation for the `NoireHistoryLogger` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Creating and Adding Logs](#creating-and-adding-logs)
- [Log Levels](#log-levels)
- [Displaying the History Logger Window](#displaying-the-history-logger-window)
- [Using Your Own Window](#using-your-own-window)
- [Database Persistence](#database-persistence)
- [Auto-Logging with Proxies](#auto-logging-with-proxies)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`NoireHistoryLogger` logs entries in memory, optionally to the database, with a window to view and manage them. It provides:
- **In-memory and database storage** for log entries
- **Built-in UI window** for viewing and managing logs
- **Your own window** in place of the built-in one, fed by a cached `HistoryLogView`
- **Multiple log levels** (Trace, Debug, Info, Warning, Error, Critical)
- **Category-based organization** for filtering and grouping logs
- **Auto-logging** with dynamic proxies
- **User permissions** for UI control
- **Advanced query support** for database operations

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Add Your First Log Entry

Log events in your plugin:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

logger?.AddEntry("Plugin initialized successfully");
logger?.AddEntry("Configuration loaded", category: "Config");

logger?.AddEntry(
    message: "Failed to connect to server",
    category: "Network",
    level: HistoryLogLevel.Error,
    source: "ConnectionManager"
);
```

### 2. View Your Logs

Open the history logger window:

```csharp
logger?.ShowWindow();
```

---

## Configuration

### Module Parameters

Configure the module using the constructor:

```csharp
var historyLogger = new NoireHistoryLogger(
    moduleId: "MyLogger",
    active: true,
    enableLogging: true,
    persistLogs: false,
    databaseName: "MyPluginLogs",
    allowUserTogglePersistence: false,
    allowUserClearInMemory: true,
    allowUserClearDatabase: true,
    allowManualEntryCreation: false
);
```

### Property Configuration

Or configure it after creation:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

logger?.SetPersistLogs(persist: true, loadExisting: true);
logger?.SetDatabaseName("CustomDatabase");

logger?.SetAllowUserTogglePersistence(true)
       ?.SetAllowUserClearInMemory(true)
       ?.SetAllowUserClearDatabase(false)
       ?.SetAllowManualEntryCreation(true);

// Default: 2000
if (logger != null)
    logger.MaxInMemoryEntries = 5000;
```

### User Permission Flags

What users can do in the window:

- `AllowUserTogglePersistence`: Allow toggling database persistence on/off
- `AllowUserClearInMemory`: Allow clearing in-memory (runtime) logs
- `AllowUserClearDatabase`: Allow clearing database logs
- `AllowManualEntryCreation`: Allow creating manual entries in the window

```csharp
logger?.SetAllowUserTogglePersistence(false)
       ?.SetAllowUserClearInMemory(false)
       ?.SetAllowUserClearDatabase(false)
       ?.SetAllowManualEntryCreation(false);
```

---

## Creating and Adding Logs

### Basic Logging

Add log entries with varying levels of detail:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

logger?.AddEntry("Something happened");
logger?.AddEntry("User logged in", category: "Authentication");
logger?.AddEntry("Low memory warning", level: HistoryLogLevel.Warning);

logger?.AddEntry(
    message: "Database query failed: Timeout exceeded",
    category: "Database",
    level: HistoryLogLevel.Error,
    source: "UserRepository.GetById"
);
```

### Creating Log Entries Manually

For more control, create `HistoryLogEntry` objects:

```csharp
var entry = new HistoryLogEntry
{
    Timestamp = DateTime.UtcNow,
    Category = "Custom",
    Level = HistoryLogLevel.Info,
    Message = "Custom log message",
    Source = "MyClass.MyMethod"
};

logger?.AddEntry(entry);
```

### Entry Retention

Control how many entries are kept in memory:

```csharp
// Set maximum in-memory entries (default: 2000)
if (logger != null)
    logger.MaxInMemoryEntries = 1000;

// Set to 0 or negative to disable trimming
if (logger != null)
    logger.MaxInMemoryEntries = -1;
```

Past the limit, the oldest entries are removed from memory.

---

## Log Levels

The module supports six severity levels:

```csharp
public enum HistoryLogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}
```

---

## Displaying the History Logger Window

### Manual Display

Show or hide the history logger window:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

logger?.ShowWindow();
logger?.HideWindow();
logger?.ToggleWindow();

// SetShowWindow(null) toggles.
logger?.SetShowWindow(true);

var isOpen = logger?.IsWindowOpen ?? false;
```

### Window Features

The built-in window provides:
- **Filtering** by category and log level
- **Search**
- **Sorting** (newest or oldest first, or alphabetical)
- **Color-coded log levels**
- **Entry details**: timestamp, source and message
- **Clear actions**, following the user permissions
- **Persistence toggle**, if enabled

---

## Using Your Own Window

Register any Dalamud `Window` in place of the built-in one. `ShowWindow()`, `HideWindow()`, `ToggleWindow()`, `SetShowWindow()` and `IsWindowOpen` then act on it. Add it to your own `WindowSystem`.

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

logger?.SetCustomWindow(myLogsWindow);   // ShowWindow() now opens myLogsWindow
logger?.SetCustomWindow(null);           // back to the built-in window
var custom = logger?.CustomWindow;
```

Switching while a window is open closes it and opens the other.

### HistoryLogView

The search, filters, sorting and paging of the built-in window. Results are cached and rebuilt only when the entries or a setting change. Reading them every frame does not allocate.

```csharp
var view = new HistoryLogView(logger);

view.SearchText = "penumbra";                          // message, category and source, case-insensitive
view.ToggleLevel(HistoryLogLevel.Warning);             // empty level set = every level
view.SetCategorySelected("Swap", true);                // empty category set = every category
view.ClearLevelFilter();
view.ClearCategoryFilter();
view.SortColumn = HistoryLogSortColumn.Time;           // Time, Level, Category, Source
view.SortDescending = true;                            // newest first (default)
view.ItemsPerPage = 50;                                // back to page 1
view.Page = view.PageCount;                            // 1-based, clamped
```

Changing the search, a filter or the page size returns to page 1. Sorting keeps the page.

```csharp
IReadOnlyList<HistoryLogEntry> page = view.PageEntries;   // current page, in sort order
IReadOnlyList<HistoryLogEntry> all = view.Entries;        // every matching entry
int first = view.PageStartIndex;                          // index in Entries of page[0]
int total = view.TotalCount;                              // before any filter
int warnings = view.CountOf(HistoryLogLevel.Warning);     // before any filter
IReadOnlyList<string> categories = view.Categories;       // distinct, sorted
int revision = view.Revision;                             // changes on every rebuild: rebuild cached text when it does
```

### Operations

Everything the built-in window does is public on the module:

```csharp
logger.AddEntry("What happened", "General", HistoryLogLevel.Info, "Manual");   // manual entry
if (logger.CanUserDeleteEntries)
    logger.RemoveEntry(entry);                     // delete one entry
logger.ClearEntries();                             // clear in-memory (gate on AllowUserClearInMemory)
logger.ClearDatabaseEntries();                     // clear database (gate on AllowUserClearDatabase)
logger.LoadEntriesFromDatabase(true);              // refresh from the database
logger.SetPersistLogs(true);                       // persistence toggle (gate on AllowUserTogglePersistence)

ImGui.SetClipboardText(NoireHistoryLogger.FormatEntries(view.Entries));   // export or copy
var line = NoireHistoryLogger.FormatEntry(entry);  // "yyyy-MM-dd HH:mm:ss | Level | Category | Message | Source"
```

The built-in window clears in-memory entries while not persisting and database entries while persisting. Its display options are persisted in `HistoryLoggerConfig` (`ShowLevelBackgroundColors`, `SelectLinesSeparately`, `HideCategoryColumn`, `HideSourceColumn`, `ItemsPerPage`) for your window to read.

---

## Database Persistence

### Enabling Persistence

Store logs in the database:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

// Enable persistence and load existing logs
logger?.SetPersistLogs(persist: true, loadExisting: true);

// Enable persistence without loading existing logs
logger?.SetPersistLogs(persist: true, loadExisting: false);

// Disable persistence
logger?.SetPersistLogs(persist: false);
```

### Custom Database Name

Override the database name:

```csharp
var logger = new NoireHistoryLogger(
    persistLogs: true,
    databaseName: "MyPluginLogs"
);

// Reloads from the new database if persistence is enabled.
logger?.SetDatabaseName("MyPluginLogs");
```

Default database name: `"NoireHistoryLogger"`

### Loading from Database

Reload logs from the database:

```csharp
logger?.LoadEntriesFromDatabase(replaceExisting: true);
logger?.LoadEntriesFromDatabase(replaceExisting: false);
```

### Clearing Database Logs

Clear persisted logs:

```csharp
logger?.ClearDatabaseEntries();  // also clears the underlying rows when persistence is on
logger?.ClearEntries();          // in-memory only
```

These two clear unconditionally, unlike `RemoveEntry`. The `AllowUserClear*` permissions only gate the window's buttons.

---

## Auto-Logging with Proxies

Method calls can be logged automatically through dynamic proxies.

### Basic Proxy Usage

A proxy that logs method calls:

```csharp
public class UserService
{
    public virtual void CreateUser(string username) { /* ... */ }
    public virtual void DeleteUser(int userId) { /* ... */ }
}

var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

// Create a logged proxy instance
var service = logger?.CreateLoggedProxy<UserService>(
    logAllMethods: true,
    category: "UserService"
);

// Method calls are automatically logged
service?.CreateUser("john.doe");  // Logs: "UserService.CreateUser invoked"
service?.DeleteUser(123);         // Logs: "UserService.DeleteUser invoked"
```

A proxy subclasses the type. The methods to log must be `virtual`. A non-virtual one is skipped with a warning naming it.

### Proxy with Existing Instance

Wrap an existing instance:

```csharp
var existingService = new UserService();

var loggedService = logger?.CreateLoggedProxy(
    instance: existingService,
    logAllMethods: true,
    category: "UserService"
);
```

### Type Registration for Auto-Logging

Register types for automatic logging:

```csharp
logger?.RegisterTypeForAutoLogging<UserService>(category: "Services");

var service1 = logger?.CreateLoggedProxy<UserService>();  // uses the "Services" category

logger?.ClearAutoLoggingRegistrations();
```

### Selective Method Logging

Choose which methods to log:

```csharp
var proxy1 = logger?.CreateLoggedProxy<MyClass>(logAllMethods: true);

// false logs only the members decorated with [NoireLog].
var proxy2 = logger?.CreateLoggedProxy<MyClass>(logAllMethods: false);

logger?.RegisterTypeForAutoLogging<MyClass>(category: "Auto");
var proxy3 = logger?.CreateLoggedProxy<MyClass>(
    logAllMethods: false,  // overrides the registered setting
    category: "Manual"     // overrides the registered category
);
```

### The [NoireLog] Attribute

`[NoireLog]` marks what a proxy logs when `logAllMethods` is off, and overrides the entry's message, category and level. It applies to a method, a property, a constructor or a whole class:

```csharp
public class UserService
{
    // Logged with the default message, "UserService.CreateUser invoked"
    [NoireLog]
    public virtual void CreateUser(string username) { /* ... */ }

    // Logged with everything spelled out, arguments included
    [NoireLog("User deleted", category: "Accounts", level: HistoryLogLevel.Warning, IncludeArguments = true)]
    public virtual void DeleteUser(int userId) { /* ... */ }
}
```

A throwing call is logged at `Error` with the exception appended, unless the attribute names a level. The exception keeps propagating.

---

## Advanced Features

### Retrieving Log Entries

Snapshots of log entries:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

// Get current entries (returns runtime or database entries based on persistence mode)
IReadOnlyList<HistoryLogEntry>? entries = logger?.GetEntriesSnapshot();

IReadOnlyList<HistoryLogEntry>? runtimeEntries = logger?.GetRuntimeEntriesSnapshot();
IReadOnlyList<HistoryLogEntry>? databaseEntries = logger?.GetDatabaseEntriesSnapshot();
```

### Category Management

Categories:

```csharp
IReadOnlyList<string>? categories = logger?.GetCategories();

foreach (var category in categories ?? Enumerable.Empty<string>())
{
    Console.WriteLine($"Category: {category}");
}
```

### Removing Specific Entries

Remove individual entries:

```csharp
// AddEntry returns the entry as stored, and RemoveEntry matches on it
var stored = logger?.AddEntry("Test entry", category: "Test");
bool removed = stored != null && (logger?.RemoveEntry(stored) ?? false);
```

Both `AddEntry` overloads return the stored entry, normalized and stamped with its database `Id` when persisted. Pass that value to `RemoveEntry`.

```csharp
var entry = new HistoryLogEntry
{
    Message = "Test entry",
    Category = "Test"
};

// Returns the stored entry; `entry` itself stays untouched. Keep `stored` for a later RemoveEntry.
var stored = logger?.AddEntry(entry);
```

Entries from `GetEntriesSnapshot()`, `GetRuntimeEntriesSnapshot()` or `GetDatabaseEntriesSnapshot()` are stored entries too.

`null` passed to `AddEntry(HistoryLogEntry)` or `RemoveEntry` throws `ArgumentNullException`.

Removal respects `AllowUserClearInMemory` and `AllowUserClearDatabase`.

### Direct Database Queries

Custom queries on the log database:

```csharp
logger?.ExecuteDatabaseQuery(builder =>
{
    builder.Where("level", "Error")
           .Where("timestamp", ">", DateTime.UtcNow.AddDays(-7))
           .Delete();
});

var errorCount = logger?.ExecuteDatabaseQuery(builder =>
{
    return builder.Where("level", "Error").Count();
}) ?? 0;

var recentLogs = logger?.ExecuteDatabaseQuery(builder =>
{
    return builder.Where("timestamp", ">", DateTime.UtcNow.AddHours(-1))
                  .OrderByDesc("timestamp")
                  .Get();
});
```

### Checking Module State

Module properties:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

bool isPersisting = logger?.PersistLogs ?? false;
string dbName = logger?.DatabaseName ?? "Unknown";
int maxEntries = logger?.MaxInMemoryEntries ?? 0;

bool canTogglePersist = logger?.AllowUserTogglePersistence ?? false;
bool canClearMemory = logger?.AllowUserClearInMemory ?? false;
bool canClearDatabase = logger?.AllowUserClearDatabase ?? false;
bool canAddManually = logger?.AllowManualEntryCreation ?? false;

bool hasWindow = logger?.HasWindow ?? false;
bool isWindowOpen = logger?.IsWindowOpen ?? false;
```

---

## Troubleshooting

### Logs not appearing
- Ensure the module is active (`IsActive == true`).
- Check entries are not trimmed by `MaxInMemoryEntries`.
- Enable `enableLogging: true` to see exceptions.
- Check `/xllog`.

### Database persistence not working
- Verify `PersistLogs` is set to `true`.
- Check the database name is not null or empty.
- Ensure NoireLib's database system is initialized.
- Call `LoadEntriesFromDatabase(true)` manually.
- Check file permissions on the database.

### Window not showing
- Confirm the module has a registered window (`HasWindow == true`).
- Ensure `ShowWindow()` is called after module initialization.
- Check that the module is active. Deactivating it closes the window.
- Read `IsWindowOpen` after the call to tell a window that never opened from one off screen.
- With `SetCustomWindow()`, add your window to your plugin's `WindowSystem`. The module only toggles `IsOpen`.

### Proxy auto-logging not working
- The method must be public and `virtual`. Each skipped member is named in a warning.
- Check `logAllMethods`. When off, only members carrying `[NoireLog]` are logged.
- Call the proxy `CreateLoggedProxy` returned.
- `RegisterTypeForAutoLogging<T>()` only supplies the default for a null `logAllMethods`.

### Entries being trimmed unexpectedly
- Check `MaxInMemoryEntries` (default 2000).
- Increase the limit: `logger.MaxInMemoryEntries = 5000;`
- Enable database persistence to retain all logs.
- Archive entries yourself with `GetEntriesSnapshot()`.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
