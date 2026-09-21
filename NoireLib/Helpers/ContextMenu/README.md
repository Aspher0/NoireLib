# Helper Documentation : ContextMenuHelper

You are reading the documentation for the `ContextMenuHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [One entry](#one-entry)
- [The entry record](#the-entry-record)
- [Items](#items)
- [Glyphs](#glyphs)
- [Filtering](#filtering)
- [Submenus](#submenus)
- [Ordering](#ordering)
- [Lifetime](#lifetime)
- [Dropping to the primitive](#dropping-to-the-primitive)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`ContextMenuHelper` is a static helper in the `NoireLib.Helpers` namespace that puts entries on the game's
right-click menus.

- **One call per entry**, returning a token that removes it again.
- **Every menu**: the one opened on a player or an NPC, the one opened on an inventory item, both, or any menu
  opened on an item wherever it sits: the inventory, a chat link, a shop, a recipe, an item list.
- **Filtered per opening** by the target, by the addon the menu sits over, or by any predicate.
- **Several glyphs** ahead of the label, where Dalamud's prefix slot holds one.
- **Submenus** built when the entry is clicked, from whatever the plugin knows at that moment.
- **No subscription to manage**: the menu event is taken on the first registration and dropped on the last
  disposal.

---

## One entry

```csharp
using NoireLib.Helpers;

var entry = ContextMenuHelper.Register("Inspect gear", click => Inspect(click.Context.TargetObjectId));

entry.Dispose();   // the entry is gone
```

The third argument picks the menu:

```csharp
ContextMenuHelper.Register("Price check", click => Check(click.Context.ItemId), ContextMenuScope.Item);
```

`ContextMenuScope` is `Default`, `Inventory`, `Everywhere` or `Item`.

---

## The entry record

Everything the entry branches on lives on `ContextMenuEntry`, and a copy is made with `with { }`.

```csharp
var entry = new ContextMenuEntry
{
    Label = "Add to shopping list",
    Glyphs = [SeIconChar.BoxedLetterM, SeIconChar.BoxedLetterB],
    GlyphColor = 45,
    Scope = ContextMenuScope.Inventory,
    Priority = -1,
    IsEnabled = true,
    IsReturn = false,
    Addons = ["Inventory", "InventoryLarge", "InventoryExpansion"],
    ShowWhen = context => Market.Sells(context.ItemId),
    EnabledWhen = context => !Market.IsBusy,
    SubmenuTitle = "Shopping lists",
    Submenu = context => Lists.Select(list => new ContextMenuEntry
    {
        Label = list.Name,
        OnClick = _ => list.Add(context.ItemId),
    }).ToList(),
};

var hidden = entry with { ShowWhen = _ => false };

using var registration = ContextMenuHelper.Register(entry);
```

`ContextMenuContext` is what the opening carried: `Menu`, `AddonName`, `Item`, `ItemId`, `IsHqItem`, `ItemSource`,
`TargetName`, `TargetObjectId`, `TargetContentId`, `TargetHomeWorldId`, `TargetObject`, and `Args` for Dalamud's own
arguments. `Menu` is `Default` or `Inventory`, never `Everywhere` or `Item`.

---

## Items

`ContextMenuScope.Item` keeps an entry on every menu opened on an item, whichever menu the game uses for it. The item is
read on every opening, whatever the scope.

```csharp
ContextMenuHelper.Register(new ContextMenuEntry
{
    Label = "Add to shopping list",
    Scope = ContextMenuScope.Item,
    ShowWhen = context => Market.Sells(context.ItemId),
    OnClick = click => Lists.Add(click.Context.ItemId, click.Context.IsHqItem),
});
```

| Opening | `ItemSource` | Read from |
| --- | --- | --- |
| An inventory slot | `Inventory` | The inventory menu target |
| An item link in `ChatLog` | `ChatLink` | `AgentChatLog.ContextItemId` |
| `RecipeNote` | `Recipe` | `AgentRecipeNote.ContextMenuResultItemId` |
| `RecipeTree`, `RecipeMaterialList`, `RecipeProductList` | `Recipe` | `AgentRecipeItemContext.ResultItemId` |
| Any other addon, such as `Shop` or `ItemSearch` | `Hovered` | `IGameGui.HoveredItem` |

`ItemId` is the row id without its quality offset, `IsHqItem` the quality. A chat or recipe menu with no item of its own
falls back to the hovered item. A menu opened on a player, an NPC or a chat name carries no item. An event item carries
none either.

---

## Glyphs

`Glyphs` is a list, and the entries appear in order ahead of the label.

```csharp
Glyphs = [SeIconChar.BoxedLetterM, SeIconChar.BoxedLetterB]   // two boxed letters, then the label
```

The game's prefix slot holds one glyph and draws a space after it. The helper puts the first glyph there and
folds the rest into the label with the same colour and the same spacing. An empty list leaves Dalamud's own
prefix. The game applies it to any top-level entry that has none.

`GlyphColor` is a `UIColor` row id. Build the two halves by hand with `GlyphPrefix` and `BuildLabel`.

---

## Filtering

Three filters run in order, and the first that fails ends the opening for that entry.

```csharp
Scope = ContextMenuScope.Inventory,                       // which menu, or Item for any menu on an item
Addons = ["Inventory", "InventoryLarge"],                 // exact addon names, null for any
ShowWhen = context => Market.Sells(context.ItemId),       // anything else
```

`EnabledWhen` runs after the entry is kept and decides whether it is clickable. A false answer draws it faded.
It never removes it.

```csharp
ContextMenuHelper.Matches(entry, context);   // the same answer, for a test
```

---

## Submenus

`Submenu` runs on click and its result opens under the entry. An empty list opens nothing, because the game
refuses an empty submenu.

```csharp
Submenu = context => [
    new ContextMenuEntry { Label = "Buy one", OnClick = _ => Buy(context.ItemId, 1) },
    new ContextMenuEntry { Label = "Buy a stack", OnClick = _ => Buy(context.ItemId, 99) },
],
```

`OnClick` runs before the submenu opens. An entry can do both. To decide the submenu after some other work,
open it from the click:

```csharp
OnClick = click => click.OpenSubmenu("Shopping lists", BuildLists(click.Context)),
```

---

## Ordering

`Priority` is where the entry sits, lowest first. Below zero puts it above the game's own entries, zero and
above put it below them. Entries that share a priority stay in registration order.

```csharp
ContextMenuHelper.Order(entries);   // the order the game draws them in
```

---

## Lifetime

Registering the first entry subscribes to the menu event, disposing the last one unsubscribes, and disposing a
token twice does nothing the second time. NoireLib's own disposal drops every remaining entry and the
subscription with them.

Filters, `EnabledWhen`, `OnClick` and `Submenu` all run on the framework thread.

---

## Dropping to the primitive

`OnConfigure` hands over the finished `MenuItem` for anything the record does not name.

```csharp
OnConfigure = item => item.Name = MyOwnSeString(),
```

`BuildItem(entry, context)` returns that item without registering anything, for a plugin driving
`NoireService.ContextMenu` itself.

---

## Troubleshooting

### The entry shows a boxed D instead of my glyph

- Check that `Glyphs` is not empty. The game gives any top-level entry with no prefix Dalamud's own.
- Check that the glyph is a `SeIconChar` the game has a texture for. `BoxedLetterA` through `BoxedLetterZ` and
  the boxed numbers all render.

### The entry never appears

- Check `Scope` against the menu you are opening. An `Inventory` entry never shows on a player.
- Check `ItemId` for an `Item` entry. An addon whose item is not listed under [Items](#items) is read from the hovered
  item. It is zero when the cursor left the item before the menu opened.
- Check `Addons` against the real addon name. It is case sensitive and empty for some openings.
- Log what `ShowWhen` is answering, and check `/xllog` for an exception thrown inside it.

### The submenu does not open

- Check that the list is not empty.
- Check that `Submenu` returned and did not throw. A thrown exception is swallowed and logged to `/xllog`.

If it still does not work, report it.

---

## See Also

- [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [AddonHelper](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/AddonHelper/README.md)
- [Hotkey Manager Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/HotkeyManager/README.md)
