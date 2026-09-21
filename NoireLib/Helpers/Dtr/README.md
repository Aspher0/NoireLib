# Helper Documentation : DtrEntryHelper

You are reading the documentation for the `DtrEntryHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [One entry](#one-entry)
- [An entry driven by a source](#an-entry-driven-by-a-source)
- [The options](#the-options)
- [Clicks](#clicks)
- [States](#states)
- [Building the text](#building-the-text)
- [Lifetime](#lifetime)
- [Dropping to the primitive](#dropping-to-the-primitive)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`DtrEntryHelper` is a static helper in the `NoireLib.Helpers` namespace that puts entries on the server-info bar beside the world name.

- **One call per entry**, returning a token that removes it again.
- **A source**: hand it a `Func<SeString>` and an interval. The entry is written only when the value changes.
- **Left and right clicks** reach their own callbacks.
- **A cycle of states** on one entry, each with its own text and tooltip.
- **No subscription to manage**: the framework tick is taken with the first sourced entry and dropped with the last. Every remaining entry is removed with the library.

---

## One entry

```csharp
using NoireLib.Helpers;

var entry = DtrEntryHelper.Register("Gathering", "Ready", _ => OpenWindow());

entry.Text = "Busy";   // writes to the bar
entry.Text = "Busy";   // writes nothing, the value did not change
entry.Dispose();       // the entry is gone
```

The title is the key Dalamud holds the entry under and its name in Dalamud's settings. Unique to the plugin.

---

## An entry driven by a source

```csharp
var clock = DtrEntryHelper.Register("Eorzea time", () => EorzeaTimeHelper.Now().ToString("HH:mm"));
```

The source is polled on the framework thread every `RefreshInterval`, a quarter second by default.

**Never assign `Text` every frame.** Every write rebuilds the Dalamud node.

---

## The options

```csharp
var entry = DtrEntryHelper.Register(new DtrEntryOptions
{
    Title = "Retainers",
    Tooltip = "Ventures done",
    Shown = true,
    MinimumWidth = 90,            // stops the bar jittering as the number changes
    Source = () => $"{Done()}/{Total()}",
    RefreshInterval = TimeSpan.FromSeconds(1),
    OnLeftClick = _ => OpenWindow(),
    OnRightClick = _ => ToggleMute(),
});
```

`Title` is the only required value. The rest maps to the Dalamud entry's properties.

---

## Clicks

Both callbacks take Dalamud's `DtrInteractionEvent`: the button, the modifier keys, the scroll direction and the screen position:

```csharp
OnLeftClick = click => { if (click.ModifierKeys == ClickModifierKeys.Ctrl) Reset(); else Open(); }
```

---

## States

An entry with `States` cycles through them, one per click, wrapping at the end. Each carries its own text and optional tooltip.

```csharp
var toggle = DtrEntryHelper.Register(new DtrEntryOptions
{
    Title = "Auto-repair",
    States =
    [
        new DtrEntryState("on",     SeStringHelper.Colored("REPAIR", 43), "Repairing at 20%"),
        new DtrEntryState("paused", SeStringHelper.Colored("REPAIR", 14), "Paused"),
        new DtrEntryState("off",    "REPAIR"),
    ],
    CycleOn = MouseClickType.Left,
    OnStateChanged = state => Config.Mode = state.Name,
});

toggle.SetState("off");      // moves without calling OnStateChanged: the caller already knows
toggle.State?.Name;          // "off"
```

`CycleOn` picks the button that advances the cycle. The other button keeps its own callback.

---

## Building the text

Text building is not DTR-specific and lives on `SeStringHelper`:

```csharp
SeStringHelper.WithIcon(SeIconChar.Gil, "12,400");
SeStringHelper.Colored("OFFLINE", 17);
SeStringHelper.Join(" | ", left, middle, right);   // skips the parts carrying no text
"a very long label".Truncate(12);                  // StringHelper
```

---

## Lifetime

Disposing an entry removes it. A disposed entry ignores every later write.

`DtrEntryHelper.RemoveAll()` clears every entry. The library calls it on its own disposal.

---

## Dropping to the primitive

`DtrEntry.Bar` is the `IDtrBarEntry` Dalamud handed back, for anything this wrapper does not expose:

```csharp
entry.Bar.UserHidden;      // whether the user hid it in Dalamud's settings
entry.Bar.ScreenBounds;    // where it is drawn
```

`NoireService.DtrBar` is the bar itself.

---

## Troubleshooting

### The entry never appears

The user can hide any entry from Dalamud's settings, whatever `Shown` says. `entry.Bar.UserHidden` tells.

### The text lags behind

Set `Text` directly for a value that must land the same frame.

### Two plugins fight over one entry

Two registrations under the same title share one Dalamud entry. Prefix the title with the plugin's name.

---

## See Also

- [ContextMenuHelper](../ContextMenu/README.md) for the same registration shape on the game's right-click menus.
- [TitleScreenMenuHelper](../TitleScreen/README.md) for entries on the title screen.
