
# Module Documentation : NoireHotkeyManager

You are reading the documentation for the `NoireHotkeyManager` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Registering Hotkeys](#registering-hotkeys)
- [Binding UI](#binding-ui)
- [Activation Modes](#activation-modes)
- [Changing a Hotkey at Runtime](#changing-a-hotkey-at-runtime)
- [Taking a Key for a Moment](#taking-a-key-for-a-moment)
- [Threading](#threading)
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
- **Pressed, released, hold, repeat, hold-and-repeat activation modes** (with delays if applicable)
- **Framework thread callbacks**, so handlers can touch game state safely
- **Optional self-managed persistence** of the whole hotkey, every option and not just the binding
- **Live reconfiguration**: set a property on the entry and it takes effect and persists, with no remove-and-re-add
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
    Binding = new HotkeyBinding(VirtualKey.C, ctrl: true),
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
    Binding = new HotkeyBinding(VirtualKey.F6),
    Callback = () => ToggleBorderless(),
    ActivationMode = HotkeyActivationMode.Pressed,
    BlockGameInput = true,
    RequireGameFocus = true,
});
```

### HotkeyEntry fields

- `Id`: Unique identifier. Required. Matched ignoring case, so `my.hotkey` and `My.Hotkey` are one hotkey, and
  either spelling works everywhere an id is taken (`TryGetHotkey`, `SetHotkeyBinding`, `DrawKeybindInputButton`,
  `StartListening`, `UnregisterHotkey`, and the stored keybinds).
- `DisplayName`: Label used by the binding UI. Defaults to `Id` if empty.
- `Binding`: Initial `HotkeyBinding`.
- `Callback`: Action invoked when the hotkey triggers. Required.
- `Enabled`: Enable/disable this hotkey (default `true`).
- `ActivationMode`: Pressed, Released, Held, Repeat, or HoldAndRepeat.
- `HoldDelay`: Delay before `Held` triggers, and the initial delay before `HoldAndRepeat` starts repeating (default 400ms).
- `FixedRepeatDelay`: Delay between repeats when `Repeat` is fixed.
- `RepeatDelayMin`/`RepeatDelayMax`: Bounds for random repeat delay.
- `UseRandomRepeatDelay`: Randomize repeat delay between min/max.
- `BlockWhenTextInputActive`: Prevent firing while text input is active (default `true`).
- `BlockGameInput`: Block the associated game inputs when triggering (default `false`).
- `RequireGameFocus`: Require the game window to be focused for the hotkey to trigger (default `true`).

### Keyboard bindings

```csharp
Binding = new HotkeyBinding(VirtualKey.DELETE)
Binding = new HotkeyBinding(VirtualKey.G, ctrl: true, shift: true)
Binding = VirtualKey.DELETE   // A plain key converts implicitly
```

### Modifier-only bindings

```csharp
Binding = new HotkeyBinding(0, ctrl: true, shift: true, alt: false)
```

### Gamepad bindings

```csharp
Binding = new HotkeyBinding(GamepadButtons.R2)
Binding = GamepadButtons.R2   // Converts implicitly too
```

### Reading a binding yourself

`KeybindsHelper.IsBindingHeld(binding)` answers "is this binding held right now" using the exact rules this module triggers with (exact modifiers for a keyboard binding, required modifiers only for a modifier-only one, raw state for a gamepad button). It reads the physical keyboard, so it needs no active frame and works from any thread.

This is what lets another widget be gated by a hotkey the user rebinds here without reimplementing the matching. `NoireComboBox<T>.BindWheelCycleHotkey` is the shipped example; see the [NoireLib.UI documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/UI/README.md#plugging-in-the-hotkey-manager).

```csharp
if (hotkeyManager.TryGetHotkey("my.hotkey", out var entry) && KeybindsHelper.IsBindingHeld(entry.Binding))
{
    // The user is holding whatever they bound to "my.hotkey".
}
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
var listeningId = hotkeyManager?.ListeningHotkeyId;
if (listeningId != null)
{
    // Rebinding listeningId.
}
```

`ListeningHotkeyId` is null exactly when nothing is being rebound, so reading it once answers both questions at
once. Prefer that over testing `IsListening` and then reading `ListeningHotkeyId`: the detection timer stops
listening from its own thread the moment it captures a binding, so a capture landing between those two reads
leaves the second one null. `IsListening` is there for the case where the id is not needed.

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

### HoldAndRepeat
Waits `HoldDelay`, triggers once, then repeats on the same cadence as `Repeat`. It composes the two: the initial
delay is `HoldDelay`, and the repeat interval is `FixedRepeatDelay`, or the `RepeatDelayMin`/`RepeatDelayMax` range
when `UseRandomRepeatDelay` is set. Use it for a key that should fire after a deliberate press and then auto-repeat,
without a separate first trigger the instant it goes down.

```csharp
ActivationMode = HotkeyActivationMode.HoldAndRepeat,
HoldDelay = TimeSpan.FromMilliseconds(400),
FixedRepeatDelay = TimeSpan.FromMilliseconds(80)
```

### Defaults

- `HoldDelay`: 400ms
- `FixedRepeatDelay`: 80ms
- `RepeatDelayMin`/`RepeatDelayMax`: 80ms

---

## Changing a Hotkey at Runtime

The entry `TryGetHotkey` hands back is the **live** entry the module runs on, so reconfiguring a hotkey is just
setting a property on it. There is no remove-and-re-add.

```csharp
if (hotkeyManager.TryGetHotkey("my.hotkey", out var hotkey))
{
    hotkey.ActivationMode = HotkeyActivationMode.HoldAndRepeat;
    hotkey.HoldDelay = TimeSpan.FromMilliseconds(300);
    hotkey.BlockGameInput = false;
}
```

Each assignment takes effect on the next detection tick, and, when the manager persists, is saved as well. A burst
of sets like the one above coalesces into a single write while the game is running, so writing several options in
one frame is one save, not one per property.

- Every configurable option behaves this way: `Enabled`, `ActivationMode`, `HoldDelay`, `FixedRepeatDelay`,
  `RepeatDelayMin`/`RepeatDelayMax`, `UseRandomRepeatDelay`, `BlockWhenTextInputActive`, `RequireGameFocus`,
  `BlockGameInput`, and `DisplayName`.
- Assigning `Binding` is equivalent to calling `SetHotkeyBinding`: it raises the binding-changed notifications on
  the framework thread and persists, exactly as that method does.
- `Callback` is runtime-only and is not persisted. `Id` must not be changed after registration.
- `SuppressGameInput` is runtime-only too, and is the option to reach for when a key is wanted only while something
  is going on. See [Taking a key for a moment](#taking-a-key-for-a-moment).
- The convenience methods do the same thing for a single option: `SetHotkeyEnabled`, `SetHotkeyBinding`,
  `SetHotkeyCallback`. `SetHotkeyEnabled` persists its change, just as setting `Enabled` on the entry does.
- An entry you have unregistered is detached from the manager, so a later property set on it neither takes effect
  nor writes its removed hotkey back to storage.

---

## Taking a Key for a Moment

`BlockGameInput` is the hotkey's **standing** answer: it is persisted, and a stored hotkey overrides the values it
is registered with on the next load. That makes it the wrong tool for a key you want only while something is
happening — a panel being worked in, a mode being held. Written for a moment, it is saved forever, and the key
stays swallowed on every launch afterwards with the moment long since over.

For that, suppress it instead:

```csharp
if (hotkeyManager.TryGetHotkey("my.hotkey", out var hotkey))
    hotkey.SuppressGameInput();

// ... later, once the thing that wanted the key is over
hotkey.ReleaseGameInputSuppression();
```

- Either one takes the key: the blocker honours `BlockGameInput` and an outstanding suppression alike.
- A suppression is **never persisted** and cannot outlive the session, so forgetting to release one costs the rest
  of the session and nothing beyond it.
- Calls **nest**. Two callers can suppress the same hotkey, and the key goes back to the game when the last one
  releases. Releasing more than was taken is ignored rather than left as a debt against the next suppression.
- `IsGameInputSuppressed` reports whether anything is holding the key right now, which is what a UI should show.

`NoireReorderableList` is the worked example: its `BlockGameInputWhileActive` holds a suppression while a row is
focused in a focused window, and leaves the hotkey's own settings exactly as it found them.

---

## Threading

Key detection runs on its own 16ms timer rather than on the framework update, because the framework update
is bound to the frame rate and would drop short keypresses when FPS is low.

Delivery is separate from detection. **Every** callback, CLR event and EventBus publication this module makes is
invoked on the **framework thread**, so you can read and write game state directly from any of them:

```csharp
Callback = () => NoireService.TargetManager.Target = null,
```

That covers `Callback`, `OnHotkeyTriggered`, `OnHotkeyChanged`, and all four events
(`HotkeyTriggeredEvent`, `HotkeyBindingChangedEvent`, `HotkeyListeningStartedEvent`,
`HotkeyListeningStoppedEvent`).

The binding and listening surfaces need this as much as the trigger ones do, because the module changes bindings
from **two** different threads: your own call to `SetHotkeyBinding`, and the detection timer capturing a rebind
in `DrawKeybindInputButton`. Marshalling everything to the framework thread is what stops your handler's thread
from depending on which of those reached it.

### Trigger delivery

- A hotkey fires on the frame *after* detection, so a callback runs up to one frame later than the keypress.
- Triggers are never coalesced. If a hotkey triggers more than once between two frames (a `Repeat` hotkey at
  its 80ms default does so below roughly 12 FPS), the callback is invoked once per trigger, in order, rather
  than collapsed into a single call.
- A hotkey unregistered in that one-frame gap does not fire. Registration is re-checked when the trigger is
  delivered, so a callback you retire with `UnregisterHotkey` never runs afterwards.

### Binding and listening delivery

- The binding is **written before** `OnHotkeyChanged` is raised, and the manager holds no lock while raising it.
  Your handler may call back into the manager freely, including changing or unregistering the hotkey it was just
  told about.
- If you are already on the framework thread (the usual case: a config window calling `SetHotkeyBinding`), the
  notification runs inline, before the call returns. If you are not, it arrives on a later frame.
- `HotkeyBindingChangedEvent.Hotkey` and `OnHotkeyChanged` hand you the **live** entry, not a snapshot, exactly
  like every other surface that gives you a `HotkeyEntry`. Writing to it (`entry.Enabled = false`) affects the
  registered hotkey, which a defensive copy would silently discard. The trade is that a further rebind arriving
  before delivery shows through; `IsNewBinding` is captured when the binding is written and is never stale.

### Both

An exception thrown by a callback or handler is caught and logged, and does not prevent the deliveries queued
behind it.

When NoireLib is not initialized there is no framework thread to marshal onto, so deliveries run inline on the
calling thread instead. Once the module is disposed, nothing is delivered again.

---

## Persistence

When `ShouldSaveKeybinds` is true, the module stores hotkeys in `HotkeyManagerConfig.json`.

```csharp
hotkeyManager?.SetShouldSaveKeybinds(true);
```

- When enabled, existing hotkeys are saved immediately.
- When disabled, persistence stops and you manage storage yourself.
- Hotkeys are saved and restored by hotkey `Id`.

### The whole hotkey is stored, not just the binding

A stored record carries the binding **and every option**: `DisplayName`, `Enabled`, `ActivationMode`,
`HoldDelay`, `FixedRepeatDelay`, `RepeatDelayMin`/`RepeatDelayMax`, `UseRandomRepeatDelay`,
`BlockWhenTextInputActive`, `RequireGameFocus`, and `BlockGameInput`. A change a user makes at runtime therefore
survives a restart.

`Callback` and `SuppressGameInput` are the exceptions, and deliberately so: both are runtime state rather than
settings, and neither is written to a record.

On load, the stored record **overrides** the values a hotkey is registered with, the same way the stored binding
already did. The values you pass to `RegisterHotkey` are the defaults for a hotkey that has never been stored; once
a hotkey is stored, its stored options win. Changing a default in your code later will not move a user who already
has that hotkey stored, exactly as changing a default binding would not.

That is worth keeping in mind when writing an option from code rather than from a settings panel: whatever you
write is the user's stored answer from then on, and a value written for the moment will still be there next launch.
For anything momentary, use [a suppression](#taking-a-key-for-a-moment) instead.

### Upgrading from an older config

Configs written before this (version 1) stored only a binding per id. They are migrated to the version 2 shape on
load: each stored binding is lifted into a full record whose other options come up at their defaults, so an older
plugin's saved bindings are preserved. The migration is handled by NoireLib's configuration framework, which backs
up the file first and refuses to persist a load that failed, so a migration that cannot complete leaves the file
untouched.

### Multiple instances share one store

`HotkeyManagerConfig.json` is keyed by hotkey `Id` alone, and every `NoireHotkeyManager` in the plugin reads
and writes the same file. Saving therefore **updates** the ids an instance holds and leaves every other id
untouched, so two instances can persist their hotkeys side by side:

```csharp
var combat = NoireLibMain.AddModule<NoireHotkeyManager>("Hotkeys_Combat");
var ui = NoireLibMain.AddModule<NoireHotkeyManager>("Hotkeys_UI");
```

`UnregisterHotkey(id)` is the only call that deletes a stored hotkey, and it deletes exactly that one id.
A stored id that no registered hotkey owns is left alone, because it may belong to another instance or to a
hotkey that has not been registered yet. Give your ids a per-instance prefix if two instances could otherwise
pick the same one, since a shared id means a shared hotkey. Ids are matched ignoring case here too, so two
prefixes that differ only in case are the same prefix.

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
hotkeyManager?.SetHotkeyBinding("my.hotkey", new HotkeyBinding(VirtualKey.F1));
```

### Manage hotkeys programmatically

```csharp
hotkeyManager?.SetHotkeyEnabled("my.hotkey", false);
hotkeyManager?.SetHotkeyCallback("my.hotkey", () => DoSomething());
hotkeyManager?.UnregisterHotkey("my.hotkey");
```

To change any other option, set it on the live entry from `TryGetHotkey`; see
[Changing a Hotkey at Runtime](#changing-a-hotkey-at-runtime). There is no need to unregister and re-add a hotkey
to reconfigure it.

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
- Ensure the module was activated *after* NoireLib was initialized. Detection reads the game's key state and
  delivery runs on the framework thread, so a module activated beforehand records `IsActive` but wires
  nothing, and stays inert until it is activated again. It logs a warning when this happens.

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
