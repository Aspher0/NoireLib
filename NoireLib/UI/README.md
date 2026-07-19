# Helper Documentation : NoireLib.UI

You are reading the documentation for the `NoireLib.UI` helpers.

## Table of Contents
- [Overview](#overview)
- [The hub (NoireUI)](#the-hub-noireui)
  - [Automatic drawing](#automatic-drawing)
  - [Running work on the draw thread](#running-work-on-the-draw-thread)
  - [Reduced motion and strings](#reduced-motion-and-strings)
- [Scopes that take their body](#scopes-that-take-their-body)
- [Motion (NoireAnim)](#motion-noireanim)
- [Transient widget state (UiFrameState)](#transient-widget-state-uiframestate)
- [Persisted widget memory (NoireUiState)](#persisted-widget-memory-noireuistate)
- [Diagnostics](#diagnostics)
- [Theming (NoireTheme)](#theming-noiretheme)
- [Buttons (NoireButtons)](#buttons-noirebuttons)
- [Layout structures](#layout-structures)
- [Toasts (NoireToast)](#toasts-noiretoast)
- [Dialogs you await (NoireModal)](#dialogs-you-await-noiremodal)
- [Overlay Buttons](#overlay-buttons)
- [Positioning (UiPosition)](#positioning-uiposition)
- [Combo Box](#combo-box)
  - [Plugging in the Hotkey Manager](#plugging-in-the-hotkey-manager)
- [Custom Tooltips](#custom-tooltips)
- [Images (UiImageSource)](#images-uiimagesource)
- [The UI scale](#the-ui-scale)
- [Text at any size (NoireText)](#text-at-any-size-noiretext)

---

## Overview

`NoireLib.UI` is a set of ImGui UI helpers, built on a small shared foundation.

**Foundations**

- **`NoireUI`** - The hub. Owns the automatic-drawing policy, the element registry, the draw-thread queue (`RunOnDraw`), the frame clock, the UI scale, the reduced-motion switch and diagnostics.
- **`NoireLayout` / `NoireStyle`** - Containers and style scopes that take their body. No `using`, no `Dispose`, no `End()` anywhere, and an unbalanced raw-ImGui push inside a body is unwound at the boundary and logged once.
- **`NoireAnim`** - Time-based animation keyed by id: easing over twenty-one curves (or one of your own), springs, presence, pulses, sweeps and one-shots. Nothing to register or dispose.
- **`UiFrameState`** - Id-keyed transient state for immediate-mode helpers, typed per value and pruned automatically.
- **`NoireUiState`** - The small amount of widget memory that survives a reload (a dragged position, a collapsed section). One JSON file; every `Persist` switch defaults off.
- **`UiDiagnostics`** - Live counts, recent faults, the fault ladder, and the stack-leak net. Answers "why did nothing draw".
- **`NoireTheme`** - One palette the whole library follows, plus the type scale. An unset token falls through to the ImGui style, so a plugin that never touches it looks unchanged; set an accent and every widget re-tints at once.
- **`NoireText`** - Text at any size without ImGui's resampled-atlas blur: a real font built at the size asked for, behind a four-step type scale the theme owns.

**Widgets and elements**

- **`NoireButtons`** - The buttons ImGui does not ship: hold-to-confirm, an asynchronous button that disables and reports, a split button, an animated toggle, a segmented control and a spinner. Each takes a full `*Style` object and a custom-draw hook.
- **`NoireLayout`** (structures) - A draggable splitter, a collapsible section that can remember whether it was open, and a row that wraps.
- **`NoireToast` / `NoireToastArea`** - Real notifications: anchored, stacked, animated, actionable, with live progress, an undo pattern and a countdown that pauses on hover.
- **`NoireModal`** - Dialogs you `await`. Confirm, prompt and choice tiers, hold-to-confirm for destructive answers, and an optional "don't ask again".
- **`NoireOverlayButton`** - A standalone button overlayed on the game screen, drawn independently from any window. Anchorable anywhere (nine anchors, absolute pixels or screen ratio), with click/scroll callbacks, a hover mouse cursor, tooltips, a visibility condition evaluated on draw, per-state draw conditions (cutscene / gpose / hidden UI / always), drag-to-reposition, optional manual drawing, and full styling. Auto-disposed with NoireLib.
- **`NoireComboBox<T>`** - A combo box with an optional auto-focused filter input (pinned above the options or scrolling with them), arrow-key cycling of the highlighted option inside the dropdown, and an optional "hold a binding + mouse wheel" shortcut to cycle the selection on the closed combo (with or without looping). The shortcut is a `HotkeyBinding` matched with the same rules as a hotkey, and can be driven straight from the Hotkey Manager so the user can rebind it.
- **`NoireTooltip`** - A custom tooltip system independent from `ImGui.SetTooltip()`, with customizable background transparency (0% to 100%) and mixed inline content built from `NoireContent`.
- **`NoireContent`** - A reusable block of rich inline content (text, dynamic text, FontAwesome icons, images, keycaps, and any widget), flowing on lines with vertical centering. Rendered by `NoireTooltip`, and by anything of your own through its public `Draw()`.

---

## The hub (NoireUI)

`NoireUI` is static and needs no setup. It installs its per-frame pass the first time anything needs one, and tears it down with NoireLib.

### Automatic drawing

Screen-anchored elements (everything deriving `NoireDrawable`, `NoireOverlayButton` today) can draw themselves. Whether one does is a single rule:

```
effective = component.AutoDraw ?? NoireUI.AutoDraw
```

```csharp
NoireUI.AutoDraw = true;     // master DEFAULT: everything draws itself, unless it says otherwise
button.AutoDraw = false;     // ...except this one
button.AutoDraw = true;      // opt a single element in without flipping the master
button.AutoDraw = null;      // follow the master again
```

- **`NoireUI.AutoDraw`** is a `bool`, default `false`. It is the default policy every element inherits, **not** a kill switch.
- **`component.AutoDraw`** is a `bool?`, default `null`, meaning "follow the master". An explicit value always wins, in either direction, including over a master that is off. Every one of those values was set by the same plugin, so a master that overrode an explicit request would only be lying to its author.
- `NoireOverlayButton` ships with an explicit `true`, because an overlay exists precisely so nothing has to draw it. Set it to `null` to follow the master instead.
- **`Draw()` always works**, whatever the policy says. Call it from your own draw code to control layering; the hub skips anything already drawn manually on the same frame, so the two modes compose instead of doubling up.
- Draw order is yours by construction: if you call them, you ordered them. The hub only orders what you left to it.

```csharp
foreach (var element in NoireUI.GetDrawables())
    Log(element.Kind, element.Id, element.EffectiveAutoDraw);

NoireUI.RemoveAllDrawables();   // or RemoveAllOverlayButtons() for that one kind
```

### Running work on the draw thread

```csharp
NoireUI.RunOnDraw(() => combo.Select(index));   // safe from any thread
```

Anything touching ImGui or a widget from a timer, a socket, a hotkey callback or a background task goes through here. The queue is bounded (`NoireUI.RunOnDrawCapacity`, default 512) and drops the oldest rather than growing, and a frame drains only what was queued when it began, so an action that posts more work cannot stretch the frame. When NoireLib is not initialized there is no draw thread to marshal onto and the action runs inline.

```csharp
NoireUI.PendingDrawActions;   // waiting for the next frame
NoireUI.DroppedDrawActions;   // dropped because the queue was full, since startup
```

### Reduced motion and strings

```csharp
NoireUI.ReducedMotion = config.ReducedMotion;   // eased values snap, decorative motion stops
NoireUI.StringProvider = key => myLocalizer.GetOrNull(key);   // null falls back to the shipped English
```

`ReducedMotion` is manual: Dalamud exposes no accessibility signal to seed it from, so a plugin that wants to offer the option sets it from its own settings. NoireLib depends on no localization system and ships no locale files.

---

## Scopes that take their body

There is **no `using`, no `Dispose` and no `End()`** in this API. A container takes its body, nesting is the scope, and the layout the container implies comes with it.

```csharp
NoireLayout.Section("Filters", () =>
{
    NoireLayout.Disabled(!enabled, () => ImGui.SliderInt("Interval", ref interval, 0, 100));

    NoireLayout.Child("list", new Vector2(0f, 120f), () =>
    {
        // Simply not called if the region is entirely clipped. There is no success flag to check.
    }, border: true);
});

NoireStyle.With(new UiStyle { TextColor = theme.Danger }, () => ImGui.TextUnformatted("Careful"));
NoireStyle.WithAlpha(0.5f, DrawPreview);
```

Containers: `Group`, `Indent`, `Id`, `Disabled`, `ItemWidth`, `WrapText`, `Child`, `Tooltip`, `TooltipOnItemHover`, `Section`. Style scopes: `NoireStyle.With`, `WithColor`, `WithAlpha`.

- **Nothing to forget.** No dispose exists, so no dispose can be missed. An exception thrown inside a body unwinds the scope on its way out and keeps travelling, so a bug in your drawing code still surfaces as a bug.
- **`WrapText` takes a width, not a wrap position.** ImGui's own `PushTextWrapPos` wants a window-local x coordinate; passing it a screen coordinate (the natural mistake, since laying a panel out uses screen coordinates) puts the wrap point off to the right where it silently does nothing and the text never wraps. The container does the conversion, so the mistake is not reachable.
- **`Indent` means pixels.** `NoireLayout.Indent(0f, ...)` indents by nothing, deliberately unlike ImGui's own `Indent`, which reads zero as "use the default step". An animated indent easing down to zero would otherwise jump a whole step outwards on its final frame, which looks like the block teleporting into place. Ask for the standard amount by name with `NoireLayout.DefaultIndent`.
- **Raw ImGui stays fully available** inside a body, and is the real escape hatch. If you push and forget to pop, NoireUI unwinds it at the container boundary and logs **once**, naming the container. A leak becomes a log line instead of a week of "why is everything red". Turn it off with `NoireUI.Diagnostics.RepairStackLeaks = false`.

`UiStyle` carries named properties for what widgets usually touch (`TextColor`, `ButtonColor`, `FrameRounding`, `FramePadding`, ...) over three maps that reach everything ImGui has:

```csharp
var style = new UiStyle { TextColor = theme.Danger, FrameRounding = 0f };
style.Colors[ImGuiCol.PlotHistogram] = theme.Danger;   // anything without a named property
style.With(ImGuiStyleVar.ScrollbarSize, 14f);
var softer = style.Clone();
```

### Allocation

A body lambda allocates one delegate per call per frame (a few dozen bytes, invisible in most UIs). Where it matters, every container has a state overload that keeps the body `static` and allocates nothing:

```csharp
NoireLayout.Section("Filters", this, static (self) => self.DrawFilters());
```

---

## Motion (NoireAnim)

Time-based and keyed by id. Nothing is registered, created or disposed: each call reads the value for this frame and stores what it needs in [`UiFrameState`](#transient-widget-state-uiframestate). A widget that stops calling stops animating, and its state is pruned on its own.

```csharp
// Eased: changing the target continues from where the value is, so a reversed hover never snaps.
var hover = NoireAnim.Ease("save-button", "hover", ImGui.IsItemHovered() ? 1f : 0f);

// Spring: carries momentum, so a target that keeps moving is followed rather than restarted.
var offset = NoireAnim.Spring("panel", "slide", expanded ? 220f : 0f);

// Presence: draw while it is above zero, rather than while the flag is true.
var presence = NoireAnim.Presence("panel", "shown", isOpen);
if (presence > 0.001f)
    NoireStyle.WithAlpha(presence, DrawPanel);

// Stateless periodic reads of the shared clock.
var glow = NoireAnim.Pulse(period: 1.5f, min: 0.3f, max: 1f);
var shimmer = NoireAnim.Sweep(period: 2f);

// One-shots: start once, run themselves out.
if (saved)
    NoireAnim.Trigger("save-button", "saved");

var flash = NoireAnim.Flash("save-button", "saved");    // 1 down to 0
var nudge = NoireAnim.Shake("name-field", "rejected");  // pixels, dying out
```

Curves are `UiEasing` (21 of them, `OutCubic` by default), and `easing.Apply(t)` is pure so it composes with anything. A curve of your own is an overload, never a fork:

```csharp
var value = NoireAnim.Ease("id", "sub", target, duration: 0.3f, curve: t => t * t);
```

**Two-part ids are the point.** Pass the widget id and the property separately rather than interpolating them: two existing strings cost nothing to look up, while `$"{id}.hover"` allocates on every property of every widget, every frame.

Everything degrades under `NoireUI.ReducedMotion`: eased values and springs snap to their target, `Pulse` holds at its high end, `Sweep` returns 1, and `Flash`/`Shake` return 0. Nothing becomes unusable; only the movement goes away.

---

## Transient widget state (UiFrameState)

The small amount of memory a stateless-looking widget needs between frames: a hold progress, a drag origin, an animation phase. Entries are keyed by a caller id plus a sub key, **typed per value** (so a `float` and an `int` on the same key never collide), and pruned once they have gone untouched for `PruneAfterFrames` frames.

```csharp
var held = UiFrameState.Get<float>("delete-button", "hold");
UiFrameState.Set("delete-button", "hold", held + ImGui.GetIO().DeltaTime);

var origin = UiFrameState.GetOrAdd("splitter", "origin", () => ImGui.GetMousePos());

UiFrameState.Update<DragState>("row", "drag", (ref DragState s) => s.Offset += delta);
```

It is not a configuration store: nothing is persisted, and everything is lost on reload. **Draw thread only.**

No member returns a `ref` into the store, deliberately. A `ref` into a dictionary is invalidated by the next insert (growing abandons the backing array), so a write through a stale one vanishes silently, triggered by an unrelated widget happening to exist on the same frame. `Update` covers that case safely by copying out, mutating, and writing back.

---

## Persisted widget memory (NoireUiState)

Where `UiFrameState` forgets everything between sessions, `NoireUiState` is the small amount that has to survive one: where a user dragged an overlay, which sections they left collapsed, which column they last sorted by. One JSON file beside your configuration, one flat key space, written on a debounce and again on shutdown.

```csharp
NoireUiState.Set("myplugin.rows.sort", "name");
var sort = NoireUiState.Get("myplugin.rows.sort", "date");

NoireUiState.RemoveAll("myplugin.rows.");   // forget one widget
NoireUiState.Save();                        // write now instead of waiting out SaveDelay
```

**This is not a replacement for your configuration.** Nothing here is versioned, migrated, validated or backed up, and it is deleted without ceremony when a user resets their layout. Anything a user would be upset to lose belongs in the configuration system, which does all of that. What belongs here is state a widget would rebuild without complaint, and which is only worth keeping because rebuilding it is mildly annoying.

A stored value of the wrong shape reads as absent rather than throwing: the file is editable by hand, and one bad entry must not take a window down.

### Widgets that persist

Every `Persist` switch on a widget defaults to **off**, so a plugin that never opts in never grows a state file.

```csharp
var button = new NoireOverlayButton("my-toggle")   // a stable id, not a generated one
{
    Draggable = true,
    PersistPosition = true,     // remembers where the user dragged it
};
```

**Persisting needs a stable id.** A widget created without one gets a fresh GUID every session, so an entry keyed on it could never be read back: the file would grow forever and restore nothing, and the symptom (a position that silently never sticks) points nowhere near the cause. NoireUI refuses to persist against a generated id and logs once, naming the fix.

---

## Diagnostics

```csharp
var stats = NoireUI.Diagnostics.Snapshot();
// Frame, Drawables, AutoDrawn, StateEntries, PendingDrawActions,
// DroppedDrawActions, StackRepairs, Faults, DisabledDrawables

NoireUI.Diagnostics.OnFault = fault => myLog.Add($"{fault.Source}: {fault.Message}");
NoireUI.Diagnostics.RecentFaults;   // the last 32, oldest first
```

Faults are logged before they reach `OnFault`, and an exception thrown by the handler is swallowed so a broken reporter cannot take the frame down.

**The fault ladder** disables the narrowest broken thing rather than logging forever. An element that throws on `FaultTolerance` consecutive frames (default 10) has its `AutoDraw` switched off, alone, with one error explaining why. `Draw()` still works, so nothing is unrecoverable. Set `FaultTolerance = 0` to never switch anything off.

---

## Theming (NoireTheme)

`NoireTheme.Current` is the palette every widget resolves against. Nothing is required: a token left `null` falls through to the host's ImGui style, so a plugin that never touches this looks exactly as it did before.

```csharp
NoireTheme.Current = NoireTheme.FromAccent("#C8A96A");   // one color in, a whole palette out
NoireTheme.Current.Danger = myRed;                       // override one token, inherit the rest
```

Resolution runs in three steps, always in this order: **the value the widget was given, then the theme, then the ImGui style.** That is what lets one theme set two colors and leave everything else alone.

**Derived states, not stored ones.** `Hover()` and `Active()` derive from the base color instead of holding separate values, so a re-skin can never leave a stale hover color behind.

`TintSource` decides which way they move:

| `ThemeTintSource` | What decides the direction |
|---|---|
| `Item` (default) | Each color decides for itself: a dark button brightens, a pale accent one darkens. Both visibly respond. |
| `Surface` | The theme decides for everything: a dark theme brightens, a light one darkens. Consistent, but washes out a color already close to that direction. |
| `Lighten` / `Darken` | Always that direction, whatever the color. |

The default is `Item` because a single fixed direction does not survive a whole palette: brightening looks right on a dark neutral button and washes out a pale accent one.

```csharp
var theme = NoireTheme.Current;
theme.Resolve(ThemeColor.Accent);      // never null, whatever is or is not set
theme.Hover(theme.Resolve(ThemeColor.Accent));
theme.On(fill);                        // a text color legible on that fill
theme.Muted(color);                    // faded to MutedAlpha
theme.CustomColors["deco.hairline"] = c;   // tokens the library does not define
```

Shape lives here too (`Rounding`, `SurfaceRounding`, `BorderSize`, `FramePadding`, `ItemSpacing`), each resolving the same way. `ToStyle()` returns a `UiStyle` that paints raw ImGui with the theme, for your own `ImGui.Button` calls sitting beside NoireUI widgets:

```csharp
NoireStyle.With(NoireTheme.Current.ToStyle(), () => DrawMyWindowBody());
```

**Sharing.** A theme travels as an ordinary share code, tagged with its own kind:

```csharp
var code = NoireTheme.Current.ToShareCode();

var result = NoireTheme.FromShareCode(pasted);
if (result.Success)
    NoireTheme.Current = result.Value!;
else
    ShowError(result.Message);      // never throws on a bad paste
```

Decoding targets an inert `ThemeSnapshot`, never the live theme, and a color name this version does not know is skipped rather than failing the import. See the [ShareCodeHelper README](../Helpers/ShareCode/README.md).

---

## Buttons (NoireButtons)

Immediate, nothing to construct or dispose, all of it themed.

```csharp
if (NoireButtons.Button("Save", ButtonTone.Accent))
    Save();
```

A **tone** is what a button means (`Neutral`, `Accent`, `Success`, `Warning`, `Danger`, `Ghost`), which is what decides its colors. Passing a tone allocates nothing; pass a `ButtonStyle` when you want to override individual values.

**Hold to confirm** is the alternative to a confirmation dialog for a destructive action. The pause is the confirmation.

```csharp
if (NoireButtons.HoldToConfirm("Hold to delete everything"))
    DeleteEverything();

// The fill is the whole interface of a hold button, so its shape is a setting.
new ButtonStyle { Tone = ButtonTone.Danger, HoldFill = HoldFillMode.CenterOut };
```

`HoldFillMode` covers `LeftToRight`, `RightToLeft`, `CenterOut`, `BottomUp` and `Border` (which traces the outline clockwise instead of filling). `HoldFillColor` overrides the fill, which otherwise defaults to a markedly brighter form of the button's own color: one derived state along is invisible on a colored button, and a hold nobody can see reads as a button that does not work.

It fires once per press, not once per frame, and the fill runs off wall-clock time so it takes the same real duration whatever the frame rate. It deliberately ignores `ReducedMotion`: the delay is a safety mechanism, not decoration.

**Async** disables itself and shows a spinner until the task finishes. Clicking twice cannot start the work twice, and a failure is reported through `UiDiagnostics` rather than becoming an unobserved exception.

```csharp
NoireButtons.Async("Upload", () => UploadAsync(), onCompleted: failure =>
{
    if (failure != null)
        NoireToast.Error($"Upload failed: {failure.Message}");
});
```

**Split**, **Toggle** and **Segmented** round it out. The menu of a split button takes its body, so nothing is drawn while it is closed:

```csharp
if (NoireButtons.Split("Export", () => { if (ImGui.MenuItem("Export as CSV")) ExportCsv(); }))
    ExportDefault();

NoireButtons.Toggle("Enabled", ref config.Enabled);
NoireButtons.Segmented("quality", ref config.Quality, new[] { "Low", "Medium", "High" });
```

**Drawing it yourself.** Every style carries a custom-draw hook. NoireUI keeps doing the sizing, the hit testing and the state; the hook paints. A bespoke button is configuration, not a fork:

```csharp
var deco = new ButtonStyle
{
    Tone = ButtonTone.Accent,
    CustomDraw = args =>
    {
        args.DrawList.AddRectFilled(args.Min, args.Max, ColorHelper.Vector4ToUint(args.Color));
        args.DrawList.AddText(args.Center - ImGui.CalcTextSize(args.Label) * 0.5f,
            ColorHelper.Vector4ToUint(args.TextColor), args.Label);
    },
};
```

`UiButtonDraw` carries the geometry, the hover and held flags, the resolved colors and the hold progress, so a custom button still follows the theme. `ToggleStyle.CustomDraw` works the same way through `UiToggleDraw`.

---

## Layout structures

Beside the containers that take their body, `NoireLayout` ships the three pieces ImGui leaves to you.

**Splitter.** ImGui has none, so every plugin that wants a resizable sidebar writes the same invisible button, mouse-delta and cursor dance.

```csharp
NoireLayout.Child("left", new Vector2(paneWidth, paneHeight), DrawLeft, border: true);
ImGui.SameLine(0f, 0f);
NoireLayout.Splitter("split", ref paneWidth, minSize: 120f, maxSize: 420f, length: paneHeight);
ImGui.SameLine(0f, 0f);
NoireLayout.Child("right", new Vector2(0f, paneHeight), DrawRight, border: true);
```

Give it a `length` whenever the panes are a fixed size. Left at zero it fills the rest of the region, which is right for panes that do the same and visibly wrong for panes that do not: the divider runs past them down the window.

The value is clamped every frame, not only while dragging, so a width restored from a config written on a wider screen is corrected on the first frame instead of leaving a pane off the edge.

**Collapsible sections** fold their body away, and can remember whether they were open.

```csharp
NoireLayout.Collapsible("filters", "Filters", DrawFilters, new CollapsibleOptions
{
    Persist = true,                       // survives a reload; off by default
    HeaderExtras = () => DrawActiveFilterCount(),
    HeaderExtrasWidth = 120f,
    Danger = false,
});
```

Header extras are drawn open or closed, so a summary survives the fold. Persistence is keyed on the section id, so **the id has to be stable across sessions**; a section that asks to persist against a blank id is refused with one log line rather than filling the state file with entries nothing will ever read back.

**Wrapping rows.** `SameLine` alone cannot know whether the item after it fits, so a hand-written row either overflows or breaks early.

```csharp
NoireLayout.Flow(tags, tag => ImGui.CalcTextSize(tag) + padding * 2f, DrawChip);
```

`NoireLayout.FlowItem(width, first)` is the primitive underneath, for a loop that is not a list you can hand over.

**Where a row wraps.** ImGui has no concept of a right margin: indenting moves the left edge only, and the content region keeps reporting the *window's* right edge however deeply nested the drawing is. So a row inside a hand-drawn panel has nothing to wrap against and overflows it. Both calls take an optional `width` for exactly that case:

```csharp
NoireLayout.Flow(tags, Measure, DrawChip, width: myPanelWidth);
```

Left at zero they use the text wrap position if one is set (which is what `NoireLayout.WrapText` sets, so a row inside one wraps where its text does), and the window's content edge otherwise. **A panel that owns a width nobody else can see has to say so.**

---

## Toasts (NoireToast)

Real notifications: anchored, stacked, animated, actionable.

```csharp
NoireToast.Success("Preset saved");
NoireToast.Error("Could not reach the server")
    .WithTitle("Sync failed")
    .WithAction("Retry", _ => Retry(), ButtonTone.Accent);
```

Raising a toast is safe from any thread and needs no wiring: `NoireToastArea.Default` draws itself, so a toast from a command handler, a background task or a hotkey callback appears on its own. Nothing touches ImGui off the draw thread; the toast queues and the area picks it up on its next frame, which is also when its clock starts. A toast raised while the interface is hidden therefore still gets its full duration rather than expiring unseen.

**The undo pattern** replaces a confirmation dialog for anything reversible: do the thing, then offer a way back.

```csharp
DeletePresets(selected);
NoireToast.Undo($"{selected.Count} presets deleted", () => RestorePresets(selected));
```

**The countdown.** `ToastStyle.Timer` decides how a toast shows the time it has left: `BottomBar` (the default), `TopBar`, `Stripe` (beside the severity stripe, not over it), `Border` (traced clockwise from the top left, half a thickness inside the edge so all four sides come out the same weight), `TintLeftToRight`, `TintRightToLeft`, or `None`. `TimerThickness`, `TimerColor`, `TimerTintAlpha` and `TimerDrains` tune it. It is inert on a toast with no duration, which has nothing to count down to.

A toast that vanishes with no warning reads as a glitch, and one that is visibly about to vanish while being read is worth reaching for, which is what the hover pause is for.

**Live progress** for work in flight, coloured by `ProgressColor` / `ProgressTrackColor` / `ProgressHeight`. The filled part defaults to a slightly darker form of the severity colour (`ProgressDarken`), so the bar sits under the message rather than competing with the stripe and the icon, which are already showing that colour at full strength. A toast with a progress reading stays until you dismiss it:

```csharp
var toast = new NoireToast("Importing presets").WithProgress(() => importProgress).Show();
// ...when the work finishes:
NoireUI.RunOnDraw(() => toast.Dismiss());
```

The countdown **pauses while a toast is hovered**, so a message cannot expire while it is being read. Errors stay noticeably longer than the rest by default.

**Placing the stack yourself.** Construct an area to put a second stack somewhere else, or to draw one inside a window of your own. An area you construct follows the `NoireUI.AutoDraw` master default, because constructing one is how you say where the stack goes:

```csharp
var area = new NoireToastArea("Sidebar")
{
    Position = UiPosition.AtAnchor(UiAnchor.TopRight, new Vector2(-20f, 20f)),
    Width = 300f,
    MaxVisible = 3,
};
new NoireToast("Only in this corner").Show(area);
```

`AlwaysOnTop` is on by default: a notification hidden behind the window that raised it has not notified anyone.

**Being on top is two orders in ImGui, and `AlwaysOnTop` moves both.** Drawing is decided by the draw layer first and the display list second; input is decided by the display list alone. Promote only the layer (`ImGuiWindowFlags.Tooltip`) and the element is painted over everything while receiving none of the clicks aimed at it. Reorder only the display list and clicking a window behind moves it in front for one frame before the next frame puts things back, which shows up as a flicker. `AlwaysOnTop` does both: the layer keeps the drawing immune to that churn, the reorder keeps the input right.

It deliberately does not take keyboard focus, which would make text fields in every other window impossible to type in. Within the top layer the last window to ask each frame wins, which is what keeps a tooltip above an always-on-top element it overlaps.

The queue is bounded (`Capacity`, drop-oldest, counted in `DroppedCount`): an unbounded queue in front of an interface that has stopped drawing is a memory leak.

---

## Dialogs you await (NoireModal)

Asking the user a question is a question, so it reads like one. No popup-open boolean, no pending-action field to stash the answer against, no callback three frames later somewhere else in the file.

```csharp
if (await NoireModal.ConfirmAsync("Delete preset", $"Delete '{name}'? This cannot be undone.",
        new ModalOptions { Danger = true, HoldSeconds = 1f }))
    DeletePreset(name);

var newName = await NoireModal.PromptAsync("Rename", "What should it be called?", name);
if (newName != null)
    Rename(newName);

var choice = await NoireModal.ChoiceAsync("Unsaved changes", "You have unsaved changes.",
    new[] { "Save", "Discard", "Keep editing" });
```

`ConfirmAsync` returns `false` when the dialog is dismissed with Escape or by clicking away, `PromptAsync` returns `null`, and `ChoiceAsync` returns `-1`. Dialogs queue, and raising one from any thread is safe.

**Two rules worth stating plainly:**

- **Never block on one of these from the draw or framework thread.** The task completes on the draw thread, so waiting on it there waits for a frame that cannot start until the wait ends. `await` it, or hang the game.
- **Every pending dialog is completed as cancelled when NoireLib is disposed**, so a plugin unload can never leave an `await` suspended forever.

**"Don't ask again"** is available through `RememberKey`, stored in `NoireUiState` and cleared with `NoireModal.Forget(key)`:

```csharp
await NoireModal.ConfirmAsync("Close to tray", "Keep running in the background?",
    new ModalOptions { RememberKey = "close-to-tray" });
```

Only for confirmations whose answer is genuinely stable. **Never offer it for a destructive action or for applying content from outside the plugin**: a remembered yes turns the confirmation into no confirmation at all, which is exactly what those dialogs exist to prevent. An answer is only remembered when the user ticked the box, and a cancelled dialog remembers nothing.

`NoireModal.Host` presents the dialogs and draws itself, because an awaited dialog nobody drew would never return and the symptom is a hang with nothing on screen to explain it. Set its `AutoDraw` to `false` and call `NoireModal.Draw()` to place it in your own draw order.

---

## Overlay Buttons

An overlay button lives on its own, on top of the game. `AlwaysOnTop` keeps it in front of other windows for clicks as well as for drawing (see the note under [Toasts](#toasts-noiretoast) for why those are separate in ImGui). Create it once and keep the instance - no per-frame call is needed on your side. It is disposed automatically when NoireLib is disposed; dispose it yourself earlier if you no longer need it (see [Lifetime & disposal](#lifetime--disposal)).

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

`AutoDraw` is a `bool?`: `null` follows the `NoireUI.AutoDraw` master default instead of deciding for itself. An overlay button starts at an explicit `true` because that is what an overlay is for. See [Automatic drawing](#automatic-drawing) for the full rule.

`Draw()` is available whatever the setting is, and the hub skips anything already drawn manually on the same frame, so calling it yourself once in a while does not double-draw.

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

## The UI scale

Dalamud lets the user pick how large the interface is, and applies that scale to the ImGui style: text, frame padding and everything else you read out of `ImGui.GetStyle()` already arrives at the right size. Numbers a library ships do not. NoireUI handles this in one place, and the rule is worth knowing because getting it wrong is invisible on the machine you develop on.

```csharp
NoireUI.Scale                       // the user's scale, where 1 is 100%
NoireUI.Scaled(12f)                 // a pixel value of your own, authored at 100%
NoireUI.Scaled(new Vector2(12, 10))
```

**A number NoireUI has an opinion about is written at 100% and scaled for you.** Everything on `NoireTheme`, on any `*Style`, on `ModalOptions`, on a `UiPosition`, plus `NoireToastArea.Width` and `NoireOverlayButton.Size`. Set a toast width of 340 and it is 340 pixels at 100% and 510 at 150%, without your code knowing the scale exists. Each value is multiplied once, where it resolves, so it cannot be scaled twice by two call sites each being careful.

**A number NoireUI only hands to ImGui is already in real pixels and is left alone.** A `size` argument on `NoireButtons.Button`, `NoireComboBox.Width`, a `Splitter`'s `size` and its bounds, the `width` of a `Flow` or `WrapText`, the amount given to `NoireLayout.Indent`. These sit in the same space as the `CalcTextSize` and `GetContentRegionAvail` they are usually computed from, and scaling them would break the arithmetic they are part of. NoireUI's own defaults inside those calls (a splitter's minimum, a button's smallest size) do scale.

**Anything a `Resolve` method gives back is finished.** `theme.ResolveFramePadding()`, `theme.ResolveRounding()` and the rest return real pixels. Passing one through `NoireUI.Scaled` is the one way to get this wrong, and at 100% it looks perfect.

For pixel values of your own, use `NoireUI.Scaled` rather than reading Dalamud's scale a second time, so your drawing and the widgets beside it cannot disagree about how large the interface is. `NoireUIDemoPlugin` does exactly this for its own bespoke shapes.

---

## Text at any size (NoireText)

An ImGui font is a bitmap atlas rasterized once at one size. `SetWindowFontScale` and a scaled font push do not rasterize anything larger, they sample that bitmap larger, so a heading at twice the base size is a pixel-crawled upscale of a small glyph. No ImGui setting fixes it. `NoireText` builds a real font at the size asked for and draws with that.

```csharp
NoireText.Draw("Settings", TextSize.Heading);
NoireText.Muted("3 profiles loaded", TextSize.Caption);
NoireText.Colored(theme.Resolve(ThemeColor.Danger), "Not connected", TextSize.Body);

NoireText.At(TextSize.Display, () =>
{
    ImGui.TextUnformatted("Noire");   // raw ImGui inside the scope draws at the size too
    NoireText.Draw("Deco");
});
```

`Draw`, `Colored`, `Muted`, `Disabled`, `Wrapped`, `Bullet`, `Centered`, `CalcSize`, `LineHeight`, and the `At` scopes. Sizes are logical pixels at 100%, like every other measurement here.

### Ask by role, not by number

`TextSize` has four steps: `Display`, `Heading`, `Body`, `Caption`. They resolve through `NoireTheme`, and every step except the body derives from the body size by a shipped proportion, so one number moves the whole scale:

```csharp
NoireTheme.Current.BodySize = 20f;      // the whole scale grows with it
NoireTheme.Current.HeadingSize = 24f;   // this step opts out; the others keep following
NoireTheme.Current.HeadingSize = null;  // and back onto the proportion
```

`BodySize` left unset is the host's own default font size (`NoireTheme.DefaultBodySize`), so an untouched theme is indistinguishable from ordinary `ImGui.TextUnformatted` beside it, and costs no atlas space at all.

An explicit `NoireText.Draw(text, 22f)` is there when you need it, and is the thing to avoid at thirty call sites: a number at a call site is a number the next thirty will each pick differently.

### What it costs, and the limit

Every distinct size is a full glyph atlas. NoireUI builds them into an atlas of its own, so adding a heading never forces the host plugin's fonts to rebuild alongside it, and caches one entry per size for the life of the plugin.

**The cache is bounded at 16 distinct sizes.** Past that it refuses to build more, draws at the nearest size it already has, and logs once naming the limit. It refuses rather than evicting because something may be mid-draw with the handle it would have thrown away. An interface with more than sixteen genuinely different text sizes has a type scale that has stopped being one, and running out of texture memory is the wrong place to find that out. `NoireUI.Diagnostics.Snapshot().TextFontSizes` reports the count.

### While a size is still building

Rasterizing a size takes a moment, and NoireUI does two things so you never watch it happen.

**The whole scale is built in one go.** Registering a font asks the atlas to rebuild, so building the four steps as they were each first drawn meant four rebuilds back to back, which is a second or two of interface at the wrong size. Any miss builds every step of the current theme's scale at once instead.

**The wait is the right size, not the right sharpness.** Until the real font is ready the text is drawn by stretching the font already loaded to the size asked for. That is the blurry scaling this whole section exists to replace, used deliberately and briefly, because the alternative is worse in the way that shows: text that starts small and jumps when its font arrives takes the layout around it along with it. Right size and briefly soft beats right sharpness and briefly wrong.

**`NoireText.Prewarm()` removes even that.** Call it when your plugin loads, or after setting a theme, and the scale is rasterized before anything asks to draw with it. Safe to call repeatedly; a size already built is not built again.

```csharp
public Plugin()
{
    NoireLibMain.Initialize(PluginInterface, this);
    NoireTheme.Current = NoireTheme.FromAccent("#C8A96A");
    NoireText.Prewarm();
}
```

**`CalcSize` measures whatever would draw.** It pushes the same font first, stand-in included, so a layout built on it cannot end up a few pixels wrong everywhere with neither font looking like the one lying.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Hotkey Manager Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/HotkeyManager/README.md)
