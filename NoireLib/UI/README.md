# Helper Documentation : NoireLib.UI

You are reading the documentation for the `NoireLib.UI` helpers.

## Table of Contents
- [Overview](#overview)
- [Two ways to reach every surface](#two-ways-to-reach-every-surface)
- [The hub (NoireUI)](#the-hub-noireui)
  - [Automatic drawing](#automatic-drawing)
  - [Running work on the draw thread](#running-work-on-the-draw-thread)
  - [Reduced motion and strings](#reduced-motion-and-strings)
- [Scopes that take their body](#scopes-that-take-their-body)
- [Motion (NoireAnim)](#motion-noireanim)
- [Transient widget state (UiFrameState)](#transient-widget-state-uiframestate)
- [Persisted widget memory (NoireUiState)](#persisted-widget-memory-noireuistate)
- [Session-only memory (NoireUiSession)](#session-only-memory-noireuisession)
- [Diagnostics](#diagnostics)
- [Theming (NoireTheme)](#theming-noiretheme)
- [Buttons (NoireButtons)](#buttons-noirebuttons)
- [Layout structures](#layout-structures)
- [Framed containers (NoirePanel)](#framed-containers-noirepanel)
- [Windows you draw yourself (NoireWindowChrome)](#windows-you-draw-yourself-noirewindowchrome)
- [The window menu (NoireWindowMenu)](#the-window-menu-noirewindowmenu)
- [Sliders (NoireSliders)](#sliders-noiresliders)
- [Toasts (NoireToast)](#toasts-noiretoast)
- [Dialogs you await (NoireModal)](#dialogs-you-await-noiremodal)
- [Guided tours (NoireTour)](#guided-tours-noiretour)
- [Overlay Buttons](#overlay-buttons)
- [Positioning (UiPosition)](#positioning-uiposition)
  - [Targets that may not be there](#targets-that-may-not-be-there)
- [Pinning a window to the game (NoireAddonAttach)](#pinning-a-window-to-the-game-noireaddonattach)
- [Labels in the world (NoireWorldLabel)](#labels-in-the-world-noireworldlabel)
- [Gauges and sparklines (NoireGauges)](#gauges-and-sparklines-noiregauges)
- [Combo Box](#combo-box)
  - [Keeping the search](#keeping-the-search)
  - [Long lists](#long-lists)
  - [Custom rows](#custom-rows)
  - [Plugging in the Hotkey Manager](#plugging-in-the-hotkey-manager)
- [Tag fields (NoireTagInput)](#tag-fields-noiretaginput)
- [Picking several at once (NoireMultiCombo)](#picking-several-at-once-noiremulticombo)
- [Game data pickers (NoireExcelPicker)](#game-data-pickers-noireexcelpicker)
- [Settings fields (NoireInputs)](#settings-fields-noireinputs)
- [Data grids (NoireTable)](#data-grids-noiretable)
- [Reorderable lists (NoireReorderableList)](#reorderable-lists-noirereorderablelist)
- [Tab bars you can drive (NoireTabBar)](#tab-bars-you-can-drive-noiretabbar)
- [Badges and attention (NoireBadge, NoireAttention)](#badges-and-attention-noirebadge-noireattention)
- [Keyboard focus (NoireFocus)](#keyboard-focus-noirefocus)
- [Custom Tooltips](#custom-tooltips)
- [Images (UiImageSource)](#images-uiimagesource)
- [Brand and custom icons (NoireIcons)](#brand-and-custom-icons-noireicons)
- [The UI scale](#the-ui-scale)
- [Text at any size (NoireText)](#text-at-any-size-noiretext)
  - [Letter-spacing](#letter-spacing)
- [Fonts of your own (NoireFont)](#fonts-of-your-own-noirefont)
- [Drawing shapes (NoireShapes)](#drawing-shapes-noireshapes)
  - [Where it draws](#where-it-draws)
  - [Plates](#plates)
  - [Gradients over anything](#gradients-over-anything)
  - [Frames and corner ticks](#frames-and-corner-ticks)
  - [Arcs, rings and wedges](#arcs-rings-and-wedges)
  - [Pattern fills](#pattern-fills)
  - [Glows, clipping and sweeps](#glows-clipping-and-sweeps)
  - [Shapes NoireUI does not ship](#shapes-noireui-does-not-ship)
- [Ribbon backdrop (NoireRibbonField)](#ribbon-backdrop-noireribbonfield)

---

## Overview

`NoireLib.UI` is a set of ImGui UI helpers, built on a small shared foundation.

**Foundations**

- **`NoireUI`** - The hub. Owns the automatic-drawing policy, the element registry, the draw-thread queue (`RunOnDraw`), the frame clock, the UI scale, the reduced-motion switch and diagnostics.
- **`NoireLayout` / `NoireStyle`** - Containers and style scopes that take their body. No `using`, `Dispose` or `End()`. An unbalanced raw-ImGui push inside a body is unwound at the boundary and logged once.
- **`NoireAnim`** - Time-based animation keyed by id: easing over twenty-one curves (or one of your own), springs, presence, pulses, sweeps and one-shots. Nothing to register or dispose.
- **`UiFrameState`** - Id-keyed transient state for immediate-mode helpers, typed per value and pruned automatically.
- **`NoireUiSession`** - The same widget memory for the session only. No file: any type may be stored and a generated widget id is safe to key on. Widgets choose between the two with `UiMemoryScope`.
- **`NoireUiState`** - The small amount of widget memory that survives a reload (a dragged position, a collapsed section). One JSON file. Every `Persist` switch defaults off.
- **`UiDiagnostics`** - Live counts, recent faults, the fault ladder, and the stack-leak net. Answers "why did nothing draw".
- **`NoireTheme`** - One palette the whole library follows, plus the type scale. An unset token falls through to the ImGui style, except the button fill and the semantic colors. Set an accent and every widget re-tints.
- **`NoireText`** - Text at any size without ImGui's resampled-atlas blur: a real font built at the size asked for, behind a four-step type scale the theme owns. Also draws text with matched characters picked out (`Highlighted`), for filter results.
- **`NoireFont` / `NoireFontFamily`** - A typeface of your own from TTF bytes (an embedded resource, a file), drawn and measured at CSS em sizes with letter-spacing and ellipsis, built asynchronously.
- **`NoireShapes`** - The shapes a draw list does not have: gradients at any angle over any shape, notched and rounded plates, beveled edges, glows, hairline frames with corner ticks, arcs and wedges, and two pattern fills. Every one is drawn by three public calls over a public path.

**Widgets and elements**

- **`NoireButtons`** - The buttons ImGui does not ship: hold-to-confirm, an asynchronous button that disables and reports, a split button, an animated toggle, a segmented control and a spinner. Each takes a full `*Style` object and a custom-draw hook.
- **`NoireLayout`** (structures) - A draggable splitter, a collapsible section that can remember whether it was open, and a row that wraps.
- **`NoireToast` / `NoireToastArea`** - Real notifications: anchored, stacked, animated, actionable, with live progress, an undo pattern and a countdown that pauses on hover.
- **`NoireModal`** - Dialogs you `await`. Confirm, prompt and choice tiers, hold-to-confirm for destructive answers, and an optional "don't ask again".
- **`NoireOverlayButton`** - A standalone button overlayed on the game screen, drawn independently from any window. Anchorable anywhere (nine anchors, absolute pixels or screen ratio), with click/scroll callbacks, a hover mouse cursor, tooltips, a visibility condition evaluated on draw, per-state draw conditions (cutscene / gpose / hidden UI / always), drag-to-reposition, optional manual drawing, and full styling. Auto-disposed with NoireLib.
- **`NoireTagInput`** - A chips field for tags, filters and names. Pasted lists split on their separators, backspace takes the last chip back for editing, and every refusal is named.
- **`NoireMultiCombo<T>`** - A dropdown of tick boxes that stays open while picking. Selection is held by value and survives the option list being replaced.
- **`NoireExcelPicker<TRow>`** - A searchable dropdown with icons over any sheet of game data, in one line. A `NoireComboBox` underneath.
- **`NoireComboBox<T>`** - A combo box with an optional filter input, arrow-key cycling, and an optional "hold a binding + mouse wheel" shortcut to cycle the closed combo. The shortcut can be driven from the Hotkey Manager.
- **`NoireTooltip`** - Tooltips independent from `ImGui.SetTooltip()`, with adjustable background opacity and mixed inline content built from `NoireContent`.
- **`NoireIcons`** - Textured icons drawn like glyphs: built-in brand marks (`NoireIcon.Discord`, `NoireIcon.Kofi`) and artwork a plugin registers by name. `ButtonStyle` takes either.
- **`NoireRibbonField`** - An animated ribbon backdrop (gradient, translucent ribbons, vignette) laid out once per frame and painted into any number of rounded views as one continuous field.
- **`NoireContent`** - A reusable block of rich inline content (text, dynamic text, FontAwesome icons, images, keycaps, any widget). Rendered by `NoireTooltip`, and by anything of your own through `Draw()`.

---

## Two ways to reach every surface

Every surface above is also reachable under `NoireUI`. Type `NoireUI.`, pick a surface, and its members follow:

```csharp
NoireText.Draw("Ready", TextSize.Heading);          // the surface's own name
NoireUI.Text.Draw("Ready", TextSize.Heading);       // the same call, reached from the root

NoireShapes.Glow(min, max, colour);
NoireUI.Shapes.Glow(min, max, colour);
```

Both names are the same feature. The top-level names are fully supported and not deprecated.

The grouped name is the surface's own without the `Noire` prefix. Two surfaces carry an explicit name instead:

| Surface | Reached as |
|---|---|
| `NoireText`, `NoireShapes`, `NoireLayout`, `NoirePanel`, `NoireStyle`, `NoireAnim`, `NoireButtons`, `NoireInputs`, `NoireSliders`, `NoireGauges`, `NoireBadge`, `NoireAttention`, `NoireFocus`, `NoireTooltip`, `NoireModal`, `NoireToast`, `NoireWindowChrome`, `NoireWindowMenu` | `NoireUI.Text`, `NoireUI.Shapes`, ... |
| `NoireUiState` | `NoireUI.State` |
| `NoireUiSession` | `NoireUI.Session` |

The grouped call takes the same parameters, defaults and documentation as the direct one.

### Widgets you construct

Widgets you build and drive are reached from the same root:

```csharp
var table = NoireUI.Table<Player>("roster", players);   // creation method
var table = new NoireTable<Player>("roster", players);  // direct construction, unchanged
```

`Table`, `TabBar`, `ComboBox`, `MultiCombo`, `ExcelPicker`, `TagInput`, `ReorderableList`, `Content`, `OverlayButton`, `WorldLabel` and `AddonAttach` all have one. Constructing them directly stays supported.

The modal host is reached through `NoireUI.Modal.Host`, toast areas through `NoireUI.Toast`, and the profiler window through `NoireUI.Profiler`.

---

## The hub (NoireUI)

`NoireUI` is static and needs no setup.

### Automatic drawing

Screen-anchored elements (everything deriving `NoireDrawable`, `NoireOverlayButton` today) can draw themselves:

```
effective = component.AutoDraw ?? NoireUI.AutoDraw
```

```csharp
NoireUI.AutoDraw = true;     // master DEFAULT: everything draws itself, unless it says otherwise
button.AutoDraw = false;     // ...except this one
button.AutoDraw = true;      // opt a single element in without flipping the master
button.AutoDraw = null;      // follow the master again
```

- **`NoireUI.AutoDraw`** is a `bool`, default `false`. It is the default every element inherits.
- **`component.AutoDraw`** is a `bool?`, default `null`: follow the master. An explicit value always wins.
- `NoireOverlayButton` ships with an explicit `true`. Set it to `null` to follow the master.
- **`Draw()` always works.** The hub skips anything already drawn manually on the same frame.

```csharp
foreach (var element in NoireUI.GetDrawables())
    Log(element.Kind, element.Id, element.EffectiveAutoDraw);

NoireUI.RemoveAllDrawables();   // or RemoveAllOverlayButtons() for that one kind
```

### Running work on the draw thread

```csharp
NoireUI.RunOnDraw(() => combo.Select(index));   // safe from any thread
```

Use it for anything touching ImGui from a timer, a socket, a hotkey callback or a background task. The queue holds `NoireUI.RunOnDrawCapacity` actions (default 512) and drops the oldest when full. A frame drains only what was queued when it began. Before NoireLib is initialized the action runs inline.

```csharp
NoireUI.PendingDrawActions;   // waiting for the next frame
NoireUI.DroppedDrawActions;   // dropped because the queue was full, since startup
```

### Reduced motion and strings

```csharp
NoireUI.ReducedMotion;                          // what is in effect right now
NoireUI.HostReducedMotion;                      // what Dalamud says the user asked for
NoireUI.HasReducedMotionOverride;               // whether a plugin has taken it over

NoireUI.ReducedMotion = config.ReducedMotion;   // take it over: eased values snap, decorative motion stops
NoireUI.ClearReducedMotion();                   // hand it back to the host

NoireUI.StringProvider = key => myLocalizer.GetOrNull(key);   // null falls back to the shipped English
```

**`ReducedMotion` follows Dalamud's own setting until a plugin assigns it.**

Assigning takes it over for good, including `false`. Call `ClearReducedMotion()` to hand it back. Persist the override only when one exists (`TryGet`).

NoireLib depends on no localization system and ships no locale files.

---

## Scopes that take their body

There is no `using`, `Dispose` or `End()` in this API. A container takes its body.

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

- **An exception inside a body** unwinds the scope and keeps travelling.
- **`WrapText` takes a width.** The container converts it to ImGui's window-local wrap position.
- **`Indent` means pixels.** `NoireLayout.Indent(0f, ...)` indents by nothing, unlike ImGui's `Indent`. Use `NoireLayout.DefaultIndent` for the standard amount.
- **Raw ImGui stays available** inside a body. A push left unpopped is unwound at the container boundary and logged once. Turn it off with `NoireUI.Diagnostics.RepairStackLeaks = false`.

`UiStyle` has named properties for common values (`TextColor`, `ButtonColor`, `FrameRounding`, `FramePadding`, ...) over three maps that reach everything ImGui has:

```csharp
var style = new UiStyle { TextColor = theme.Danger, FrameRounding = 0f };
style.Colors[ImGuiCol.PlotHistogram] = theme.Danger;   // anything without a named property
style.With(ImGuiStyleVar.ScrollbarSize, 14f);
var softer = style.Clone();
```

### Performance

Everything here runs on the draw thread every frame. Every widget in this namespace follows these rules:

- **Nothing allocates per frame.** Ids are built once by `UiIds`. No `ToString()` on an unchanged value, no `ToArray()`/`ToList()` in a draw path, no closure where a state overload exists.
- **Working sets are borrowed.** A buffer with a known maximum is `stackalloc`. One sized by the data is a `PooledBuffer<T>` or a list kept between frames.
- **A shorthand overload costs no more than the long one.** It writes into a reused options instance. See `NoireInputs.Number`, `NoireLayout.Splitter` and `NoireButtons.Segmented`.
- **Hold style and options objects.** `new SplitterOptions { ... }` inside a draw call is 112 bytes every frame. The same goes for every `*Style` and `*Options` type. Keep them in a `static readonly` field and write the values that move into it before the call.
- **Style is pushed through `UiPush`.** Every `ImRaii` push wrapper is a class and costs 24 bytes per call, even when it pushes nothing.
- **A font push allocates.** `IFontHandle.Push()` allocates inside Dalamud on every call. Text at the host's own size pushes nothing. The headless tests cannot see this cost.
- **Nothing unchanged is recomputed.** Text measurement and layout arithmetic are cached.
- **Nothing off screen is drawn.** Collections of unknown length virtualize past a threshold, with a `Virtualize` override.
- **Tessellation follows the radius.**
- **Nothing loop-invariant sits inside a loop.** Expensive ornament is tessellated once and resubmitted.
- **Literals handed to ImGui are UTF-8** (`"Save"u8`).

Measure with the profiler below and read the **self** column. Allocated bytes are the more reliable figure.

Every drawing surface has a test that runs it inside a real ImGui frame and asserts zero allocated bytes.

### Allocation

A body lambda allocates one delegate per call per frame. Every container has a state overload that keeps the body `static`:

```csharp
NoireLayout.Section("Filters", this, static (self) => self.DrawFilters());
```

`UiPush` is a `ref struct` over the raw `ImGui.PushStyleColor` family. Push one thing with `UiPush.Color(slot, colour)`, or accumulate several into one scope. It covers colours, style variables, fonts, disabled scopes and text wrap positions.

---

## Motion (NoireAnim)

Time-based and keyed by id. Nothing to register or dispose. State lives in [`UiFrameState`](#transient-widget-state-uiframestate) and is pruned when a widget stops calling.

```csharp
// Eased: a new target continues from the current value.
var hover = NoireAnim.Ease("save-button", "hover", ImGui.IsItemHovered() ? 1f : 0f);

// Spring: carries momentum toward a moving target.
var offset = NoireAnim.Spring("panel", "slide", expanded ? 220f : 0f);

// Presence: draw while it is above zero.
var presence = NoireAnim.Presence("panel", "shown", isOpen);
if (presence > 0.001f)
    NoireStyle.WithAlpha(presence, DrawPanel);

// Stateless periodic reads of the shared clock.
var glow = NoireAnim.Pulse(period: 1.5f, min: 0.3f, max: 1f);
var shimmer = NoireAnim.Sweep(period: 2f);
var turn = NoireAnim.Spin(secondsPerTurn: 240f);        // turns, for anything that rotates

// One-shots: start once, run themselves out.
if (saved)
    NoireAnim.Trigger("save-button", "saved");

var flash = NoireAnim.Flash("save-button", "saved");    // 1 down to 0
var nudge = NoireAnim.Shake("name-field", "rejected");  // pixels, dying out
```

Curves are `UiEasing` (21 of them, `OutCubic` by default). `easing.Apply(t)` is pure. A curve of your own is an overload:

```csharp
var value = NoireAnim.Ease("id", "sub", target, duration: 0.3f, curve: t => t * t);
```

`UiCubicBezier` is a CSS `cubic-bezier(x1, y1, x2, y2)`, with `Ease`, `EaseIn`, `EaseOut` and `EaseInOut` built in. Build one once and pass its cached `Curve`:

```csharp
private static readonly UiCubicBezier Silk = new(0.22f, 1f, 0.36f, 1f);

var left = NoireAnim.Ease("tabs", "pill", target, 0.4f, Silk.Curve);
```

**Pass the id and the property separately.** `$"{id}.hover"` allocates on every property of every widget, every frame.

Under `NoireUI.ReducedMotion`, eased values and springs snap to their target, `Pulse` holds at its high end, `Sweep` returns 1, and `Flash`/`Shake` return 0.

---

## Transient widget state (UiFrameState)

The memory a widget needs between frames: a hold progress, a drag origin, an animation phase. Entries are keyed by a caller id plus a sub key, typed per value, and pruned after `PruneAfterFrames` untouched frames.

```csharp
var held = UiFrameState.Get<float>("delete-button", "hold");
UiFrameState.Set("delete-button", "hold", held + ImGui.GetIO().DeltaTime);

var origin = UiFrameState.GetOrAdd("splitter", "origin", () => ImGui.GetMousePos());

UiFrameState.Update<DragState>("row", "drag", (ref DragState s) => s.Offset += delta);
```

Nothing is persisted. **Draw thread only.**

No member returns a `ref` into the store. A dictionary insert invalidates it. Use `Update` to copy out, mutate and write back.

---

## Persisted widget memory (NoireUiState)

The widget state that survives a session: an overlay's position, collapsed sections, the last sorted column. One JSON file beside your configuration, written on a debounce and on shutdown.

```csharp
NoireUiState.Set("myplugin.rows.sort", "name");
var sort = NoireUiState.Get("myplugin.rows.sort", "date");

NoireUiState.RemoveAll("myplugin.rows.");   // forget one widget
NoireUiState.Save();                        // write now instead of waiting out SaveDelay
```

**This is not your configuration.** Nothing here is versioned, migrated, validated or backed up. Anything a user would be upset to lose belongs in the configuration system.

A stored value of the wrong shape reads as absent.

### Widgets that persist

Every `Persist` switch on a widget defaults to **off**.

```csharp
var button = new NoireOverlayButton("my-toggle")   // a stable id
{
    Draggable = true,
    PersistPosition = true,     // remembers where the user dragged it
};
```

**Persisting needs a stable id.** A widget without one gets a new GUID every session. NoireUI refuses to persist against a generated id and logs once.

---

## Session-only memory (NoireUiSession)

`NoireUiState` without the file. Everything is gone on reload.

```csharp
NoireUiSession.Set("myplugin.roster.search", search);
var search = NoireUiSession.Get("myplugin.roster.search", string.Empty);
```

For state worth keeping while someone works: a search a window was narrowed to, the open tab, a scroll position.

- **Any type may be stored**, including ones that do not serialize. A reference type comes back as the same instance.
- **A generated widget id is safe to key on.** The key and the value expire with the session.

`Get` / `TryGet` / `Set` / `Remove` / `RemoveAll(prefix)` / `Clear` / `Count` / `GetKeys` mirror `NoireUiState`. A value asked for as another type reads as absent.

Widgets that can remember something take a `UiMemoryScope` (`None`, `Session`, `Persisted`).

## Diagnostics

```csharp
var stats = NoireUI.Diagnostics.Snapshot();
// Frame, Drawables, AutoDrawn, StateEntries, PendingDrawActions,
// DroppedDrawActions, StackRepairs, Faults, DisabledDrawables

NoireUI.Diagnostics.OnFault = fault => myLog.Add($"{fault.Source}: {fault.Message}");
NoireUI.Diagnostics.RecentFaults;   // the last 32, oldest first
```

Faults are logged before they reach `OnFault`. An exception thrown by the handler is swallowed.

**The fault ladder.** An element that throws on `FaultTolerance` consecutive frames (default 10) has its `AutoDraw` switched off, with one error. `Draw()` still works. Set `FaultTolerance = 0` to never switch anything off.

### Profiling

What each part of the interface costs to build per frame, by name. Off by default and free when off.

```csharp
NoireUI.Profiler.Enabled = true;

foreach (var entry in NoireUI.Profiler.Snapshot())   // most expensive first
    log($"{entry.Name}: {entry.AverageMs:0.000} ms over {entry.Calls} call(s), peak {entry.PeakMs:0.000}");

NoireUI.Profile("inventory grid", () => DrawInventoryGrid());   // your own code, same list
```

Every drawing surface the library ships measures itself. An analyzer refuses a surface that takes a draw list without opening a scope. Read the **average**. The **peak** is a high-water mark only `Reset()` clears.

**`Detailed` adds the per-method rows.** By default the profiler measures widgets and surfaces. `NoireUI.Profiler.Detailed = true` (the **Detail** checkbox) also measures every drawing helper, such as `NoireShapes.Glow`. Those rows are most of the measuring cost. While off, a method's time folds into the scope around it.

**Scopes nest.** *Total* includes everything measured inside a scope, *self* does not. `TotalAverageMs` sums self time.

The host's figure for the whole plugin is always larger. It also covers windowing and ImGui work outside any scope.

**Allocated bytes need `TrackAllocations`.** The byte columns and `TotalAverageBytes` read zero until it is on. Reading the allocation counter costs more per scope than timing it. A scope open across the switch reports no bytes.

```csharp
NoireUI.Profiler.TrackAllocations = true;   // fills the byte columns

foreach (var entry in NoireUI.Profiler.Snapshot())
    log($"{entry.Name}: {entry.SelfAverageBytes:N0} bytes of its own per frame");
```

The readout ships as a window:

```csharp
var profiler = new NoireProfilerWindow();
windowSystem.AddWindow(profiler);
profiler.IsOpen = true;
```

A sortable, searchable table of every scope with its calls, last, longest and average, plus **Reset all** and **Copy all** (tab separated). `DrawContents()` is public to put it on a settings page.

**Right-click a row to leave that scope out of the totals.** The row turns red and `TotalAverageMs`, `TotalAverageBytes` and the totals line stop counting it. Right-click again, or use **Include all**, to put it back. The row itself keeps reporting.

A mark excludes one node. Marking a branch means marking its rows. `Reset()` forgets the marks with the measurements. `ClearExclusions()` lifts every mark. In code: `SetExcluded(id, excluded)`, `ToggleExcluded(id)`, `IsExcluded(id)` and `ExcludedCount`.

This measures the draw-thread time spent building the draw data. It is not the GPU cost.

---

## Theming (NoireTheme)

`NoireTheme.Current` is the palette every widget resolves against. A token left `null` falls through to the host's ImGui style. `Control` and the semantic colors (`Success`, `Warning`, `Danger`, `Info`, `Shadow`) use shipped defaults.

```csharp
NoireTheme.Current = NoireTheme.FromAccent("#C8A96A");   // one color in, a whole palette out
NoireTheme.Current.Danger = myRed;                       // override one token, inherit the rest
```

Resolution order: **the widget's own value, then the theme, then the ImGui style.**

**Derived states.** `Hover()` and `Active()` derive from the base color.

`TintSource` decides which way they move:

| `ThemeTintSource` | What decides the direction |
|---|---|
| `Item` (default) | Each color decides for itself: a dark button brightens, a pale accent one darkens. Both visibly respond. |
| `Surface` | The theme decides for everything: a dark theme brightens, a light one darkens. Consistent, but washes out a color already close to that direction. |
| `Lighten` / `Darken` | Always that direction, whatever the color. |

The default is `Item`. A fixed direction washes out a pale accent.

```csharp
var theme = NoireTheme.Current;
theme.Resolve(ThemeColor.Accent);      // never null, whatever is or is not set
theme.Hover(theme.Resolve(ThemeColor.Accent));
theme.On(fill);                        // a text color legible on that fill
theme.Muted(color);                    // faded to MutedAlpha
theme.CustomColors["deco.hairline"] = c;   // tokens the library does not define
```

Shape lives here too (`Rounding`, `SurfaceRounding`, `BorderSize`, `FramePadding`, `ItemSpacing`). `ToStyle()` returns a `UiStyle` that paints raw ImGui with the theme:

```csharp
NoireStyle.With(NoireTheme.Current.ToStyle(), () => DrawMyWindowBody());
```

**Sharing.** A theme travels as a share code tagged with its own kind:

```csharp
var code = NoireTheme.Current.ToShareCode();

var result = NoireTheme.FromShareCode(pasted);
if (result.Success)
    NoireTheme.Current = result.Value!;
else
    ShowError(result.Message);      // never throws on a bad paste
```

Decoding targets an inert `ThemeSnapshot`. An unknown color name is skipped. See the [ShareCodeHelper README](../Helpers/ShareCode/README.md).

---

## Buttons (NoireButtons)

Immediate and themed. Nothing to construct or dispose.

```csharp
if (NoireButtons.Button("Save", ButtonTone.Accent))
    Save();
```

A **tone** is what a button means (`Neutral`, `Accent`, `Success`, `Warning`, `Danger`, `Ghost`) and decides its colors. Passing a tone allocates nothing. Pass a `ButtonStyle` to override individual values. A neutral button fills with the theme's `Control` color.

**Hold to confirm** replaces a confirmation dialog for a destructive action.

```csharp
if (NoireButtons.HoldToConfirm("Hold to delete everything"))
    DeleteEverything();

// The fill's shape is a setting.
new ButtonStyle { Tone = ButtonTone.Danger, HoldFill = HoldFillMode.CenterOut };
```

`HoldFillMode` covers `LeftToRight`, `RightToLeft`, `CenterOut`, `BottomUp` and `Border` (traces the outline clockwise). `HoldFillColor` overrides the fill. It defaults to a brighter form of the button's color.

It fires once per press. The fill runs on wall-clock time. It ignores `ReducedMotion`.

**Async** disables itself and shows a spinner until the task finishes. A failure is reported through `UiDiagnostics`.

```csharp
NoireButtons.Async("Upload", () => UploadAsync(), onCompleted: failure =>
{
    if (failure != null)
        NoireToast.Error($"Upload failed: {failure.Message}");
});
```

**Split**, **Toggle** and **Segmented** complete the set. A split button's menu takes its body:

```csharp
if (NoireButtons.Split("Export", () => { if (ImGui.MenuItem("Export as CSV")) ExportCsv(); }))
    ExportDefault();

NoireButtons.Toggle("Enabled", ref config.Enabled);
NoireButtons.Segmented("quality", ref config.Quality, new[] { "Low", "Medium", "High" });
```

**Drawing it yourself.** Every style carries a custom-draw hook. NoireUI keeps the sizing, hit testing and state. The hook paints:

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

`UiButtonDraw` carries the geometry, the hover and held flags, the resolved colors and the hold progress. `ToggleStyle.CustomDraw` works the same way through `UiToggleDraw`. `ToggleStyle.AnimationCurve` swaps the knob's `OutCubic` for any curve.

**Textured icons.** `ButtonStyle.NoireIcon` (a built-in mark) and `ButtonStyle.IconName` (a name registered with `NoireIcons.Register`) sit beside `Icon`. Setting one clears the other two. `IconSize` sets the square's side, the label's line height by default.

```csharp
new ButtonStyle { Color = kofiRed, TextColor = Vector4.One, NoireIcon = NoireIcon.Kofi, IconSize = 19f };
```

---

## Layout structures

`NoireLayout` also ships three structures ImGui lacks.

**Splitter.**

```csharp
NoireLayout.Child("left", new Vector2(paneWidth, paneHeight), DrawLeft, border: true);
ImGui.SameLine(0f, 0f);
NoireLayout.Splitter("split", ref paneWidth, minSize: 120f, maxSize: 420f, length: paneHeight);
ImGui.SameLine(0f, 0f);
NoireLayout.Child("right", new Vector2(0f, paneHeight), DrawRight, border: true);
```

Give it a `length` whenever the panes are a fixed size. At zero it fills the rest of the region.

The value is clamped every frame. A width restored from a wider screen is corrected on the first frame.

**The divider follows the pointer's position.** Overshooting the bounds is free and the divider stays under the cursor. Grabbing it off-centre does not snap it.

**A `SplitterOptions` overload** opens the look up, including a `CustomDraw` hook. `Thickness` is the grab area, `LineWidth` the drawn hairline.

```csharp
NoireLayout.Splitter("split", ref paneWidth, new SplitterOptions
{
    MinSize = 120f, MaxSize = 420f, Length = paneHeight,
    Thickness = 9f,                        // comfortable to grab
    CustomDraw = static _ => { },          // and invisible: this design draws its own divider
});
```

An empty hook makes an existing divider draggable. `UiSplitterDraw.DrawLine()` draws the shipped line.

**Put the handle inside one of the two regions, along its edge.** ImGui hit tests windows before items, and a child region is a window.

**Collapsible sections** fold their body away and can remember whether they were open.

```csharp
NoireLayout.Collapsible("filters", "Filters", DrawFilters, new CollapsibleOptions
{
    Persist = true,                       // survives a reload, off by default
    HeaderExtras = () => DrawActiveFilterCount(),
    HeaderExtrasWidth = 120f,
    Danger = false,
});
```

Header extras are drawn open or closed. Persistence is keyed on the section id: **the id must be stable across sessions**. A blank id is refused with one log line.

The header sits on a plate of the theme's `Control` color at 30% opacity, lighter while hovered. Every part of the plate is an option:

```csharp
NoireLayout.Collapsible("advanced", "Advanced", DrawAdvanced, new CollapsibleOptions
{
    HeaderBackground = accent with { W = 0.30f },         // null uses ThemeColor.Control
    HeaderHoveredBackground = accent with { W = 0.42f },  // null lightens HeaderBackground
    HeaderRounding = 0f,                                   // null uses the theme rounding
    HeaderPadding = new Vector2(10f, 6f),                  // null uses the theme frame padding
});
```

A `HeaderBackground` with zero alpha draws no plate. The hover then lightens the label.

**Wrapping rows.**

```csharp
NoireLayout.Flow(tags, tag => ImGui.CalcTextSize(tag) + padding * 2f, DrawChip);
```

`NoireLayout.FlowItem(width, first)` is the primitive underneath.

**Where a row wraps.** ImGui has no right margin. The content region reports the window's right edge however nested the drawing is. Both calls take an optional `width`:

```csharp
NoireLayout.Flow(tags, Measure, DrawChip, width: myPanelWidth);
```

At zero they use the text wrap position if one is set (`NoireLayout.WrapText`), and the window's content edge otherwise.

`NoireLayout.ContentWidth()` returns that width. Use it in place of `GetContentRegionAvail()` for anything defaulting to "the space available".

---

## Framed containers (NoirePanel)

`NoirePanel` runs the body, measures it and paints the chrome behind it.

```csharp
NoirePanel.Frame(() =>
{
    NoireText.Draw("Selected index");
    NoireText.Draw("14");
},
new FrameStyle { TickLength = 11f, TickColor = gold });
```

`Frame` draws a `FrameStyle` border. `Plate` draws a `PlateStyle` fill. Both take their body.

**The chrome is drawn after the body and appears behind it.** The panel splits the window's draw list into a chrome channel and a content channel.

**Nested panels share the split.** An inner panel's chrome lands on its parent's chrome and behind all content. The split is tracked per draw list.

**A panel is a fixed-width box**, filling the available width unless `PanelOptions.Width` says otherwise. The body is told the width it has.

`PanelOptions.Header` puts a tracked label and a hairline across the top. Its height is rounded up to a whole pixel.

---

## Windows you draw yourself (NoireWindowChrome)

ImGui's window decoration comes from its style and cannot be replaced. Removing it also removes everything Dalamud draws on it.

```csharp
// On the window:
Flags = NoireWindowChrome.Flags;
public override void PreDraw()  => pushed = NoireWindowChrome.PushWindowStyle();
public override void PostDraw() => NoireWindowChrome.PopWindowStyle(pushed);

// In Draw():
NoireWindowChrome.Draw(() =>
{
    var handle = DrawMasthead();
    NoireWindowChrome.DragFrom(handle.Min, handle.Max);

    if (NoireWindowChrome.CloseButton(closeAt, 16f))
        IsOpen = false;
},
new WindowChromeStyle { Plate = mySurface, Frame = myBorder });
```

**Always on top.** Call `KeepInFront()` from inside the window, once per frame.

```csharp
public override void Draw()
{
    if (alwaysOnTop)
        NoireWindowChrome.KeepInFront();

    // ... the window
}
```

It moves the window to the front of the display list for clicks, and lifts it into the top draw layer for drawing. ImGui reorders the display list after every plugin has drawn.

The layer flag is set after `Begin`. The window keeps its position, background, border and item width. There is nothing to pass at `PreDraw`.

Every NoireUI popup inherits the layer of the window that opened it. A popup of your own calls `KeepInFront()` from inside itself. Among windows in front, the last caller each frame wins.

**Four flag sets.** `Flags`: no title bar, background, border or scrollbar, dragged from anywhere free. `FixedFlags` adds no-resize. `HandleOnlyFlags` adds `NoMove`, with `DragFrom` naming the handle. `FixedBodyFlags` stops the window scrolling as a whole.

`DragFrom` holds the drag by window id. `ChromeButton` draws the window's buttons (`ChromeGlyph.Close`, `Minimize`, `Restore`, `Menu`) from strokes, independent of FontAwesome. One `ChromeButtonStyle` covers every glyph, each on a plate that lights on hover.

**What a custom window gives up:** Dalamud's pin, clickthrough and background-blur controls live on its title bar. A window with `NoTitleBar` has none.

**`WindowChromeStyle.Opacity` fades the surface only.** The text, border and controls stay at full strength. Scale anything painted behind the content by the same number.

**`DragFromBody()` moves the window from anywhere no item claimed.** Call it once per frame, inside the window and after its contents:

```csharp
public override void Draw()
{
    var handle = DrawMasthead();
    DrawBody();

    NoireWindowChrome.DragFrom(handle.Min, handle.Max);
    NoireWindowChrome.DragFromBody();
}
```

A press starts a drag only while the window is hovered and no item is hovered or active. Anything hit-tested by hand from raw mouse coordinates is not protected. Give it an `InvisibleButton`. The two calls share one drag. Both take an optional `ImGuiMouseCursor`.

**`ContinueDrag()` moves a window mid-drag before its contents are drawn.** Call it first thing inside the window: a drag started from `DragFrom`/`DragFromBody` then moves the frame on the frame the pointer moved, the way ImGui's own move does, instead of one frame later.

**`DoubleClickFrom(min, max)` reports a title bar's double click.** A double click on a chrome button belongs to the button. It needs no `NoMove`:

```csharp
if (NoireWindowChrome.DoubleClickFrom(handle.Min, handle.Max))
    SetCollapsed(!collapsed);
```

**`DragFrom` requires `NoMove`.** With ImGui's drag also running, the contents swim behind the frame. Use `HandleOnlyFlags` for a handle.

**Push `ItemSpacing` to zero** for a window with stated margins. ImGui adds `ItemSpacing.Y` between every two items.

**The scrollbar goes, the wheel stays.**

**The chrome is painted at the window rectangle. The body is advanced from the cursor.** An absolute body position pins the contents while the window scrolls.

`PushWindowStyle` zeroes ImGui's own window padding and border.

---

## The window menu (NoireWindowMenu)

A window's options menu: opacity and text size sliders, six behaviour switches (always on top, reduced motion, lock position, click through, lock width, lock height) and four stay-visible switches (gpose, UI hidden, cutscene, auto hide). The menu edits a `WindowMenuSettings` and reports what changed. Applying a change is the window's job.

```csharp
private readonly WindowMenuSettings menu = new();

// In the chrome, where the menu button is drawn:
if (NoireWindowChrome.ChromeButton("menu", at, size, ChromeGlyph.Menu))
    NoireWindowMenu.Toggle("options");

var result = NoireWindowMenu.Draw("options", buttonMin, buttonMax, menu);

if ((result.Changes & WindowMenuChange.Visibility) != 0)
    Visibility = menu.Visibility;
```

`Toggle` opens or closes it. `Draw` is called every frame in the same window and lines the menu's right edge up with the button. Share one `WindowMenuSettings` to share settings between windows. `WindowMenuResult` also carries the hovered switch and the one right clicked this frame. `StayAutoHide` maps to `UiBuilder.DisableAutomaticUiHide`.

Every length, colour, gap, label and hint is a property of `WindowMenuStyle`. It defaults to a dark 300 px menu with dot switches. Three levels of restyling:

- **Values**: sizes, colours, headings, `SetLabel`/`SetHint` per switch, `Note` and `ClickThroughNote`, `TextSteps` or `TextStepNames`.
- **Delegated widgets**: `OpacitySlider`/`TextStepSlider` draw the rows through `NoireSliders`, `ToggleOn`/`ToggleOff` draw the switches through `NoireButtons`.
- **Hooks**: `CustomDrawSlider` (`UiWindowMenuSliderDraw`), `CustomDrawToggle` (`UiWindowMenuToggleDraw`), `CustomShowHint` (`UiWindowMenuHint`), and `CustomDrawText`/`MeasureText` (`UiWindowMenuText`) for a font of your own. `PaintSlider` and `PaintToggle` are the built-in painters.

A menu opened from a window in the top layer joins it. It closes on Escape.

---

## Sliders (NoireSliders)

ImGui's slider is drawn from four style entries and cannot be replaced. `NoireSliders` draws its own, with the same style-plus-hook shape as buttons.

```csharp
NoireSliders.Int("Visible options", ref config.VisibleOptions, 1, 20);

NoireSliders.Float("Opacity", ref opacity, 0f, 1f, new SliderStyle
{
    Grab = SliderGrab.Diamond,
    FillColor = goldDeep,
    FillTo = goldHi,
    GlowColor = goldHi,
});
```

`SliderGrab` ships Square, Rounded, Circle and Diamond. `SliderStyle.CustomDraw` replaces the painting. The widget keeps the sizing, hit testing, dragging and value.

**The value follows the pointer's position.** A click and a drag are the same operation. `ResolveValue` and `ResolveFraction` are the two halves.

The label column matches [`NoireInputs`](#settings-fields-noireinputs).

---

## Toasts (NoireToast)

Anchored, stacked, animated and actionable notifications.

```csharp
NoireToast.Success("Preset saved");
NoireToast.Error("Could not reach the server")
    .WithTitle("Sync failed")
    .WithAction("Retry", _ => Retry(), ButtonTone.Accent);
```

Raising a toast is safe from any thread and needs no wiring. `NoireToastArea.Default` draws itself. A toast's clock starts on the frame the area picks it up.

**The undo pattern** replaces a confirmation dialog for anything reversible:

```csharp
DeletePresets(selected);
NoireToast.Undo($"{selected.Count} presets deleted", () => RestorePresets(selected));
```

**The countdown.** `ToastStyle.Timer` picks how a toast shows its remaining time: `BottomBar` (the default), `TopBar`, `Stripe`, `Border` (traced clockwise from the top left), `TintLeftToRight`, `TintRightToLeft`, or `None`. `TimerThickness`, `TimerColor`, `TimerTintAlpha` and `TimerDrains` tune it. A toast with no duration has no countdown.

**Live progress** for work in flight, styled by `ProgressColor`, `ProgressTrackColor` and `ProgressHeight`. The fill defaults to a darker form of the severity colour (`ProgressDarken`). A toast with a progress reading stays until you dismiss it:

```csharp
var toast = new NoireToast("Importing presets").WithProgress(() => importProgress).Show();
// ...when the work finishes:
NoireUI.RunOnDraw(() => toast.Dismiss());
```

The countdown **pauses while a toast is hovered**. Errors stay longer by default.

**`ToastStyle.CustomDraw` replaces the chrome**: the background, the severity stripe, the border and the countdown. The body (icon, title, message, progress, actions, close button) is still drawn. `UiToastDraw` carries the toast, the full-height plate (`Min`/`Max`) and the clipped slot (`SlotMin`/`SlotMax`), every colour, and the shipped parts `DrawBackground()`, `DrawStripe()`, `DrawBorder()` and `DrawTimer()`.

`CloseButtonStyle` restyles the dismiss cross and `ActionButtonStyle` every action button. Both are `ButtonStyle`s.

**Placing the stack yourself.** Construct an area for a second stack, or to draw one inside a window of your own. It follows the `NoireUI.AutoDraw` master default:

```csharp
var area = new NoireToastArea("Sidebar")
{
    Position = UiPosition.AtAnchor(UiAnchor.TopRight, new Vector2(-20f, 20f)),
    Width = 300f,
    MaxVisible = 3,
};
new NoireToast("Only in this corner").Show(area);
```

`AlwaysOnTop` is on by default.

**`AlwaysOnTop` moves both of ImGui's orders.** Drawing follows the draw layer, then the display list. Input follows the display list alone. Promoting only the layer paints the element over everything while clicks go through it.

It does not take keyboard focus. Within the top layer the last window to ask each frame wins.

**A leaving toast does not move the others.** The stack is laid out from the edge pinned to the screen.

**A leaving toast holds still.** It is cropped to its closing slot from the edge the stack hangs from.

**The stack's height is rounded up to a whole pixel.** A bottom-anchored window is placed at *(fixed edge - height)* and snapped to the pixel grid. A fractional height leaves its fraction behind:

```
bottom = snap(C - total) + total  =  C - frac(C - total)
```

While a toast animates, the anchored edge would sawtooth across a pixel and the whole stack wander. `NoireToastAreaTests` pins it.

**Every slot and gap is on the pixel grid too.** ImGui floors the cursor after each item. A block's measured height depends on the fraction it started at, and the error grows away from the anchor.

A leaving toast's measured height is frozen.

The queue is bounded (`Capacity`, drop-oldest, counted in `DroppedCount`).

---

## Dialogs you await (NoireModal)

A dialog is awaited. No popup-open boolean, no pending-action field, no callback.

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

Dismissed with Escape or by clicking away, `ConfirmAsync` returns `false`, `PromptAsync` returns `null` and `ChoiceAsync` returns `-1`. Dialogs queue. Raising one is thread safe.

- **Never block on one from the draw or framework thread.** The task completes on the draw thread. `await` it.
- **Every pending dialog completes as cancelled when NoireLib is disposed.**

**"Don't ask again"** goes through `RememberKey`, stored in `NoireUiState` and cleared with `NoireModal.Forget(key)`:

```csharp
await NoireModal.ConfirmAsync("Close to tray", "Keep running in the background?",
    new ModalOptions { RememberKey = "close-to-tray" });
```

**Never offer it for a destructive action or for applying content from outside the plugin.** An answer is only remembered when the box is ticked. A cancelled dialog remembers nothing.

**Drawing a dialog yourself.** With `CustomDraw = true`, read `NoireModal.Active`, call `MarkPresented()` every frame you draw it, and answer with `Confirm()`, `Cancel()` or `Choose(index)`. `ConfirmLabel`, `CanConfirm` and `SecondsUntilEnabled` carry the countdown. A frame without `MarkPresented()` hands the dialog to the built-in popup.

```csharp
if (NoireModal.Active is { Options.CustomDraw: true } view)
{
    view.MarkPresented();
    // draw view.Title, view.Message and two buttons
}
```

`NoireModal.Host` draws itself. Set its `AutoDraw` to `false` and call `NoireModal.Draw()` to place it in your own draw order.

---

## Guided tours (NoireTour)

A tour dims the screen, leaves one widget lit and explains it on a card. Widgets are named by the key they are marked with as they draw.

```csharp
// While the window draws
ImGui.InputText("##name", ref name, 64);
NoireTour.Mark("scenario.name");

// From a "How this works" button
NoireTour.Create("scenario.intro")
    .Step("scenario.name", "Name the run", "Type a name, then press Add.")
    .Step("scenario.add", "Add it", "The run appears in the list on the left.").AdvanceOnClick()
    .Say("That is all", "Everything else is optional.")
    .WhenCompleted(() => Configuration.TourSeen = true)
    .Start();
```

`Mark` takes the rectangle of the widget drawn last. `Mark(key, min, max)` takes any rectangle. A step whose widget is not on screen keeps its card centred.

A step moves on when the user presses the button, when the widget is clicked (`AdvanceOnClick`), or when a condition holds (`AdvanceWhen`). A step that waits shows no button, only its `Hint` line and `Skip`. `SkipWhen` passes over a step, in both directions. `Place` and `Shape` set the card's side and the outline around the widget.

`AdvanceWhenSettled(isReady, seconds, changes)` waits for the condition to hold still. A change in the value `changes` reads restarts the countdown. Built for a field the user is still typing in.

While a tour runs, `NoireTour.IsInteractive(key)` is false for every widget except the step's target and those `AlsoInteractive` names:

```csharp
NoireLayout.Disabled(!NoireTour.IsInteractive("scenario.add"), () =>
{
    if (NoireButtons.Button("Add", ButtonTone.Accent))
        Add();
});
```

A widget scrolled out of its panel is not spotlighted. The card says so and arrows point at the edge to scroll towards. `Mark` records the window's clipping rectangle. `Mark(key, min, max, clip)` takes one of its own.

`NoireTour.Start(id, steps, options)` takes the steps as a list. The options live on `TourOptions`: `DimAlpha`, `DimColor`, `SpotlightColor`, `SpotlightThickness`, `SpotlightRounding`, `Pulse`, `CardWidth`, `CardGap`, `ShowCounter`, `ShowBack`, `ShowSkip`, `ShowStop`, `BlockOtherWidgets`, the button labels, `ScrollHint`, `OnCompleted`, `OnStopped` and `OnStepChanged`.

The dim is an input-less window in the top layer under the card. A window showing a tooltip over the dim calls `NoireTour.Tooltip(text)`. With no tour running it falls back to `ImGui.SetTooltip`. The step's window is held in front of the others while the step is up.

`NoireTour.Host` draws itself. Set its `AutoDraw` to `false` and call `NoireTour.Draw()` to place it in your own draw order. Marking and driving a tour are draw-thread work.

---

## Overlay Buttons

A standalone button on top of the game. `AlwaysOnTop` keeps it in front for clicks and drawing. Create it once and keep the instance. It is disposed with NoireLib (see [Lifetime & disposal](#lifetime--disposal)).

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

The content can be text, a FontAwesome icon, an image, or any combination (icon, then image, then text):

```csharp
var button = new NoireOverlayButton
{
    Icon = FontAwesomeIcon.Cog,
    Text = "Settings",
    Image = UiImageSource.FromGameIcon(66413),
    ImageSize = new Vector2(20f, 20f),
};
```

Or fully custom content (set an explicit `Size`):

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

Both tooltip kinds can show **at the same time**:

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

The mouse cursor while hovered:

```csharp
using Dalamud.Bindings.ImGui;

button.HoverCursor = ImGuiMouseCursor.Hand;   // null (default) leaves the cursor unchanged
```

The cursor shows over the game while `UiBuilder.OverrideGameCursor` is enabled (the default).

### Draw conditions (cutscene / gpose / hidden UI)

By default an overlay button hides like any plugin UI: during cutscenes, in group pose, and with the game UI hidden. `DrawConditions` keeps it visible in some or all of those states:

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

**The flags apply to that button only.**

Overlays are drawn from Dalamud's frame, outside your plugin's draw callback. Dalamud's per-plugin hiding does not reach them.

If NoireLib cannot install its draw hook, overlays fall back to your plugin's draw callback and Dalamud's per-plugin hiding applies to them all. NoireLib logs a warning. Check for it:

```csharp
if (!NoireUI.OverlaysDrawIndependently)
{
    // Overlays share your plugin's draw callback. Their draw conditions are plugin-wide.
}
```

### Manual drawing

Set `AutoDraw = false` and call `Draw()` yourself to control layering. The button stays registered and auto-disposed:

```csharp
button.AutoDraw = false;

// In your own UiBuilder.Draw handler / window:
button.Draw();
```

`AutoDraw` is a `bool?`. `null` follows the `NoireUI.AutoDraw` master. An overlay button starts at `true`. See [Automatic drawing](#automatic-drawing).

`Draw()` always works. The hub skips anything already drawn manually on the same frame.

### Lifetime & disposal

Every overlay button is disposed with NoireLib. `Dispose()` removes it earlier and is safe to call twice. To drop every button at once:

```csharp
button.Dispose();              // Remove a single button now
NoireUI.RemoveAllOverlayButtons(); // Dispose every registered overlay button
```

### Styling

A `null` style value falls back to the current ImGui style:

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

**`OverlayButtonStyle.CustomDraw` replaces the painting** (background, border and content). The hitbox, dragging, clicks, scrolling and tooltips stay NoireUI's. `UiOverlayButtonDraw` carries the rect, the state, the resolved colours, and the parts `DrawBackground()`, `DrawBorder()` and `DrawContent()`. `CustomContent` is not called while the hook is set.

```csharp
button.Style.CustomDraw = static args =>
{
    args.DrawBackground();
    NoireShapes.On(args.DrawList, args, static b =>
        NoireShapes.RectOutline(b.Min, b.Max, b.Hovered ? b.Background * 1.4f : b.BorderColor, 1f));
    args.DrawContent();
};
```

---

## Positioning (UiPosition)

A screen position in one of four modes, used by the overlay button:

```csharp
// 1. One of the nine screen anchors, plus an optional pixel offset:
UiPosition.AtAnchor(UiAnchor.BottomCenter, new Vector2(0f, -40f));

// 2. Absolute pixels, relative to the top left corner of the game window:
UiPosition.AtAbsolute(100f, 250f);

// 3. Screen ratio: 10% from the left, 10% from the top:
UiPosition.AtRatio(0.1f, 0.1f);

// 4. A corner of a native game window, followed as the player moves or rescales it:
UiPosition.AtAddon("_PartyList", UiAnchor.TopRight);

// ...or alongside one:
UiPosition.NextToAddon("_PartyList", UiSide.Right, UiAlign.Start);
```

Options:

```csharp
UiPosition.AtRatio(0.5f, 0.5f)
    .WithPivot(new Vector2(0.5f, 0.5f)) // Which point of the element is pinned (default: the anchor point in Anchor mode, the top left corner otherwise)
    .WithOffset(new Vector2(0f, 10f))
    .WithClampToViewport(false);        // Clamping is enabled by default: the element always stays fully on screen
```

In `Anchor` mode the pivot follows the anchor: `BottomRight` pins the element's bottom right corner to the screen's, `MiddleCenter` centers it. `Addon` mode does the same against the game window's rectangle.

### Targets that may not be there

Only `Addon` mode can fail to resolve. `TryResolve` returns `false` when the game window is not on screen:

```csharp
if (position.TryResolve(size, out var topLeft))
    ImGui.SetNextWindowPos(topLeft);
```

The overlay button already skips the frame: `button.Position = UiPosition.AtAddon("ContentsFinder")` is enough.

`Resolve` always answers, falling back to the equivalent screen anchor.

Every overload takes an optional source of rectangles:

```csharp
position.TryResolve(size, viewportPos, viewportSize, name => myRects[name], out var topLeft);
```

`UiAddon` is the live source:

```csharp
if (UiAddon.TryGetRect("_PartyList", out var rect))
    // rect.Position is relative to the game window, rect.Size is in real pixels
```

---

## Pinning a window to the game (NoireAddonAttach)

Docks one of your windows to a native game window by writing the window's position.

```csharp
new NoireAddonAttach(myWindow, "_PartyList", UiSide.Right) { Gap = 8f };
```

The attachment registers itself and applies every frame.

| Property | Default | What it does |
|---|---|---|
| `AddonName` | ctor | The game window to follow. |
| `Side` | `Right` | Which side to sit on. `Over` shares its area instead. |
| `Align` | `Start` | How to line up along that side. |
| `Gap` | `0` | Distance from the game window, always measured away from it. |
| `Offset` | zero | An additional nudge, taken verbatim. |
| `MatchWidth` / `MatchHeight` | off | Resize to the game window on that axis. Independent of each other. |
| `FollowVisibility` | on | Close while the game window is not on screen. |
| `RestoreOnReappear` | on | Reopen when it comes back. |
| `Enabled` | on | Turning it off hands the window straight back. |
| `PositionOverride` | none | A `UiPosition` replacing the side/align/gap trio. |
| `IsAttached` | - | Whether the game window was found last frame. |
| `IsAddonVisible` | - | Whether it is on screen right now, asked directly. |
| `OnAttachedChanged` | none | Raised when that changes. |

### Matched axes are independent

| `MatchWidth` | `MatchHeight` | The window |
|---|---|---|
| off | off | resizes freely on both axes |
| on | off | is pinned to the game window's width, and still resizes vertically |
| off | on | is pinned to its height, and still resizes horizontally |
| on | on | does not resize |

Matching is written as `SizeConstraints`. A `Size` would pin the unmatched axis to its last value.

### Taking the window, and giving it back

The attachment holds a window's position and size constraints only while placing it. Turn `Enabled` off, turn both `MatchWidth` and `MatchHeight` off, or let the game window leave the screen, and both are handed back as found.

### Visibility is decided before the frame

`FollowVisibility` runs on the framework tick. A window closed from `PreDraw` would still draw once.

Opening a window whose game window is absent closes it again at once. Ask first:

```csharp
if (attach.FollowVisibility && !attach.IsAddonVisible)
    NoireToast.Error($"{attach.AddonName} is not on screen.");
else
    myWindow.IsOpen = true;
```

### Keeping up with a drag

Apply it from the window's own `PreDraw` to track a game window being dragged:

```csharp
public override void PreDraw() => attach.Apply();
```

Dalamud applies a window's position right after `PreDraw`. The automatic pass can land one frame behind during a drag.

---

## Labels in the world (NoireWorldLabel)

A label pinned to a place in the world, projected every frame.

```csharp
new NoireWorldLabel("target")
{
    Text = "Target",
    WorldOffset = new Vector3(0f, 2.2f, 0f),
    OffScreen = WorldLabelOffScreen.EdgeArrow,
    MaxDistance = 60f,
    FadeDistance = 40f,
}
.Follow(() => NoireService.TargetManager.Target);
```

| Property | Default | What it does |
|---|---|---|
| `WorldPosition` / `At(...)` | zero | A fixed point to pin to. |
| `PositionSource` | none | Where to read the position each tick. |
| `ObjectSource` / `Follow(...)` | none | Which game object to follow. |
| `WorldOffset` | zero | Added in world space. `(0, 2.2, 0)` is about head height. |
| `Text` / `Content` / `Renderer` | - | Plain text, rich content, or a body you draw. |
| `Pivot` | `(0.5, 1)` | Which point of the label sits on the world point. |
| `MaxDistance` | `0` | Where it stops being drawn. Zero means no limit and no fade. |
| `FadeDistance` | `0` | Where it starts fading toward that. |
| `BaseScale` | `1` | A fixed multiplier on the whole label, distance scaling aside. |
| `ScaleWithDistance` | off | Whether distance changes the size at all. |
| `Scaling` | `Perspective` | `Perspective` (reference distance) or `Ramp` (between two distances). |
| `ScaleReferenceDistance` | `20` | `Perspective`: where it is drawn at its authored size. |
| `ShrinkFromDistance` / `ShrinkToDistance` | `10` / `60` | `Ramp`: where shrinking starts and finishes. |
| `MinScale` / `MaxScale` | `0.6` / `1.4` | The bounds, in both modes. |
| `ScaleStep` | `0.25` | The steps the distance scale rounds to. Zero scales smoothly. |
| `Background` / `BackgroundOpacity` | theme / `0.8` | The plate colour, and how opaque it is drawn. |
| `OffScreen` | `Hide` | `Hide`, `Clamp`, or `EdgeArrow`. |
| `EdgeMargin` | `24` | How far a pinned label stays clear of the edges. |
| `ArrowSize` / `ArrowGap` | `14` / `4` | The edge arrow, and how far it stands off the label. |
| `AlwaysOnTop` | off | Keep in front of every other window, for clicks as well as drawing. |
| `OnClick` / `Tooltip` | none | Setting either makes the label take the mouse. |
| `IsInView` / `IsOnScreen` / `Distance` | - | What happened last frame. |

Every setting belongs to the label it is set on.

A world label starts at `AutoDraw = true`. Set `AutoDraw = false` and call `Draw()` yourself, or `null` to follow the `NoireUI.AutoDraw` master. See [Automatic drawing](#automatic-drawing).

- **What it follows is read on the framework thread** and reduced to a position. Reading a game object from the draw thread can access-violate.
- **The label takes no input** until `OnClick` or `Tooltip` is set.

**Off screen, only the direction is read.** The game's `WorldToScreen` divides by the absolute clip-space w: a point behind the camera comes back reflected through the screen centre. The label is cast out from the centre along that direction and pinned to the edge. The arrow follows the same direction.

**Two ways to shrink.** `Perspective`: authored size at `ScaleReferenceDistance`, half at twice that. `Ramp`: shrinks evenly between `ShrinkFromDistance` and `ShrinkToDistance`. Both clamp to `MinScale` and `MaxScale`. `BaseScale` multiplies on top, with `ScaleWithDistance` on or off.

**Scaling is stepped.** Each `NoireText` size is a full glyph atlas. `ScaleStep` rounds the distance part of the scale to a handful of sizes. Zero gives a smooth, stretched ramp. `BaseScale` multiplies after the stepping. A `Renderer` or `Content` body picks the size up too.

**`AlwaysOnTop` moves both of ImGui's orders**, drawing and input. Off by default.

`UiWorldProjection` holds the arithmetic (distance fade and scale, scale stepping, the off-screen direction, edge pinning, arrow geometry) and is public.

---

## Gauges and sparklines (NoireGauges)

Small readouts that show a number as a shape. Immediate and stateless: each one draws at the cursor and reserves what it used.

```csharp
NoireGauges.Bar(hp / (float)maxHp, new BarStyle
{
    Label = $"{hp} / {maxHp}",
    Marks = [0.25f, 0.5f],
    Thresholds =
    [
        new GaugeThreshold(0.5f, theme.Resolve(ThemeColor.Warning)),
        new GaugeThreshold(0.25f, theme.Resolve(ThemeColor.Danger)),
    ],
});

NoireGauges.Ring(0.72f, new RingStyle { Label = "72%" });
NoireGauges.Pips(charges, 5);
NoireGauges.Timer(remaining, total, new RingStyle { Size = 46f });
NoireGauges.Sparkline(history, new SparklineStyle { Min = 0f, Max = 165f, Baseline = 60f });
```

Every gauge takes a fraction from 0 to 1 and clamps it.

**Thresholds** apply at or below their value. The lowest match wins. A gauge counting the other way inverts the values.

**Countdowns empty.** `Timer` takes a `TimeSpan` pair and labels itself unless the style carries a label.

**A countdown's label reads whole seconds, rounded up.** `13s`. The last second reads `1s` and `0s` only once the time is gone.

**A ring's centre label shrinks to fit the hole**, and a bar's label to fit the bar, down to a readable minimum.

**A gauge's label is painted.** It goes through `NoireText.DrawAt` and submits no ImGui item.

**Sparkline bounds default to the data.** Pin `Min` and `Max` when two sparklines are compared. A flat or empty series still gets a usable range.

Rings are drawn from `NoireShapes.Wedge`. An open dial:

```csharp
new RingStyle { StartTurns = 0.625f, SweepTurns = 0.75f }   // the speedometer sweep
```

**Every gauge style carries a `CustomDraw` hook.** NoireUI keeps the sizing, clamping, thresholds and reserved space. Each record carries the resolved geometry and colors plus the shipped parts: `UiRingDraw` (`DrawTrack`/`DrawFill`/`DrawLabel`, with a `Timer`'s text in `Label`), `UiBarDraw` (`DrawTrack`/`DrawFill`/`DrawMarks`/`DrawLabel`), `UiPipDraw` (`DrawPip`, once per pip) and `UiSparklineDraw` (`DrawArea`/`DrawLine`/`DrawMark`, with the projected points).

```csharp
new PipStyle
{
    CustomDraw = static pip =>
    {
        var radius = (pip.Max.X - pip.Min.X) * 0.5f;

        if (pip.Filled)
            NoireShapes.Diamond(pip.Center, radius, pip.Color);
        else
            NoireShapes.DiamondOutline(pip.Center, radius, pip.Color);
    },
}
```

---

## Combo Box

`NoireComboBox<T>` is stateful: create one instance per combo, keep it, and call `Draw()` every frame.

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

With `FilterEnabled = true`, the dropdown shows a text input at the top, focused when the dropdown opens.

**Filtering is fuzzy by default.** `dkn` finds `Dark Knight`. The options are reordered by score and the matched characters are highlighted. The scorer is [`FuzzyMatcher`](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/FuzzyMatcher/README.md).

```csharp
combo.FilterHint = "Search a job...";
combo.ClearFilterOnOpen = true;                     // Default
combo.FilterAutoFocus = true;                       // Default
combo.FilterFuzzy = true;                           // Default. False = case-insensitive "contains", original order
combo.FilterHighlight = true;                       // Default. Picks out the matched characters
combo.FilterHighlightColor = null;                  // Default: the theme's accent
combo.FilterPredicate = (item, filter) => ...;      // Custom matching, overriding both of the above
combo.NoResultsText = "Nothing found";
```

`FilterPredicate` overrides `FilterFuzzy`. The options keep their order and nothing is highlighted.

`FilterText` is public. Setting it rebuilds the matches immediately.

### Keeping the search

`ClearFilterOnOpen = false` keeps the search between openings. `FilterMemory` keeps it beyond the widget itself:

```csharp
combo.FilterMemory = UiMemoryScope.Session;     // until the plugin reloads
combo.FilterMemory = UiMemoryScope.Persisted;   // across reloads; needs a stable id
combo.WheelCycleFiltered = true;                // the wheel then walks only what the search matches
```

Anything but `None` turns `ClearFilterOnOpen` off.

`Session` uses [`NoireUiSession`](#session-only-memory-noireuisession) and needs no stable id. `Persisted` uses `NoireUiState` and needs one. A combo without an id refuses with a single log line. `HasGeneratedId` tells which you have.

`WheelCycleFiltered` wheels through the matches on the closed combo. With nothing typed everything matches. When the selection is not a match, cycling enters the matches at one end.

The filter is **pinned above the options** by default. `FilterPinned = false` scrolls it with them:

```csharp
combo.FilterPinned = true;   // Default: only the option list scrolls
combo.FilterPinned = false;  // The whole dropdown scrolls, filter included
```

The dropdown shows **exactly one scrollbar**, and **none while every option fits**.

ImGui floors a constrained window's size before its scrollbar test. A precomputed height is off by the layout's fractions.

The dropdown and its list record what ImGui reported needing and ask for it back, rounded up, as a size minimum on the next frame. The `VisibleItemCount` cap still applies to longer lists.

### Long lists

Past `VirtualizeThreshold` options (100 by default) the list draws through a clipper.

```csharp
combo.Virtualize = null;          // Default: on past the threshold
combo.VirtualizeThreshold = 100;  // Default
combo.Virtualize = false;         // Force off, for rows of genuinely varying height
```

It requires **every row to be the same height**. A taller renderer declares its height through `ItemHeight`.

Arrow-key navigation keeps working through a virtualized list.

### Custom rows

`ItemRenderer` paints an option yourself. The combo keeps the row's size, hit testing, selection, keyboard state, filtering and scrolling.

```csharp
combo.ItemHeight = 22f;                       // Logical pixels; needed once rows are taller than a line
combo.ItemRenderer = option =>
{
    DrawIcon(option.Item);
    ImGui.SameLine();
    option.DrawLabel();                       // The combo's own text, filter highlighting included
};
```

`UiComboItemDraw<T>` carries the item, its index, its display text, and whether it is selected or highlighted. Call `DrawLabel()` to keep the filter highlighting. An exception in a renderer is caught and logged.

While the dropdown is open:
- **Mouse wheel** scrolls the option list, as in any list.
- **Up/Down arrows** cycle the highlighted option (the list follows it).
- **Enter** confirms the highlighted option.
- Clicking an option selects it, as usual.

```csharp
combo.DropdownCycleLoop = false;  // Default. Whether arrow key cycling wraps around.
combo.VisibleItemCount = 8;       // Options shown before the list scrolls.
```

### Restyling the closed box

`BoxStyle` paints the box with `NoireShapes`. The frame background and border are pushed transparent.

```csharp
combo.BoxStyle = new PlateStyle
{
    Fill = ink, FillTo = inkDeep, FillAxis = GradientAxis.Vertical,
    BorderColor = gold, BorderSize = 1f,
};
combo.BoxArrowColor = gold;
```

`PopupStyle` does the same for the dropdown: surface, border, rounding, padding, row padding and spacing, hovered and selected row colours, text and scrollbar. The filter box and the scrollbar follow it.

The plate is drawn under ImGui's transparent frame. The preview text, hit box, popup, filter and keyboard keep working. The rectangle comes from `ImGui.CalcItemWidth()`.

`BoxArrowColor` sets the arrow colour. `ImGuiComboFlags.NoArrowButton` removes it.

### Hold a binding + wheel cycling (closed combo)

Scrolling the mouse wheel over the **closed** combo cycles the selection, optionally gated behind a held binding, with or without looping:

```csharp
combo.WheelCycleEnabled = true;
combo.WheelCycleBinding = VirtualKey.CONTROL; // Default: an empty binding, meaning no key is required
combo.WheelCycleLoop = true;                  // true = wrap around, false = stop at the first/last item
```

`WheelCycleBinding` is a `HotkeyBinding`, matched like a hotkey (`KeybindsHelper.IsBindingHeld`): a key, modifiers, a key plus modifiers, or a gamepad button. Modifiers must match **exactly**.

```csharp
combo.WheelCycleBinding = VirtualKey.CONTROL;                    // A plain key converts implicitly
combo.WheelCycleBinding = new HotkeyBinding(0, ctrl: true, shift: true); // Ctrl + Shift, no key
combo.WheelCycleBinding = new HotkeyBinding(VirtualKey.G, ctrl: true);   // Ctrl + G
combo.WheelCycleBinding = GamepadButtons.North;                  // A gamepad button
```

While the combo cycles a scroll, **nothing else scrolls**. A scroll over an idle combo scrolls the window normally.

Hovering the combo shows a hint tooltip: each key of the shortcut as a keycap, then "+ Scroll", or "Scroll to cycle" with no binding. It follows a rebinding. `WheelCycleHintContent` takes any `NoireContent`.

```csharp
combo.WheelCycleHintEnabled = true; // Default
combo.WheelCycleHintContent = new NoireContent() // Optional override
    .AddText("CTRL + ")
    .AddImage(UiImageSource.FromFile(@"C:\path\to\mouse_scroll.png"), new Vector2(16f, 16f));
combo.WheelCycleHintStyle = new TooltipStyle { BackgroundOpacity = 0.75f };
```

### Plugging in the Hotkey Manager

Attach a hotkey registered on a [`NoireHotkeyManager`](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/HotkeyManager/README.md) and the combo reads its binding live.

```csharp
// Register the shortcut as a normal, user-rebindable hotkey.
// The combo reads the binding only. A hotkey registered just to gate a combo takes an empty callback.
hotkeyManager.RegisterHotkey(new HotkeyEntry("combo.cycle", "Cycle job", VirtualKey.CONTROL, () => { }, true, HotkeyActivationMode.Pressed));

combo.WheelCycleEnabled = true;
combo.BindWheelCycleHotkey(hotkeyManager, "combo.cycle");

// Anywhere in your settings window. The combo and its hint tooltip follow the new binding immediately:
hotkeyManager.DrawKeybindInputButton("combo.cycle");
```

The manager keeps owning the hotkey. Its binding is only read and its callback is untouched. `BindWheelCycleHotkey` does not enable the cycling. Set `WheelCycleEnabled` as well.

```csharp
combo.ResolvedWheelCycleBinding;   // The binding actually in effect (the hotkey's when attached, else WheelCycleBinding)
combo.UnbindWheelCycleHotkey();    // Detach: falls back to WheelCycleBinding
```

A disabled or unregistered hotkey turns the cycling **off**.

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

## Tag fields (NoireTagInput)

Collects short strings as chips: tags, filters, names, whitelist entries.

```csharp
var tags = new NoireTagInput("tags", config.Tags)
{
    Suggestions = knownTags,
    Validate = tag => tag.Contains(' ') ? "Tags cannot contain spaces." : null,
};

if (tags.Draw())
    config.Tags = tags.Tags.ToArray();
```

**Pasted text splits on `Separators`** (comma, semicolon, newline, tab by default). Empty pieces are dropped.

**Backspace on the empty input takes the last chip back for editing.** The chip's cross deletes it.

**Every refusal is named** as a `TagRejection`: `Empty`, `Duplicate`, `TooLong`, `Full`, or `Invalid` from your own `Validate`. `LastRejection` and `LastError` say which, and the field shakes (honouring `ReducedMotion`).

```csharp
tags.AllowDuplicates = false;      // Default; matched with Comparer, case-insensitive by default
tags.MaxTags = 10;                 // null for no limit
tags.MaxTagLength = 64;
tags.TrimWhitespace = true;        // Default
```

`TryAdd(tag, out var rejection)` is the full path, `Add` the shorthand, `AddRange(text)` splits and adds. `SetTags` restores a persisted list and drops anything the rules refuse.

**`RemoveAt(index)` removes a position. `Remove(tag)` removes the first match.** Chips are keyed on their index.

Suggestions are ranked with [`FuzzyMatcher`](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/FuzzyMatcher/README.md) and shown under the field. Tags already held are dropped from the list.

**`ChipDraw` replaces each chip's painting.** Layout, hit testing, removal and culling stay NoireUI's. `UiTagChipDraw` carries the chip's rect, index, tag, hover state and colours, plus `DrawPill()`, `DrawLabel()` and `DrawCross()`:

```csharp
tags.ChipDraw = static chip =>
{
    NoireShapes.Rect(chip.Min, chip.Max, chip.FillColor);   // squared instead of the pill
    chip.DrawLabel();
    chip.DrawCross();
};
```

## Picking several at once (NoireMultiCombo)

A dropdown of tick boxes that stays open while picking. The closed widget summarises the selection.

```csharp
var categories = new NoireMultiCombo<string>("categories", allCategories);

if (categories.Draw())
    config.Enabled = categories.Selected.ToArray();
```

**Selection is held by value.** Replacing the option list keeps what still applies. Pass an `IEqualityComparer<T>` to the constructor when needed.

```csharp
combo.Toggle(item);                  // returns whether it is now selected
combo.Set(item, true);               // returns whether anything actually changed
combo.SetSelection(saved);           // restore a persisted set outright
combo.SelectAll();
combo.ClearSelection();
```

`Selected` comes back in option order.

The preview names up to `PreviewMaxItems` items and summarises the rest (`PreviewOverflowFormat`, default `+{0} more`). `PreviewFunc` replaces it. If it throws, the built-in preview is used.

The **All** and **None** shortcuts apply to what the filter shows. `ShowSelectAll = false` hides them.

Filtering works as in the single-select combo: fuzzy matching, highlighting, and a clipper past `VirtualizeThreshold`. One scrollbar, around the option list.

`CloseOnSelect = true` makes it an ordinary combo.

## Game data pickers (NoireExcelPicker)

A searchable dropdown with icons over any sheet of game data, in one line.

```csharp
var items = new NoireExcelPicker<Item>("itemPicker", row => row.Name.ExtractText())
{
    Icon = row => row.Icon,
    Include = row => row.Icon != 0,      // which rows are offered; null means all of them
};

if (items.Draw())
    config.ItemId = items.SelectedRowId; // the row id is what you persist
```

`Select(rowId)` restores a persisted value, **even before the sheet has been read**.

**The sheet is read once, on a background thread.** Until then the picker draws a disabled stand-in. Reads are serialized across pickers.

**Display names are built once**, when the sheet is read. The filter scores every row on every keystroke.

`Include` is a predicate: equippable items, unlocked emotes, worlds on one data centre. `SkipEmptyNames` is on by default.

Changing `Language` reloads. Changing `Display`, `Icon` or `Include` needs `Reload()`.

`Combo` is the public `NoireComboBox<ExcelPickerEntry<TRow>>` underneath: the wheel-cycle shortcut, filter pinning, visible option count and renderers are all reachable through it.

## Settings fields (NoireInputs)

The fields a settings window is made of.

```csharp
NoireInputs.Number("Interval", ref config.IntervalMs, unit: "ms");
NoireInputs.Duration("Cooldown", ref config.Cooldown);
NoireInputs.HexColor("Accent", ref config.Accent);
```

Pass the value by reference. The return is "changed this frame".

**A number carries its unit inside the field.** `NumberStyle` has the stepper, the range and the decimals. The `int` overloads draw the same way.

**A duration is typed the way people write one.** The value is a `TimeSpan`:

```csharp
NoireInputs.Duration("Cooldown", ref config.Cooldown, new DurationStyle
{
    Default = TimeSpan.FromSeconds(30),
    BareUnit = DurationUnit.Milliseconds,   // what a number typed with no unit means
});
```

`90s`, `1m30s`, `1h30`, `2m 30s`, `1.5h` and `1:30` all read. A bare tail takes the unit below the one before it: `1h30` is ninety minutes. The reading shows beside the field while typing and is **written when the field loses focus**. `DurationHelper` is the parser on its own.

**A colour takes a pasted hex**, short or full, with a swatch that opens a picker. `ColorHelper.TryHexToVector4` is the non-throwing form.

**A row lays itself out inside its column.** Labels are padded to a shared `NoireInputs.LabelWidth` (110 logical px, a minimum) and aligned to the frame padding. The field takes the rest of the column, from `NoireLayout.ContentWidth()`.

**Give a `Default` and the modified dot appears.** Move a value off it and a dot shows beside the field. Click the dot to restore the default. The dot's column is always reserved. `NoireInputs.ResetDot` is the same dot for your own widgets.

**`ResetDotDraw` on each field style replaces the dot's painting.** Hit testing, layout and tooltip stay NoireUI's. The standalone `ResetDot` takes the same hook. `UiResetDotDraw` carries the centre, the hover-grown radius, the hover state and the colour, plus `DrawDot()`. The refusal message has no hook: its height drives the layout.

**Refusals report without blocking.** A `Validate` message slides in under the field. The value is still written. A refusal is **held until the field is typed in again**. `NoireInputs.Validated` wraps drawing of your own the same way:

```csharp
NoireInputs.Validated("port", port < 1024 ? "Ports under 1024 need administrator rights." : null, () =>
    ImGui.InputInt("Port", ref port));
```

## Data grids (NoireTable)

The column layout is ImGui's `BeginTable`. This adds the rest.

```csharp
var table = new NoireTable<PlayerModel>("players", players)
{
    SelectionMode = TableSelection.Multiple,
    Columns =
    {
        new TableColumn<PlayerModel> { Header = "Name", Text = p => p.Name },
        new TableColumn<PlayerModel> { Header = "World", Text = p => p.World },
        new TableColumn<PlayerModel> { Header = "Level", Text = p => $"{p.Level}", SortKey = p => p.Level },
    },
};

table.Draw();
```

**Column filters match like the search does**, and the matched characters are highlighted in the cell.

A column needs a header and a `Text`. The column sorts on that text, the search and filters read it, and the CSV export writes it.

**`SortKey` for text that does not sort like its data.** `"100"` sorts before `"80"` as text. Return the underlying value. `Sort` takes a full `Comparison<T>`.

**The table never copies your rows.** Filtering and sorting run when something changes. Editing the list in place needs `Invalidate()`.

**The rightmost column takes the leftover width.** Its own `Width` is ignored and it has no resize grip. It follows the *display* order.

**`Stretch` picks the column that takes the room instead.** Several stretch columns share the room by their `Width`, read as a weight.

```csharp
new TableColumn<Item> { Header = "Name", Text = i => i.Name, Stretch = true },
new TableColumn<Item> { Header = "Price", Text = i => $"{i.Price}", Width = 90f },
```

**Every other column keeps its own width.** Auto-fitting a stretch column would nudge all the others.

**Ties break on the source index.** Sorting is deterministic.

**The search filters without reordering.** The order is the one the user chose by clicking a header.

**Selection is held by value.** A plain click selects, ctrl or shift adds.

**Aggregates are computed over the rows showing.**

**The footer is pinned to the bottom of the table.** Body and footer are two tables in one bordered frame. The footer's columns take the body's actual widths, in display order.

```csharp
new TableColumn<Item> { Header = "Weight", Text = i => $"{i.Weight:0.0}", SortKey = i => i.Weight,
                        Aggregate = shown => $"{shown.Sum(i => i.Weight):0.0} total" }
```

**`ToCsv()` exports what is on screen**: the visible columns, the surviving rows, the chosen order, quoted per RFC 4180.

**Long lists draw only what is on screen**, past `VirtualizeThreshold` (100 rows) or whenever `Virtualize` says so. `DrawnRowCount` reports how many rows were drawn.

A `Renderer` on a column paints a cell in place of the plain text. The table keeps the sizing, sort, filtering and selection. The hook gets a `UiTableCellDraw<T>`, `DrawText()` included.

## Reorderable lists (NoireReorderableList)

A list whose rows can be dragged into a different order.

```csharp
var list = new NoireReorderableList<Step>("steps", config.Steps)
{
    Label = step => step.Name,
    AllowDelete = true,
    AllowDuplicate = true,
};

if (list.Draw())
    config.Save();
```

**The list is yours.** The widget reorders it in place and reports it. It never holds a copy.

**The drag starts on the grip.** `DragAnywhere` allows the whole row, for rows that are only a label.

**The list holds still during a drag.** The picked row dims, a ghost follows the pointer, and an outlined gap shows where it lands. The move happens on release.

**The drop target comes from the pointer position.** During a drag no other item is hovered. `ResolveSlot` is pure and unit-tested.

**`MoveItem` is a tested pure function.** It is unit-tested over every from/to pair: **a reorder never loses or invents a row.**

**A drag past the end puts the row last.**

**`AllowKeyboard` moves the focused row with the arrow keys.** Click a row, then press up or down. `KeyboardModifier` adds a required modifier, none by default. Focus follows a dropped row.

**The reorder keys are read like a hotkey.** With no text field active ImGui does not get the arrow keys. `KeybindsHelper.IsBindingHeld` reads the key state directly.

**The keys are a `HotkeyBinding`**, like the combo box's wheel-cycle shortcut: a local binding, or a hotkey id for rebinding.

```csharp
list.MoveUpBinding = VirtualKey.PRIOR;                              // local
list.BindReorderHotkeys(hotkeys, "list.moveUp", "list.moveDown");   // rebindable
```

Modifiers are matched exactly. `ResolvedMoveUpBinding` reports the binding in force.

**With hotkeys attached, the list blocks the key from the game only while a row and the window are focused.** `BlockGameInputWhileActive` turns this off.

**The key is taken through `HotkeyEntry.SuppressGameInput`, never by writing `BlockGameInput`.** `BlockGameInput` is a persisted setting of whoever registered the hotkey. A suppression is runtime only and reference-counted.

**The block expires on its own.** It is renewed every drawn frame and released by a watchdog once the frames stop: closing the window, switching tab, emptying the list, detaching the hotkeys or turning `BlockGameInputWhileActive` off. The focus is dropped with it.

**Clicking outside the list drops the focus**, tested against the rows' own bounds.

**`Duplicate` matters for anything mutable.** Without it the copy and the original are the same object. A record needs `step with { ... }`, a class a real copy.

```csharp
list.Duplicate = step => step with { Name = $"{step.Name} (copy)" };
```

A `Renderer` paints a row between the grip and the buttons, with a `UiReorderRowDraw<T>` that has `DrawLabel()`.

**Flat lists only.** Trees are out of scope.

---

## Tab bars you can drive (NoireTabBar)

A tab bar whose tabs you can open from code, from anywhere.

```csharp
var tabs = new NoireTabBar("Settings")
{
    Tabs =
    {
        new UiTab("general", "General", () => DrawGeneral()),
        new UiTab("filters", "Filters", () => DrawFilters()) { Badge = () => activeFilters },
        new UiTab("about",   "About",   () => DrawAbout())
        {
            Enabled = () => hasData,
            DisabledReason = "Load a log first.",
        },
    },
    OnTabChanged = id => Log($"now on {id}"),
};

tabs.Draw();

tabs.SwitchTab("filters");   // from another window, a hotkey, a command, a toast action
tabs.Current;                // "general"
```

ImGui opens a tab from code through `ImGuiTabItemFlags.SetSelected`, set for **exactly one frame**.

`SwitchTab` handles it:

| You do this | It does this |
|---|---|
| Switch to the tab already open | Nothing. It is not a switch. |
| Switch twice before a frame runs | Keeps the last request. |
| Switch before the bar has ever drawn | Applies on the first frame it draws. |
| Switch from a background thread | Marshals through `RunOnDraw`. |
| Switch to a disabled tab | Refused. Code may not reach a tab a click cannot. |
| Switch to an unknown or removed id | Refused, and logged **once per id**. |

| Property | Default | What it does |
|---|---|---|
| `Tabs` | empty | The tabs, in draw order. Add, remove or replace at any time. |
| `Current` | `null` | The tab open as of the last draw. Null before the first one. |
| `PendingTab` | `null` | A switch waiting for the next frame. |
| `OnTabChanged` / `OnTabClosed` | none | Raised once per change, by click or by code. |
| `Reorderable` | off | Let the user drag tabs. |
| `ScrollWhenCrowded` | off | Scroll when they do not fit. |
| `WheelScrolls` / `WheelScrollStep` | on / `80` | Wheel over the strip scrolls it. See below. |
| `Width` | `0` | How wide the bar may be. Zero fits the column it is in. |
| `EmptyState` | none | Drawn when there are no tabs at all. |

Each `UiTab` carries its own `Body`. `Label` may change every frame. ImGui is keyed on `Id`.

**A tab disabled while open stays open.** `Enabled` gates reaching a tab. Set `DisabledReason` whenever you set `Enabled`.

**`Reorderable`** lets the user drag tabs. ImGui does not report the order back and `Tabs` is left as written.

**The wheel scrolls the strip.** `WheelScrolls` (on by default) scrolls the strip under the pointer without selecting anything. It does nothing while every tab fits.

The windows behind do not scroll on the same notch. ImGui hands the wheel out inside `NewFrame`. While the pointer is over the strip, the bar marks its scrollable ancestors as not scrolling with the mouse, a frame ahead.

**`Width` keeps it inside your column.** At `0` the bar asks `NoireLayout.ContentWidth()`. It can only narrow the bar.

---

## Badges and attention (NoireBadge, NoireAttention)

The mark that says something is waiting, and the motion that draws the eye to it.

```csharp
ImGui.Button("Inbox");
NoireBadge.OnLast(unread);              // a count in the corner; nothing at all when it is 0

ImGui.Button("Settings");
NoireBadge.DotOnLast(hasChanges);       // just a dot

ImGui.Button("Apply");
NoireAttention.Glow(hasUnsavedChanges); // a halo while the condition holds

NoireAttention.Shake("password");       // fired once, from the failure path
NoireAttention.ApplyOffset("password"); // read back on the frames that follow, before the widget
```

Both are immediate and stateless, and both draw **over** a rectangle you already have.

**A badge costs no layout.** It writes straight to the draw list and submits no ImGui item.

**States and events.** `Pulse` and `Glow` are states: pass the condition every frame. `Shake`, `Bounce` and `Flash` are events: fired once by id, they settle back to zero.

`ApplyOffset` nudges the cursor before the widget. A shake or a bounce moves where the widget is drawn.

| `BadgeStyle` | Default | What it does |
|---|---|---|
| `Scale` | `1` | One knob for the whole badge. See below. |
| `Color` / `TextColor` | danger / text | Danger, because a badge exists to be seen before anything else on the element. |
| `MaxCount` | `99` | Above it, the badge reads `99+`. Zero shows everything. |
| `Anchor` / `Offset` | top right | Which point of the element it straddles, and the nudge from it. |
| `OutlineThickness` | `1.5` | A ring in the surrounding colour. |
| `Pulse` | off | A slow fade to catch the eye without moving anything. |
| `DotSize` / `MinSize` / `PaddingX` | `7` / `15` / `4` | Sizing, in logical pixels. |
| `CustomDraw` | unset | Replaces the painting for the count and the dot both. See below. |

A count of zero or less draws nothing. `NoireBadge.OnLast(count)` can be called unconditionally.

**`CustomDraw` replaces the painting.** Placement and measurement stay NoireUI's. `UiBadgeDraw` carries the rectangle, the formatted count (`null` for a dot) and every colour, plus `DrawPlate()` and `DrawLabel()`. `CountSize` answers for the space:

```csharp
new BadgeStyle
{
    CustomDraw = static args =>
    {
        NoireShapes.Diamond(args.Bounds.Center, args.Bounds.Size.Y * 0.5f, args.Color);
        args.DrawLabel();
    },
}
```

**A badge is never moved to fit.** It straddles its anchor corner. Where it may not overflow, it is clipped. `NoireTabBar` clips badges to the ends of its bar.

**`Scale` is the size knob:**

```csharp
NoireBadge.OnLast(unread, new BadgeStyle { Scale = 2f });   // twice the size, still in proportion
```

It scales the text, padding, minimum size, dot, outline and anchor offset together, on top of `NoireUI.Scale`. Each distinct value is a distinct font size.

**Everything stops under `NoireUI.ReducedMotion`** except `Glow`. It holds at full strength.

## Keyboard focus (NoireFocus)

Marks the control the keyboard is pointed at. **Every NoireUI widget draws it itself:**

```csharp
NoireFocus.Style.Shape = FocusShape.Corners;   // everywhere, once
NoireFocus.Enabled = false;                    // or not at all

ImGui.InputText("##notes", ref notes, 256);
NoireFocus.OnLast();                           // a control the library does not provide
```

**Focus is drawn hard edged.** Hover, selection and emphasis use soft marks: a glow, a tint, a lit plate.

| `FocusShape` | What it is | Where it fits |
|---|---|---|
| `Ring` | A hairline outline following the whole edge | The default. Unambiguous at any size or proportion |
| `Corners` | A short elbow inside each corner | Quieter, and lighter on a busy surface |
| `Brackets` | A matched `[` and `]`, one each side | The most decorative, and the one needing the most room |
| `Underline` | A bar along the bottom edge alone | Quietest. Suits a text field |
| `None` | Nothing | How one widget opts out while the rest keep their mark |

**Three levels of control.** `NoireFocus.Enabled = false` turns the mark off everywhere. Each widget takes a style of its own (`NumberStyle.Focus`, `DurationStyle.Focus`, `HexColorStyle.Focus`, `NoireComboBox.FocusStyle`, `NoireTagInput.FocusStyle`), and `Shape = FocusShape.None` hides one. `FocusStyle.CustomDraw` replaces the painter:

```csharp
combo.FocusStyle = new FocusStyle { Shape = FocusShape.None };          // this one widget, unmarked

numberStyle.Focus = new FocusStyle
{
    CustomDraw = args =>
    {
        args.DrawShape();                                               // what NoireUI would have drawn
        args.DrawList.AddCircleFilled(args.Min, 3f * args.Arrival, gold); // and something of your own
    },
};
```

The hook gets the rect with the spread and arrival applied, the faded colour, the control's rectangle, and `Arrival` from 0 to 1. `DrawShape()` paints the shipped look.

**The mark moves on arrival only.** `ArrivalSeconds` (0.12 by default) is how long it takes to settle, drifting in from `ArrivalSpread` and fading up. Focus returning to a control counts as an arrival too.

**It survives `NoireUI.ReducedMotion`.** The arrival is skipped and the mark placed instantly. Turning `NoireFocus.Enabled` off is an accessibility loss.

Arms on `Corners` and `Brackets` are sized by `ArmRatio`, a fraction of the control's shorter side. `ArmLength` overrides it with a fixed distance. Either is clamped so two arms on one edge never meet.

The mark submits no ImGui item.

## Custom Tooltips

`NoireTooltip` tooltips are independent windows on the topmost layer. A custom tooltip and `ImGui.SetTooltip()` can show at the same time.

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

Content is built from inline segments in a `NoireContent`. Segments flow on one line, **vertically centered**, until `AddNewLine()` or `AddSeparator()`:

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

`NoireContent.Draw()` is public. The same block renders anywhere:

```csharp
content.Draw();   // Renders at the current cursor.
```

### Style & transparency

The background opacity goes from 0% to 100%:

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
    MaxWidth = 380f,                                 // Text wraps past this width at 100%. Zero means no limit
};

NoireTooltip.ShowOnItemHover(content, style);
```

Every tooltip wraps at `MaxWidth`, 380 by default.

**For a flagged window of your own, push the field the flag selects.** ImGui picks between the window, popup and child style fields by window flag:

| | Border size | Rounding | Background |
|---|---|---|---|
| Ordinary window | `WindowBorderSize` | `WindowRounding` | `WindowBg` |
| Popup | `PopupBorderSize` | `PopupRounding` | `PopupBg` |
| Tooltip | `PopupBorderSize` | `WindowRounding` | `PopupBg` |
| Child | `ChildBorderSize` | `ChildRounding` | `ChildBg` |

Pushing the wrong one does nothing. A popup styled with `WindowRounding` and a tooltip styled with `WindowBorderSize` are both no-ops.

**`TooltipStyle.CustomDraw` replaces the chrome.** The window is begun with `NoBackground` and the hook paints before the content. Placement, measuring and content are untouched. The hook is not called while the tooltip is parked off screen. `UiTooltipDraw` carries the window rectangle, every resolved value, and `DrawBackground()` and `DrawBorder()`.

```csharp
style.CustomDraw = static args =>
{
    args.DrawBackground();
    NoireShapes.On(args.DrawList, args, static chrome =>
        NoireShapes.RectOutline(chrome.Min, chrome.Max, chrome.BorderColor, chrome.BorderSize));
};
```

### Placement

```csharp
style.Placement = TooltipPlacement.Mouse;       // Default: follows the mouse cursor
style.MouseOffset = new Vector2(16f, 16f);

style.Placement = TooltipPlacement.AboveItem;   // Or BelowItem / LeftOfItem / RightOfItem
style.ItemGap = 6f;                             // Pushes the tooltip away from the item, along the placement axis
style.ItemOffset = new Vector2(12f, -4f);       // Shifts it freely on both axes, on top of the gap
```

`ItemGap` moves the tooltip along the placement's axis. `ItemOffset` nudges it in x and y.

A tooltip is placed correctly on its first frame.

`Show(content, style)` can be called every frame the tooltip should stay visible, without a hovered item.

---

## Images (UiImageSource)

An image for overlay buttons and tooltip contents:

```csharp
UiImageSource.FromFile(@"C:\path\to\image.png"); // From disk
UiImageSource.FromGameIcon(66413);               // From a game icon id
UiImageSource.FromGameTexture("ui/uld/image.tex"); // From an internal game texture path
UiImageSource.FromWrap(myTextureWrap);           // From a texture wrap you own (and dispose) yourself
```

File and game sources go through Dalamud's shared texture cache and load asynchronously.

`UiImageSource.FromManifestResource(assembly, name)` reads an embedded image. An unknown name resolves to `null`.

---

## Brand and custom icons (NoireIcons)

Dalamud's icon font is FontAwesome Solid only, without brand marks. `NoireIcons` draws textured icons through one call.

```csharp
NoireIcons.Draw(NoireIcon.Discord, 16f);                               // built-in, as an ImGui item
NoireIcons.Register("MyPlugin.Patreon", UiImageSource.FromManifestResource(Assembly.GetExecutingAssembly(), "MyPlugin.patreon.png"));
NoireIcons.Draw("MyPlugin.Patreon", 16f, tint: accent);                // registered
NoireIcons.DrawAt(drawList, NoireIcon.Kofi, min, sidePx, packedTint);  // into a draw list, no item
```

- **Built-in marks** are white (Ko-fi keeps its red heart) on transparent, embedded at 20, 24, 32, 40, 48, 64 and 128 px. `Source(icon, pixelSize)` picks the smallest raster that covers the drawn size. Owners and sources are in `UI/Icons/Assets/ATTRIBUTION.md`.
- **Registered names are per plugin.** Names may be registered before `NoireLibMain.Initialize`.
- **Nothing throws in a frame.** Loading art draws nothing. An unknown name draws nothing and reports one fault per name to `NoireUI.Diagnostics`.
- **Registered art is fitted** into its square.
- **Title bars**: Dalamud's `TitleBarButton` only takes a FontAwesome glyph. Use `NoireWindowChrome`.

---

## The UI scale

Dalamud applies the user's interface scale to the ImGui style. Numbers a library ships need scaling. NoireUI does it in one place.

```csharp
NoireUI.Scale                       // the user's scale, where 1 is 100%
NoireUI.Scaled(12f)                 // a pixel value of your own, authored at 100%
NoireUI.Scaled(new Vector2(12, 10))
```

**A number NoireUI has an opinion about is written at 100% and scaled for you.** Everything on `NoireTheme`, any `*Style`, `ModalOptions`, `UiPosition`, `NoireToastArea.Width` and `NoireOverlayButton.Size`. A toast width of 340 is 340 pixels at 100% and 510 at 150%.

**A number NoireUI only hands to ImGui is in real pixels.** A `size` argument on `NoireButtons.Button`, `NoireComboBox.Width`, a `Splitter`'s `size` and bounds, the `width` of `Flow` or `WrapText`, the amount given to `NoireLayout.Indent`. NoireUI's own defaults inside those calls do scale.

**Anything a `Resolve` method returns is in real pixels.** Never pass it through `NoireUI.Scaled`.

For pixel values of your own, use `NoireUI.Scaled`.

---

## Text at any size (NoireText)

ImGui scales its one-size font atlas up for bigger text. The result is blurry. `NoireText` builds a real font at the size asked for.

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

`Draw`, `Colored`, `Muted`, `Disabled`, `Wrapped`, `Bullet`, `Centered`, `Highlighted`, `DrawAt`, `CalcSize`, `LineHeight`, `CenterOffset`, and the `At` scopes. Sizes are logical pixels at 100%.

**`DrawAt` paints text at a screen position without submitting an ImGui item**, for a label over something that already reserved its room.

```csharp
NoireText.DrawAt(centre - (measured * 0.5f), color, "72%", TextSize.Caption);
```

An ordinary text call submits an item and moves the cursor.

### Ask by role

`TextSize` has four steps: `Display`, `Heading`, `Body`, `Caption`. They resolve through `NoireTheme`, and every step but the body derives from the body size:

```csharp
NoireTheme.Current.BodySize = 20f;      // the whole scale grows with it
NoireTheme.Current.HeadingSize = 24f;   // this step opts out; the others keep following
NoireTheme.Current.HeadingSize = null;  // and back onto the proportion
```

`BodySize` left unset is the host's default font size (`NoireTheme.DefaultBodySize`) and costs no atlas space.

`NoireText.Draw(text, 22f)` draws a one-off size.

### Lining a drawn shape up with a label

The em box reserves room for descenders. A shape centred on the line sits one to two pixels above the words beside it.

`NoireText.CenterOffset()` is where the text looks centred, read off the font's capital band:

```csharp
var middle = rowTop + NoireText.CenterOffset();          // where the label looks centred
var half = side * 0.5f;

NoireShapes.Rect(new Vector2(x, middle - half), new Vector2(x + side, middle + half), color);
```

Every label at a given size gets the same offset.

### What a size costs to build

Rasterizing is per glyph and per size. A complete font is several thousand glyphs, and three sizes of it take seconds.

NoireText re-sizes the user's own font specification with a reduced glyph range: Latin with accents, prose punctuation, currency, arrows and common symbols. Around seven hundred glyphs. The glyphs for the user's Dalamud language are added on top.

The icon font and the rest of Unicode are dropped. Two knobs put them back:

```csharp
// Wider glyphs: Greek and Cyrillic on top of the usual Latin.
NoireText.GlyphRanges = [0x0020, 0x00FF, 0x0370, 0x03FF, 0x0400, 0x04FF, 0];

// Or take over the build entirely. This is what NoireText did before it was made fast:
// everything the default font has, icons included, and slower for it.
NoireText.FontBuilder = (toolkit, sizePx) => toolkit.AddDalamudDefaultFont(sizePx);
```

Set either before the first size is built.

### A type scale that changes while you watch

A size not built yet is rasterized once the scale has held still for `NoireText.RebuildSettleDelay` (120 ms by default). Meanwhile text draws at the right size with the stretched stand-in. A size already built is sharp immediately.

Sizes are cached at whole pixels. Sizes out of the scale and unused are dropped after 20 seconds.

### The size limit

Every distinct size is a full glyph atlas, in an atlas of NoireUI's own. One entry per size for the life of the plugin.

**The cache is bounded at 16 distinct sizes.** Past that it draws at the nearest built size and logs once. `NoireUI.Diagnostics.Snapshot().TextFontSizes` reports the count.

### While a size is still building

**The whole scale is built in one rebuild.** A rebuild re-rasterizes every font in the atlas. Every step of the theme's scale is registered at once. The build time is logged.

**Until the real font is ready, text draws with the loaded font stretched to the right size.** The layout does not jump when the font arrives.

**`NoireText.Prewarm()` starts the build at load. `Prewarm(wait: true)` blocks until it is done.**

```csharp
public Plugin()
{
    NoireLibMain.Initialize(PluginInterface, this);
    NoireTheme.Current = NoireTheme.FromAccent("#C8A96A");   // set the theme first: it decides the sizes
    NoireText.Prewarm(wait: true);
}
```

Use it from a constructor, never from a draw callback.

With `wait`, the glyphs are rasterized on the calling thread.

Safe to call repeatedly.

### Warming the code

**A .NET method is compiled the first time it runs.** A large window jits its whole draw path in its first frame. Measured on the library's acceptance window: **170 ms in one frame**.

`NoireUI.WarmDrawPath()` compiles it in advance, on a background thread:

```csharp
public Plugin()
{
    NoireLibMain.Initialize(PluginInterface, this);
    NoireText.Prewarm(wait: true);                   // the fonts
    NoireUI.WarmDrawPath(typeof(MyBigWindow));       // the code
}
```

Every NoireUI drawing surface is included. The types you pass are added. `NoireUI.DrawPathWarmed` says when it has finished.

**Opt in.** It spends CPU at load and makes nothing faster afterwards. Use it when a window's first open is visibly slow. In the profiler's **Longest** column, jitting shows as every scope peaking at ten to a hundred times its average on its first frame.

Methods that cannot compile early, such as an open generic, are skipped. The warmup runs once per process.

**`CalcSize` measures whatever would draw**, stand-in included.

---

### Letter-spacing

ImGui has no tracking. `NoireText.Tracked` places each glyph.

```csharp
NoireText.Tracked("OVERLAYS", NoireText.CapsTracking, TextSize.Caption);
var width = NoireText.TrackedSize("OVERLAYS", NoireText.CapsTracking, TextSize.Caption).X;
```

`Tracked` returns the size it drew. Glyph advances are measured once per font size. Neither call allocates.

**`TrackedSize` answers from a remembered measurement**, keyed on the run, its tracking, both sizes, the UI scale and the font generation. `Tracked` files its measurement under the same key. Measuring needs a font push, and a font push allocates.

**Never measure text outside a frame.** `CalcSize`, `Draw`, `Tracked` and the rest need a frame in progress. Calling them from a constructor crashes. `NoireText.Request(sizePx)` is the frame-safe call: it asks for a size to be built ahead of time.

**`UiFontCache.MaxSizes` bounds the distinct sizes.** The default of 16 suits one type scale. Raise it when offering several scales.

**Tracking is in ems, like CSS letter-spacing.** One value is right at every step and every UI scale. `CapsTracking` is the shipped default for capitals.

The run is drawn onto the draw list and reserved with a single `Dummy`. The trailing gap after the last character is excluded.

---

## Fonts of your own (NoireFont)

`NoireFont` draws **a typeface you ship**: one TTF per weight, from memory, a file or a manifest resource. NoireLib ships no font.

```csharp
var ui = new NoireFontFamily("Hanken Grotesk")
    .Add(400, assembly, "MyPlugin.Fonts.HankenGrotesk-Regular.ttf")
    .Add(700, assembly, "MyPlugin.Fonts.HankenGrotesk-Bold.ttf");

ui[700].Request(15f);                                                  // from the constructor: build before first open
ui[700].Draw(drawList, pos, color, "Title", 15f, trackingPx: -0.3f);  // CSS: font: 700 15px; letter-spacing: -.3px
ui[400].Draw(drawList, pos, color, name, 13f, 0f, maxWidth: 180f);    // cut with an ellipsis past 180 px
var size = ui[400].CalcSize(name, 13f);
using (ui[400].Push(13f)) ImGui.TextUnformatted("raw ImGui in the face"u8);
```

| Member | |
|---|---|
| `NoireFont.FromMemory(bytes, name)` / `FromFile(path)` / `FromManifestResource(assembly, name)` | One face. |
| `Draw(drawList, pos, color, text, sizePx, trackingPx = 0, maxWidth = 0)` | Paints and returns the drawn size. `DrawAt` paints into the current window, `Text` submits an item. |
| `CalcSize(text, sizePx, trackingPx = 0, maxWidth = 0)` / `IsTruncated(...)` | Measures exactly what `Draw` paints. Cached. |
| `Push(sizePx)` | A `NoireFontScope` that makes the face the current ImGui font. |
| `LineHeight`, `Ascent`, `HalfLeading(sizePx, lineHeight)` | CSS line boxes: `HalfLeading(13, 1.4)` is where the text of a `13px/1.4` line starts. |
| `Request(sizePx)` / `Request(sizes)` / `IsReady(sizePx)` / `Font(sizePx)` | Warming, readiness, and the raw `ImFontPtr` once built. |
| `GlyphRanges`, `MergeLanguageGlyphs`, `Oversample`, `SizeStep`, `OnBuild` | Build settings, read at each build. |
| `NoireFontFamily[weight]` | The face for a weight by CSS `font-weight` matching (400 tries 500 first, and so on). |

- **Sizes are em sizes, like CSS `font-size`.** ImGui sizes a font by its ascender-to-descender height, about 1.3 em. The face's `head` and `hhea` tables convert it. Sizes and tracking are logical pixels at 100%. `maxWidth` is real pixels.
- **Each built size is measured.** The host rounds the size and the rasterizer maps it its own way. A run comes out as wide as in a browser.
- **Kerning is applied**, from the face's GPOS `kern` feature or its legacy `kern` table. `Kerning = false` turns it off.
- **A missing weight is synthesised** like a browser does. `NoireFontFamily.Pick` returns the smear (`NoireFont.SyntheticBoldPixels`) for a weight of 600 or more with no face that heavy. `Draw` takes it as `syntheticBoldPx`.
- **Never blocks.** Each size rasterizes asynchronously on first use. Until then text draws with the current font stretched to the same line height.
- **Zero allocation per frame**, tracking and ellipsis included. Drawing needs no font push.
- **Sizes are shared by step.** `SizeStep` (0.5 real px) rounds sizes. A size unused for 30 seconds is dropped at the next build. `NoireFont.MaxBuiltSizes` (256) bounds the total.

---

## Drawing shapes (NoireShapes)

ImGui's draw list has rounded rectangles but no chamfered ones, one axis-aligned gradient, no bevel and no glow.

`NoireShapes` adds them. No state, no id, no hit testing.

```csharp
var min = ImGui.GetCursorScreenPos();
var max = min + new Vector2(320f, 90f) * NoireUI.Scale;

NoireShapes.Plate(min, max, new PlateStyle { CornerShape = CornerShape.Notched, CornerSize = 12f, BevelSize = 2f });
NoireShapes.Frame(min, max, new FrameStyle { TickLength = 14f, Inset = 6f });
```

**Arguments are real pixels. Style values are logical.** A coordinate or size passed as an argument is used as is. A value on a `PlateStyle` or `FrameStyle` is at 100% and scaled. See [The UI scale](#the-ui-scale).

**`NoireShapes` sets antialiasing around its own drawing** and restores it. `NoireShapes.AntiAlias` turns it off for these shapes.

Unrelated to the Draw3D renderer.

### Where it draws

The current window's draw list by default. `NoireShapes.On` redirects a block of drawing, and nests:

```csharp
NoireShapes.On(ImGui.GetBackgroundDrawList(), () =>
{
    NoireShapes.Sunburst(centre, 400f, glow);       // behind every window, across the whole screen
});
```

`NoireShapes.DrawList` is the list being painted into.

### Plates

A plate is the surface of a panel, card, masthead or button face. One call paints the fill or gradient, the bevel, the border and the glow.

```csharp
NoireShapes.Plate(min, max, new PlateStyle
{
    CornerShape = CornerShape.Notched,
    CornerSize = 14f,
    Corners = RectCorners.Diagonal,       // chamfer two corners
    Fill = accent with { W = 0.22f },
    FillTo = accent with { W = 0.02f },   // unset for a flat plate
    BevelSize = 2f,
    GlowSpread = 10f,
});
```

Only the fill and the theme's border are on by default.

`CornerShape` is `Square`, `Rounded` or `Notched`. `RectCorners` picks which corners, with the diagonal pairs named.

**The bevel works on any shape.** Each edge is lit by how far its normal faces the light. `BevelDirection` moves the light, above and to the left by default.

### Gradients over anything

The gradient is a **scope over arbitrary drawing**. It shades whatever the body drew.

```csharp
// One ramp across a plate and the ring inside it.
NoireShapes.Gradient(min, max, GradientAxis.Horizontal, accent, warning, () =>
{
    NoireShapes.Rect(min, max, Vector4.One, CornerShape.Rounded, 20f);
    NoireShapes.Ring((min + max) * 0.5f, 22f, Vector4.One, 3f);
});
```

Pass two points in place of a `GradientAxis` for any angle. `NoireShapes.GradientRect` is the shorthand for a rectangle.

**Colour is replaced and alpha is multiplied.** A body drawn in white takes the gradient exactly. A coloured body is tinted. A gradient fading to zero alpha fades the shape out.

Nesting works.

### Frames and corner ticks

A rectangle with a short bracket inside each corner.

```csharp
NoireShapes.Frame(min, max, new FrameStyle
{
    Inset = 6f,          // set the frame off the content without moving the content
    DoubleGap = 3f,      // a second line inside the first
    TickLength = 16f,
    TickCorners = RectCorners.Diagonal,
});
```

`TickLength` zero draws an ordinary outline.

**Corner ticks disappear below twice the tick length on either axis.** `TickFallback.Brackets` draws a full-height bracket at each end instead:

```csharp
new FrameStyle { TickLength = 16f, TickFallback = TickFallback.Brackets };
```

The brackets and corner ticks are public on their own:

```csharp
NoireShapes.Brackets(min, max, gold, armLength: 7f, thickness: 1.5f);
NoireShapes.Bracket(min, max, gold, 7f, 1.5f, BracketSide.Right);   // just the "]"
NoireShapes.CornerTicks(min, max, gold, length: 7f, thickness: 1.5f);
NoireShapes.CornerTicks(min, max, gold, 7f, 1.5f, RectCorners.TopLeft | RectCorners.BottomRight);
```

### Arcs, rings and wedges

**Angles are turns.** Zero is twelve o'clock and a quarter is three o'clock. Sixty eight percent is `0.68`.

```csharp
NoireShapes.Wedge(centre, radius - 12f, radius, 0.125f, 0.875f, track);        // the empty track
NoireShapes.Wedge(centre, radius - 12f, radius, 0.125f, 0.125f + 0.75f * value, accent);
NoireShapes.Arc(centre, radius + 5f, 0.125f, 0.875f, accent, 2f);
NoireShapes.Ring(centre, radius, hairline);
```

A wedge with an inner radius is a thick arc. With an inner radius of zero it is a filled pie.

**A full turn closes cleanly.** `NoireShapes.ArcPath` reports whether the sweep came all the way round, and such a path stops one step short of its first point. Pass the `closed` flag on to `Stroke`:

```csharp
Span<Vector2> points = stackalloc Vector2[NoireShapes.MaxArcPathPoints];
var count = NoireShapes.ArcPath(points, centre, radius, 0f, value, out var closed);
NoireShapes.Stroke(points[..count], accent, 4f, closed);
```

**`NoireShapes.ArcError` sets curve smoothness**: how far a chord may sit inside the true curve, in real pixels. It defaults to 0.15, finer than ImGui's 0.30. The segment count follows from the error and the radius. A thick arc is tessellated for its outer edge.

### Pattern fills

Two patterns, both drawn as geometry.

```csharp
NoireShapes.Sunburst(centre, 240f, accent with { W = 0.5f }, new SunburstStyle { Rays = 32, Duty = 0.35f });
NoireShapes.Guilloche(centre, 120f, accent, new GuillocheStyle { Lobes = 9, Rings = 3, RingRotationTurns = 0.5f / 9f });
```

The guilloche is the interlaced rosette engraved on banknotes and watch dials, a hypotrochoid. `Lobes` is the ratio between the two circles and `Depth` how far out the pen sits.

**The sunburst's rays have soft sides** (`SunburstStyle.Softness`, 0.35 by default). Zero gives hard edges.

**The hole in the middle is a ratio or a distance.** `InnerRatio` is a fraction of the radius. `InnerSize` is a distance at 100% and takes precedence.

For per-pixel patterns, build a texture and draw it with [`UiImageSource`](#images-uiimagesource).

### Glows, clipping and sweeps

`Glow` grows a **rectangle**. `GlowPath` grows the path itself.

```csharp
NoireShapes.Diamond(centre, 6f, gold, glow: goldHi, glowSpread: 8f);   // sugar over both
NoireShapes.GlowPath(myPoints, goldHi, 8f);                           // any convex, clockwise path
```

Each vertex moves along its edges' bisector. The miter is floored.

**`Clipped` keeps a whole composition inside a box.**

```csharp
NoireShapes.Clipped(min, max, () => { PaintSunburst(); PaintRosette(); });
```

**`SweepLine` is a line with a bright band travelling along it.** The band runs off both ends.

`FadeIn`, `Diamond`, `DiamondOutline` and `DiamondPath` are the small marks a deco interface repeats. The diamond helpers wind clockwise for `Fill` and `GlowPath`.

### Shapes NoireUI does not ship

Every shape above is drawn by the same three public calls over a path:

```csharp
Span<Vector2> tag = [ /* your own points, clockwise */ ];

NoireShapes.Fill(tag, fill);
NoireShapes.Bevel(tag, light, shadow, 2f);
NoireShapes.Stroke(tag, border);
```

`NoireShapes.RectPath` and `NoireShapes.ArcPath` are public. A buffer of `NoireShapes.MaxRectPathPoints` or `MaxArcPathPoints` is always large enough.

`Fill` needs a **convex** path. Draw a concave shape as convex pieces. `Bevel` needs it wound **clockwise**. Every `RectPath` output is both.

**`FillUnder` fills between a left-to-right polyline and a horizontal line**, for area charts and sparklines. `Fill` cannot.

```csharp
NoireShapes.FillUnder(projectedPoints, plot.Bottom, ColorHelper.ScaleAlpha(accent, 0.18f));
NoireShapes.Stroke(projectedPoints, accent, 1.5f, closed: false);
```

It writes one triangle strip. A translucent fill has no seams.

---

## Ribbon backdrop (NoireRibbonField)

A dark vertical gradient, translucent ribbons on two summed sine waves with a thickness swell and a centre line, and a two-circle radial vignette, clipped to a rounded rectangle. The ribbons lean toward the pointer and ripple on demand.

```csharp
private readonly NoireRibbonField field = new();          // defaults: five ribbons, one window

// Draw(), once per frame: lay out, then paint as the window background.
field.Frozen = NoireUI.ReducedMotion;
field.Update(min, max);                                    // reads the pointer; or Update(min, max, mousePos)
field.Draw(min, max, rounding: 16f * NoireUI.Scale, opacity: 1f);

field.Wave(ImGui.GetMousePos().X, accent);                 // a ripple from a point, leaning odd ribbons toward a colour
field.Lean(null);                                          // back to the rest colour
```

- **One field, several views.** `Update` lays the field out once per frame. `Draw(drawList, min, max, rounding)` paints any rectangle of it. Ribbons run continuously across adjacent windows.
- **Clipped by geometry.** ImGui's rectangular clip is never used for the corners.
- **Every number is an option** on `RibbonFieldOptions`: the ribbons (`Ribbon` records, with `Quieter(factor)`), samples and overscan, both wave frequencies, thickness swell, gradient stops, stroke, lean space and sharpness, smoothing, waves, background and vignette. `RibbonLeanSpace.Screen` keeps the pointer in screen pixels, for a field shared by several windows.
- **Frame-rate independent.** Smoothing fractions are per `ReferenceFrameRate` (60) frame. Time advances by the frame's delta, capped at `MaxTimeStep`.
- **`Frozen`** stops time and smoothing and clears waves. **`Reset()`** returns the clock, pointer, lean colour and waves to their initial state.
- `DrawBackground`, `DrawRibbons` and `DrawVignette` paint one layer each. Zero allocation per frame.
- **Opacity** multiplies each layer's alpha.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Hotkey Manager Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/HotkeyManager/README.md)
