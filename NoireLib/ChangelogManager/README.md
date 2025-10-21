# Module Documentation : NoireChangelogManager

You are reading the documentation for the `NoireChangelogManager` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Creating Changelogs](#creating-changelogs)
- [Displaying the Changelog Window](#displaying-the-changelog-window)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireChangelogManager` is a module that manages and displays changelogs for your plugin. It provides:
- **Automatic changelog loading** from your assembly
- **Automatic display** of new versions to users
- **Version management** with ordered display (newest to oldest)
- **Rich formatting** with colors, icons, headers, separators, and buttons
- **EventBus integration** for reacting to changelog actions

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create Your First Changelog

Create a new class that inherits from `BaseChangelogVersion`.
It needs to implement the `GetVersions()` method, returning a list of `ChangelogVersion`:

```csharp
using NoireLib.Changelog;
using System.Collections.Generic;

namespace MyPlugin.Changelog;

public class MyChangelog : BaseChangelogVersion
{
    public override List<ChangelogVersion> GetVersions() => new()
    {
        V1_0_0_0(),
        //...
    };

    private static ChangelogVersion V1_0_0_0() => new()
    {
        Version = new(0, 0, 0, 1),
        Date = "2025-01-01",
        Title = "Initial Release",
        TitleColor = Blue,
        Description = "Sample short description.",
        Entries = new List<ChangelogEntry>
        {
            Header("New Features", Green),
            Entry("Feature 1: Amazing functionality"),
            Entry("Feature 2: Cool new tool"),
                Entry("Feature 2.1: ...", null, 1),
                Entry("Feature 2.2: ...", null, 1),

            Separator(),

            Header("Known Issues", Orange),
            Entry("Minor bug with settings UI"),

            Button("Check out the GitHub Repo", null, "Click me!", White, Blue, (e) => { CommonHelper.OpenUrl("https://github.com/Aspher0/NoireLib"); }),

            Raw(() => { ImGui.TextColored(Blue, "This is some raw code!"); }),
        }
    };
}
```

Helper methods are provided to create entries easily (see [Creating Changelogs](#creating-changelogs) for details).<br/>
That's it! You have created your first changelog.

---

## Configuration

### Module Parameters

You can configure the most important options of the module with the module's constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Changelog"); // Optional

var changelogManager = new NoireChangelogManager(
    active: true,                               // Enable/disable the module
    moduleId: "MyChangelog",                    // Optional identifier
    shouldAutomaticallyShowChangelog: true,     // Auto-show on new versions
    versions: null,                             // Optional pre-loaded list of versions, if null, it loads from your assembly
    eventBus: eventBus                          // Optional EventBus for publishing events
);
```

Additionnaly, you can modify the following properties after having created the module:

- `ShouldAutomaticallyShowChangelog`: If true, the changelog window will automatically open when a new version is detected. Default: `false`.
- `WindowName`: Optional custom name for the changelog window. Default: `"Changelog"`.
- `TitleBarButtons`: Optional list of buttons to add to the title bar. Default: `empty list`. Use methods to modify.
- `EventBus`: Optional EventBus instance for publishing changelog events. Default: `null`.

You can also use the provided methods to modify the module configuration after creation (see [Property Configuration](#property-configuration)).

### Property Configuration

You can also configure the module after creation:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

// Set automatic display behavior
changelogManager?.SetAutomaticallyShowChangelog(true);

// Change window name
changelogManager?.SetWindowName("My Plugin Updates");

// Add title bar buttons
changelogManager?.AddTitleBarButton(new TitleBarButton
{
    Icon = FontAwesomeIcon.QuestionCircle,
    Click = (e) => { /* Open help */ },
    IconOffset = new(2, 2),
});
```

You can also chain these methods for convenience:
```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

changelogManager?
    .SetAutomaticallyShowChangelog(true)
    .SetWindowName("My Plugin Updates")
    .AddTitleBarButton(new TitleBarButton { /* ... */ });
```

### Automatic Display

When `ShouldAutomaticallyShowChangelog` is enabled:
- The changelog window opens automatically when a new version is detected
- Therefore, when users update the plugin, they will see the changelog for the new version

If you would rather control when the window is shown, you can disable this feature and call `ShowChangelogWindow()` manually.

---

## Creating Changelogs

### Basic Structure

Every changelog class inherits from `BaseChangelogVersion` and implements `GetVersions()`:

```csharp
public class MyChangelog : BaseChangelogVersion
{
    public override List<ChangelogVersion> GetVersions() => new()
    {
        V1_0_0_0(),
        V1_1_0_0(),
        V1_2_0_0(),
    };

    private static ChangelogVersion V1_0_0_0() => new()
    {
        Version = new(1, 0, 0, 0),                   // Required: Version object, always displayed as Major.Minor.Build.Revision
        Date = "2025-01-01",                         // Required: Release date
        Title = "Initial Release",                   // Optional: Version title
        TitleColor = Blue,                           // Optional: Title color
        Description = "Sample short description.",   // Optional: Short description
        Entries = new List<ChangelogEntry>
        {
            // Your changelog entries here
        }
    };

    // Other versions...
}
```

**Note:** Versions are automatically normalized to 4 components. If you create a version with `new Version(1, 0)`, it will be stored and displayed as `1.0.0.0`.

### Entry Types

#### 1. Headers

Creates section headers with optional icons:

```csharp
Header("New Features", Green),
Header("Bug Fixes", Red, FontAwesomeIcon.Bug),
Header("Changes", Orange, FontAwesomeIcon.Wrench, Blue),
```

#### 2. Regular Entries

Standard changelog entries with optional indentation and icons:

```csharp
Entry("Added new feature"),
Entry("Fixed critical bug", Red),
Entry("Sub-feature 1", null, indentLevel: 1),
Entry("Sub-feature 2", null, indentLevel: 2),
Entry("With icon", Green, icon: FontAwesomeIcon.Check),
```

#### 3. Separators

Add visual separators between sections:

```csharp
Separator(),
```

#### 4. Buttons

Interactive buttons with custom actions:

```csharp
Button(
    text: "Learn more",
    textColor: White,
    buttonText: "Click Here",
    buttonTextColor: White,
    buttonColor: Blue,
    action: (e) => 
    {
        if (e == ImGuiMouseButton.Left)
        {
            // Open URL, show dialog, etc.
        }
    },
    icon: FontAwesomeIcon.ExternalLinkAlt
),
```

#### 5. Raw

Raw C# code that will be executed during rendering:
```csharp
Raw(() =>
{
    ImGui.TextColored(Blue, "This is some raw code!"); 
    // Other code, anything really
}),
```

### Available Colors

The base changelog class (`BaseChangelogVersion`) provides predefined colors. Alternatively, can also use custom/dalamud colors.
Refer to the `ColorHelper` class for useful utility methods.

---

## Displaying the Changelog Window

### Manual Display

Show the changelog window programmatically:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

// Toggle the changelog window
changelogManager?.ShowChangelogWindow();

// Force show the changelog window
changelogManager?.ShowChangelogWindow(true);

// Force hide the changelog window
changelogManager?.ShowChangelogWindow(false);
```

### Show Specific Version

Display a specific version in the changelog:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();
changelogManager?.ShowChangelogWindow(true, new(1, 0, 0, 0));
```

### Automatic Display

When `ShouldAutomaticallyShowChangelog` is enabled, the window automatically opens for new versions. This happens:
- When the module is activated (`IsActive == true`)
- When a new version is detected (compared to last seen version)
- When no version has been recorded before (first run)

---

## EventBus Integration

The `NoireChangelogManager` can publish events to a `NoireEventBus` for all important changelog actions.<br/>
This allows you to react to user interactions with the changelog.

### Quick Example

```csharp
// Create EventBus
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Changelog");

// Subscribe to changelog events **before** creating the ChangelogManager, since it will be showing the window on initialization
eventBus?.Subscribe<ChangelogWindowOpenedEvent>(evt =>
{
    NoireLogger.LogInfo($"Changelog opened for version {evt.Version}");
}, owner: this);

// Create ChangelogManager with EventBus
var changelogManager = NoireLibMain.AddModule(new NoireChangelogManager(
    active: true,
    shouldAutomaticallyShowChangelog: true,
    eventBus: eventBus
));
```

### Available Events

- `ChangelogWindowOpenedEvent` - Window opened
- `ChangelogWindowClosedEvent` - Window closed
- `ChangelogVersionChangedEvent` - User changed version
- `ChangelogVersionAddedEvent` - Version added
- `ChangelogVersionRemovedEvent` - Version removed
- `ChangelogVersionsClearedEvent` - All versions cleared
- `ChangelogLastSeenVersionUpdatedEvent` - Last seen version updated
- `ChangelogLastSeenVersionClearedEvent` - Last seen version cleared

---

## Advanced Features

### Title Bar Buttons

Add custom buttons to the changelog window's title bar:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

// Add a single button
changelogManager?.AddTitleBarButton(new TitleBarButton
{
    Icon = FontAwesomeIcon.Cog,
    IconOffset = new(2, 2),
    Click = (e) => { /* Open settings */ },
});

// Set multiple buttons
changelogManager?.SetTitleBarButtons(new List<TitleBarButton>
{
    new() { Icon = FontAwesomeIcon.Home, IconOffset = new(2, 2), Click = (e) => { /* Home */ } },
    new() 
    {
        Icon = FontAwesomeIcon.QuestionCircle,
        IconOffset = new(2, 2),
        Click = (e) => { /* Help */ },
        ShowTooltip = () => { ImGui.SetTooltip("Right click to join the discord server."); }
    },
});

// Remove button by index
changelogManager?.RemoveTitleBarButton(0);

// Clear all buttons
changelogManager?.ClearTitleBarButtons();
```

### Version Management

Manually manage versions:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

// Get all versions (newest to oldest)
var versions = changelogManager?.GetAllVersions();

// Get specific version
var version = changelogManager?.GetVersion(new Version(1, 0, 0, 0));

// Get latest version
var latest = changelogManager?.GetLatestVersion();

// Add version manually
changelogManager?.AddVersion(new ChangelogVersion
{
    Version = new Version(1, 3, 0, 0),
    Date = "2024-04-01",
    Entries = new List<ChangelogEntry> { /* ... */ }
});

// Add multiple versions
changelogManager?.AddVersions(versionsList);

// Remove version
changelogManager?.RemoveVersion(new Version(1, 0, 0, 0));

// Clear all versions
changelogManager?.ClearVersions();
```

---

## Troubleshooting

### Changelog doesn't appear
- Ensure NoireLib is initialized before adding the module.
- Confirm the module is active (`IsActive == true`).
- Check that your changelog class inherits from `BaseChangelogVersion`.
- Verify the changelog class is in the same assembly (project) as your plugin.
- Make sure `GetVersions()` returns a non-empty list.
- Check the dalamud logs with `/xllog`.
- If it still does not work, please report it.

### Automatic display not working
- Set `ShouldAutomaticallyShowChangelog = true`.
- Ensure the module is active (`IsActive = true`).
- Check that a new version is detected (compare with last seen version).
- Additionnaly, you can manually call `ClearLastSeenVersion()`, set `ShouldAutomaticallyShowChangelog = true`, and check if the changelog window appears the next time the module is initialized.

### EventBus events not firing
- Ensure an `EventBus` is provided to the ChangelogManager (either in constructor or via property).
- Check that the EventBus is active and has subscribers.
- Enable EventBus logging with `enableLogging: true` for debugging.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/EventBus/README.md)
