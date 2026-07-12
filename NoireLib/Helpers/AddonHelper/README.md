# Helper Documentation : AddonHelper

You are reading the documentation for the `AddonHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [Two Layers: Fluent Wrappers vs. Pointer Primitives](#two-layers-fluent-wrappers-vs-pointer-primitives)
- [Getting Started](#getting-started)
- [Finding Addons](#finding-addons)
- [Working with Nodes](#working-with-nodes)
- [Reading Text and Values](#reading-text-and-values)
- [Sending Callbacks](#sending-callbacks)
- [Node Events, Hover and Cursor](#node-events-hover-and-cursor)
- [Lifecycle Listeners](#lifecycle-listeners)
- [Waiting for an Addon to be Ready](#waiting-for-an-addon-to-be-ready)
- [Keyed Event Registrations](#keyed-event-registrations)
- [Extension Methods](#extension-methods)
- [Safety Model](#safety-model)
- [API Reference](#api-reference)

---

## Overview

`AddonHelper` is a static helper in the `NoireLib.Helpers` namespace for working with FFXIV game UI
addons (`AtkUnitBase`) and their nodes (`AtkResNode`). It covers:

- **Finding addons** by name, by lifecycle args, or by event data (raw or ready-checked).
- **Node traversal** by id or by a chain of ids (including descending into component nodes).
- **Reading** text and `AtkValue` data, with readable formatting for logging.
- **Sending callbacks** with automatic marshalling of managed values into `AtkValue`.
- **Events** — node events, hover handlers, cursor-on-hover, and addon lifecycle listeners.
- **Ready-waiting** — run an action (or `await`) as soon as an addon is loaded and ready.
- **Keyed registrations** — store disposable/event registrations under a key for bulk cleanup.

It ships as a set of partial files:

| File | Responsibility |
|------|----------------|
| `AddonHelper.cs` | Pointer-level primitives (find, traverse, read, callback, events). |
| `AddonHelper.Convenience.cs` | Fluent entry points, typed access, lifecycle listeners, ready-waiting. |
| `NoireAddon.cs` | Null-safe fluent wrapper around an addon. |
| `NoireAddonNode.cs` | Null-safe fluent wrapper around a node. |
| `AddonHelperExtensions.cs` | Bridges Dalamud addon types into the fluent wrappers. |

---

## Two Layers: Fluent Wrappers vs. Pointer Primitives

`AddonHelper` exposes **two** ways to do the same thing:

1. **Fluent wrappers** (`NoireAddon` / `NoireAddonNode`) — chainable, usable **without `unsafe`**, and safe
   to call even when the addon/node is missing or not ready. **Prefer these for everyday usage.**
2. **Pointer primitives** — the `unsafe` `Try*` methods on `AddonHelper` that hand you `AtkUnitBase*` /
   `AtkResNode*` directly, for when you need low-level access.

```csharp
// Fluent (recommended)
var addon = AddonHelper.GetAddon("ContextMenu");
if (addon.IsReady)
    addon.GetNode(5u).TrySetText("Hello");

// Pointer-level (requires unsafe)
unsafe
{
    if (AddonHelper.TryGetReadyAddon("ContextMenu", out AtkUnitBase* ptr))
    {
        // work with ptr directly
    }
}
```

***❗ We will assume you have already initialized NoireLib in your plugin.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

---

## Getting Started

```csharp
using NoireLib.Helpers;

// Get a fluent wrapper (valid even if the addon doesn't exist)
NoireAddon addon = AddonHelper.GetAddon("Talk");

// The wrapper is truthy when the addon is ready
if (addon)
{
    string line = addon.ReadText(2, 3);        // read text by node-id chain
    addon.GetNode(4u).Hide();                   // hide a node
}
```

`GetAddon` always returns a wrapper — never null. Guard with `IsReady` (or use the wrapper in a boolean
context) before interacting.

---

## Finding Addons

```csharp
// Fluent
NoireAddon addon      = AddonHelper.GetAddon("SelectYesno");        // whether ready or not
NoireAddon readyAddon = AddonHelper.GetReadyAddon("SelectYesno");   // invalid unless ready
bool ready            = AddonHelper.IsAddonReady("SelectYesno");

// Pointer-level (unsafe)
AddonHelper.TryGetAddon("SelectYesno", out AtkUnitBase* ptr);
AddonHelper.TryGetReadyAddon("SelectYesno", out AtkUnitBase* readyPtr);

// Typed pointer to a concrete addon struct
AddonHelper.TryGetReadyAddon<AddonSelectYesno>("SelectYesno", out AddonSelectYesno* typed);

// From a lifecycle callback or event data
AddonHelper.TryGetAddon(addonArgs, out AtkUnitBase* fromArgs);
AddonHelper.TryGetAddon(eventData, out AtkUnitBase* fromEvent);
```

`IsAddonLoaded` is the single source of truth for "ready": visible, fully loaded, and interactable.

---

## Working with Nodes

Nodes are resolved by a single id, or by a **chain** of ids that traverses children — descending into
component nodes automatically.

```csharp
NoireAddon addon = AddonHelper.GetReadyAddon("SomeAddon");

NoireAddonNode root  = addon.RootNode;
NoireAddonNode byId  = addon.GetNode(5u);          // by node id
NoireAddonNode byId2 = addon[5u];                   // indexer shorthand
NoireAddonNode chain = addon.GetNode(1, 4, 12);     // traverse id chain

// Node operations (all null-safe)
chain.TrySetText("New label");
chain.SetVisible(false);
chain.SetAlpha(128);
float x = chain.ScreenX;
bool textNode = chain.IsTextNode;
```

Pointer-level equivalents (`unsafe`): `TryGetRootNode`, `TryGetNode(addon, id, out node)`,
`TryGetNode(addon, out node, params int[] ids)`, `TryGetTextNode`, `TryGetComponentNode`.

---

## Reading Text and Values

```csharp
// Text
string text = addon.ReadText(2, 3);                 // "" if unavailable
bool ok     = addon.TryReadText(out var t, 2, 3);
string node = addon.GetNode(2, 3).Text;

// AtkValue (pointer-level, unsafe)
AddonHelper.TryReadValue(atkValuePtr, out object? value);
object? v   = AddonHelper.ReadValueOrDefault(atkValuePtr);

// Formatting for logs
string one  = AddonHelper.FormatValue(atkValuePtr);        // e.g. "42", "\"text\"", "0x1F"
string many = AddonHelper.FormatValues(valuesPtr, count);  // e.g. "[[0]=42, [1]=\"text\"]"
```

`TryReadValue` handles Null, Bool, Int/UInt, Int64/UInt64, Float, String/ManagedString, and Pointer types.

---

## Sending Callbacks

Callbacks marshal managed values into `AtkValue` for you — pass primitives, strings, enums, pointers, or
even enumerables (marshalled as vectors).

```csharp
// Fluent — updates addon state by default
addon.SendCallback(0, "confirm", true);

// Control the updateState flag explicitly
addon.SendCallback(updateState: false, 0, 1);

// Pointer-level (unsafe)
AddonHelper.SendCallback(addonPtr, updateState: true, 0, "value");
```

Returns `false` when the addon isn't ready. `null` values become `AtkValueType.Null`; unknown types fall
back to their `Convert.ToString` representation.

---

## Node Events, Hover and Cursor

```csharp
NoireAddonNode button = addon.GetNode(5u);

// Click / arbitrary node event
IAddonEventHandle? click = button.AddClickEvent((type, data) => NoireLogger.LogInfo("Clicked!"));
IAddonEventHandle? evt   = button.AddEvent(AddonEventType.MouseOver, (type, data) => { /* ... */ });

// Hover handlers (returns a disposable unregistering every created event)
IDisposable? hover = button.AddHoverEvents(
    onMouseOver: (t, d) => NoireLogger.LogInfo("hover in"),
    onMouseOut:  (t, d) => NoireLogger.LogInfo("hover out"));

// Cursor-on-hover
IDisposable? cursor = button.AddCursorOnHover(AddonCursorType.Clickable);

// Manual cursor control
AddonHelper.SetCursor(AddonCursorType.Hand);
AddonHelper.ResetCursor();

// Cleanup
AddonHelper.RemoveEvent(click);
hover?.Dispose();
```

---

## Lifecycle Listeners

Register handlers for addon lifecycle events. The convenience wrappers hand you a `NoireAddon` and support
**one-shot** registration (auto-unregister after the first invocation).

```csharp
// Convenience wrappers
IDisposable setup    = AddonHelper.OnAddonSetup("Talk", addon => NoireLogger.LogInfo($"Setup {addon.Name}"));
IDisposable refresh  = AddonHelper.OnAddonRefresh("Talk", addon => { /* ... */ });
IDisposable finalize = AddonHelper.OnAddonFinalize("Talk", name => NoireLogger.LogInfo($"Closing {name}"));

// Fire only once, then auto-unregister
IDisposable once = AddonHelper.OnAddonSetup("Talk", addon => { /* ... */ }, once: true);

// Lower-level: any AddonEvent, single/multiple/global addon names
IDisposable listener = AddonHelper.RegisterLifecycleListener(
    AddonEvent.PostSetup, "Talk", (type, args) => { /* ... */ });

// Always dispose to unregister
setup.Dispose();
```

---

## Waiting for an Addon to be Ready

Instead of polling manually, run an action (or `await`) as soon as the addon becomes ready. Checks run once
per framework tick and the action runs on the framework thread.

```csharp
// Callback style, with an optional timeout
IDisposable wait = AddonHelper.RunWhenReady("SomeAddon", addon =>
{
    addon.GetNode(5u).TrySetText("Ready!");
}, timeout: TimeSpan.FromSeconds(5));

// Async style — returns false if the timeout elapses first
bool ready = await AddonHelper.WaitUntilReadyAsync("SomeAddon", TimeSpan.FromSeconds(5), cancellationToken);
```

Dispose the returned registration to cancel a pending `RunWhenReady`.

---

## Keyed Event Registrations

Store any `IDisposable` (or `IAddonEventHandle`) registration under a string key so it can be unregistered in
bulk later — handy for grouping everything a feature creates.

```csharp
AddonHelper.RegisterEvent("myFeature", addon.GetNode(5u).AddClickEvent(handler));
AddonHelper.RegisterEvent("myFeature", AddonHelper.OnAddonSetup("Talk", h));

bool has = AddonHelper.HasRegisteredEvents("myFeature");

AddonHelper.UnregisterEvents("myFeature");   // dispose everything under this key
AddonHelper.UnregisterAllEvents();            // dispose all keyed registrations
```

Registering a new value under an existing key disposes the previously stored registration.

---

## Extension Methods

`AddonHelperExtensions` bridges Dalamud types into the fluent wrappers:

```csharp
bool ready       = atkUnitBasePtr.IsAddonLoaded();
NoireAddon a1    = atkUnitBasePtr.ToNoireAddon();
NoireAddon a2    = addonArgs.ToNoireAddon();
NoireAddon a3    = eventData.ToNoireAddon();
```

`NoireAddon` also has implicit conversions from `AtkUnitBase*` and `AtkUnitBasePtr`, and both wrappers convert
implicitly to `bool` (`NoireAddon` → `IsReady`, `NoireAddonNode` → `IsValid`).

---

## Safety Model

- `GetAddon` / `GetReadyAddon` **never return null** — they return a wrapper that may be invalid.
- Every wrapper member is safe on an invalid/not-ready target: getters return sensible defaults
  (`""`, `0`, `default`) and actions become no-ops returning `false`/`null`.
- Pointer-level `Try*` methods follow the standard bool + `out` pattern and never dereference null.
- Prefer the fluent wrappers to avoid `unsafe` blocks entirely; drop to pointers only when you need them.

---

## API Reference

**`AddonHelper` (pointer primitives)** — `TryGetAddon`, `TryGetReadyAddon`, `IsAddonLoaded`,
`TryGetRootNode`, `TryGetNode`, `TryGetTextNode`, `TryGetComponentNode`, `TryReadText`, `ReadTextOrEmpty`,
`TryReadValue`, `ReadValueOrDefault`, `FormatValue`, `FormatValues`, `SendCallback`, `AddEvent`,
`RemoveEvent`, `RemoveEvents`, `AddHoverEvents`, `AddCursorOnHover`, `TrySetNodeCursor`, `SetCursor`,
`ResetCursor`, `TryPreventOriginal`, `GetOriginalVirtualTable`.

**`AddonHelper` (convenience)** — `GetAddon`, `GetReadyAddon`, `IsAddonReady`, `TryGetAddon<T>`,
`TryGetReadyAddon<T>`, `OnAddonSetup`, `OnAddonRefresh`, `OnAddonFinalize`, `RunWhenReady`,
`WaitUntilReadyAsync`, `RegisterLifecycleListener`, `RegisterEvent`, `HasRegisteredEvents`,
`UnregisterEvents`, `UnregisterAllEvents`.

**`NoireAddon`** — `IsValid`, `IsReady`, `IsVisible`, `Name`, `X`, `Y`, `Scale`, `Width`, `Height`,
`RootNode`, `GetNode`, `ReadText`, `TryReadText`, `SendCallback`, `Show`, `Hide`, `Close`.

**`NoireAddonNode`** — `IsValid`, `NodeId`, `IsTextNode`, `IsComponentNode`, `IsVisible`, `Width`, `Height`,
`ScreenX`, `ScreenY`, `Text`, `TryReadText`, `TrySetText`, `SetVisible`, `Show`, `Hide`, `SetAlpha`,
`AddEvent`, `AddClickEvent`, `AddHoverEvents`, `AddCursorOnHover`, `Addon`.
