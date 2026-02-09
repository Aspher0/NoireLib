
# Module Documentation : NoireHotkeyManager

You are reading the documentation for the `NoireHotkeyManager` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Registering Hotkeys](#registering-hotkeys)
- [Binding UI](#binding-ui)
- [Activation Modes](#activation-modes)
- [Persistence](#persistence)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireHotkeyManager` is a module that lets you register editable hotkeys and bind them with a fully managed ImGui button.

It provides:
- **Keyboard + gamepad hotkeys**
- **Pressed, released, hold, repeat activation modes** (with delays if applicable)
- **Optional self-managed persistence** via NoireLib configuration
- **EventBus integration** for hotkey lifecycle events
- **Per-hotkey control** over enabled state, activation mode, delays, and text input blocking
- **Managed listen state** for rebinding (with modifier-only support)

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create the module

```csharp
var hotkeyManager = NoireLibMain.AddModule<NoireHotkeyManager>("HotkeyManager");
```

### 2. Register a hotkey

```csharp
hotkeyManager?.RegisterHotkey(new HotkeyEntry
{
    Id = "my.hotkey",
    DisplayName = "Example Hotkey",
    Binding = new HotkeyBinding((int)VirtualKey.C, ctrl: true),
    Callback = () => NoireLogger.PrintToChat("Hotkey pressed"),
    ActivationMode = HotkeyActivationMode.Pressed,
});
```

And you're all set!
You can now use the hotkey in game and configure the module and hotkeys as you wish.

---

## Configuration

### Module Parameters

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Hotkeys");

var hotkeyManager = new NoireHotkeyManager(
    moduleId: "HotkeyManager", // Optional identifier for this module instance.
    active: true,              // Whether the module should start active.
    enableLogging: true,       // Enable module logs for troubleshooting.
    shouldSaveKeybinds: true,  // Persist bindings in NoireLib config storage.
    eventBus: eventBus         // Optional `NoireEventBus` for publishing hotkey events.
);
```

### Module Properties

- `ShouldSaveKeybinds`: Controls persistence of bindings.
- `IsActive`: Enables/disables hotkey processing.
- `EventBus`: Publish hotkey events (can be set after creation).
- `IsListening`: Indicates if a hotkey is currently being rebound.
- `ListeningHotkeyId`: The hotkey id currently listening for input.

### Property Configuration

```csharp
hotkeyManager?
    .SetShouldSaveKeybinds(true)
    .SetActive(true);
```

You can also set `EventBus` after construction if needed:
```csharp
hotkeyManager!.EventBus = eventBus;
```

---

## Registering Hotkeys

Use `RegisterHotkey` with a `HotkeyEntry` payload.

```csharp
hotkeyManager?.RegisterHotkey(new HotkeyEntry
{
    Id = "screen.toggleBorderless",
    DisplayName = "Toggle Borderless",
    Binding = new HotkeyBinding((int)VirtualKey.F6),
    Callback = () => ToggleBorderless(),
    ActivationMode = HotkeyActivationMode.Pressed,
    BlockGameInput = true,
    RequireGameFocus = true,
});
```

### HotkeyEntry fields

- `Id`: Unique identifier. Required.
- `DisplayName`: Label used by the binding UI. Defaults to `Id` if empty.
- `Binding`: Initial `HotkeyBinding`.
- `Callback`: Action invoked when the hotkey triggers. Required.
- `Enabled`: Enable/disable this hotkey (default `true`).
- `ActivationMode`: Pressed, Released, Held, or Repeat.
- `HoldDelay`: Delay before `Held` triggers (default 400ms).
- `FixedRepeatDelay`: Delay between repeats when `Repeat` is fixed.
- `RepeatDelayMin`/`RepeatDelayMax`: Bounds for random repeat delay.
- `UseRandomRepeatDelay`: Randomize repeat delay between min/max.
- `BlockWhenTextInputActive`: Prevent firing while text input is active (default `true`).
- `BlockGameInput`: Block the associated game inputs when triggering (default `false`).
- `RequireGameFocus`: Require the game window to be focused for the hotkey to trigger (default `true`).

### Keyboard bindings

```csharp
Binding = new HotkeyBinding((int)VirtualKey.DELETE)
```

### Modifier-only bindings

```csharp
Binding = new HotkeyBinding(0, ctrl: true, shift: true, alt: false)
```

### Gamepad bindings

```csharp
Binding = new HotkeyBinding(GamepadButtons.R2)
```

---

## Binding UI

The manager provides a fully managed button to rebind hotkeys.

```csharp
hotkeyManager?.DrawKeybindInputButton("my.hotkey");
```

The button handles listening state, display text, and updates the binding automatically.

### Button options

- `label`: Text shown on the button. Use `string.Empty` for binding-only display.
- `size`: Optional `Vector2` size for the button.
- `mode`: `HotkeyListenMode.Keyboard` or `HotkeyListenMode.Gamepad`.
- `allowClear`: Allow right-click to clear the binding.
- `showClearTooltip`: Show tooltip hint when hovering.

### Hide the label

If you only want the binding text (e.g. `Ctrl + A`), pass an empty label:

```csharp
hotkeyManager?.DrawKeybindInputButton("my.hotkey", string.Empty);
```

### Gamepad listening

```csharp
hotkeyManager?.DrawKeybindInputButton("pad.hotkey", mode: HotkeyListenMode.Gamepad);
```

### Cancel / clear

- **Right-click** while listening: cancels without clearing
- **Right-click** when idle: clears binding (optional)
- **Esc** cancels listening for both keyboard and gamepad

### Listen state

You can inspect listening state if you need to build custom UI:

```csharp
if (hotkeyManager?.IsListening == true)
{
    var listeningId = hotkeyManager.ListeningHotkeyId;
}
```

---

## Activation Modes

### Pressed
Triggers once when the key is pressed.

### Released
Triggers once when the key is released.

### Held
Triggers once when held for `HoldDelay`.

### Repeat
Triggers repeatedly while held. Use `FixedRepeatDelay`, or enable random delay with `UseRandomRepeatDelay` + min/max.

```csharp
ActivationMode = HotkeyActivationMode.Repeat,
FixedRepeatDelay = TimeSpan.FromMilliseconds(90)
```

```csharp
ActivationMode = HotkeyActivationMode.Repeat,
UseRandomRepeatDelay = true,
RepeatDelayMin = TimeSpan.FromMilliseconds(60),
RepeatDelayMax = TimeSpan.FromMilliseconds(120)
```

### Defaults

- `HoldDelay`: 400ms
- `FixedRepeatDelay`: 80ms
- `RepeatDelayMin`/`RepeatDelayMax`: 80ms

---

## Persistence

When `ShouldSaveKeybinds` is true, the module stores bindings in `NoireHotkeyManager.json`.

```csharp
hotkeyManager?.SetShouldSaveKeybinds(true);
```

- When enabled, existing keybinds are saved immediately.
- When disabled, persistence stops and you manage storage yourself.
- Bindings are saved and restored by hotkey `Id`.

---

## EventBus Integration

The `NoireHotkeyManager` can publish events to a `NoireEventBus`.

### Quick Example

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Hotkeys");
var hotkeyManager = NoireLibMain.AddModule<NoireHotkeyManager>("HotkeyManager");
hotkeyManager!.EventBus = eventBus;

eventBus?.Subscribe<HotkeyTriggeredEvent>(evt =>
{
    NoireLogger.LogInfo($"Triggered: {evt.Hotkey.Id}");
});
```

### Available Events

- `HotkeyTriggeredEvent`
- `HotkeyBindingChangedEvent`
- `HotkeyListeningStartedEvent`
- `HotkeyListeningStoppedEvent`
- `HotkeyEvent` payloads include the affected hotkey or id depending on the event type.

---

## Advanced Features

### Block while text input is active

```csharp
BlockWhenTextInputActive = true
```

This prevents hotkeys from firing while a game text input is focused.

### Update bindings programmatically

```csharp
hotkeyManager?.SetHotkeyBinding("my.hotkey", new HotkeyBinding((int)VirtualKey.F1));
```

### Manage hotkeys programmatically

```csharp
hotkeyManager?.SetHotkeyEnabled("my.hotkey", false);
hotkeyManager?.SetHotkeyCallback("my.hotkey", () => DoSomething());
hotkeyManager?.UnregisterHotkey("my.hotkey");
```

### Query registered hotkeys

```csharp
if (hotkeyManager?.TryGetHotkey("my.hotkey", out var entry) == true)
{
    var binding = entry.Binding;
}

var allHotkeys = hotkeyManager?.GetHotkeys();
```

---

## Troubleshooting

### Hotkey not firing
- Ensure the module is active (`IsActive == true`)
- Check `Enabled` on the hotkey entry
- If `BlockWhenTextInputActive` is true, verify no text input is active

### Binding not saved
- Ensure `ShouldSaveKeybinds` is enabled
- Confirm `HotkeyManagerConfig.json` is present in your plugin config folder

### EventBus events not firing
- Ensure `EventBus` is assigned to the hotkey manager
- Verify EventBus is active and has subscribers

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
