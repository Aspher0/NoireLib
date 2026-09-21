# Helper Documentation : TitleScreenMenuHelper

You are reading the documentation for the `TitleScreenMenuHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [One entry](#one-entry)
- [The icon](#the-icon)
- [The options](#the-options)
- [Lifetime](#lifetime)
- [Dropping to the primitive](#dropping-to-the-primitive)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`TitleScreenMenuHelper` is a static helper in the `NoireLib.Helpers` namespace that adds entries to the title screen's menu, the column where Dalamud's settings and plugin installer sit.

- **One call per entry**, returning a token that removes it again.
- **Any icon**: a game icon row, an image file, or a resolved texture, at any size. It is scaled to what the title screen accepts.
- **Before login**: the entry shows on the title screen only.
- **No cleanup**: every remaining entry is removed with the library.

---

## One entry

```csharp
using NoireLib.Helpers;

var entry = TitleScreenMenuHelper.Register("My Plugin", iconId: 66001, () => window.Toggle());

entry.Dispose();   // the entry is gone
```

---

## The icon

**Any icon size works.** The helper scales it so its longest side is 64 pixels and keeps the aspect ratio.

**Dalamud removes an entry whose texture is not 64 on a side** the first time the title screen draws, with one log line. Game icon rows run 28x28, 32x32, 40x40 and 72x72. None of the 154 rows in the 60000 range has a 64-pixel side.

`NormalizeIcon = false` hands Dalamud the texture as given. The helper then logs an error naming the real size once it loads. `TitleScreenMenuHelper.RequiredIconSize` is the number.

Dalamud draws the entry from an `ISharedImmediateTexture`. Any of the three forms produces one:

```csharp
// A game icon row.
TitleScreenMenuHelper.Register("Settings", iconId: 66001, Open);

// An image shipped beside the plugin.
TitleScreenMenuHelper.Register("Settings", Path.Combine(directory, "icon.png"), Open);

// A texture already resolved, for an icon this helper cannot name.
TitleScreenMenuHelper.Register(new TitleScreenEntryOptions
{
    Name = "Settings",
    Icon = NoireService.TextureProvider.GetFromManifestResource(assembly, "Plugin.icon.png"),
    OnTriggered = Open,
});
```

`Icon` wins over `IconId`, which wins over `IconPath`. Registering with none throws. `TitleScreenMenuHelper.ResolveIcon` applies the same order without registering.

---

## The options

```csharp
var entry = TitleScreenMenuHelper.Register(new TitleScreenEntryOptions
{
    Name = "My Plugin",
    IconId = 66001,
    OnTriggered = () => window.Toggle(),
    Priority = 1000,     // lower sits higher; see the warning below before setting one
});
```

**Leave `Priority` unset** unless the order matters. A high value pushes the entry past the column's room.

---

## Lifetime

Disposing an entry removes it. `TitleScreenMenuHelper.RemoveAll()` clears every entry. The library calls it on its own disposal.

The callback runs with **no character logged in**. Player, territory and localised sheet reads are unavailable.

---

## Working out why an entry is not there

Nothing about a title screen entry fails loudly. Two calls report:

```csharp
entry.IsIconValid(out var problem);     // the size rule, and why it fails
entry.IsIconReady(out var error);       // only whether the texture has loaded
TitleScreenMenuHelper.Describe();       // every entry Dalamud holds, its own included
```

`IsIconValid` covers the size rule. Check it first after turning scaling off.

`Describe` reports each entry's priority, whether it is Dalamud's own, and **whether its show condition holds**. Dalamud gates entries on held keys, like its own "Toggle Dev Menu".

Absent from `Describe`: never accepted. `shown=False`: skipped on its show condition. `shown=True` and off screen: unset `Priority`.

`IsIconReady` reads false for a frame or two after registering.

---

## Dropping to the primitive

`TitleScreenEntry.Entry` is the `IReadOnlyTitleScreenMenuEntry` Dalamud returned. `NoireService.TitleScreenMenu` is the menu, whose `Entries` lists everything including Dalamud's own.

---

## Troubleshooting

### The entry does not appear

It shows on the title screen only.

### The entry registers but never appears

With `NormalizeIcon = false`, the icon is not 64 on a side. `IsIconValid(out var problem)` names the real size.

Otherwise read `shown=` in `Describe()`. `False` means a show condition skips it. `True` points at `Priority`.

### The icon is a blank square

The texture has not loaded, or the icon row or path does not resolve. `IsIconReady` tells which.

---

## See Also

- [DtrEntryHelper](../Dtr/README.md) for entries on the server-info bar.
- [ContextMenuHelper](../ContextMenu/README.md) for entries on the game's right-click menus.
