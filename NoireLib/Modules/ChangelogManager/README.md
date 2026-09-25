# Module Documentation : NoireChangelogManager

You are reading the documentation for the `NoireChangelogManager` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Creating Changelogs](#creating-changelogs)
- [Displaying the Changelog Window](#displaying-the-changelog-window)
- [Using Your Own Window](#using-your-own-window)
- [Skinned Window](#skinned-window)
- [Localization](#localization)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`NoireChangelogManager` manages and displays your plugin's changelogs. It provides:
- **Automatic changelog loading** from your assembly
- **Automatic display** of new versions to users
- **Version management** with ordered display (newest to oldest)
- **Rich formatting** with colors, icons, headers, separators, and buttons
- **Your own window** in place of the built-in one, drawn from the module's versions and selection
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

Helper methods create entries (see [Creating Changelogs](#creating-changelogs)).

---

## Configuration

### Module Parameters

The main options go through the constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Changelog");

var changelogManager = new NoireChangelogManager(
    active: true,
    moduleId: "MyChangelog",
    shouldAutomaticallyShowChangelog: true,
    versions: null,     // null loads from your assembly
    eventBus: eventBus
);
```

Properties after creation:

- `ShouldAutomaticallyShowChangelog`: Opens the changelog window when a new version is detected. Default: `false`.
- `DisplayWindowName`: A custom name for the changelog window, set with `SetWindowName`. Default: `"Changelog"`.
- `TitleBarButtons`: Buttons added to the title bar. Default: `empty list`. Modify it through the methods.
- `EventBus`: An EventBus for changelog events. Default: `null`.

### Property Configuration

Or configure it after creation:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

changelogManager?.SetAutomaticallyShowChangelog(true);
changelogManager?.SetWindowName("My Plugin Updates");
changelogManager?.AddTitleBarButton(new TitleBarButton
{
    Icon = FontAwesomeIcon.QuestionCircle,
    Click = (e) => { /* Open help */ },
    IconOffset = new(2, 2),
});
```

The methods chain:
```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

changelogManager?
    .SetAutomaticallyShowChangelog(true)
    .SetWindowName("My Plugin Updates")
    .AddTitleBarButton(new TitleBarButton { /* ... */ });
```

### Automatic Display

With `ShouldAutomaticallyShowChangelog` on, the window opens when a new version is detected.

Otherwise call `ShowWindow()` yourself.

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
        Version = new(1, 0, 0, 0),                   // Required
        Date = "2025-01-01",                         // Required
        Title = "Initial Release",                   // Optional
        TitleColor = Blue,                           // Optional
        Description = "Sample short description.",   // Optional
        Entries = new List<ChangelogEntry>
        {
            // Your changelog entries here
        }
    };

    // Other versions...
}
```

Versions are normalized to 4 components. `new Version(1, 0)` is stored and displayed as `1.0.0.0`.

### Entry Types

#### 1. Headers

Section headers with optional icons:

```csharp
Header("New Features", Green),
Header("Bug Fixes", Red, icon: FontAwesomeIcon.Bug),
Header("Changes", Orange, icon: FontAwesomeIcon.Wrench, iconColor: Blue),
```

#### 2. Regular Entries

Entries with optional indentation and icons:

```csharp
Entry("Added new feature"),
Entry("Fixed critical bug", Red),
Entry("Sub-feature 1", null, indentLevel: 1),
Entry("Sub-feature 2", null, indentLevel: 2),
Entry("With icon", Green, icon: FontAwesomeIcon.Check),
```

#### 3. Separators

Separators between sections:

```csharp
Separator(),
```

#### 4. Buttons

Buttons with custom actions:

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

#### 5. Effects

A bulleted entry drawn with a `NoireGradient`, a `NoireMotion`, or both (see the UI README). The motion never moves the entries around it.

```csharp
private static readonly NoireMotion Wiggle = NoireMotion.Create().Scaling(0.97f, 1.04f).Rocking(2.5f);

EffectEntryBullet("Someone moved in.", NoireGradient.Rainbow, Wiggle, indentLevel: 1),
```

Any entry takes them through its `Gradient` and `Motion` properties.

#### 6. Raw

C# code executed during rendering:
```csharp
Raw(() =>
{
    ImGui.TextColored(Blue, "This is some raw code!"); 
    // Other code, anything really
}),
```

### Available Colors

`BaseChangelogVersion` provides predefined colors. Custom and Dalamud colors work too. See `ColorHelper`.

---

## Displaying the Changelog Window

### Manual Display

Show the window:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

changelogManager?.ShowWindow();
changelogManager?.HideWindow();
changelogManager?.ToggleWindow();

// SetShowWindow(null) toggles.
changelogManager?.SetShowWindow(true);

var isOpen = changelogManager?.IsWindowOpen ?? false;
```

### Show Specific Version

Show a specific version:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

changelogManager?.ShowChangelogForVersion(new Version(1, 0, 0, 0));
changelogManager?.ShowChangelogForVersion();
```

An unknown version falls back to the latest. With no version at all the window stays closed and a notification is raised.

### Automatic Display

With `ShouldAutomaticallyShowChangelog` on, the window opens:
- When the module is activated (`IsActive == true`)
- When a new version is detected against the last seen one
- When no version has been recorded before (first run)

---

## Using Your Own Window

Register any Dalamud `Window` in place of the built-in one. `ShowWindow()`, `HideWindow()`, `ToggleWindow()`, `SetShowWindow()`, `IsWindowOpen`, `ShowChangelogForVersion()` and the automatic display then act on it. Add it to your own `WindowSystem`.

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

changelogManager?.SetCustomWindow(myChangelogWindow);   // every open path now opens myChangelogWindow
changelogManager?.SetCustomWindow(null);                // back to the built-in window
var custom = changelogManager?.CustomWindow;
```

Switching while a window is open closes it and opens the other. Set the window before activating the module for the automatic display to open it.

The window draws from the module:

```csharp
public override void Draw()
{
    foreach (var version in changelogManager.Versions)             // newest first, cached
    {
        if (ImGui.Selectable(version.Version.ToString(), ReferenceEquals(version, changelogManager.SelectedVersion)))
            changelogManager.SelectVersion(version.Version);      // publishes ChangelogVersionChangedEvent
    }

    if (changelogManager.SelectedVersion is { } selected)          // null when there is no version
    {
        foreach (var entry in selected.Entries)
        {
            // entry.Text, TextColor, Icon, IconColor, IsHeader, IsSeparator, IndentLevel, HasBullet,
            // Gradient, Motion, ButtonText, ButtonAction, IsRaw, RawAction
        }
    }

    if (ImGui.Button("Close"))
        changelogManager.CloseWindow();                           // publishes ChangelogWindowClosedEvent

    var autoShow = changelogManager.ShouldAutomaticallyShowChangelog;
    if (ImGui.Checkbox("Show the changelog after an update", ref autoShow))
        changelogManager.SetAutomaticallyShowChangelog(autoShow);
}
```

`ShowChangelogForVersion()` selects the version before opening. The window only reads `SelectedVersion`.

A bullet takes the color of its own text. To draw an entry's `Gradient` and `Motion` the way the built-in window does, open `NoireEffects.Begin(entry.Gradient, entry.Motion)` around its text and bullet.

---

## Skinned Window

Once the plugin registers skins (`NoireSkins.Register`, see the UI README), every open path shows `NoireChangelogWindow`
instead of the built-in window: the same version selector, entries and footer, as components the user can arrange, in
the active skin's frame. A custom window set with `SetCustomWindow` still wins. A window the active skin presents as
`Presentation.Hidden` is passed over, down to the built-in window.

```csharp
public sealed class GlassSkin : NoireSkin
{
    public GlassSkin() : base("glass", L.Glass) => View<NoireChangelogWindow, GlassChangelogView>();
}

public sealed class GlassChangelogView : NoireView<NoireChangelogWindow>
{
    protected override void Draw(NoireChangelogWindow window) => painter.Draw(window.Manager);   // Versions, SelectedVersion
}
```

The built-in window stays in use for a plugin that registers no skin.

---

## Localization

Every title, description, entry text and button text of a changelog is a declared text (see the Localizer README's
Declared Texts): the translation editor lists it and a language file translates it like any other text. Its key is
the version and a checksum of the text:

```
# Reworked how animations are refreshed.
changelog.2.3.0.0.1a2b3c4d = ...
```

The changelog is written in the source language as before. `Versions` and `SelectedVersion` hold the texts in the
active language and follow a language switch; a text with no translation shows as written. Entries can be added,
removed or reordered in a translated version without losing a translation. Editing a text gives it a new key: its
translation is asked for again instead of showing the translation of the old text; the same text twice in one version
shares one translation. The build step of the Localizer README's Template and Build Step section carries that old
translation to the new key, flagged as outdated, and its template lists every key for translators working from files.

---

## EventBus Integration

The module publishes events to a `NoireEventBus` for every changelog action.

### Quick Example

```csharp
// Create EventBus
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Changelog");

// Subscribe before creating the ChangelogManager. It may show the window on initialization.
eventBus?.Subscribe<ChangelogWindowOpenedEvent>(evt =>
{
    NoireLogger.LogInfo($"Changelog opened for version {evt.Version}");
}, owner: this);

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

Custom title bar buttons:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

changelogManager?.AddTitleBarButton(new TitleBarButton
{
    Icon = FontAwesomeIcon.Cog,
    IconOffset = new(2, 2),
    Click = (e) => { /* Open settings */ },
});

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

changelogManager?.RemoveTitleBarButton(0);
changelogManager?.ClearTitleBarButtons();
```

### Version Management

Manage versions by hand:

```csharp
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>();

var versions = changelogManager?.GetAllVersions();
var version = changelogManager?.GetVersion(new Version(1, 0, 0, 0));
var latest = changelogManager?.GetLatestVersion();

changelogManager?.AddVersion(new ChangelogVersion
{
    Version = new Version(1, 3, 0, 0),
    Date = "2024-04-01",
    Entries = new List<ChangelogEntry> { /* ... */ }
});

changelogManager?.AddVersions(versionsList);
changelogManager?.RemoveVersion(new Version(1, 0, 0, 0));
changelogManager?.ClearVersions();
```

---

## Troubleshooting

### Changelog doesn't appear
- Ensure NoireLib is initialized before adding the module.
- Confirm the module is active (`IsActive == true`).
- Check that your changelog class inherits from `BaseChangelogVersion`.
- The changelog class must be in your plugin's assembly.
- Make sure `GetVersions()` returns a non-empty list.
- Check `/xllog`.
- If it still does not work, please report it.

### Automatic display not working
- Set `ShouldAutomaticallyShowChangelog = true`.
- Ensure the module is active (`IsActive = true`).
- Check that a new version is detected against the last seen one.
- Call `ClearLastSeenVersion()` with `ShouldAutomaticallyShowChangelog = true` and check the window on the next initialization.

### My own window does not open
- Call `SetCustomWindow()` before `Activate()` when the automatic display must open it.
- Add the window to your plugin's `WindowSystem`. The module only toggles `IsOpen`.

### EventBus events not firing
- Give the ChangelogManager an `EventBus`, in the constructor or through the property.
- Check that the EventBus is active and has subscribers.
- Enable EventBus logging with `enableLogging: true`.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
