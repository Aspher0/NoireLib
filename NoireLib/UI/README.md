# Helper Documentation : NoireLib.UI

You are reading the documentation for the `NoireLib.UI` helpers.

## Table of Contents
- [Overview](#overview)
- [Overlay Buttons](#overlay-buttons)
- [Positioning (UiPosition)](#positioning-uiposition)
- [Combo Box](#combo-box)
- [Custom Tooltips](#custom-tooltips)
- [Images (UiImageSource)](#images-uiimagesource)

---

## Overview

`NoireLib.UI` is a set of ImGui UI helpers:

- **`NoireOverlayButton`** — A standalone button overlayed on the game screen, drawn independently from any window. Anchorable anywhere (nine anchors, absolute pixels or screen ratio), with click/scroll callbacks, a hover mouse cursor, tooltips, a visibility condition evaluated on draw, per-state draw conditions (cutscene / gpose / hidden UI / always), drag-to-reposition, optional manual drawing, and full styling. Auto-disposed with NoireLib.
- **`NoireComboBox<T>`** — A combo box with an optional auto-focused filter input, wheel/arrow cycling of the highlighted option inside the dropdown, and an optional "hold key + mouse wheel" shortcut to cycle the selection on the closed combo (with or without looping).
- **`NoireTooltip`** — A custom tooltip system independent from `ImGui.SetTooltip()`, with customizable background transparency (0% to 100%) and mixed inline content (text, FontAwesome icons, images).

---

## Overlay Buttons

An overlay button lives on its own, on top of the game. Create it once and keep the instance — no per-frame call is needed on your side. It is disposed automatically when NoireLib is disposed; dispose it yourself earlier if you no longer need it (see [Lifetime & disposal](#lifetime--disposal)).

### Quick start

```csharp
using NoireLib.UI;

var button = new NoireOverlayButton("MyOverlayButton")
{
    Text = "Click me",
    Position = UiPosition.AtAnchor(UiAnchor.TopRight, new Vector2(-10f, 10f)),
    Tooltip = "I am a regular tooltip",
    OnLeftClick = _ => NoireLogger.PrintToChat("Left click!"),
};

// Optional: dispose it yourself when you no longer need it.
// Otherwise it is disposed automatically when NoireLib is disposed.
button.Dispose();
```

### Content

The button content can be a text, a FontAwesome icon, an image, or any combination (icon, then image, then text, horizontally centered and vertically aligned):

```csharp
var button = new NoireOverlayButton
{
    Icon = FontAwesomeIcon.Cog,
    Text = "Settings",
    Image = UiImageSource.FromGameIcon(66413),
    ImageSize = new Vector2(20f, 20f),
};
```

Or fully custom content (consider setting an explicit `Size`):

```csharp
var button = new NoireOverlayButton
{
    Size = new Vector2(120f, 40f),
    CustomContent = self =>
    {
        ImGui.TextUnformatted("Anything");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), "goes here");
    },
};
```

### Interactions

```csharp
button.OnLeftClick = self => { /* left click */ };
button.OnRightClick = self => { /* right click */ };
button.OnMiddleClick = self => { /* mouse wheel click */ };
button.OnScroll = (self, delta) => { /* mouse wheel scrolled over the button, delta > 0 = up */ };
```

### Visibility

```csharp
button.Show();
button.Hide();
button.Toggle();
button.SetVisible(null); // Toggles too

// Evaluated every frame, on draw:
button.VisibleCondition = () => NoireService.ClientState.IsLoggedIn;

// Disable interactions without hiding (dimmed with Style.DisabledAlpha):
button.Enabled = false;
```

### Tooltips

Both tooltip kinds can be shown **at the same time**:

```csharp
button.Tooltip = "A regular ImGui tooltip";
button.CustomTooltip = new TooltipContent()
    .AddText("CTRL + ")
    .AddIcon(FontAwesomeIcon.Mouse);
button.CustomTooltipStyle = new TooltipStyle { BackgroundOpacity = 0.5f };
```

### Dragging

```csharp
button.Draggable = true;
button.OnDragEnd = self =>
{
    // self.Position is now an absolute UiPosition, persist it if needed:
    MyConfig.ButtonPosition = self.Position.AbsolutePosition;
    MyConfig.Save();
};
```

### Hover cursor

The mouse cursor can be changed while the button is hovered:

```csharp
using Dalamud.Bindings.ImGui;

button.HoverCursor = ImGuiMouseCursor.Hand;   // null (default) leaves the cursor unchanged
```

The cursor is shown over the game as long as `UiBuilder.OverrideGameCursor` is enabled (it is, by default).

### Draw conditions (cutscene / gpose / hidden UI)

By default, an overlay button hides in the same situations as any plugin UI: during cutscenes, in group pose, and while the user has hidden the game UI. Use `DrawConditions` to keep a button visible in some or all of those states:

```csharp
using NoireLib.UI;

button.DrawConditions = OverlayDrawConditions.DrawInCutscenes;    // stays visible during cutscenes
button.DrawConditions = OverlayDrawConditions.DrawInGpose;        // stays visible in gpose
button.DrawConditions = OverlayDrawConditions.DrawWhenGameUiHidden; // stays visible when the game UI is hidden

// Combine them:
button.DrawConditions = OverlayDrawConditions.DrawInCutscenes | OverlayDrawConditions.DrawInGpose;

// Or always draw, no matter what:
button.DrawConditions = OverlayDrawConditions.AlwaysDraw;
```

NoireLib enables the matching per-plugin `UiBuilder` switch automatically as soon as one button needs it, and reverts it when no button needs it anymore. Because those switches are **per-plugin** (a Dalamud limitation), enabling any of these flags on a single overlay button also prevents Dalamud from auto-hiding **the rest of your plugin's UI** in that state — other overlay buttons that don't carry the matching flag are still hidden individually, but your own windows drawn through the same `UiBuilder` will stay visible.

### Manual drawing

By default NoireLib draws the button for you every frame. Set `AutoDraw = false` to take over and call `Draw()` yourself, from your own ImGui draw code (e.g. to control layering relative to your windows). The button stays registered and auto-disposed either way:

```csharp
button.AutoDraw = false;

// In your own UiBuilder.Draw handler / window:
button.Draw();
```

### Lifetime & disposal

Every overlay button registers itself for automatic disposal through `NoireLibMain.RegisterOnDispose`, so you don't have to track it: it is disposed when NoireLib is disposed. Call `Dispose()` to remove it earlier; it is safe to call multiple times. You can also drop every registered button at once:

```csharp
button.Dispose();              // Remove a single button now
NoireUI.RemoveAllOverlayButtons(); // Dispose every registered overlay button
```

### Styling

Every `null` style value falls back to the current ImGui style, so an unstyled button matches the active theme:

```csharp
button.Style = new OverlayButtonStyle
{
    Background = ColorHelper.HexToVector4("#20202080"),
    BackgroundHovered = null,   // Derived from Background automatically
    TextColor = new Vector4(1f, 1f, 1f, 1f),
    Rounding = 6f,
    BorderSize = 1f,
    Padding = new Vector2(10f, 6f),
    Alpha = 0.9f,
    FontScale = 1.2f,
};

button.AlwaysOnTop = true; // Draws above every regular ImGui window
```

---

## Positioning (UiPosition)

`UiPosition` is used by the overlay button and describes a screen position in one of three modes:

```csharp
// 1. One of the nine screen anchors, plus an optional pixel offset:
UiPosition.AtAnchor(UiAnchor.BottomCenter, new Vector2(0f, -40f));

// 2. Absolute pixels, relative to the top left corner of the game window:
UiPosition.AtAbsolute(100f, 250f);

// 3. Screen ratio: 10% from the left, 10% from the top:
UiPosition.AtRatio(0.1f, 0.1f);
```

Options:

```csharp
UiPosition.AtRatio(0.5f, 0.5f)
    .WithPivot(new Vector2(0.5f, 0.5f)) // Which point of the element is pinned (default: the anchor point in Anchor mode, the top left corner otherwise)
    .WithOffset(new Vector2(0f, 10f))
    .WithClampToViewport(false);        // Clamping is enabled by default: the element always stays fully on screen
```

In `Anchor` mode the pivot is automatic and intuitive: `BottomRight` pins the bottom right corner of the element to the bottom right corner of the screen, `MiddleCenter` centers it, etc.

---

## Combo Box

`NoireComboBox<T>` is stateful: create one instance per combo, keep it, and call `Draw()` every frame inside your window.

### Quick start

```csharp
private readonly NoireComboBox<string> jobCombo = new("JobCombo", new[] { "Paladin", "Warrior", "Dark Knight", "Gunbreaker" })
{
    Label = "Job",
    Width = 250f,
    FilterEnabled = true,
};

// In your Draw():
if (jobCombo.Draw())
    NoireLogger.PrintToChat($"Selected: {jobCombo.SelectedItem}");
```

### Filter

With `FilterEnabled = true`, the dropdown shows a text input at the top, **automatically focused when the dropdown opens**. Typing filters the options (case-insensitive "contains" on the display text by default).

```csharp
combo.FilterHint = "Search a job...";
combo.ClearFilterOnOpen = true;                     // Default
combo.FilterAutoFocus = true;                       // Default
combo.FilterPredicate = (item, filter) => ...;      // Custom matching
combo.NoResultsText = "Nothing found";
```

While the dropdown is open:
- **Mouse wheel** or **Up/Down arrows** cycle the highlighted option (the list follows it).
- **Enter** confirms the highlighted option.
- Clicking an option selects it, as usual.

```csharp
combo.DropdownWheelCycle = true;  // Default. Set to false to let the wheel scroll the list normally.
combo.DropdownCycleLoop = false;  // Default. Whether highlight cycling wraps around.
combo.VisibleItemCount = 8;       // Items shown before the list scrolls.
```

### Hold key + wheel cycling (closed combo)

The selection can be cycled by scrolling the mouse wheel over the **closed** combo, optionally gated behind a held key, with or without looping at the boundaries:

```csharp
combo.WheelCycleEnabled = true;
combo.WheelCycleHoldKey = VirtualKey.CONTROL; // null = no key required
combo.WheelCycleLoop = true;                  // true = wrap around, false = stop at the first/last item
```

A hint tooltip (drawn with `NoireTooltip`) is shown automatically when hovering the combo, e.g. "CONTROL + 🖱 ↕ to cycle":

```csharp
combo.WheelCycleHintEnabled = true; // Default
combo.WheelCycleHintContent = new TooltipContent() // Optional override
    .AddText("CTRL + ")
    .AddImage(UiImageSource.FromFile(@"C:\path\to\mouse_scroll.png"), new Vector2(16f, 16f));
combo.WheelCycleHintStyle = new TooltipStyle { BackgroundOpacity = 0.75f };
```

### Items & selection

```csharp
combo.SetItems(newItems);            // Keeps the selected item if still present
combo.Select(2);                     // By index, invokes OnSelectionChanged
combo.Select(item);                  // By item
combo.ClearSelection();
combo.SelectedIndex = 3;             // Silent (no callback)
var current = combo.SelectedItem;

combo.DisplayFunc = job => job.Abbreviation; // How items are displayed
combo.OnSelectionChanged = (oldItem, newItem) => { ... };
```

---

## Custom Tooltips

`NoireTooltip` draws tooltips that are **not** part of the regular ImGui tooltip system: they are independent windows on the topmost display layer. This means you can show a custom tooltip **and** a regular `ImGui.SetTooltip()` at the same time.

### Quick start

```csharp
ImGui.Button("Hover me");

// Plain strings are implicitly converted to TooltipContent:
NoireTooltip.ShowOnItemHover("I am a custom tooltip");

// And the regular tooltip still works alongside it:
if (ImGui.IsItemHovered())
    ImGui.SetTooltip("I am a regular tooltip");
```

### Content

Content is built from inline segments. Segments flow on the same line, **vertically centered against each other**, until `AddNewLine()` / `AddSeparator()`:

```csharp
var content = new TooltipContent()
    .AddText("CTRL + ")
    .AddImage(UiImageSource.FromFile(@"C:\path\to\mouse_scroll_down.png"), new Vector2(20f, 20f))
    .AddNewLine()
    .AddText("Scroll while pressing CTRL", new Vector4(0.7f, 0.7f, 0.7f, 1f))
    .AddSeparator()
    .AddIcon(FontAwesomeIcon.InfoCircle, new Vector4(0.4f, 0.7f, 1f, 1f))
    .AddText(" Icons, images and text can be mixed freely")
    .AddCustom(() => ImGui.ProgressBar(0.5f, new Vector2(120f, 0f)));

NoireTooltip.ShowOnItemHover(content);
```

### Style & transparency

The background transparency is customizable from 0% to 100%:

```csharp
var style = new TooltipStyle
{
    BackgroundOpacity = 0.25f,                       // 0 = fully transparent, 1 = fully opaque
    BackgroundColor = new Vector4(0f, 0f, 0f, 1f),   // Optional, defaults to the theme popup background
    TextColor = null,                                // Defaults to the theme text color
    BorderColor = new Vector4(1f, 1f, 1f, 0.3f),
    BorderSize = 1f,
    Rounding = 8f,
    Padding = new Vector2(10f, 8f),
};

NoireTooltip.ShowOnItemHover(content, style);
```

### Placement

```csharp
style.Placement = TooltipPlacement.Mouse;       // Default: follows the mouse cursor
style.MouseOffset = new Vector2(16f, 16f);

style.Placement = TooltipPlacement.AboveItem;   // Or BelowItem / LeftOfItem / RightOfItem
style.ItemGap = 6f;                             // Gap between the tooltip and the item
```

`Show(content, style)` can also be called unconditionally (every frame the tooltip should stay visible), independently from any hovered item.

---

## Images (UiImageSource)

`UiImageSource` describes an image usable by overlay buttons and tooltip contents:

```csharp
UiImageSource.FromFile(@"C:\path\to\image.png"); // From disk
UiImageSource.FromGameIcon(66413);               // From a game icon id
UiImageSource.FromGameTexture("ui/uld/image.tex"); // From an internal game texture path
UiImageSource.FromWrap(myTextureWrap);           // From a texture wrap you own (and dispose) yourself
```

File/game sources go through Dalamud's shared texture cache: they are cheap to resolve every frame and load asynchronously (an empty placeholder is drawn while loading).

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Hotkey Manager Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/HotkeyManager/README.md)
