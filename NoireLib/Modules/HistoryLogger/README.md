
# Module Documentation : NoireHistoryLogger

You are reading the documentation for the `NoireHistoryLogger` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Creating and Adding Logs](#creating-and-adding-logs)
- [Log Levels](#log-levels)
- [Displaying the History Logger Window](#displaying-the-history-logger-window)
- [Database Persistence](#database-persistence)
- [Auto-Logging with Proxies](#auto-logging-with-proxies)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireHistoryLogger` is a module that provides comprehensive logging capabilities with optional database persistence and a built-in UI for viewing and managing logs. It provides:
- **In-memory and database storage** for log entries
- **Built-in UI window** for viewing and managing logs
- **Multiple log levels** (Trace, Debug, Info, Warning, Error, Critical)
- **Category-based organization** for filtering and grouping logs
- **Auto-logging capabilities** with dynamic proxies
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

// Simple log
logger?.AddEntry("Plugin initialized successfully");

// Log with category
logger?.AddEntry("Configuration loaded", category: "Config");

// Log with level and source
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

That's it! You now have a fully functional history logger with UI.

---

## Configuration

### Module Parameters

Configure the module using the constructor:

```csharp
var historyLogger = new NoireHistoryLogger(
    moduleId: "MyLogger",                           // Optional identifier
    active: true,                                   // Enable/disable the module
    enableLogging: true,                            // Enable internal logging
    persistLogs: false,                             // Enable database persistence
    databaseName: "MyPluginLogs",                   // Optional custom database name
    allowUserTogglePersistence: false,              // Allow user to toggle persistence in UI
    allowUserClearInMemory: true,                   // Allow user to clear in-memory logs
    allowUserClearDatabase: true,                   // Allow user to clear database logs
    allowManualEntryCreation: false                 // Allow user to create manual entries in UI
);
```

### Property Configuration

You can also configure the module after creation:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

// Set database persistence
logger?.SetPersistLogs(persist: true, loadExisting: true);

// Change database name
logger?.SetDatabaseName("CustomDatabase");

// Configure user permissions
logger?.SetAllowUserTogglePersistence(true)
       ?.SetAllowUserClearInMemory(true)
       ?.SetAllowUserClearDatabase(false)
       ?.SetAllowManualEntryCreation(true);

// Set maximum in-memory entries (default: 2000)
if (logger != null)
    logger.MaxInMemoryEntries = 5000;
```

### User Permission Flags

Control what users can do in the UI:

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

// Minimal log
logger?.AddEntry("Something happened");

// With category
logger?.AddEntry("User logged in", category: "Authentication");

// With level
logger?.AddEntry("Low memory warning", level: HistoryLogLevel.Warning);

// Complete log entry
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

When the limit is exceeded, the oldest entries are automatically removed from memory.

---

## Log Levels

The module supports six severity levels:

```csharp
public enum HistoryLogLevel
{
    Trace,      // Detailed diagnostic information
    Debug,      // Debugging information
    Info,       // General informational messages
    Warning,    // Warning messages
    Error,      // Error messages
    Critical    // Critical failures
}
```

---

## Displaying the History Logger Window

### Manual Display

Show or hide the history logger window:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

// Show the window
logger?.ShowWindow();

// Hide the window
logger?.HideWindow();

// Toggle the window
logger?.ToggleWindow();

// Show, hide or toggle from a single call (null toggles)
logger?.SetShowWindow(true);

// Check if the window is open
var isOpen = logger?.IsWindowOpen ?? false;
```

### Window Features

The built-in UI provides:
- **Log filtering** by category and log level
- **Search functionality** to find specific entries
- **Sorting options** (newest/oldest first, or alphabetical ordering)
- **Color-coded log levels** for easy identification
- **Entry details** including timestamp, source, and message
- **Clear actions** (respecting user permissions)
- **Persistence toggle** (if enabled)

---

## Database Persistence

### Enabling Persistence

Enable database persistence to store logs permanently:

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

Override the default database name:

```csharp
// Via constructor
var logger = new NoireHistoryLogger(
    persistLogs: true,
    databaseName: "MyPluginLogs"
);

// Via method (will reload from new database if persistence is enabled)
logger?.SetDatabaseName("MyPluginLogs");
```

Default database name: `"NoireHistoryLogger"`

### Loading from Database

Manually reload logs from the database:

```csharp
// Replace existing entries with database logs
logger?.LoadEntriesFromDatabase(replaceExisting: true);

// Append database logs to existing entries
logger?.LoadEntriesFromDatabase(replaceExisting: false);
```

### Clearing Database Logs

Clear persisted logs from the database:

```csharp
// Clear the database entries, and the rows behind them when persistence is on
logger?.ClearDatabaseEntries();

// Clear in-memory entries only
logger?.ClearEntries();
```

**Note:** unlike `RemoveEntry`, these two clear unconditionally. The `AllowUserClear*` permissions gate the
matching buttons in the window, not these calls.

---

## Auto-Logging with Proxies

The `NoireHistoryLogger` supports automatic logging of method calls using dynamic proxies.

### Basic Proxy Usage

Create a proxy that automatically logs method calls:

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

A proxy works by subclassing the type and overriding its members, so the methods to log must be `virtual`.
A non-virtual one is skipped, with a warning naming it.

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
// Register a single type
logger?.RegisterTypeForAutoLogging<UserService>(category: "Services");

// Create proxies - automatically uses registered settings
var service1 = logger?.CreateLoggedProxy<UserService>();  // Uses "Services" category

// Clear registrations
logger?.ClearAutoLoggingRegistrations();
```

### Selective Method Logging

Control which methods to log:

```csharp
// Log all methods
var proxy1 = logger?.CreateLoggedProxy<MyClass>(logAllMethods: true);

// Log only the members decorated with [NoireLog]
var proxy2 = logger?.CreateLoggedProxy<MyClass>(logAllMethods: false);

// Override registered settings
logger?.RegisterTypeForAutoLogging<MyClass>(category: "Auto");
var proxy3 = logger?.CreateLoggedProxy<MyClass>(
    logAllMethods: false,  // Override registered setting
    category: "Manual"     // Override registered category
);
```

### The [NoireLog] Attribute

`[NoireLog]` marks what a proxy logs when `logAllMethods` is off, and overrides the message, category and
level of the entry a call produces. It applies to a method, a property, a constructor, or a whole class:

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

A call that throws is logged at `Error` and the exception is appended to the message, unless the attribute
names a level of its own. The exception then keeps propagating.

---

## Advanced Features

### Retrieving Log Entries

Get snapshots of log entries:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

// Get current entries (returns runtime or database entries based on persistence mode)
IReadOnlyList<HistoryLogEntry>? entries = logger?.GetEntriesSnapshot();

// Get runtime-only entries
IReadOnlyList<HistoryLogEntry>? runtimeEntries = logger?.GetRuntimeEntriesSnapshot();

// Get database entries
IReadOnlyList<HistoryLogEntry>? databaseEntries = logger?.GetDatabaseEntriesSnapshot();
```

### Category Management

Retrieve and work with categories:

```csharp
// Get all distinct categories
IReadOnlyList<string>? categories = logger?.GetCategories();

// Use in UI dropdown, filtering, etc.
foreach (var category in categories ?? Enumerable.Empty<string>())
{
    Console.WriteLine($"Category: {category}");
}
```

### Removing Specific Entries

Remove individual log entries:

```csharp
// AddEntry returns the entry as it was stored, which is what RemoveEntry matches on
var stored = logger?.AddEntry("Test entry", category: "Test");

// Remove the entry (respects user permissions)
bool removed = stored != null && (logger?.RemoveEntry(stored) ?? false);
```

Both `AddEntry` overloads return the stored entry. Keep that value rather than the one you handed in: the
module normalizes an entry on the way in, and persistence stamps the stored copy with its database `Id`, so
the entry you built yourself is not the one that ended up stored and will not reliably match it.

```csharp
var entry = new HistoryLogEntry
{
    Message = "Test entry",
    Category = "Test"
};

// Returns the stored entry; `entry` itself stays untouched. Keep `stored` for a later RemoveEntry.
var stored = logger?.AddEntry(entry);
```

Entries read from `GetEntriesSnapshot()`, `GetRuntimeEntriesSnapshot()` or `GetDatabaseEntriesSnapshot()`
are stored entries too, and can be passed to `RemoveEntry` directly.

Passing `null` to `AddEntry(HistoryLogEntry)` or to `RemoveEntry` throws an `ArgumentNullException`.

**Note:** Removal respects `AllowUserClearInMemory` and `AllowUserClearDatabase` permissions.

### Direct Database Queries

Execute custom queries against the log database:

```csharp
// Execute a query without returning a result
logger?.ExecuteDatabaseQuery(builder =>
{
    builder.Where("level", "Error")
           .Where("timestamp", ">", DateTime.UtcNow.AddDays(-7))
           .Delete();
});

// Execute a query with a result
var errorCount = logger?.ExecuteDatabaseQuery(builder =>
{
    return builder.Where("level", "Error").Count();
}) ?? 0;

// Get entries from last hour
var recentLogs = logger?.ExecuteDatabaseQuery(builder =>
{
    return builder.Where("timestamp", ">", DateTime.UtcNow.AddHours(-1))
                  .OrderByDesc("timestamp")
                  .Get();
});
```

### Checking Module State

Access module properties:

```csharp
var logger = NoireLibMain.GetModule<NoireHistoryLogger>();

// Check persistence status
bool isPersisting = logger?.PersistLogs ?? false;

// Get database name
string dbName = logger?.DatabaseName ?? "Unknown";

// Get max entries
int maxEntries = logger?.MaxInMemoryEntries ?? 0;

// Check user permissions
bool canTogglePersist = logger?.AllowUserTogglePersistence ?? false;
bool canClearMemory = logger?.AllowUserClearInMemory ?? false;
bool canClearDatabase = logger?.AllowUserClearDatabase ?? false;
bool canAddManually = logger?.AllowManualEntryCreation ?? false;

// Check window state
bool hasWindow = logger?.HasWindow ?? false;
bool isWindowOpen = logger?.IsWindowOpen ?? false;
```

---

## Troubleshooting

### Logs not appearing
- Ensure the module is active (`IsActive == true`).
- Confirm `AddEntry()` is being called correctly.
- Check that entries are not being trimmed due to `MaxInMemoryEntries` limit.
- Verify no exceptions are being thrown (enable `enableLogging: true`).
- Check the dalamud logs with `/xllog`.

### Database persistence not working
- Verify `PersistLogs` is set to `true`.
- Check that the database name is valid (not null/empty).
- Ensure NoireLib's database system is properly initialized.
- Try calling `LoadEntriesFromDatabase(true)` manually.
- Check for database file permissions issues.

### Window not showing
- Confirm the module has a registered window (`HasWindow == true`).
- Ensure `ShowWindow()` is called after module initialization.
- Check that the module is active. Deactivating the module closes the window.
- Read back `IsWindowOpen` after the call to tell a window that never opened from one drawn off screen.
- Verify no UI framework conflicts.

### Proxy auto-logging not working
- Confirm the method being called is public and `virtual`. A proxy subclasses the type, so it cannot
  intercept a non-virtual member; each one it has to skip is named in a warning.
- Check that `logAllMethods` is set correctly. With it off, only members carrying `[NoireLog]` are logged.
- Confirm you are calling the proxy that `CreateLoggedProxy` returned rather than the original instance.
- Note that `RegisterTypeForAutoLogging<T>()` only supplies the default used when `logAllMethods` is left
  null, so passing `logAllMethods` explicitly ignores it.

### Entries being trimmed unexpectedly
- Check `MaxInMemoryEntries` value (default: 2000).
- Increase the limit: `logger.MaxInMemoryEntries = 5000;`
- Enable database persistence to retain all logs.
- Implement custom archiving logic using `GetEntriesSnapshot()`.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
