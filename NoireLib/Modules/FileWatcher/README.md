# Module Documentation : NoireFileWatcher

You are reading the documentation for the `NoireFileWatcher` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Thread Contract](#thread-contract)
- [Configuration](#configuration)
- [Watching Files and Directories](#watching-files-and-directories)
- [Managing Callbacks](#managing-callbacks)
- [Managing Watch Registrations](#managing-watch-registrations)
- [Querying Registrations](#querying-registrations)
- [CLR Events](#clr-events)
- [EventBus Integration](#eventbus-integration)
- [Statistics and Monitoring](#statistics-and-monitoring)
- [Models Reference](#models-reference)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireFileWatcher` is a filesystem watch module for plugin automation and reactive workflows. It provides:
- **Directory and file watch registration** with unique watch IDs and optional user-defined keys
- **Automatic target type detection** (directory vs. file) when using `FileWatchTargetType.Auto`
- **Sync and async callback subscriptions** per watch, with inline registration or post-creation attachment
- **Owner-based callback grouping** for bulk cleanup
- **Token-based callback removal** for precise unsubscription
- **Duplicate notification suppression** with a configurable time window and automatic cache sweeping
- **Pattern-based filtering** for directory watches (`*`, `?` wildcard support)
- **Per-event-type toggle** to selectively observe Changed, Created, Deleted, Renamed, and Error events
- **CLR events** (`NotificationReceived`, `Changed`, `Created`, `Deleted`, `Renamed`, `Error`)
- **Framework thread delivery** so handlers can touch game state directly, with no overlapping callbacks (see [Thread Contract](#thread-contract))
- **EventBus integration** for decoupled event pipelines
- **Module lifecycle awareness** (activation/deactivation automatically enables/disables all underlying watchers)
- **Fluent API** for chaining configuration calls
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

## Thread Contract

**Every handler you register runs on the framework thread, so it is safe to touch game state directly.** This applies to sync callbacks, CLR events (`NotificationReceived`, `Changed`, `Created`, `Deleted`, `Renamed`, `Error`), and **every** EventBus event the module publishes. The registration lifecycle events (`FileWatchRegisteredEvent`, `FileWatchStateChangedEvent`, `FileWatchRemovedEvent`, `FileWatchesClearedEvent`) get exactly the same guarantee as `FileWatchNotificationEvent` and `FileWatchErrorEvent`.

There is no split in this contract, and that is deliberate. `NoireEventBus.Publish` runs sync handlers inline, so if the module published a lifecycle event straight from `Watch`, your EventBus subscriber would run on whichever thread the caller of `Watch` happened to use. That caller is a different component: your subscriber cannot see which thread it used, cannot control it, and would break the day that component moved its registration onto a background task. Everything is queued instead, so the thread your subscriber runs on never depends on anyone else's call site.

```csharp
// Safe: this runs on the framework thread.
fileWatcher.Changed += n =>
{
    var player = NoireService.ClientState.LocalPlayer;
    NoireLogger.LogInfo($"{player?.Name} saw {n.Name} change.");
};
```

The underlying `System.IO.FileSystemWatcher` raises its events on thread pool threads, and it does not serialize them: several can fire concurrently. The module therefore does not hand you those threads. Each notification is queued and drained on the framework thread instead, which gives two guarantees:

- **Handlers run on the framework thread**, not on a thread pool thread.
- **Handlers never overlap.** Deliveries are drained one at a time, so your callback is never re-entered while it is still running.

### Async callbacks

An `asyncCallback` is *started* on the framework thread and runs fire-and-forget, so everything before its first `await` gets the same guarantee. What runs after an `await` is governed by that callback's own awaits, not by this module. If a continuation needs to touch game state, marshal it back with `AsyncHelper`.

### Lifecycle events are asynchronous

The price of the guarantee above is that a lifecycle event is queued like everything else, so it reaches subscribers **after** the call that caused it has already returned:

```csharp
eventBus.Subscribe<FileWatchRegisteredEvent>(e => knownWatches.Add(e.Registration.WatchId));

var watchId = fileWatcher.WatchDirectory(directory);
// knownWatches does not contain watchId yet. The subscriber runs on a later frame.
```

If you need the registration synchronously, use the returned watch ID or `GetWatch(watchId)`; both are up to date the moment `Watch` returns. The EventBus is for the components that did *not* make the call.

A watch does not start raising events until its `FileWatchRegisteredEvent` is queued, so a subscriber that tracks watches by ID is always told a watch exists before it can receive that watch's first `FileWatchNotificationEvent`. Within a single `ClearAllWatches`, the per-watch `FileWatchRemovedEvent` deliveries precede the `FileWatchesClearedEvent`.

### Delivery ordering and backpressure

Notifications are delivered in the order they were observed. The queue is bounded at 4096 pending deliveries: a filesystem event storm can outpace the game's frame rate, so once the queue is full the **oldest** pending delivery is dropped and a warning is logged. Dropping the oldest keeps the newest notification for a path, which is what a handler re-reading that path needs. A drop is a sign that handlers are too slow for the event volume; keep them short and move heavy work off the framework thread.

Drops are counted in `TotalDeliveriesDropped`, so you can detect them without reading the log:

```csharp
if (fileWatcher.GetStatistics().TotalDeliveriesDropped > 0)
    NoireLogger.LogWarning("Some filesystem notifications never reached a handler.");
```

The queue does **not** merge notifications. Collapsing the burst of events that a single file write produces is [duplicate suppression](#duplicate-suppression)'s job, and it happens before the queue. If you turn suppression off, you get every notification the filesystem reports.

### When NoireLib is not initialized

There is no framework thread to marshal onto, so handlers run inline on the calling or observing thread. This is what makes the module usable in unit tests.

### Disposal

**Once `Dispose` returns, no handler of this module runs again.** That is a hard boundary, not a very likely one: `Dispose` blocks until any delivery that had already started has finished. The flip side is that a handler which blocks forever blocks disposal with it, so keep them short (which the framework thread already demands of you).

Nothing is published from disposal either. `Dispose` removes every watch, but those removals raise no `FileWatchRemovedEvent` or `FileWatchesClearedEvent`: a module being torn down should not be calling into the subscribers of a plugin that is unloading.

A callback is free to dispose the module that invoked it. `Dispose` recognizes the calling thread's own delivery and does not wait for it, so this does not deadlock.

The same retirement holds per watch: a notification that was queued before `RemoveWatch` or `RemoveCallback` is not delivered to the callbacks you retired.

Registering on a disposed module is silently abandoned rather than throwing. `Watch` still returns an ID, but no watch exists under it and `GetWatch(watchId)` reports none, so a registration racing your plugin's teardown cannot leave a live `FileSystemWatcher` behind the module's back.

---

## Configuration

### Module Constructor

All configuration can be set at construction time:

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

Constructor parameters:
- `moduleId` - Optional identifier for this module instance.
- `active` - Whether the module starts active. When inactive, all underlying watchers stop raising events.
- `enableLogging` - Whether the module logs lifecycle and notification activity.
- `eventBus` - Optional `NoireEventBus` for publishing structured events (see [EventBus Integration](#eventbus-integration)).
- `autoEnableNewWatches` - Whether newly registered watches are automatically started while the module is active.
- `suppressDuplicateNotifications` - Whether duplicate notifications within the suppression window are discarded.
- `duplicateNotificationWindowMs` - Duplicate suppression window in milliseconds.

The `EventBus` property can also be set or changed after construction:

```csharp
fileWatcher.EventBus = myEventBus;
```

### Duplicate Suppression

The module tracks recent notifications by a composite key of `WatchId + EventType + FullPath + OldFullPath`. If an identical notification arrives within the configured window, it is silently suppressed. The duplicate cache is periodically swept to prevent unbounded growth.

Control this behavior at runtime:

```csharp
fileWatcher.SetDuplicateSuppression(
    enabled: true,
    window: TimeSpan.FromMilliseconds(150));
```

Or set the properties individually:

```csharp
fileWatcher.SuppressDuplicateNotifications = true;
fileWatcher.DuplicateNotificationWindow = TimeSpan.FromMilliseconds(200);
```

Setting `DuplicateNotificationWindow` to zero or a negative value clamps it to 1 ms.

### Auto-Enable Behavior

Control whether newly added watches start automatically while the module is active:

```csharp
fileWatcher.SetAutoEnableNewWatches(true);
```

Both `SetDuplicateSuppression` and `SetAutoEnableNewWatches` return the module instance for fluent chaining:

```csharp
fileWatcher
    .SetAutoEnableNewWatches(true)
    .SetDuplicateSuppression(true, TimeSpan.FromMilliseconds(150));
```

### Module Lifecycle

When the module is **activated** (going from `IsActive = false` to `true`), all watches that are individually enabled will resume raising events.

When the module is **deactivated** (going from `IsActive = true` to `false`), all underlying watchers stop raising events regardless of their individual enabled state.

---

## Watching Files and Directories

### Watch a Directory

Use `WatchDirectory` for a simple directory watch. You can optionally provide glob patterns, an inline callback, an async callback, an owner, and a key:

```csharp
var watchId = fileWatcher.WatchDirectory(
    directoryPath: "Data",
    callback: n => NoireLogger.LogInfo($"Detected: {n.FullPath}"),
    asyncCallback: async n => await ProcessAsync(n),
    owner: this,
    patterns: ["*.json", "*.txt"],
    includeSubdirectories: true,
    key: "data-watch");
```

Parameters:
- `directoryPath` - The directory to watch.
- `callback` - Optional synchronous callback invoked on every matching notification.
- `asyncCallback` - Optional asynchronous callback invoked on every matching notification (fire-and-forget; exceptions are caught and logged).
- `owner` - Optional owner object associated with the inline callbacks (used for bulk removal via `RemoveCallbacksByOwner`).
- `key` - Optional user-defined key. If a watch with the same key already exists, it is removed and replaced.
- `patterns` - Optional list of glob patterns (e.g. `"*.json"`, `"config_??.txt"`). Defaults to `["*"]` (all files).
- `includeSubdirectories` - Whether subdirectories are watched recursively. Defaults to `true`.

### Watch a Single File

Use `WatchFile` for a single-file watch. The directory containing the file is used as the watcher path, and the filename is used as the filter:

```csharp
var watchId = fileWatcher.WatchFile(
    filePath: "Config/plugin.json",
    callback: n => NoireLogger.LogInfo($"Config changed: {n.RelativePath}"),
    key: "config-file");
```

### Advanced Watch Options

For full control, use the `Watch` method with a `FileWatchRegistrationOptions` object:

```csharp
var watchId = fileWatcher.Watch(new FileWatchRegistrationOptions
{
    Path = "Logs",
    TargetType = FileWatchTargetType.Directory,
    IncludeSubdirectories = false,
    Patterns = ["*.log"],
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
    InternalBufferSize = 16384,
    NotifyOnChanged = true,
    NotifyOnCreated = true,
    NotifyOnDeleted = false,
    NotifyOnRenamed = false,
    NotifyOnError = true,
    AllowNonExistingPath = false,
    StartEnabled = true,
    Key = "logs"
},
callback: n => NoireLogger.LogInfo($"Log event: {n.EventType} {n.Name}"),
owner: this);
```

`FileWatchRegistrationOptions` properties:
- `Path` - The target path to watch (directory or file).
- `TargetType` - `Auto` (default, auto-detected), `Directory`, or `File`. When set to `Auto`, the module checks whether the path is an existing directory, an existing file, or falls back to heuristics based on whether the path has a file extension.
- `Patterns` - Glob patterns for filtering. Defaults to `["*"]`.
- `IncludeSubdirectories` - Recursive directory watching. Defaults to `true`. Ignored for file watches.
- `NotifyFilter` - Native `NotifyFilters` flags. Defaults to `FileName | DirectoryName | LastWrite | CreationTime | Size`.
- `InternalBufferSize` - Internal buffer size for the underlying `FileSystemWatcher`. Defaults to `8192`.
- `StartEnabled` - Whether the watch starts enabled. Defaults to `true`.
- `NotifyOnChanged` - Observe change events. Defaults to `true`.
- `NotifyOnCreated` - Observe creation events. Defaults to `true`.
- `NotifyOnDeleted` - Observe deletion events. Defaults to `true`.
- `NotifyOnRenamed` - Observe rename events. Defaults to `true`.
- `NotifyOnError` - Observe watcher-level errors. Defaults to `true`.
- `AllowNonExistingPath` - If `false` (default), the module validates that the path exists before registering.
- `Key` - Optional user-defined key. Registering a new watch with an existing key automatically removes the previous one.

### Pattern Filtering

For directory watches, incoming events are matched against the configured patterns using wildcard rules:
- `*` matches any sequence of characters.
- `?` matches any single character.

Matching is case-insensitive. If multiple patterns are provided, a file matches if it satisfies **any** of them.

For single-file watches, pattern filtering is bypassed; the module only dispatches events for the exact watched file.

### Key-Based Re-Registration

When you register a watch with a `Key` that already exists, the previous watch with that key is automatically removed (disposed and cleaned up) before the new one is created. This lets you safely replace a watch without manually calling `RemoveWatchByKey` first.

A key resolves to the watch that registered it most recently, and removing a watch only retires the key that still resolves to it. Retiring one watch therefore never makes another unreachable through `GetWatchByKey` or `RemoveWatchByKey`.

Resolving the previous holder of a key and registering the new watch are two steps rather than one atomic operation, so registering the same key from two threads at once can leave both watches registered instead of replacing one with the other. The key resolves to whichever registered last; the other stays live and reachable through its watch ID, `GetWatches`, and `ClearAllWatches`. Register a given key from one thread to get the replacement described above.

---

## Managing Callbacks

### Add a Sync Callback

Attach additional sync callbacks to an existing watch by its ID. Returns a `FileWatchCallbackToken` for later removal:

```csharp
var token = fileWatcher.AddCallback(watchId, notification =>
{
    NoireLogger.LogInfo($"[{notification.EventType}] {notification.FullPath}");
}, owner: this);
```

### Add an Async Callback

Async callbacks are invoked fire-and-forget. If the returned task faults, the exception is caught, counted, and logged:

```csharp
var token = fileWatcher.AddAsyncCallback(watchId, async notification =>
{
    await Task.Delay(25);
    NoireLogger.LogInfo($"Async handled: {notification.FullPath}");
}, owner: this);
```

### Remove a Callback by Token

Remove a specific callback from all watches using its token:

```csharp
bool removed = fileWatcher.RemoveCallback(token);
```

### Remove All Callbacks by Owner

Remove every callback associated with a specific owner object across all watches:

```csharp
int removedCount = fileWatcher.RemoveCallbacksByOwner(this);
```

This is useful for cleanup when a subscriber is being disposed.

---

## Managing Watch Registrations

### Enable and Disable a Watch

Toggle a specific watch on or off. A disabled watch stops raising events but remains registered:

```csharp
fileWatcher.SetWatchEnabled(watchId, enabled: false);
fileWatcher.SetWatchEnabled(watchId, enabled: true);
```

### Enable or Disable All Watches

Bulk-enable or bulk-disable every registered watch. These methods return the module instance for fluent chaining:

```csharp
fileWatcher.EnableAllWatches();
fileWatcher.DisableAllWatches();
```

Both publish one `FileWatchStateChangedEvent` per watch that actually changed, exactly as `SetWatchEnabled` does, so a subscriber tracking watch state stays correct without needing to know which API the caller used. Watches already in the requested state are left alone and report nothing, so a bulk enable over watches that are all enabled publishes no events at all.

### Remove a Watch

Remove and dispose a watch by its ID:

```csharp
bool removed = fileWatcher.RemoveWatch(watchId);
```

Or by its key:

```csharp
bool removed = fileWatcher.RemoveWatchByKey("logs");
```

### Clear All Watches

Remove and dispose every registered watch:

```csharp
fileWatcher.ClearAllWatches();
```

This also returns the module instance for chaining.

---

## Querying Registrations

### Get All Registrations

Returns an immutable snapshot of all current watch registrations as `FileWatchRegistrationInfo` records:

```csharp
IReadOnlyList<FileWatchRegistrationInfo> watches = fileWatcher.GetWatches();
```

### Get a Single Registration

By watch ID:

```csharp
FileWatchRegistrationInfo? info = fileWatcher.GetWatch(watchId);
```

By key:

```csharp
FileWatchRegistrationInfo? info = fileWatcher.GetWatchByKey("data-watch");
```

Both return `null` if not found.

---

## CLR Events

The module exposes CLR events for reactive subscriptions without needing a callback token:

```csharp
// Fires for every dispatched notification regardless of event type.
fileWatcher.NotificationReceived += n => NoireLogger.LogInfo($"Any: {n.FullPath}");

// Type-specific events.
fileWatcher.Changed += n => NoireLogger.LogInfo($"Changed: {n.FullPath}");
fileWatcher.Created += n => NoireLogger.LogInfo($"Created: {n.FullPath}");
fileWatcher.Deleted += n => NoireLogger.LogInfo($"Deleted: {n.FullPath}");
fileWatcher.Renamed += n => NoireLogger.LogInfo($"Renamed: {n.OldName} -> {n.Name}");

// Error event (receives a FileWatchError, not a FileWatchNotification).
fileWatcher.Error += e => NoireLogger.LogError(e.Exception, $"Watcher error: {e.RootPath}");
```

All CLR event handlers are invoked on the framework thread and never overlap each other (see [Thread Contract](#thread-contract)).

CLR event handlers that throw exceptions are caught, counted in statistics, and logged.

All CLR events are cleared when the module is disposed.

---

## EventBus Integration

When `EventBus` is set, the module automatically publishes structured events for every significant action:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();
var fileWatcher = NoireLibMain.AddModule(new NoireFileWatcher(eventBus: eventBus));
```

Published event types:
- `FileWatchRegisteredEvent` - A new watch registration was created. Contains a `FileWatchRegistrationInfo` snapshot.
- `FileWatchRemovedEvent` - A watch was removed. Contains the `WatchId`, `Path`, and `Key`.
- `FileWatchStateChangedEvent` - A watch was enabled or disabled, whether through `SetWatchEnabled` or through a bulk `EnableAllWatches`/`DisableAllWatches` call. Contains the `WatchId` and new `Enabled` state. A bulk call publishes one of these per watch that actually changed state.
- `FileWatchNotificationEvent` - A filesystem notification was dispatched. Contains the `FileWatchNotification`.
- `FileWatchErrorEvent` - An underlying watcher error occurred. Contains the `FileWatchError`.
- `FileWatchesClearedEvent` - All watches were removed via `ClearAllWatches`. Contains the `RemovedCount`.

All six are delivered to subscribers on the framework thread, and all six are queued, so a lifecycle event arrives after the call that caused it has returned. Disposal publishes nothing. See [Thread Contract](#thread-contract) for the reasoning and the consequences.

---

## Statistics and Monitoring

Retrieve aggregated metrics for the module instance:

```csharp
FileWatcherStatistics stats = fileWatcher.GetStatistics();

NoireLogger.LogInfo($"Registered watches: {stats.RegisteredWatches}");
NoireLogger.LogInfo($"Enabled watches: {stats.EnabledWatches}");
NoireLogger.LogInfo($"Total registrations: {stats.TotalRegistrations}");
NoireLogger.LogInfo($"Total removed: {stats.TotalRemoved}");
NoireLogger.LogInfo($"Notifications observed: {stats.TotalNotificationsObserved}");
NoireLogger.LogInfo($"Notifications dispatched: {stats.TotalNotificationsDispatched}");
NoireLogger.LogInfo($"Errors: {stats.TotalErrors}");
NoireLogger.LogInfo($"Duplicates suppressed: {stats.TotalDuplicateNotificationsSuppressed}");
NoireLogger.LogInfo($"Callback exceptions caught: {stats.TotalCallbackExceptionsCaught}");
NoireLogger.LogInfo($"Deliveries dropped: {stats.TotalDeliveriesDropped}");
```

`FileWatcherStatistics` fields:
- `RegisteredWatches` - Current number of registered watches.
- `EnabledWatches` - Current number of enabled watches.
- `TotalRegistrations` - Cumulative number of watch registrations created.
- `TotalRemoved` - Cumulative number of watch registrations removed.
- `TotalNotificationsObserved` - Total raw notifications received from underlying watchers.
- `TotalNotificationsDispatched` - Total notifications dispatched to callbacks and events (after duplicate suppression).
- `TotalErrors` - Total watcher-level errors observed.
- `TotalDuplicateNotificationsSuppressed` - Total notifications discarded by duplicate suppression.
- `TotalCallbackExceptionsCaught` - Total exceptions caught from user callbacks and CLR event handlers.
- `TotalDeliveriesDropped` - Total deliveries discarded because the delivery queue was at capacity (see [Delivery ordering and backpressure](#delivery-ordering-and-backpressure)). While this is non-zero, `TotalNotificationsDispatched` undercounts what the filesystem reported: those notifications were observed and accepted but never reached a handler.

---

## Models Reference

### `FileWatchNotification`

A record describing a single captured filesystem event.

Properties:
- `WatchId` - The ID of the watch registration that captured this event.
- `RootPath` - The root directory path of the watch.
- `TargetType` - Whether the watched target is a `Directory` or `File`.
- `FullPath` - The full path of the file or directory that triggered the event.
- `Name` - The name of the file or directory, if available.
- `EventType` - The semantic type: `Changed`, `Created`, `Deleted`, or `Renamed`.
- `OccurredAtUtc` - The UTC timestamp when the event was captured.
- `NativeChangeType` - The raw `WatcherChangeTypes` value from the underlying `FileSystemWatcher`.
- `OldFullPath` - The previous full path (only populated for `Renamed` events).
- `OldName` - The previous name (only populated for `Renamed` events).
- `WatchKey` - The key of the watch registration, if one was set.
- `RelativePath` - Computed property returning the path relative to `RootPath`.

### `FileWatchError`

A record describing a watcher-level error.

Properties:
- `WatchId` - The ID of the watch registration.
- `RootPath` - The root path of the watch.
- `Exception` - The exception thrown by the underlying watcher.
- `OccurredAtUtc` - The UTC timestamp of the error.
- `WatchKey` - The key of the watch registration, if one was set.

### `FileWatchRegistrationInfo`

An immutable snapshot of a watch registration.

Properties:
- `WatchId` - The unique ID.
- `Path` - The watched path.
- `TargetType` - `Directory` or `File`.
- `Patterns` - The list of glob patterns.
- `IncludeSubdirectories` - Whether subdirectories are included.
- `NotifyFilter` - The configured `NotifyFilters`.
- `IsEnabled` - Whether the watch is currently enabled.
- `Key` - The optional user key.
- `CallbackCount` - Number of registered sync callbacks.
- `AsyncCallbackCount` - Number of registered async callbacks.

### `FileWatchCallbackToken`

A readonly record struct wrapping a `Guid`. Returned when adding a callback, and used to remove that specific callback later via `RemoveCallback(token)`.

### `FileWatchTargetType`

Enum controlling how the target path is interpreted:
- `Auto` - Detect automatically (checks if the path is an existing directory, then file, then uses extension heuristics).
- `Directory` - Force directory mode.
- `File` - Force single-file mode.

### `FileWatchEventType`

Enum representing the semantic type of a notification:
- `Created`
- `Changed`
- `Deleted`
- `Renamed`
- `Error` - **never carried by a notification.** A watcher-level error reports an exception rather than a path, so it travels as a `FileWatchError` through the `Error` CLR event and `FileWatchErrorEvent`, not as a `FileWatchNotification`. `FileWatchNotification.EventType` is never set to this value, so a `switch` case testing for it is unreachable. The member is retained only because removing it would break consumers that name it.

---

## Troubleshooting

### No notifications received
- Ensure the module is active (`IsActive == true`). When inactive, all underlying watchers are paused.
- Ensure the specific watch is enabled (`SetWatchEnabled` or `StartEnabled` in options).
- Verify the watched path exists, unless `AllowNonExistingPath` is enabled in the options.
- Check that the relevant event type toggles are enabled (e.g. `NotifyOnChanged`, `NotifyOnCreated`).
- For directory watches, check that your glob patterns match the files you expect.
- For file watches, ensure the full path matches exactly (comparison is case-insensitive).

### Too many repeated notifications
- Enable duplicate suppression via `SetDuplicateSuppression(true)`.
- Increase `DuplicateNotificationWindow` if the default 100 ms is not enough.
- Check the `TotalDuplicateNotificationsSuppressed` statistic to confirm suppression is working.

### Callbacks throw exceptions
- All callback exceptions (sync, async, and CLR event handlers) are caught and logged automatically. They do not crash the module.
- Inspect logs for callback failure messages.
- Check `TotalCallbackExceptionsCaught` in the statistics for a count.
- Keep callbacks short and delegate heavy work to background tasks. Callbacks run on the framework thread, so a slow one stalls the game's frame.
- Prefer async callbacks for IO-bound work.

### Notifications are missing under heavy filesystem activity
- Check `TotalDeliveriesDropped` in the statistics, or the log for a delivery queue capacity warning. Under an event storm the module drops the oldest pending deliveries rather than growing memory (see [Thread Contract](#thread-contract)).
- Handlers that block the framework thread let the queue back up. Move heavy work off the callback.
- Raise `InternalBufferSize` in the options if the underlying watcher itself is overflowing, which surfaces as an `Error` event rather than a drop.

### Watch registration fails
- If `AllowNonExistingPath` is `false` (default), the target directory must exist. For file watches, the parent directory must exist.
- The `Path` property must not be null, empty, or whitespace.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [EventBus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
