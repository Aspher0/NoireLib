# Helper Documentation : NoireLib.UI

You are reading the documentation for the `NoireLib.UI` helpers.

## Table of Contents
- [Overview](#overview)
- [Overlay Buttons](#overlay-buttons)
- [Positioning (UiPosition)](#positioning-uiposition)
- [Combo Box](#combo-box)
  - [Plugging in the Hotkey Manager](#plugging-in-the-hotkey-manager)
- [Custom Tooltips](#custom-tooltips)
- [Images (UiImageSource)](#images-uiimagesource)

---

## Overview

`NoireLib.UI` is a set of ImGui UI helpers:

- **`NoireOverlayButton`** - A standalone button overlayed on the game screen, drawn independently from any window. Anchorable anywhere (nine anchors, absolute pixels or screen ratio), with click/scroll callbacks, a hover mouse cursor, tooltips, a visibility condition evaluated on draw, per-state draw conditions (cutscene / gpose / hidden UI / always), drag-to-reposition, optional manual drawing, and full styling. Auto-disposed with NoireLib.
- **`NoireComboBox<T>`** - A combo box with an optional auto-focused filter input (pinned above the options or scrolling with them), arrow-key cycling of the highlighted option inside the dropdown, and an optional "hold a binding + mouse wheel" shortcut to cycle the selection on the closed combo (with or without looping). The shortcut is a `HotkeyBinding` matched with the same rules as a hotkey, and can be driven straight from the Hotkey Manager so the user can rebind it.
- **`NoireTooltip`** - A custom tooltip system independent from `ImGui.SetTooltip()`, with customizable background transparency (0% to 100%) and mixed inline content built from `NoireContent`.
- **`NoireContent`** - A reusable block of rich inline content (text, dynamic text, FontAwesome icons, images, keycaps, and any widget), flowing on lines with vertical centering. Rendered by `NoireTooltip`, and by anything of your own through its public `Draw()`.

---

## Overlay Buttons

An overlay button lives on its own, on top of the game. Create it once and keep the instance - no per-frame call is needed on your side. It is disposed automatically when NoireLib is disposed; dispose it yourself earlier if you no longer need it (see [Lifetime & disposal](#lifetime--disposal)).

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
button.CustomTooltip = new NoireContent()
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

**These flags apply to the button that carries them, and to nothing else.** Keeping one overlay visible during a cutscene leaves your windows hiding exactly as they would have, and leaves every other overlay answering for itself.

That is worth spelling out, because Dalamud decides whether to hide plugin UI **once per plugin**, inside the draw callback it invokes for you: an overlay drawn from there could only be exempted by exempting your whole plugin along with it. NoireLib avoids that by not drawing overlays from your callback at all - they are drawn beside it, straight from Dalamud's frame, so nothing Dalamud decides about your plugin's UI reaches them and each overlay is free to answer for itself.

The single exception is a Dalamud that NoireLib cannot install its own draw hook into (a future version that moves what NoireLib reaches for). Overlays then fall back to being drawn with the rest of your UI, and Dalamud's per-plugin hiding applies to them all at once - so setting any flag on one overlay would also keep the rest of your plugin's UI visible in that state. NoireLib logs a warning when it happens, and you can check for it:

```csharp
if (!NoireUI.OverlaysDrawIndependently)
{
    // Overlays are sharing your plugin's draw callback, so their draw conditions are plugin-wide.
}
```

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

The filter is **pinned above the options** by default, so it stays put while they scroll. Set `FilterPinned = false` to let it scroll away with them:

```csharp
combo.FilterPinned = true;   // Default: only the option list scrolls
combo.FilterPinned = false;  // The whole dropdown scrolls, filter included
```

The dropdown always shows **exactly one scrollbar**, in either mode: it is sized to hold `VisibleItemCount` options plus the filter row, and shrinks to fit when there are fewer options.

While the dropdown is open:
- **Mouse wheel** scrolls the option list, as in any list.
- **Up/Down arrows** cycle the highlighted option (the list follows it).
- **Enter** confirms the highlighted option.
- Clicking an option selects it, as usual.

```csharp
combo.DropdownCycleLoop = false;  // Default. Whether arrow key cycling wraps around.
combo.VisibleItemCount = 8;       // Options shown before the list scrolls.
```

### Hold a binding + wheel cycling (closed combo)

The selection can be cycled by scrolling the mouse wheel over the **closed** combo, optionally gated behind a held binding, with or without looping at the boundaries:

```csharp
combo.WheelCycleEnabled = true;
combo.WheelCycleBinding = VirtualKey.CONTROL; // Default: an empty binding, meaning no key is required
combo.WheelCycleLoop = true;                  // true = wrap around, false = stop at the first/last item
```

`WheelCycleBinding` is a `HotkeyBinding`, the same model the [Hotkey Manager](#plugging-in-the-hotkey-manager) uses, so the whole binding surface is available and it is matched with the same rules a hotkey is (see `KeybindsHelper.IsBindingHeld`): a key, a modifier combination, a key plus modifiers, or a gamepad button. Modifiers must match **exactly**, so a combo bound to Ctrl does not cycle while Ctrl and Shift are both held.

```csharp
combo.WheelCycleBinding = VirtualKey.CONTROL;                    // A plain key converts implicitly
combo.WheelCycleBinding = new HotkeyBinding(0, ctrl: true, shift: true); // Ctrl + Shift, no key
combo.WheelCycleBinding = new HotkeyBinding(VirtualKey.G, ctrl: true);   // Ctrl + G
combo.WheelCycleBinding = GamepadButtons.North;                  // A gamepad button
```

While the combo is cycling a scroll, **nothing else scrolls**: not the window behind it, not any list it sits in. The combo claims the wheel from ImGui for as long as it is cycling, so the event is never routed anywhere else rather than being undone afterwards. This is not configurable and needs nothing from the host window. A scroll over an idle combo (cycling off, or the binding not held) still scrolls the surrounding window normally.

A hint tooltip (drawn with `NoireTooltip`) is shown automatically when hovering the combo, e.g. "Ctrl + 🖱 ↕ to cycle". It is generated from the binding actually in effect, so it follows a rebinding on its own:

```csharp
combo.WheelCycleHintEnabled = true; // Default
combo.WheelCycleHintContent = new NoireContent() // Optional override
    .AddText("CTRL + ")
    .AddImage(UiImageSource.FromFile(@"C:\path\to\mouse_scroll.png"), new Vector2(16f, 16f));
combo.WheelCycleHintStyle = new TooltipStyle { BackgroundOpacity = 0.75f };
```

### Plugging in the Hotkey Manager

Rather than hardcoding the shortcut, let the user rebind it: attach a hotkey registered on a [`NoireHotkeyManager`](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/HotkeyManager/README.md) and the combo reads its binding live.

```csharp
// Register the shortcut as a normal, user-rebindable hotkey.
// The combo reads the binding, not the trigger, so a hotkey registered only to gate a combo takes an empty callback.
hotkeyManager.RegisterHotkey(new HotkeyEntry("combo.cycle", "Cycle job", VirtualKey.CONTROL, () => { }, true, HotkeyActivationMode.Pressed));

combo.WheelCycleEnabled = true;
combo.BindWheelCycleHotkey(hotkeyManager, "combo.cycle");

// Anywhere in your settings window. The combo and its hint tooltip follow the new binding immediately:
hotkeyManager.DrawKeybindInputButton("combo.cycle");
```

The manager keeps owning the hotkey: its binding is only ever read, its own callback is untouched, and it stays usable as a regular hotkey at the same time. `BindWheelCycleHotkey` does not enable the cycling on its own, so set `WheelCycleEnabled` as well.

```csharp
combo.ResolvedWheelCycleBinding;   // The binding actually in effect (the hotkey's when attached, else WheelCycleBinding)
combo.UnbindWheelCycleHotkey();    // Detach: falls back to WheelCycleBinding
```

An attached hotkey that is disabled or unregistered resolves to an empty binding and turns the cycling **off**, rather than making it unconditional.

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

// Plain strings are implicitly converted to NoireContent:
NoireTooltip.ShowOnItemHover("I am a custom tooltip");

// And the regular tooltip still works alongside it:
if (ImGui.IsItemHovered())
    ImGui.SetTooltip("I am a regular tooltip");
```

### Content (NoireContent)

Content is built from inline segments held by a `NoireContent`. Segments flow on the same line, **vertically centered against each other**, until `AddNewLine()` / `AddSeparator()`:

```csharp
var content = new NoireContent()
    .AddText("CTRL + ")
    .AddImage(UiImageSource.FromFile(@"C:\path\to\mouse_scroll_down.png"), new Vector2(20f, 20f))
    .AddNewLine()
    .AddText("Scroll while pressing CTRL", new Vector4(0.7f, 0.7f, 0.7f, 1f))
    .AddNewLine()
    .AddText("Hold ").AddKeyCap("Ctrl").AddText(" and scroll")   // Keycap chips
    .AddNewLine()
    .AddText(() => $"Distance: {GetDistance():0.0}m")            // Dynamic text, re-evaluated each frame
    .AddSeparator()
    .AddIcon(FontAwesomeIcon.InfoCircle, new Vector4(0.4f, 0.7f, 1f, 1f))
    .AddText(" Icons, images and text can be mixed freely")
    .AddCustom(() => ImGui.ProgressBar(0.5f, new Vector2(120f, 0f)));

NoireTooltip.ShowOnItemHover(content);
```

`NoireContent` is not tooltip-specific. Its `Draw()` is public, so the same block can be rendered anywhere in your own ImGui code (a label, a table cell, a panel), not only inside a custom tooltip:

```csharp
content.Draw();   // Renders at the current cursor.
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
style.ItemGap = 6f;                             // Pushes the tooltip away from the item, along the placement axis
style.ItemOffset = new Vector2(12f, -4f);       // Shifts it freely on both axes, on top of the gap
```

`ItemGap` and `ItemOffset` do different jobs and compose: the gap moves the tooltip along whichever axis the placement implies (so it reads the same whichever side the tooltip is on), while the offset nudges it in x and y regardless of placement.

A tooltip is placed where it belongs on the frame it appears, with no visible settling into position.

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
