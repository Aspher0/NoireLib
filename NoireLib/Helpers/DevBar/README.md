# Helper Documentation : DalamudDevBarHelper

You are reading the documentation for the `DalamudDevBarHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [One menu](#one-menu)
- [The items](#the-items)
- [Drawing your own](#drawing-your-own)
- [Opening the bar](#opening-the-bar)
- [What this rests on](#what-this-rests-on)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`DalamudDevBarHelper` is a static helper in the `NoireLib.Helpers` namespace that adds menus to Dalamud's dev bar, the bar `/xldev` opens.

- **One call per menu**, returning a token that removes it again.
- **Items or raw drawing**, or both in the same menu.
- **Drawn only while the bar is open.**
- **No subscription to manage**: the draw callback is taken on the first menu and dropped with the last one.

**Each menu is a new top-level menu beside Dalamud's.** Dalamud's own "Dalamud", "GUI", "Game" and "Plugins" menus cannot be extended.

---

## One menu

```csharp
using NoireLib.Helpers;

var menu = DalamudDevBarHelper.Register(
    "My Plugin",
    new DevBarItem { Label = "Open hub", OnClick = () => window.Toggle() },
    new DevBarItem { Label = "Reload config", OnClick = Reload, Shortcut = "Ctrl+R" });

menu.Visible = false;   // stops drawing, keeps the registration
menu.Dispose();         // the menu is gone
```

---

## The items

```csharp
new DevBarItem
{
    Label = "Verbose logging",
    OnClick = () => Config.Verbose = !Config.Verbose,
    Selected = () => Config.Verbose,      // draws the tick, asked every frame
    Enabled = () => Config.Loaded,        // greys the line out, asked every frame
    SeparatorBefore = true,
    Shortcut = "Alt+V",                   // a label only, nothing binds it to a key
}
```

`Selected` and `Enabled` are read once per frame while the menu is open.

---

## Drawing your own

`OnDraw` runs between the menu's begin and end. Items are drawn first when both are set:

```csharp
DalamudDevBarHelper.Register(new DevBarMenuOptions
{
    Name = "My Plugin",
    Items = [new DevBarItem { Label = "Open hub", OnClick = Toggle }],
    OnDraw = () =>
    {
        ImGui.Separator();
        ImGui.TextUnformatted($"Frame {frame}");
        ImGui.SliderInt("Delay", ref delay, 0, 500);
    },
    Priority = 10,
});
```

`Priority` orders this library's menus among themselves, lower first.

---

## Opening the bar

```csharp
DalamudDevBarHelper.IsOpen;        // whether the bar is open right now
DalamudDevBarHelper.SetOpen(true); // what /xldev toggles
```

---

## What this rests on

Dalamud has no dev bar extension point. The helper begins the main menu bar a second time during the plugin's draw. It appends to Dalamud's bar. Whether the bar is open is read from Dalamud's **internal** interface object.

When the internal moves, `IsOpen` and `SetOpen` return false and nothing is drawn. No exception reaches the caller.

---

## Troubleshooting

### The menu does not appear

Open the bar with `/xldev` or `SetOpen(true)`. When it is open and the menu is still missing, Dalamud moved the internal `IsOpen` reads.

### My item is inside my own menu, not under "Dalamud"

Dalamud's own menus cannot be extended.

### The menu draws but the items are dead

`Enabled` is returning false. It is read every frame.

---

## See Also

- [PluginReflectionHelper](../PluginInterop/README.md) for reaching inside another plugin.
- [DalamudPluginsHelper](../DalamudPlugins/README.md) for installing, removing and updating plugins.
