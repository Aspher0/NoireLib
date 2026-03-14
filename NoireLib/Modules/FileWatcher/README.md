# Module Documentation : NoireFileWatcher

You are reading the documentation for the `NoireFileWatcher` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Watching Files and Directories](#watching-files-and-directories)
- [Managing Registrations and Callbacks](#managing-registrations-and-callbacks)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireFileWatcher` is a filesystem watch module for plugin automation and reactive workflows. It provides:
- **Directory and file watch registration** with unique watch IDs
- **Sync and async callback subscriptions** per watch
- **Owner-based callback grouping** for easier cleanup
- **Duplicate notification suppression** with configurable time windows
- **Pattern-based filtering** for directory watches (`*`, `?` wildcard support)
- **Events and EventBus integration** for decoupled event pipelines
- **Aggregated statistics and metrics** for monitoring module behavior

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Register a Directory Watch

```csharp
var watchId = fileWatcher.WatchDirectory(
    directoryPath: "pathToDirectory",
    callback: notification => NoireLogger.LogInfo($"Changed: {notification.FullPath}"),
    includeSubdirectories: true);
```

### 2. React to Notifications

```csharp
fileWatcher.Changed += notification =>
{
    NoireLogger.LogInfo($"Changed file: {notification.Name}");
};
```

That's it! You now have a working filesystem watcher.

---

## Configuration

### Module Parameters

Configure the watcher with the constructor:

```csharp
var fileWatcher = new NoireFileWatcher(
    moduleId: "MyFileWatcher",
    active: true,
    enableLogging: true,
    eventBus: eventBus,
    autoEnableNewWatches: true,
    suppressDuplicateNotifications: true,
    duplicateNotificationWindowMs: 100
);
```

### Duplicate Suppression

Control duplicate notification behavior:

```csharp
fileWatcher.SetDuplicateSuppression(
    enabled: true,
    window: TimeSpan.FromMilliseconds(150));
```

### Auto-Enable Behavior

Control whether newly added watches start automatically:

```csharp
fileWatcher.SetAutoEnableNewWatches(true);
```

---

## Watching Files and Directories

### Watch a Directory

```csharp
var directoryWatchId = fileWatcher.WatchDirectory(
    directoryPath: "Data",
    patterns: ["*.json", "*.txt"],
    includeSubdirectories: true,
    key: "data-watch");
```

### Watch a Single File

```csharp
var fileWatchId = fileWatcher.WatchFile(
    filePath: "Config/plugin.json",
    key: "config-file");
```

### Advanced Watch Options

```csharp
var watchId = fileWatcher.Watch(new FileWatchRegistrationOptions
{
    Path = "Logs",
    TargetType = FileWatchTargetType.Directory,
    IncludeSubdirectories = false,
    NotifyOnChanged = true,
    NotifyOnCreated = true,
    NotifyOnDeleted = true,
    NotifyOnRenamed = true,
    NotifyOnError = true,
    AllowNonExistingPath = false,
    StartEnabled = true,
    Key = "logs"
});
```

---

## Managing Registrations and Callbacks

### Add and Remove Callbacks

```csharp
var token = fileWatcher.AddCallback(watchId, notification =>
{
    NoireLogger.LogInfo($"[{notification.EventType}] {notification.FullPath}");
}, owner: this);

var removed = fileWatcher.RemoveCallback(token);
```

### Async Callback Registration

```csharp
fileWatcher.AddAsyncCallback(watchId, async notification =>
{
    await Task.Delay(25);
    NoireLogger.LogInfo($"Async handled: {notification.FullPath}");
}, owner: this);
```

### Enable, Disable, and Remove Watches

```csharp
fileWatcher.SetWatchEnabled(watchId, enabled: false);
fileWatcher.SetWatchEnabled(watchId, enabled: true);

fileWatcher.RemoveWatch(watchId);
fileWatcher.RemoveWatchByKey("logs");
```

---

## Advanced Features

### Events

Subscribe to module-wide events:

```csharp
fileWatcher.NotificationReceived += n => NoireLogger.LogInfo($"Any: {n.FullPath}");
fileWatcher.Created += n => NoireLogger.LogInfo($"Created: {n.FullPath}");
fileWatcher.Deleted += n => NoireLogger.LogInfo($"Deleted: {n.FullPath}");
fileWatcher.Renamed += n => NoireLogger.LogInfo($"Renamed: {n.OldName} -> {n.Name}");
fileWatcher.Error += e => NoireLogger.LogError(e.Exception, $"Watcher error: {e.WatchedPath}");
```

### EventBus Integration

When `EventBus` is configured, module events are published automatically:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();
var fileWatcher = NoireLibMain.AddModule(new NoireFileWatcher(eventBus: eventBus));
```

### Statistics and Monitoring

Inspect watcher metrics:

```csharp
var stats = fileWatcher.GetStatistics();

NoireLogger.LogInfo($"Registered: {stats.RegisteredWatches}");
NoireLogger.LogInfo($"Enabled: {stats.EnabledWatches}");
NoireLogger.LogInfo($"Observed: {stats.TotalNotificationsObserved}");
NoireLogger.LogInfo($"Dispatched: {stats.TotalNotificationsDispatched}");
```

---

## Troubleshooting

### No notifications received
- Ensure the module is active (`IsActive == true`).
- Ensure the watch is enabled (`SetWatchEnabled` or `StartEnabled`).
- Verify the watched path exists unless `AllowNonExistingPath` is enabled.
- Check pattern filters for mismatches.

### Too many repeated notifications
- Enable duplicate suppression.
- Increase `DuplicateNotificationWindow`.

### Callbacks throw exceptions
- Inspect logs for callback failures.
- Keep callbacks short and delegate heavy work to background tasks.
- Prefer async callbacks for IO-bound work.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [EventBus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
