
# Module Documentation : NoireUpdateTracker

You are reading the documentation for the `NoireUpdateTracker` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Checking On Demand](#checking-on-demand)
- [Customizing Notifications](#customizing-notifications)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireUpdateTracker` is a module that automatically checks for plugin updates by polling a JSON repository URL. It provides:
- **Automatic update checking** at configurable intervals
- **On-demand checking** with an awaitable manual check
- **Dual notification system** (in-game chat + notification toast)
- **Dynamic content tags** for customizable messages
- **EventBus integration** for reacting to update detection
- **Flexible configuration** for timing and message content
- **Automatic notification suppression** after first display (optional, and resettable)

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Initialize the Module with Your Plugin JSON repository

```csharp
// Add the update tracker with your repo URL
NoireLibMain.AddModule(new NoireUpdateTracker(
    active: true,
    moduleId: "UpdateTrackerModule",
    repoUrl: "https://raw.githubusercontent.com/YourName/YourPlugin/main/repo.json",
    notificationDurationMs: 60000
));
```

That's it! The module will now automatically check for updates every 30 minutes by default.

---

## Configuration

### Module Parameters

Configure the update tracker with the constructor:

```csharp
var updateTracker = new NoireUpdateTracker(
    active: true,                                      // Enable/disable the module
    moduleId: "MyUpdateTracker",                       // Optional identifier
    enableLogging: true,                               // Log update checks and detections
    repoUrl: "https://your-repo-url/repo.json",        // JSON repository URL
    shouldPrintMessageInChatOnUpdate: true,            // Show message in chat
    shouldShowNotificationOnUpdate: true,              // Show notification toast
    message: null,                                     // Custom chat message (null = default)
    notificationTitle: null,                           // Custom notification title (null = default)
    notificationMessage: null,                         // Custom notification message (null = default)
    notificationDurationMs: 30000,                     // The duration of the notification
    eventBus: null,                                    // Optional EventBus instance
    shouldStopNotifyingAfterFirstNotification: true    // Stop checking once an update has been shown
);
```

**Note:** `shouldStopNotifyingAfterFirstNotification` is declared after `eventBus` so that code already passing the
earlier parameters positionally keeps binding them to the same options.

### Property Configuration

Configure the module after creation:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Set repository URL
updateTracker?.SetRepoUrl("https://your-repo-url/repo.json");

// Configure notification behavior
updateTracker?.SetShouldPrintMessageInChatOnUpdate(true);
updateTracker?.SetShouldShowNotificationOnUpdate(true);
updateTracker?.SetShouldStopNotifyingAfterFirstNotification(true);

// Set check interval (in minutes)
updateTracker?.SetCheckIntervalMinutes(60); // Check every hour

// Set the delay before the first check after the timer (re)starts
updateTracker?.SetCheckStartDelayMs(2000); // 2 seconds

// Set notification duration (in milliseconds)
updateTracker?.SetNotificationDurationMs(45000); // 45 seconds

// Reopen the notification gate, so the next detected update is reported again
updateTracker?.ResetUpdateNotification();
```

You can also chain these methods for convenience:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

updateTracker?
    .SetRepoUrl("https://your-repo-url/repo.json")
    .SetCheckIntervalMinutes(60)
    .SetNotificationDurationMs(45000)
    .SetShouldStopNotifyingAfterFirstNotification(true);
```

### Check Interval

Control how often the module checks for updates:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Default is 30 minutes
updateTracker?.SetCheckIntervalMinutes(30);

// Check every hour
updateTracker?.SetCheckIntervalMinutes(60);

// Check every 15 minutes
updateTracker?.SetCheckIntervalMinutes(15);
```

**Note:** Changing the interval while the module is active restarts the timer with the new interval, and the next check
runs `CheckStartDelayMs` later rather than at the end of the interval that was already running.

### Reconfiguration Delay

Every path that starts the check timer (activation, and assigning `RepoUrl` or `CheckIntervalMinutes` while active)
waits `CheckStartDelayMs` before its first check, and every one of them restarts that countdown:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Default is 2 seconds (2000 ms)
updateTracker?.SetCheckStartDelayMs(2000);

// Check the moment the timer starts, accepting one request per configuration change
updateTracker?.SetCheckStartDelayMs(0);
```

This does two things:

1. **Reconfiguring takes effect promptly.** Assigning a new `RepoUrl` checks the new repository ~2 seconds later,
   instead of waiting out a whole `CheckIntervalMinutes` interval before noticing it changed.
2. **A run of changes costs one request.** Because each start restarts the countdown, a URL assigned once per keystroke
   from an ImGui text field produces a single check against the URL that was finally typed, not one request per
   character. A fluent startup chain collapses the same way:

   ```csharp
   // One check, ~2 seconds after the last line, not three.
   updateTracker?
       .SetRepoUrl("https://your-repo-url/repo.json")
       .SetCheckIntervalMinutes(60)
       .SetCheckStartDelayMs(2000);
   ```

Assigning `RepoUrl` the value it already holds is a no-op: it neither restarts the timer nor resets the notification
gate, so writing it every frame from your own configuration costs nothing.

A new `CheckStartDelayMs` applies the next time the timer starts. Applying it any sooner would mean restarting the
timer, which is itself a scheduled check.

### Stop Notifying After First Notification

By default, the module will only notify users once per session:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Stop notifying after first notification (default)
updateTracker?.SetShouldStopNotifyingAfterFirstNotification(true);

// Keep notifying on every check
updateTracker?.SetShouldStopNotifyingAfterFirstNotification(false);
```

#### What counts as "shown"

The gate closes only when a detected update actually reached at least one channel:

| Channel | Counts as shown when |
|---------|----------------------|
| Notification toast | `ShouldShowNotificationOnUpdate` is `true` |
| Chat message | `ShouldPrintMessageInChatOnUpdate` is `true` |
| `NewPluginVersionDetectedEvent` | An `EventBus` is attached |

The EventBus counts because a subscriber receives the detection and decides what to present, which is the same role the
two built-in channels play. A tracker that reports only through the EventBus (see
[Conditional Notifications](#conditional-notifications)) would otherwise never satisfy the gate and would keep polling
forever.

If a newer version is detected while **every** channel is off (both flags `false` and no `EventBus`), nothing is shown
and the gate stays open, so checks keep running and a later detection can still be reported once a channel is
configured.

#### Reopening the gate

The gate closes at most once per shown update, not once per session. `HasShownUpdateNotification` reports whether it is
closed, and two things reopen it:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// 1. Assigning a different RepoUrl. What was shown was an update from the previous
//    repository, and it says nothing about the new one, so this is automatic.
updateTracker?.SetRepoUrl("https://a-different-repo/repo.json");

// 2. Explicitly, when something else the shown notification was about has changed,
//    or to report a still-pending update again after the user dismissed it.
updateTracker?.ResetUpdateNotification();
```

Resetting leaves the automatic schedule alone: the next scheduled check will run. To check immediately, follow it with
[`CheckForUpdatesNowAsync()`](#checking-on-demand).

---

## Checking On Demand

`CheckForUpdatesNowAsync()` runs a check immediately, without waiting for the next scheduled one:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Fire and forget
_ = updateTracker?.CheckForUpdatesNowAsync();

// Or await it, to know when it has finished
await updateTracker!.CheckForUpdatesNowAsync();
```

The returned task completes once the check has finished and its notifications have been delivered, which is what lets a
"Check for updates" button re-enable itself:

```csharp
if (ImGui.Button("Check for updates") && !checkInFlight)
{
    checkInFlight = true;

    // Reset first, so a still-pending update the user has already been told about is
    // reported again rather than silently skipped by the notification gate.
    _ = updateTracker!
        .ResetUpdateNotification()
        .CheckForUpdatesNowAsync()
        .ContinueWith(_ => checkInFlight = false);
}
```

The task **never faults**: a check reports its own failures (a dead URL, a malformed response, an unparseable version)
through the log, exactly as the scheduled checks do. Discarding it is as safe as awaiting it.

The manual check runs under the same rules as a scheduled one, and so does nothing when:

- the module is inactive (`IsActive == false`),
- `RepoUrl` is not configured,
- NoireLib is not initialized,
- `ShouldStopNotifyingAfterFirstNotification` has already closed on a shown update. Call `ResetUpdateNotification()`
  first to check past that. See [Reopening the gate](#reopening-the-gate).

It leaves the automatic schedule alone: a manual check does not delay, advance, or restart the interval timer.

---

## Customizing Notifications

### Dynamic Content Tags

Use `UpdateTrackerTextTags` to create dynamic messages:

```csharp
// Available tags:
UpdateTrackerTextTags.PluginInternalName    // Replaced with plugin's internal name
UpdateTrackerTextTags.CurrentVersion        // Replaced with current version
UpdateTrackerTextTags.NewVersion            // Replaced with new version
```

### Custom Chat Message

Customize the message shown in chat:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

updateTracker?.SetMessage(
    $"[{UpdateTrackerTextTags.PluginInternalName}] " +
    $"Version {UpdateTrackerTextTags.NewVersion} is available! " +
    $"You're currently on {UpdateTrackerTextTags.CurrentVersion}. " +
    $"Please update via /xlplugins."
);
```

### Custom Notification

Customize the notification title and message:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Custom title
updateTracker?.SetNotificationTitle(
    $"{UpdateTrackerTextTags.PluginInternalName} - New Version Available"
);

// Custom message
updateTracker?.SetNotificationMessage(
    $"A new version is ready!\n" +
    $"Current: {UpdateTrackerTextTags.CurrentVersion}\n" +
    $"Latest: {UpdateTrackerTextTags.NewVersion}\n" +
    $"Update now in /xlplugins!"
);
```

### Default Messages

If you leave `Message`, `NotificationTitle`, or `NotificationMessage` unset (`null`), the module uses these built-in defaults. The `{{...}}` tokens are substituted at display time:

- **Notification title**: `{{PLUGIN_NAME}} Update Available`
- **Notification message**: `{{PLUGIN_NAME}} has a new update available.` with the current and new version on the following lines (`Current version: {{CURRENT_VERSION}}`, `New version: {{NEW_VERSION}}`).
- **Chat message**: `[{{PLUGIN_NAME}}] A new update is available. Please update the plugin in /xlplugins. Current version: {{CURRENT_VERSION}} - New version: {{NEW_VERSION}}.`

### Notification Duration

Control how long the notification stays visible:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Default is 30 seconds (30000 ms)
updateTracker?.SetNotificationDurationMs(30000);

// Show for 1 minute
updateTracker?.SetNotificationDurationMs(60000);

// Show for 15 seconds
updateTracker?.SetNotificationDurationMs(15000);
```

---

## EventBus Integration

The `NoireUpdateTracker` can publish events to a `NoireEventBus` when a new version is detected.

### Quick Example

```csharp
// Create EventBus
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_UpdateTracker");

// Subscribe to update events
eventBus?.Subscribe<NewPluginVersionDetectedEvent>(evt =>
{
    NoireLogger.LogInfo($"Update detected: {evt.CurrentVersion} -> {evt.NewVersion}");
}, owner: this);

// Create UpdateTracker with EventBus
var updateTracker = NoireLibMain.AddModule(new NoireUpdateTracker(
    active: true,
    repoUrl: "https://your-repo-url/repo.json",
    eventBus: eventBus
));
```

### Available Events

#### `NewPluginVersionDetectedEvent`

Published when a new plugin version is detected in the repository.

```csharp
public record NewPluginVersionDetectedEvent(Version CurrentVersion, Version NewVersion);
```

**Properties:**
- `CurrentVersion` - The currently installed version
- `NewVersion` - The new version available in the repository

---

## Advanced Features

### Conditional Notifications

Use EventBus to implement conditional notification logic, or to execute code when an update is detected:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>();

eventBus?.Subscribe<NewPluginVersionDetectedEvent>(evt =>
{
    // Only notify for major version changes
    if (evt.NewVersion.Major > evt.CurrentVersion.Major)
    {
        NoireLogger.PrintToChat(
            XivChatType.Echo,
            "MAJOR UPDATE AVAILABLE! Please update as soon as possible.",
            foregroundColor: ColorHelper.HexToVector3("#FF0000")
        );
    }
    
    // Only notify for minor/patch if user opted in
    else if (Configuration.NotifyForMinorUpdates)
    {
        NoireLogger.PrintToChat(
            XivChatType.Echo,
            "A minor update is available.",
            foregroundColor: ColorHelper.HexToVector3("#FCC203")
        );
    }
}, owner: this);

var updateTracker = new NoireUpdateTracker(
    active: true,
    repoUrl: "https://your-repo-url/repo.json",
    shouldPrintMessageInChatOnUpdate: false, // Disable default
    shouldShowNotificationOnUpdate: false,   // Disable default
    eventBus: eventBus
);
```

---

## Best Practices

1. **Respect user preferences**: Allow users to disable notifications
   ```csharp
   var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();
   
   updateTracker?
       .SetShouldPrintMessageInChatOnUpdate(Configuration.ShowChatMessages)
       .SetShouldShowNotificationOnUpdate(Configuration.ShowNotifications);
   ```

---

## Troubleshooting

### Updates not being detected
- Verify that the module is active (`IsActive == true`)
- Verify that `RepoUrl` is set. While it is null or whitespace there is nothing to fetch, so the check timer stays
  stopped; it starts as soon as a URL is assigned on an active module
- Verify that your `repo.json` URL is accessible (test in a browser)
- Ensure that `AssemblyVersion` in your repo is higher than the current plugin version
- Check that `InternalName` in your repo matches your plugin's internal name exactly
- Check `HasShownUpdateNotification`. If it is `true` and `ShouldStopNotifyingAfterFirstNotification` is enabled, no
  further check will run. See [Reopening the gate](#reopening-the-gate)
- Look for `Cannot check for updates: NoireLib is not initialized.` in the log. The module can be constructed before
  `NoireLibMain.Initialize(...)`, but a check cannot run until NoireLib is initialized
- Enable logging and check the Dalamud logs with `/xllog`
- Use `CheckForUpdatesNowAsync()` to run a check on demand rather than waiting out the interval while diagnosing

### Notifications not showing
- Verify that the module is active (`IsActive == true`)
- Check that `ShouldPrintMessageInChatOnUpdate` and `ShouldShowNotificationOnUpdate` are enabled
- If `ShouldStopNotifyingAfterFirstNotification` is true, the notification may have already been shown. Check
  `HasShownUpdateNotification`, and see [What counts as "shown"](#what-counts-as-shown)
- Check for exceptions in Dalamud logs

### Wrong version comparison
- Ensure your plugin's assembly version is set correctly in your `.csproj` file
- Check that version parsing isn't failing (enable logging to see errors)

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [EventBus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
