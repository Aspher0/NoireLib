
# Module Documentation : NoireUpdateTracker

You are reading the documentation for the `NoireUpdateTracker` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
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
- **Dual notification system** (in-game chat + notification toast)
- **Dynamic content tags** for customizable messages
- **EventBus integration** for reacting to update detection
- **Flexible configuration** for timing and message content
- **Automatic notification suppression** after first display (optional)

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
    eventBus: null                                     // Optional EventBus instance
);
```

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

// Set notification duration (in milliseconds)
updateTracker?.SetNotificationDurationMs(45000); // 45 seconds
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

**Note:** Changing the interval while the module is active will restart the timer immediately with the new interval.

### Stop Notifying After First Notification

By default, the module will only notify users once per session:

```csharp
var updateTracker = NoireLibMain.GetModule<NoireUpdateTracker>();

// Stop notifying after first notification (default)
updateTracker?.SetShouldStopNotifyingAfterFirstNotification(true);

// Keep notifying on every check
updateTracker?.SetShouldStopNotifyingAfterFirstNotification(false);
```

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

If you set messages to `null`, default messages will be used:

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
- Verify that your `repo.json` URL is accessible (test in a browser)
- Ensure that `AssemblyVersion` in your repo is higher than the current plugin version
- Check that `InternalName` in your repo matches your plugin's internal name exactly
- Enable logging and check the Dalamud logs with `/xllog`

### Notifications not showing
- Verify that the module is active (`IsActive == true`)
- Check that `ShouldPrintMessageInChatOnUpdate` and `ShouldShowNotificationOnUpdate` are enabled
- If `ShouldStopNotifyingAfterFirstNotification` is true, the notification may have already been shown
- Check for exceptions in Dalamud logs

### Wrong version comparison
- Ensure your plugin's assembly version is set correctly in your `.csproj` file
- Check that version parsing isn't failing (enable logging to see errors)

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [ChangelogManager Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/ChangelogManager/README.md)
- [EventBus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
