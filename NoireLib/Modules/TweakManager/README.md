# Module Documentation : NoireTweakManager

You are reading the documentation for the `NoireTweakManager` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Writing Tweaks](#writing-tweaks)
- [Tweak Configuration](#tweak-configuration)
- [Attributes](#attributes)
- [Managing Tweaks](#managing-tweaks)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireTweakManager` is a module that manages a collection of tweaks, which are small toggleable features that
users turn on and off individually. It provides:
- **Automatic discovery** of tweak classes from your plugin assembly
- **Persistent enabled states** restored on every launch
- **Typed per-tweak configuration** with versioning and migrations
- **Key migration** so renaming a tweak never costs users their settings or their favorites
- **Error isolation**, so a tweak that throws is reported instead of taking the plugin with it
- **A full management window** with search, favorites, and per-tweak settings panels
- **EventBus integration** for reacting to tweak actions

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Write Your First Tweak

Create a class inheriting from `TweakBase`. It must provide an `InternalKey`, a `Name`, a `Description`, and the
`OnEnable`/`OnDisable` pair that hooks and unhooks whatever the tweak does:

```csharp
using NoireLib.TweakManager;
using System.Collections.Generic;

namespace MyPlugin.Tweaks;

public class GreetOnLoginTweak : TweakBase
{
    public override string InternalKey => "MyPlugin_Tweak_GreetOnLogin";
    public override string Name => "Greet on login";
    public override string Description => "Prints a greeting to chat when you log in.";
    public override IReadOnlyList<string> Tags => ["Chat"];

    protected override void OnEnable()
    {
        NoireService.ClientState.Login += PrintGreeting;
    }

    protected override void OnDisable()
    {
        NoireService.ClientState.Login -= PrintGreeting;
    }

    private void PrintGreeting() => NoireLogger.PrintToChat("Welcome back!");
}
```

### 2. Add the Module

That is all the wiring there is. The module scans your plugin assembly on initialization, finds the class, and
registers it:

```csharp
var tweakManager = NoireLibMain.AddModule<NoireTweakManager>();
```

Your tweak now appears in the tweak manager window, and whichever state the user chooses is persisted and restored
the next time your plugin starts.

---

## Configuration

### Module Parameters

You can configure the most important options of the module with the module's constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Tweaks"); // Optional

var tweakManager = NoireLibMain.AddModule(new NoireTweakManager(
    moduleId: "MyTweaks",           // Optional identifier
    active: true,                   // Enable/disable the module
    enableLogging: true,            // Whether this module logs its actions
    automaticPersistence: true,     // Whether tweak states and configs are written to disk automatically
    additionalTweaks: null,         // Optional extra tweaks to register alongside the auto-discovered ones
    eventBus: eventBus              // Optional EventBus for publishing events
));
```

Additionnaly, you can modify the following properties after having created the module:

- `AutomaticPersistence`: If true, tweak states and configs are written to disk automatically. When false, the module
  writes nothing on its own, `GetAllTweakConfigs()` hands you the state for your own persistence, and `SaveTweakConfig`
  and `SaveAllTweakConfigs` still write when you call them. Default: `true`.
- `EventBus`: Optional EventBus instance for publishing tweak events. Default: `null`.
- `IsActive`: Whether the module is active. While false, every tweak is unhooked and the window stays closed.
  Default: `true`.
- `EnableLogging`: Whether this module logs its actions. Warnings and errors are logged regardless. Default: `true`.
- `ModuleId`: The optional identifier used to tell several instances apart. Default: `null`.
- `DisplayWindowName`: The name of the tweak manager window (set it with `SetWindowName`). Default: `"Tweak Manager"`.
- `TitleBarButtons`: Optional list of buttons to add to the title bar. Default: `empty list`. Use methods to modify.

You can also use the provided methods to modify the module configuration after creation (see
[Property Configuration](#property-configuration)).

### Property Configuration

You can also configure the module after creation:

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// Take over persistence yourself
tweakManager?.SetAutomaticPersistence(false);

// Change the window name
tweakManager?.SetWindowName("My Plugin Tweaks");

// Activate or deactivate the module
tweakManager?.SetActive(true);

// Silence the module's informational logging
tweakManager?.SetEnableLogging(false);
```

You can also chain these methods for convenience:
```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

tweakManager?
    .SetAutomaticPersistence(true)
    .SetWindowName("My Plugin Tweaks")
    .SetActive(true);
```

### Automatic Persistence

When `AutomaticPersistence` is enabled (the default), the module writes to its own configuration file whenever the
user's choices change: enabling or disabling a tweak, favoriting one, or a tweak calling `MarkConfigDirty()`. A key
migration that moves something is written too, so the move is made once rather than redone on every load.

Only a user's own decision is recorded. Deactivating the module, unregistering a tweak, clearing the manager, and
disposing all unhook the tweaks that are running without recording them as turned off, so the set the user chose comes
back intact on the next activation.

One operation costs one write, however many tweaks it covers. `EnableAllTweaks()`, `DisableAllTweaks()`,
`EnableTweaks(...)`, `DisableTweaks(...)`, `ImportTweakConfigs(...)` and `SaveAllTweakConfigs()` each write the file
once rather than once per tweak, and every tweak they cover still reports its own `TweakConfigSavedEvent`.

Set it to false to manage persistence yourself. The state stays available through `GetAllTweakConfigs()`, and
`ImportTweakConfigs(...)` puts it back.

The setting governs the writes the module makes on its own, not the ones you ask for by name. With it off, nothing is
written by enabling, disabling, favoriting, `MarkConfigDirty()`, an import, or a key migration, while
`SaveTweakConfig(...)` and `SaveAllTweakConfigs()` write when called. Turning automatic persistence off is therefore a
way to decide when writes happen, not a refusal to ever write.

---

## Writing Tweaks

### Basic Structure

Every tweak inherits from `TweakBase` and implements four members:

```csharp
public class MyTweak : TweakBase
{
    public override string InternalKey => "MyPlugin_Tweak_MyTweak";  // Required: unique, persisted, never shown
    public override string Name => "My tweak";                       // Required: shown in the list
    public override string Description => "What this tweak does.";   // Required: shown in the details panel

    protected override void OnEnable() { /* Hook things up */ }
    protected override void OnDisable() { /* Take them back down */ }
}
```

The `InternalKey` is the key everything a user builds up for this tweak is stored under, so it must be unique within
the manager and stable across releases. If you do have to change it, see
[TweakKeyMigration](#1-tweakkeymigration).

`OnEnable` and `OnDisable` must be symmetrical: whatever the first hooks, the second unhooks. The module calls
`OnDisable` on deactivation and disposal, so a tweak that leaves something behind leaks it.

### Optional Members

- `Tags`: categories for the tweak, shown and searchable in the list. Default: empty.
- `ShouldShow`: return false to hide the tweak from the list entirely. Default: `true`.
- `HasConfigurationUi`: whether the details panel shows a settings section. Defaults to whether the tweak has a typed
  config, so override it to `true` for a configless tweak that still draws something.
- `DrawConfigUI()`: draws the tweak's own settings into the details panel.
- `DisposeManaged()`: releases anything the tweak holds. Called after `OnDisable` during disposal.

```csharp
public class MyTweak : TweakBase
{
    public override string InternalKey => "MyPlugin_Tweak_MyTweak";
    public override string Name => "My tweak";
    public override string Description => "What this tweak does.";
    public override IReadOnlyList<string> Tags => ["Combat", "UI"];
    public override bool HasConfigurationUi => true;

    protected override void OnEnable() { }
    protected override void OnDisable() { }

    public override void DrawConfigUI()
    {
        ImGui.TextUnformatted("Anything you want to draw.");
    }

    protected override void DisposeManaged()
    {
        // Release whatever this tweak holds.
    }
}
```

### Reading Tweak State

A tweak exposes what the manager knows about it:

```csharp
var tweak = tweakManager?.GetTweak<MyTweak>();

var isRunning = tweak?.Enabled;                  // Whether the tweak is currently hooked up
var hasError = tweak?.HasError;                  // Whether OnEnable or OnDisable threw
var error = tweak?.LastError;                    // The exception, if any
var locked = tweak?.IsGloballyDisabled;          // Whether [TweakDisabled] is on the class
var reason = tweak?.GloballyDisabledReason;      // The reason given to [TweakDisabled]
var manager = tweak?.Manager;                    // The manager this tweak is registered with
```

If `OnEnable` throws, the tweak is left disabled, the exception is recorded on `LastError`, and a `TweakErrorEvent`
is published. The rest of the tweaks are unaffected.

---

## Tweak Configuration

For a tweak that needs persistent settings of its own, inherit from `TweakBase<TConfig>` instead. The config is
created, serialized, deserialized, and migrated by the module.

### 1. Declare the Config

The config class inherits `TweakConfigBase` and overrides `Version`. Settings must be public properties with both a
getter and a setter:

```csharp
using NoireLib.TweakManager;

public class MyTweakConfig : TweakConfigBase
{
    public override int Version { get; set; } = 1;

    public bool ShowNotification { get; set; } = true;
    public int Threshold { get; set; } = 5;
    public string Label { get; set; } = "Default";
}
```

Tweak configs are not files. `Load()`, `Delete()`, and `Exists()` throw or return false by design, and `Save()` asks
the manager to persist the tweak instead of writing anything itself. Use `ToJson()` for a read-only snapshot.

### 2. Use It From the Tweak

The typed instance is available as `Config`. Change a property, then call `MarkConfigDirty()` to have the manager
record it:

```csharp
public class MyTweak : TweakBase<MyTweakConfig>
{
    public override string InternalKey => "MyPlugin_Tweak_MyTweak";
    public override string Name => "My tweak";
    public override string Description => "What this tweak does.";

    protected override void OnEnable() { }
    protected override void OnDisable() { }

    public override void DrawConfigUI()
    {
        var showNotification = Config.ShowNotification;
        if (ImGui.Checkbox("Show a notification", ref showNotification))
        {
            Config.ShowNotification = showNotification;
            MarkConfigDirty();
        }

        var threshold = Config.Threshold;
        if (ImGui.SliderInt("Threshold", ref threshold, 0, 10))
        {
            Config.Threshold = threshold;
            MarkConfigDirty();
        }
    }
}
```

`Config.Save()` does the same thing as `MarkConfigDirty()` and is there for code that only holds the config. Both are
the tweak reporting a change for the module to record, so they follow `AutomaticPersistence`. To write regardless of
that setting, the consumer-facing `SaveTweakConfig(internalKey)` on the manager is the call that asks for a write.

### 3. Migrate It

Tweak configs use the same forward-only migration support as any other NoireLib config. Raise `Version` and add a
migration covering the step, as a nested class inheriting `ConfigMigrationBase`:

```csharp
using Newtonsoft.Json.Linq;
using NoireLib.Configuration.Migrations;
using NoireLib.TweakManager;

public class MyTweakConfig : TweakConfigBase
{
    public override int Version { get; set; } = 2;

    public bool ShowNotification { get; set; } = true;
    public int Threshold { get; set; } = 5;

    // Migration from version 1 to version 2: the option was renamed
    private class MigrationV1ToV2 : ConfigMigrationBase
    {
        public override int FromVersion => 1;
        public override int ToVersion => 2;

        public override string Migrate(JObject jsonObject)
        {
            return MigrationBuilder.Create()
                .RenameProperty("ShowPopup", "ShowNotification")
                .Migrate(jsonObject, ToVersion);
        }
    }
}
```

Nested migrations are discovered automatically. A migration that lives elsewhere is declared on the config class with
`[ConfigMigration(typeof(MyMigration))]`, or registered at runtime with
`NoireConfigManager.RegisterMigration<MyTweakConfig>(new MyMigration())`.

A config whose stored version is older than the class's `Version` is migrated as it is read back. A config that fails
to deserialize falls back to a fresh instance with default values rather than failing the load.

---

## Attributes

### 1. TweakKeyMigration

An `InternalKey` is the key everything a user has for a tweak is stored under, so changing it would otherwise lose all
of it. Declaring the previous key moves it across on the next registration:

```csharp
[TweakKeyMigration("MyPlugin_Tweak_OldName")]
public class MyTweak : TweakBase
{
    public override string InternalKey => "MyPlugin_Tweak_NewName";
    // ...
}
```

The migration moves everything the old key holds, which is the enabled state, the serialized config, and the user's
favorite. They move together or not at all. Nothing is moved if the new key already holds data of its own, since that
data belongs to the tweak already using it.

The attribute can be applied several times if a tweak has been renamed more than once:

```csharp
[TweakKeyMigration("MyPlugin_Tweak_FirstName")]
[TweakKeyMigration("MyPlugin_Tweak_SecondName")]
public class MyTweak : TweakBase { /* ... */ }
```

You can also register migrations at runtime rather than on the class, which is useful for tweaks you build
dynamically (see [Key Migrations](#key-migrations)).

### 2. TweakDisabled

Marks a tweak as globally disabled, so users cannot enable it. The module also turns it off in the configuration if it
was on, so a tweak locked in a later release does not keep running:

```csharp
// Hidden from the list entirely
[TweakDisabled]
public class MyBrokenTweak : TweakBase { /* ... */ }

// Shown in the list in red with the reason as a tooltip, but not toggleable
[TweakDisabled("Broken by the latest game patch.", showInList: true)]
public class MyOutdatedTweak : TweakBase { /* ... */ }
```

The reason and the visibility are readable from the tweak as `GloballyDisabledReason` and `ShowWhenDisabled`.

---

## Managing Tweaks

### Enabling and Disabling

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// By key
tweakManager?.EnableTweak("MyPlugin_Tweak_MyTweak");
tweakManager?.DisableTweak("MyPlugin_Tweak_MyTweak");
tweakManager?.ToggleTweak("MyPlugin_Tweak_MyTweak");

// By type
tweakManager?.EnableTweak<MyTweak>();
tweakManager?.DisableTweak<MyTweak>();
tweakManager?.ToggleTweak<MyTweak>();

// Several at once, in a single write
tweakManager?.EnableTweaks("MyPlugin_Tweak_A", "MyPlugin_Tweak_B");
tweakManager?.DisableTweaks("MyPlugin_Tweak_A", "MyPlugin_Tweak_B");

// Everything eligible
tweakManager?.EnableAllTweaks();
tweakManager?.DisableAllTweaks();
```

`EnableTweak` and `DisableTweak` return whether the operation succeeded. A key that is not registered, or a tweak
carrying `[TweakDisabled]`, returns false.

### Querying

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// Find a tweak
var byKey = tweakManager?.GetTweak("MyPlugin_Tweak_MyTweak");
var typedByKey = tweakManager?.GetTweak<MyTweak>("MyPlugin_Tweak_MyTweak");
var byType = tweakManager?.GetTweak<MyTweak>();

// List them
var all = tweakManager?.GetAllTweaks();
var enabled = tweakManager?.GetEnabledTweaks();
var favorites = tweakManager?.GetFavoriteTweaks();
var errored = tweakManager?.GetErroredTweaks();
```

### Favorites

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

var isFavorite = tweakManager?.IsFavorite("MyPlugin_Tweak_MyTweak");
tweakManager?.SetFavorite("MyPlugin_Tweak_MyTweak", true);
tweakManager?.ToggleFavorite("MyPlugin_Tweak_MyTweak");
```

`SetFavorite` and `ToggleFavorite` return false for a key that is not registered. Starring a tweak is a decision the
user made, so it is recorded on the same terms as enabling one: written when `AutomaticPersistence` is on, and part of
the single write of the operation it belongs to.

### Registration

Tweaks in your plugin assembly are discovered automatically. Use these for tweaks that are not, such as ones you build
at runtime:

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// Add
tweakManager?.RegisterTweak(new MyTweak());
tweakManager?.RegisterTweaks(new List<TweakBase> { new MyTweak(), new MyOtherTweak() });

// Remove, disposing the tweak. Its persisted state is kept, so registering it again restores it.
var removed = tweakManager?.UnregisterTweak("MyPlugin_Tweak_MyTweak");

// Remove everything, disposing each tweak
tweakManager?.ClearTweaks();

// Scan another assembly
tweakManager?.LoadTweaksFromAssembly(typeof(SomeTypeInThatAssembly).Assembly);
```

Registering a key that is already registered logs a warning and is skipped.

### Window

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// Show, hide or toggle from a single call (null toggles)
tweakManager?.ShowWindow(true);
tweakManager?.ShowWindow(false);
tweakManager?.ShowWindow(null);

// Or the individual calls
tweakManager?.ShowWindow();
tweakManager?.HideWindow();
tweakManager?.ToggleWindow();

// Check if the window is open
var isOpen = tweakManager?.IsWindowOpen ?? false;
```

The window only opens while the module is active, and closes when the module is deactivated.

---

## EventBus Integration

The `NoireTweakManager` can publish events to a `NoireEventBus` for all important tweak actions.<br/>
This allows you to react to what users turn on and off without polling the manager.

### Quick Example

```csharp
// Create EventBus
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Tweaks");

// Subscribe before creating the TweakManager, since it registers tweaks on initialization
eventBus?.Subscribe<TweakEnabledEvent>(evt =>
{
    NoireLogger.LogInfo($"Tweak {evt.Name} ({evt.InternalKey}) was enabled.");
}, owner: this);

eventBus?.Subscribe<TweakErrorEvent>(evt =>
{
    NoireLogger.LogError(evt.Error, $"Tweak {evt.Name} failed.");
}, owner: this);

// Create the TweakManager with the EventBus
var tweakManager = NoireLibMain.AddModule(new NoireTweakManager(
    active: true,
    eventBus: eventBus
));
```

### Available Events

- `TweakEnabledEvent` - A tweak was enabled
- `TweakDisabledEvent` - A tweak was disabled
- `TweakErrorEvent` - A tweak threw while being enabled or disabled
- `TweakRegisteredEvent` - A tweak was registered with the manager
- `TweakUnregisteredEvent` - A tweak was unregistered from the manager
- `TweaksClearedEvent` - All tweaks were cleared from the manager
- `TweakSelectedEvent` - A tweak was selected in the window
- `TweakWindowOpenedEvent` - The tweak manager window opened
- `TweakWindowClosedEvent` - The tweak manager window closed
- `TweakConfigSavedEvent` - A tweak's configuration was written
- `TweakKeyMigrationsExecutedEvent` - Key migrations were applied

---

## Advanced Features

### Manual Persistence

With `AutomaticPersistence` off, the state is yours to store and put back:

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// A snapshot of every tweak's state, live tweaks reported over the stored entry. Reading does not write.
Dictionary<string, TweakConfigEntry> configs = tweakManager!.GetAllTweakConfigs();

foreach (var (internalKey, entry) in configs)
{
    var enabled = entry.Enabled;
    var json = entry.ConfigJson;
    var version = entry.ConfigVersion;
}

// Put a snapshot back and load it into the registered tweaks
tweakManager.ImportTweakConfigs(configs);
```

An import restores state you are holding rather than asking for a write, so it follows `AutomaticPersistence` like any
other change: with the setting off it is applied in memory only, which is what you want when your own store is the one
that matters.

You can force a write at any time, whatever the setting says:

```csharp
// Write one tweak
tweakManager?.SaveTweakConfig("MyPlugin_Tweak_MyTweak");

// Write every registered tweak, in a single write
tweakManager?.SaveAllTweakConfigs();
```

This is the difference between the two halves of the module's persistence: a write it makes on its own follows
`AutomaticPersistence`, and a write you request by name is carried out regardless. Both are collapsed into one write
when they happen inside an operation that covers several tweaks.

### Reading a Tweak's Config From Outside

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// The raw JSON, for display or export
var json = tweakManager?.GetTweakConfigAsJson("MyPlugin_Tweak_MyTweak");

// A standalone copy. Modifying it does not affect the live tweak.
var copy = tweakManager?.GetTweakConfigCopy<MyTweakConfig>("MyPlugin_Tweak_MyTweak");
```

### Key Migrations

Beyond the `[TweakKeyMigration]` attribute, mappings can be registered at runtime:

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// One mapping
tweakManager?.AddKeyMigration("MyPlugin_Tweak_OldName", "MyPlugin_Tweak_NewName");

// Several at once
tweakManager?.AddKeyMigrations(new Dictionary<string, string>
{
    ["MyPlugin_Tweak_OldA"] = "MyPlugin_Tweak_NewA",
    ["MyPlugin_Tweak_OldB"] = "MyPlugin_Tweak_NewB",
});

// Apply the pending mappings now, returning how many were applied
var migrated = tweakManager?.ExecuteKeyMigrations();
```

Pending mappings are applied automatically when the module initializes. A mapping whose old key holds nothing to move
is kept, so it still applies if that data turns up later; one that has been applied is discarded.

Registering a mapping writes nothing by itself. A mapping is a declaration your code makes on every run rather than
state the user built up, so writing one would keep it applying after the `AddKeyMigration` call that declared it is
gone, and would rewrite the configuration merely because the plugin started. The move a mapping produces is the state,
and that is what gets written.

A migration that moves something is written to disk when `AutomaticPersistence` is on, which is what makes it a
one-time move. With persistence off, the move is applied in memory only and is redone from the same old keys on the
next load, so store the result yourself with `GetAllTweakConfigs()` if it has to survive a restart.

### Title Bar Buttons

Add custom buttons to the tweak manager window's title bar:

```csharp
var tweakManager = NoireLibMain.GetModule<NoireTweakManager>();

// Add a single button
tweakManager?.AddTitleBarButton(new TitleBarButton
{
    Icon = FontAwesomeIcon.Cog,
    IconOffset = new(2, 2),
    Click = (e) => { /* Open settings */ },
});

// Set multiple buttons
tweakManager?.SetTitleBarButtons(new List<TitleBarButton>
{
    new() { Icon = FontAwesomeIcon.Home, IconOffset = new(2, 2), Click = (e) => { /* Home */ } },
    new() { Icon = FontAwesomeIcon.QuestionCircle, IconOffset = new(2, 2), Click = (e) => { /* Help */ } },
});

// Remove button by index
tweakManager?.RemoveTitleBarButton(0);

// Clear all buttons
tweakManager?.ClearTitleBarButtons();
```

---

## Troubleshooting

### A tweak does not appear in the list
- Ensure NoireLib is initialized before adding the module.
- Confirm the module is active (`IsActive == true`).
- Check that the class inherits from `TweakBase` or `TweakBase<TConfig>` and is not abstract.
- Verify the class is in the same assembly (project) as your plugin, or register it with `RegisterTweak`.
- Make sure the class has a parameterless constructor, since auto-discovery instantiates it.
- Check that `ShouldShow` is not returning false, and that the class does not carry `[TweakDisabled]` without
  `showInList: true`.
- Confirm no other tweak already uses the same `InternalKey`; the second one registered is skipped with a warning.
- Check the dalamud logs with `/xllog`.
- If it still does not work, please report it.

### A tweak does not stay enabled across restarts
- Confirm `AutomaticPersistence` is true, or that you are calling `SaveAllTweakConfigs()` yourself.
- Check that `InternalKey` has not changed between releases. If it has, declare the previous key with
  `[TweakKeyMigration]`.
- Verify `OnEnable` is not throwing; a tweak that fails to enable is reported through `HasError` and `LastError`.

### Settings do not persist
- Ensure the tweak inherits from `TweakBase<TConfig>` rather than `TweakBase`.
- Call `MarkConfigDirty()` (or `Config.Save()`) after changing a config property. Changing it alone records nothing.
- Check that config members are public properties with both a getter and a setter, not fields.
- If you raised the config `Version`, make sure a `ConfigMigration` covers the step.

### Favorites or states disappeared after an update
- If a tweak's `InternalKey` changed, declare the old key with `[TweakKeyMigration]` or register the mapping with
  `AddKeyMigration`.
- The migration is skipped when the new key already holds data of its own, in which case the old key is left as it is.

### EventBus events not firing
- Ensure an `EventBus` is provided to the TweakManager (either in constructor or via property).
- Check that the EventBus is active and has subscribers.
- Subscribe before creating the module if you want the events it publishes while registering tweaks.
- Enable EventBus logging with `enableLogging: true` for debugging.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
- [Configuration System](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Configuration/README.md)
