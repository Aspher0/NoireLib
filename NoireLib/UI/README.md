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
- [Sliders (NoireSliders)](#sliders-noiresliders)
- [Toasts (NoireToast)](#toasts-noiretoast)
- [Dialogs you await (NoireModal)](#dialogs-you-await-noiremodal)
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
- [The UI scale](#the-ui-scale)
- [Text at any size (NoireText)](#text-at-any-size-noiretext)
  - [Letter-spacing](#letter-spacing)
- [Drawing shapes (NoireShapes)](#drawing-shapes-noireshapes)
  - [Where it draws](#where-it-draws)
  - [Plates](#plates)
  - [Gradients over anything](#gradients-over-anything)
  - [Frames and corner ticks](#frames-and-corner-ticks)
  - [Arcs, rings and wedges](#arcs-rings-and-wedges)
  - [Pattern fills](#pattern-fills)
  - [Glows, clipping and sweeps](#glows-clipping-and-sweeps)
  - [Shapes NoireUI does not ship](#shapes-noireui-does-not-ship)

---

## Overview

`NoireLib.UI` is a set of ImGui UI helpers, built on a small shared foundation.

**Foundations**

- **`NoireUI`** - The hub. Owns the automatic-drawing policy, the element registry, the draw-thread queue (`RunOnDraw`), the frame clock, the UI scale, the reduced-motion switch and diagnostics.
- **`NoireLayout` / `NoireStyle`** - Containers and style scopes that take their body. No `using`, no `Dispose`, no `End()` anywhere, and an unbalanced raw-ImGui push inside a body is unwound at the boundary and logged once.
- **`NoireAnim`** - Time-based animation keyed by id: easing over twenty-one curves (or one of your own), springs, presence, pulses, sweeps and one-shots. Nothing to register or dispose.
- **`UiFrameState`** - Id-keyed transient state for immediate-mode helpers, typed per value and pruned automatically.
- **`NoireUiSession`** - The same widget memory for the life of the session only. No file, so any type may be stored and a generated widget id is safe to key on. Widgets choose between the two with `UiMemoryScope`.
- **`NoireUiState`** - The small amount of widget memory that survives a reload (a dragged position, a collapsed section). One JSON file; every `Persist` switch defaults off.
- **`UiDiagnostics`** - Live counts, recent faults, the fault ladder, and the stack-leak net. Answers "why did nothing draw".
- **`NoireTheme`** - One palette the whole library follows, plus the type scale. An unset token falls through to the ImGui style, so a plugin that never touches it looks unchanged; set an accent and every widget re-tints at once.
- **`NoireText`** - Text at any size without ImGui's resampled-atlas blur: a real font built at the size asked for, behind a four-step type scale the theme owns. Also draws text with matched characters picked out (`Highlighted`), for filter results.
- **`NoireShapes`** - The shapes a draw list does not have: gradients at any angle over any shape, notched and rounded plates, beveled edges, glows, hairline frames with corner ticks, arcs and wedges, and two pattern fills. Every one is drawn by three public calls over a public path, so a shape it does not ship is your own points through the same three.

**Widgets and elements**

- **`NoireButtons`** - The buttons ImGui does not ship: hold-to-confirm, an asynchronous button that disables and reports, a split button, an animated toggle, a segmented control and a spinner. Each takes a full `*Style` object and a custom-draw hook.
- **`NoireLayout`** (structures) - A draggable splitter, a collapsible section that can remember whether it was open, and a row that wraps.
- **`NoireToast` / `NoireToastArea`** - Real notifications: anchored, stacked, animated, actionable, with live progress, an undo pattern and a countdown that pauses on hover.
- **`NoireModal`** - Dialogs you `await`. Confirm, prompt and choice tiers, hold-to-confirm for destructive answers, and an optional "don't ask again".
- **`NoireOverlayButton`** - A standalone button overlayed on the game screen, drawn independently from any window. Anchorable anywhere (nine anchors, absolute pixels or screen ratio), with click/scroll callbacks, a hover mouse cursor, tooltips, a visibility condition evaluated on draw, per-state draw conditions (cutscene / gpose / hidden UI / always), drag-to-reposition, optional manual drawing, and full styling. Auto-disposed with NoireLib.
- **`NoireTagInput`** - A chips field for tags, filters and names. Pasted lists come apart on their separators, backspace takes the last chip back for editing, and every refusal comes back named rather than silent.
- **`NoireMultiCombo<T>`** - A dropdown that selects several things at once and does not close when you pick one. Tick-box options, a summarising preview, selection held by value so it survives the option list being replaced.
- **`NoireExcelPicker<TRow>`** - A searchable, icon-rich dropdown over any sheet of game data, in one line. Reads the sheet once on a background thread, builds its display names up front, and is a `NoireComboBox` underneath that stays fully reachable.
- **`NoireComboBox<T>`** - A combo box with an optional auto-focused filter input (pinned above the options or scrolling with them), arrow-key cycling of the highlighted option inside the dropdown, and an optional "hold a binding + mouse wheel" shortcut to cycle the selection on the closed combo (with or without looping). The shortcut is a `HotkeyBinding` matched with the same rules as a hotkey, and can be driven straight from the Hotkey Manager so the user can rebind it.
- **`NoireTooltip`** - A custom tooltip system independent from `ImGui.SetTooltip()`, with customizable background transparency (0% to 100%) and mixed inline content built from `NoireContent`.
- **`NoireContent`** - A reusable block of rich inline content (text, dynamic text, FontAwesome icons, images, keycaps, and any widget), flowing on lines with vertical centering. Rendered by `NoireTooltip`, and by anything of your own through its public `Draw()`.

---

## Two ways to reach every surface

Every surface above is also reachable under `NoireUI`, so completion branches instead of listing the whole library flat. Type `NoireUI.` to see what there is to draw, pick a surface, and its own members follow:

```csharp
NoireText.Draw("Ready", TextSize.Heading);          // the surface's own name
NoireUI.Text.Draw("Ready", TextSize.Heading);       // the same call, reached from the root

NoireShapes.Glow(min, max, colour);
NoireUI.Shapes.Glow(min, max, colour);
```

**Both names are the same feature, not two APIs.** The existing top-level names are fully supported, are not deprecated, and stay visible in completion. Nothing you have already written changes, and the two styles mix freely in one file.

The grouped name is the surface's own with the `Noire` prefix taken off, so it is guessable rather than memorised. Two surfaces would repeat the root and carry an explicit name instead:

| Surface | Reached as |
|---|---|
| `NoireText`, `NoireShapes`, `NoireLayout`, `NoirePanel`, `NoireStyle`, `NoireAnim`, `NoireButtons`, `NoireInputs`, `NoireSliders`, `NoireGauges`, `NoireBadge`, `NoireAttention`, `NoireFocus`, `NoireTooltip`, `NoireModal`, `NoireToast`, `NoireWindowChrome` | `NoireUI.Text`, `NoireUI.Shapes`, ... |
| `NoireUiState` | `NoireUI.State` |
| `NoireUiSession` | `NoireUI.Session` |

The grouped call takes every parameter the direct one takes, defaults identically, and carries the same documentation on hover. It is a shortcut, never a fence, and it costs nothing at draw time: each entry is a one-line forward that inlines away.

### Widgets you construct

Widgets you build and drive are reached from the same root, so browsing it finds a catalogue rather than half of one:

```csharp
var table = NoireUI.Table<Player>("roster", players);   // creation method
var table = new NoireTable<Player>("roster", players);  // direct construction, unchanged
```

`Table`, `TabBar`, `ComboBox`, `MultiCombo`, `ExcelPicker`, `TagInput`, `ReorderableList`, `Content`, `OverlayButton`, `WorldLabel` and `AddonAttach` all have one. Constructing any of them directly stays fully supported and is not deprecated: the creation method is an extra door, not a replacement one.

Some types deliberately have no creation method, because their system already has an entry point under the root and two front doors for one system is a worse answer than one: the modal host is reached through `NoireUI.Modal.Host`, toast areas through `NoireUI.Toast`, and the profiler window through `NoireUI.Profiler`.

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
NoireUI.ReducedMotion;                          // what is in effect right now
NoireUI.HostReducedMotion;                      // what Dalamud says the user asked for
NoireUI.HasReducedMotionOverride;               // whether a plugin has taken it over

NoireUI.ReducedMotion = config.ReducedMotion;   // take it over: eased values snap, decorative motion stops
NoireUI.ClearReducedMotion();                   // hand it back to the host

NoireUI.StringProvider = key => myLocalizer.GetOrNull(key);   // null falls back to the shipped English
```

**`ReducedMotion` follows Dalamud's own setting until a plugin assigns it.** It is an accessibility preference the user has already stated once, to the host, and a library that ignored it would have every plugin using it ask again.

Assigning takes it over for good, including assigning `false` — a plugin asking for full motion is an answer, not the absence of one. If you offer the choice in your own settings, offer the way back too, or a preference the user set once in Dalamud quietly becomes a second copy your plugin now owns. Persist the override only when it exists (`TryGet` rather than `Get` with a default), or every load writes your default over the host's answer.

NoireLib depends on no localization system and ships no locale files.

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

### Performance is a property of this code, not a task

Everything here runs on a game's draw thread and runs again next frame. Work that would be invisible anywhere else is not invisible here, so every widget in this namespace is held to the same bar, and anything added to it is too:

- **Nothing allocates per frame.** Ids are built once by `UiIds`, not interpolated per frame. No `ToString()` on an unchanged value, no `ToArray()`/`ToList()` in a draw path, no closure where a state overload exists.
- **Working sets are borrowed, not allocated.** A scratch buffer with a known maximum is `stackalloc`; one sized by the data is a `PooledBuffer<T>` or a list the surface keeps between frames.
- **A shorthand overload never costs more than the long one.** An overload taking loose arguments and forwarding them as an options or style object writes them into a reused instance instead of constructing one, because the shorthand is the overload most callers reach for and it draws every frame. See `NoireInputs.Number`, `NoireLayout.Splitter` and `NoireButtons.Segmented`.
- **Style is pushed through `UiPush`, not `ImRaii`.** Every `ImRaii` push wrapper is a class, so it costs 24 bytes per call even when its condition is false and it pushes nothing.
- **Nothing is recomputed that has not changed.** Text measurement is cached; so is layout arithmetic, against what actually moves it.
- **Nothing off screen is drawn.** Collections of unknown length virtualize past a threshold, with a `Virtualize` override.
- **Tessellation follows the radius.** A fixed segment count spends hundreds of points on a shape an inch across, each segment a fraction of a pixel: expensive and invisible.
- **Nothing loop-invariant sits inside a loop**, and expensive ornament is tessellated once and resubmitted rather than recomputed. Geometry, not a texture: ImGui builds vertex buffers the host renders at end of frame, so there is nothing to rasterize into. Cached geometry also survives tinting and rotation, which pixels would not.
- **Literals handed to ImGui are UTF-8** (`"Save"u8`), encoded once at compile time instead of re-encoded from UTF-16 every frame.

Measure before optimizing, with the profiler below, and read the **self** column rather than the total. A scope is not free to open, so a surface entered hundreds of times a frame carries more instrumentation in its reading than one entered once; compare like with like, and prefer allocated bytes, which the act of measuring does not move.

**This bar is enforced, not merely stated.** Every drawing surface in this namespace has a test that runs it inside a real ImGui frame and asserts what it allocated, and the assertion is zero: widgets, layout scopes, panels, shapes, text, content, badges and attention alike. That is what makes the guarantee a consumer's rather than a reviewer's, because none of the causes found so far were visible by reading the widget. Two were lambdas capturing a parameter, which the compiler allocates on entry to the method rather than at the point of use, so a hook nobody had set cost every button in the frame. One was a shorthand overload composing the options object it forwarded. One was a value formatted to a string that had not changed, and one an id interpolated on every frame of a section whose id is a constant. A new surface is not finished until it has its own zero, and a surface that cannot be driven headless, because it is a drawable needing an initialized plugin, has its ids asserted against the literal they replaced instead.

### Allocation

A body lambda allocates one delegate per call per frame (a few dozen bytes, invisible in most UIs). Where it matters, every container has a state overload that keeps the body `static` and allocates nothing:

```csharp
NoireLayout.Section("Filters", this, static (self) => self.DrawFilters());
```

The widgets themselves allocate nothing per frame for their ids. An id like `###NoireComboItem_myCombo_42` is a constant for the life of the widget, so it is built once and handed back on every later frame rather than re-interpolated: a two hundred row list at sixty frames a second would otherwise produce twelve thousand short-lived strings a second in the one place a plugin cannot afford a collection.

Text measurements are cached the same way, keyed on everything that can change the answer (the text, the size, the ambient font, the UI scale, and the font generation). A label that has not changed is not re-measured, which matters because measuring walks the string a glyph at a time after marshalling it to UTF-8.

So is the text a value reads as. A slider's number, a duration field's `1m30s` and a colour field's `#RRGGBB` are all written from a value that moves when the user drags something or when a second ticks over, and never sixty times a second, so the frames in between were spending a string to arrive at the text already on screen. Splitting a label is cached for the same reason: ImGui packs the text shown and the id into one string, and a field carrying a stable id (`Interval###interval`, so the state survives the label being reworded) was taken apart into two substrings on every frame it drew.

Pushing an ImGui colour, style variable or font goes through `UiPush` rather than Dalamud's `ImRaii`. The two read almost the same at a call site, and the difference is that `ImRaii`'s wrappers are classes: each push is 24 bytes on the draw thread, on every frame that draws, and it costs them even when the condition it was handed is false and it pushes nothing at all. `UiPush` is a `ref struct` over the raw `ImGui.PushStyleColor` family, so it costs nothing and cannot be boxed into costing something. Push one thing with `UiPush.Color(slot, colour)`, or accumulate several into one scope and dispose it once. Colours, style variables, fonts, disabled scopes and text wrap positions are all covered. The Begin-style `ImRaii.Child`, `ImRaii.Table`, `ImRaii.Tooltip` and `ImRaii.Combo` are structs already and are still used as they were; so is `ImRaii.PushIndent`, whose scaled overload multiplies by the global scale and so is not the same call as a raw `ImGui.Indent`.

A surface that needs somewhere to gather things while it draws does not allocate one. Where the maximum is a constant, that is `stackalloc`, which is how the shape and path code builds its point buffers. Where the size comes from the data - the segments on a line of content, the keys due to be dropped from a cache - it is a `PooledBuffer<T>`, borrowed from the runtime's array pool and given back when it leaves scope. A surface that draws every frame for its whole life, such as a toast stack, keeps its lists instead and clears them where it fills them, which costs nothing at all after the first frame.

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
var turn = NoireAnim.Spin(secondsPerTurn: 240f);        // turns, for anything that rotates

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

## Session-only memory (NoireUiSession)

The same idea as `NoireUiState`, with the file taken away: nothing is written to disk, and everything is gone on reload.

```csharp
NoireUiSession.Set("myplugin.roster.search", search);
var search = NoireUiSession.Get("myplugin.roster.search", string.Empty);
```

For the state that is worth keeping while someone works and worth forgetting afterwards: a search a window was left narrowed to, which tab was open, a panel scrolled halfway. Persisting those is worse than not — a plugin that reopens three days later still filtered to something the user has forgotten typing looks broken rather than helpful.

Two differences follow from there being no file, and both are in this store's favour:

- **Any type may be stored**, including ones that do not serialize. Values are held as they are rather than round-tripped through JSON, so a reference type comes back as the same instance.
- **A generated widget id is safe to key on.** A GUID id is a new one every session, which is exactly why `NoireUiState` refuses it, and exactly why it does not matter here: this store's lifetime is that session too, so the key and the value expire together.

`Get` / `TryGet` / `Set` / `Remove` / `RemoveAll(prefix)` / `Clear` / `Count` / `GetKeys` mirror `NoireUiState`. A value stored under a key as one type reads as absent rather than throwing when something asks for it as another, so one widget's mistake about a key cannot take another widget down.

Widgets that can remember something take a `UiMemoryScope` (`None`, `Session`, `Persisted`) rather than a pair of booleans, because those are three positions on one axis: a widget cannot meaningfully persist something it is also told to forget.

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

### Profiling

What each part of the interface costs to build, per frame, by name. Off by default and free when off, so the instrumentation stays in place rather than being added when you go looking.

```csharp
NoireUI.Profiler.Enabled = true;

foreach (var entry in NoireUI.Profiler.Snapshot())   // most expensive first
    log($"{entry.Name}: {entry.AverageMs:0.000} ms over {entry.Calls} call(s), peak {entry.PeakMs:0.000}");

NoireUI.Profile("inventory grid", () => DrawInventoryGrid());   // your own code, same list
```

Every drawing surface the library ships measures itself, so switching this on attributes the frame without any work at the call sites. That is structural rather than a matter of care: a surface inside NoireUI cannot obtain a draw list without opening a scope at the same time, and an analyzer refuses to compile one that tries. Read the **average**: a single frame competing with a texture upload is noise. The **peak** is a high-water mark that only `Reset()` clears.

**Measuring starts at widget resolution, and `Detailed` opens the per-method rows.** The everyday question is which widget is the expensive one, and the widget and surface rows answer it for a few microseconds a frame. Setting `NoireUI.Profiler.Detailed = true` (the **Detail** checkbox in the window) additionally measures every drawing helper as a row of its own, `NoireShapes.Glow` and the like, which is the resolution to switch on once a widget's row has named the suspect. Those fine rows are most of what measuring costs: a decorated window opens a scope per shape it paints, several hundred a frame against a few dozen coarse ones. Nothing goes missing while it is off; an unmeasured method scope folds into the widget or surface around it, so the totals stay complete either way.

**Scopes nest, so each is reported twice over.** *Total* includes everything measured inside a scope; *self* does not. Self is the one that adds up — totalling the total column counts a widget once for itself and again for every scope enclosing it, which is how an interface comes out looking several times its real cost. `TotalAverageMs` sums self time for exactly that reason.

The figure your host reports for the whole plugin will always be larger than this total: it covers the windowing and the ImGui work around anything instrumented here. Use this to compare parts of your interface against each other, not to reconcile with the host.

**Allocated bytes are sampled separately, through `TrackAllocations`.** The byte columns and `TotalAverageBytes` read zero until you switch it on, next to the timing rather than with it, because reading the allocation counter costs more per scope than timing the scope does and a busy interface opens several hundred scopes a frame. Switch it on when the question is whether a change allocates, which is the number that is identical on every machine; leave it off when the question is milliseconds. A scope open across the change reports no bytes for that one scope, since a difference needs both of its ends.

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

A sortable, searchable table of every scope with its calls, last, longest and average, plus **Reset all** and **Copy all** (tab separated, in the order shown, so it pastes into a spreadsheet or an issue). `DrawContents()` is public if you would rather put it on a page of your own settings than in a window.

**Right-click a row to leave that scope out of the totals.** The row turns red, and `TotalAverageMs`, `TotalAverageBytes` and the window's own totals line stop counting it; right-click again, or use **Include all**, to put it back. This is for the cost you have decided is not part of what you are measuring: the profiler window itself, a debug overlay, a page you already know about. The scope keeps being measured and keeps reporting its own figures, so its row still says what it costs.

One node, not a branch: the totals are sums of self time, so a mark removes exactly the figure its own row shows, and excluding a whole branch means marking the rows in it. Marks live on the nodes, so `Reset()` forgets them along with the measurements; `ClearExclusions()` lifts every mark without discarding anything. The same is reachable in code through `SetExcluded(id, excluded)`, `ToggleExcluded(id)`, `IsExcluded(id)` and `ExcludedCount`.

Totals count nested scopes twice over, since a widget measured inside a page that is also measured appears in both. They are a scale to read the rows against, not a frame time.

This measures the time spent building the draw data on the draw thread, which is the part a plugin controls and the part an optimization pass moves. It is not the GPU cost of drawing the result.

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

**The divider is resolved from where the pointer is, never from how far it moved.** A mouse delta that the clamp throws away is a delta the size never received, so accumulating deltas leaves the divider ahead of the pointer by everything discarded — push past the minimum, come back, and it starts moving while the cursor is still nowhere near it, for the rest of the drag. Reading the position instead makes overshooting free: the divider sits at the bound until the pointer comes back past it, and is never anywhere but under the cursor. The offset between the two is taken once when the drag starts, so grabbing the divider off-centre does not snap it. (Same rule as `NoireSliders`, and the arithmetic is a tested pure method for the same reason: a drag is the one thing that cannot demonstrate it.)

**A `SplitterOptions` overload opens the look up**, including a `CustomDraw` hook. The grab area and the divider are separate there: `Thickness` is how much of the pointer's path counts as the handle, `LineWidth` is the hairline drawn down the middle of it, and those are rarely the same number.

```csharp
NoireLayout.Splitter("split", ref paneWidth, new SplitterOptions
{
    MinSize = 120f, MaxSize = 420f, Length = paneHeight,
    Thickness = 9f,                        // comfortable to grab
    CustomDraw = static _ => { },          // and invisible: this design draws its own divider
});
```

That empty hook is the point of it. A design that already paints a rule between two panes wants the handle without a second line on top of it, and the splitter still owns the drag, the cursor and the clamping whatever the hook draws — so an existing divider becomes draggable with nothing about it changing. `UiSplitterDraw.DrawLine()` gives you the shipped line back when the hook only means to add to it.

**Put the handle in the same window as the panes' edge, not over it.** ImGui hit tests windows before items, and a child region is a window: a handle placed across the seam from the parent is underneath whichever region the pointer is over and never gets the click. Draw it inside one of the two regions, along its edge.

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

`NoireLayout.ContentWidth()` is that same answer on its own, and is what any widget defaulting to "the space available" should ask instead of `GetContentRegionAvail()`. The difference only shows up inside a page that centres its content in a narrower column: the content region still reports the window's edge, so a field sized from it runs past the end of everything around it.

---

## Framed containers (NoirePanel)

`NoireShapes` paints a box between two points, which is only useful once something knows where those points are. `NoirePanel` is what knows: it runs the body, measures what it came to, and paints the chrome behind it.

```csharp
NoirePanel.Frame(() =>
{
    NoireText.Draw("Selected index");
    NoireText.Draw("14");
},
new FrameStyle { TickLength = 11f, TickColor = gold });
```

`Frame` draws a `FrameStyle` border; `Plate` draws a `PlateStyle` fill. Both take their body, so there is nothing to close, and a body that throws still leaves the draw list balanced.

**The chrome is drawn after the body and appears behind it.** That is the whole mechanism, and it is why the alternative every plugin writes by hand does not work: you cannot paint a box before you know how tall it is, and drawing it from the height the same content happened to have *last* frame lags by a frame the moment anything inside animates. The panel splits the window's draw list into a chrome channel and a content channel, so the two can be drawn in one order and composited in the other.

**Nested panels do not split again, and must not.** A draw list can only carry one split at a time. They do not need to: chrome from every depth shares one channel and content shares the other, so an inner panel's chrome lands on top of its parent's chrome and still behind all content, which is the order the nesting means. The split is tracked per draw list rather than as a depth count, because a body may open a child window and a child window draws to a list of its own.

**A panel is a fixed-width box**, filling the width available unless `PanelOptions.Width` says otherwise. A stack of panels that each ended wherever their own longest line did would read as ragged rather than as a column. The body is told the width it has, because nothing else can tell it: ImGui's content region reports the *window's* right edge however deeply anything is nested.

`PanelOptions.Header` puts a tracked label and a hairline across the top. Its height is rounded up to a whole pixel, for the reason under [toasts](#toasts-noiretoast): a box placed from a fractional height walks across a pixel while its contents animate.

---

## Windows you draw yourself (NoireWindowChrome)

ImGui's window decoration is drawn from its style and cannot be replaced, so a design whose window edge is part of the design has nowhere to go. Taking the decoration away is easy; what is not is everything that stops working once you do.

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

**Always on top is one call.** `KeepInFront()`, from inside the window, once per frame.

```csharp
public override void Draw()
{
    if (alwaysOnTop)
        NoireWindowChrome.KeepInFront();

    // ... the window
}
```

It covers both halves of what "in front" means, because either alone is visibly wrong. **Clicks** follow the display list, which it moves the window to the front of. **Drawing** follows the draw layer first, and the display list is reordered whenever a window is focused — after every plugin has drawn and before the frame is rendered, so a window holding its place by the display list alone is drawn *behind* for one frame every time an overlapping window is clicked, and no plugin code runs late enough to undo it. So it lifts the window into the top draw layer as well.

It does that by setting the layer's flag on the window **after** it has been begun. The layer is read at render time, so the flag counts for that frame while none of what the same flag does inside `Begin` happens at all: the window is not moved to the cursor, its background and border keep reading the fields an ordinary window reads, and its default item width is unchanged. There is nothing to pass at `PreDraw`.

The layer covers the whole of the ordinary one, so anything a window opens over itself has to join it. Every NoireUI popup does that on its own, by inheriting the layer of the window that opened it. A popup of your own calls `KeepInFront()` from inside itself — which is also what settles the order between the two, since among the windows in front the last caller each frame wins.

**Four flag sets, because the trade-offs are real ones.** `Flags` is the default: no title bar, background, border or scrollbar, and ImGui keeps the drag, so the window can be picked up anywhere it is not already busy. `FixedFlags` adds no-resize. `HandleOnlyFlags` adds `NoMove` for a design whose empty space is not spare, with `DragFrom` naming the handle. `FixedBodyFlags` stops the window scrolling as a whole, for one whose masthead and rail stay put while a region inside it scrolls.

`DragFrom` holds the drag by window id, so it survives the pointer outrunning the handle. `ChromeButton` draws the window's own buttons — `ChromeGlyph.Close`, `Minimize`, `Restore`, `Menu` — from strokes rather than an icon font, so chrome does not depend on FontAwesome being in the atlas. One `ChromeButtonStyle` covers every glyph, so a row of them cannot drift apart in size, weight or hover behaviour; a bare mark floating in a corner reads as debris, so each carries a plate that lights on hover.

**What a custom window gives up:** Dalamud's pin, clickthrough and background-blur controls live on its title bar, and a window with `NoTitleBar` does not get one. They are Dalamud's own state rather than the plugin's, so they cannot be redrawn from a menu of your own. Offer what the window genuinely owns instead — opacity, collapse, close.

**`WindowChromeStyle.Opacity` fades the surface, not the contents.** It scales the plate's fill alpha; the text, the border and the controls stay at full strength. Pushing ImGui's global alpha instead fades everything, which is not a translucent window but a dim one — what a window wants to see through is its background. Anything a window paints behind its content itself (a masthead, a hero) should be scaled by the same number.

**`DragFrom` refuses unless the window carries `NoMove`.** It is the *replacement* for ImGui's drag, not an addition to it: with both running, the movement is applied twice per frame from two different reference points, and the contents visibly swim and lag behind the frame as it is dragged. Use `HandleOnlyFlags` when you want a handle.

**A design that states its own margins wants to be the only thing stating them.** ImGui puts `ItemSpacing.Y` between every two items, which is added to each margin you place: a stated gap of 22 arrives as 22 plus two lots of spacing, and the page reads as falling apart. Push `ItemSpacing` to zero for the window.

**The scrollbar goes and the wheel stays.** A design painting its own edges cannot afford ImGui's scrollbar down the inside of one, but a window that silently refuses the wheel is broken rather than clean.

**The chrome is painted at the window rectangle; the body is advanced from wherever the cursor already is.** Those are the same thing on the first frame and stop being so the moment the window scrolls, which is the trap: setting the body to an absolute position pins the contents and the wheel appears to do nothing, while the border needs to stay with the window rather than scrolling away.

`PushWindowStyle` zeroes ImGui's own window padding and border, which would otherwise sit between the window's edge and the chrome's padding and put every measurement out by it. It is not a window class: creating and registering windows is Dalamud's job, and wrapping it would take away the `Window` surface a plugin already knows.

---

## Sliders (NoireSliders)

ImGui's slider is drawn from its own style and cannot be replaced: a track and a rectangular grab, coloured from four style entries and no more. A design that wants a hairline track with a lit diamond running along it has no way to ask for one. So the drawing is the library's, through the same style-plus-hook shape as buttons and toggles.

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

`SliderGrab` ships Square, Rounded, Circle and Diamond; `SliderStyle.CustomDraw` replaces the painting entirely, with the widget keeping the sizing, the hit testing, the dragging and the value.

**The value is read from where the pointer is, never from how far it moved.** A drag driven by mouse delta accumulates a drift away from the cursor over a long gesture, and a click on the track then jumps by that drift rather than to where it was aimed. Reading the position outright makes a click and a drag the same operation and leaves nothing to accumulate. `ResolveValue` and `ResolveFraction` are the two halves, and the test that matters checks they agree: the handle has to end up under the pointer that put it there.

The label column matches [`NoireInputs`](#settings-fields-noireinputs), so a slider between two number fields lines up with them.

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

**A toast leaving does not move the ones that stay.** The stack is laid out from whichever of its edges is pinned to the screen, not from the window's own top. The window is sized to the stack every frame, so on a bottom-anchored area (the default) the bottom edge is the fixed one and the top edge moves as toasts arrive, leave and shrink. From the pinned edge, a toast's position depends only on the toasts between it and that edge — so a leaving toast disturbs the ones further from the anchor and nothing else, and since toasts expire oldest first and the oldest sits furthest from the anchor, the usual case moves nothing at all.

**A leaving toast holds still while it goes.** Its slot closes toward the edge the stack hangs from, so the toast is painted from that same edge and cropped to the slot: it looks covered rather than squashed, and it does not drift across the screen while the slot shrinks around it. Painting from the opposite edge is the difference between a toast fading out where it stands and one sliding as it goes.

**The stack's height is rounded up to a whole pixel, and that is load-bearing.** A bottom-anchored window is placed at *(fixed screen edge − its own height)*, so the edge the stack hangs from is recovered as *(placed position + height)* and the two are meant to cancel. They only cancel while the height is a whole number. Window positions are snapped to the pixel grid, and snapping a value shifted by a whole number of pixels shifts the result by that same whole number — but a fractional height leaves its fraction behind:

```
bottom = snap(C − total) + total  =  C − frac(C − total)
```

`total` sweeps continuously while a toast arrives or leaves, so `frac` sweeps 0→1 over and over and the anchored edge sawtooths across a pixel. Every remaining toast hangs off that edge, so the whole stack wanders for exactly as long as the animation runs. Rounding the height makes the snap a no-op and the edge exactly constant. `NoireToastAreaTests` pins it both ways: invariant with the rounding, demonstrably not without it.

**Every slot and gap is on the pixel grid too**, which closes the other half of the same problem. ImGui floors the cursor onto the grid after each item it lays out, so the height a block of content *measures* depends on the fraction of a pixel it started at — the same toast measures a pixel taller or shorter depending where it sits. That measurement becomes the next frame's slot, and each slot shifts every toast further from the anchor, so on fractional boundaries every toast nudges its neighbours' measurements about. The wobble that produces grows with distance from the anchor, because that is how far the error has had to accumulate: the toasts nearest the anchor look fine while the ones at the far end visibly shake. Kept on the grid, a toast always measures the same height and nothing propagates. The two roundings compose — once the slots are whole, the total already is, so the stack height adds no slack and the anchored edge stays exactly where it was.

A leaving toast's measured height is frozen for the same reason: its contents are drawn clipped to a closing slot and offset by the slide, and re-measuring under either would feed back into the share of the stack computed from it.

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

`UiPosition` is used by the overlay button and describes a screen position in one of four modes:

```csharp
// 1. One of the nine screen anchors, plus an optional pixel offset:
UiPosition.AtAnchor(UiAnchor.BottomCenter, new Vector2(0f, -40f));

// 2. Absolute pixels, relative to the top left corner of the game window:
UiPosition.AtAbsolute(100f, 250f);

// 3. Screen ratio: 10% from the left, 10% from the top:
UiPosition.AtRatio(0.1f, 0.1f);

// 4. A corner of a native game window, followed as the player moves or rescales it:
UiPosition.AtAddon("_PartyList", UiAnchor.TopRight);

// ...or alongside one rather than over it:
UiPosition.NextToAddon("_PartyList", UiSide.Right, UiAlign.Start);
```

Options:

```csharp
UiPosition.AtRatio(0.5f, 0.5f)
    .WithPivot(new Vector2(0.5f, 0.5f)) // Which point of the element is pinned (default: the anchor point in Anchor mode, the top left corner otherwise)
    .WithOffset(new Vector2(0f, 10f))
    .WithClampToViewport(false);        // Clamping is enabled by default: the element always stays fully on screen
```

In `Anchor` mode the pivot is automatic and intuitive: `BottomRight` pins the bottom right corner of the element to the bottom right corner of the screen, `MiddleCenter` centers it, etc. `Addon` mode reads the same way against the game window's own rectangle instead of the screen's.

### Targets that may not be there

`Addon` mode is the only mode that can fail to resolve, because the game window may not be on screen. `TryResolve` says so, and returning `false` is the signal to draw nothing:

```csharp
if (position.TryResolve(size, out var topLeft))
    ImGui.SetNextWindowPos(topLeft);
```

That is the whole of "a button that exists only while the Duty Finder is open": name the addon, and skip the frame when it is not there. The overlay button already does this, so `button.Position = UiPosition.AtAddon("ContentsFinder")` is enough on its own.

`Resolve` always answers, falling back to the equivalent screen anchor when the game window is missing, and stays the right call where hiding would be worse than being in the wrong place (the toast area, for one).

Every overload takes an optional source of rectangles, so a position can be resolved against something other than the live game:

```csharp
position.TryResolve(size, viewportPos, viewportSize, name => myRects[name], out var topLeft);
```

`UiAddon` is the live source, and is public for its own sake:

```csharp
if (UiAddon.TryGetRect("_PartyList", out var rect))
    // rect.Position is relative to the game window, rect.Size is in real pixels
```

---

## Pinning a window to the game (NoireAddonAttach)

Docks one of your windows to a native game window. It writes the window's own position rather than drawing anything, so it composes with whatever the window already does.

```csharp
new NoireAddonAttach(myWindow, "_PartyList", UiSide.Right) { Gap = 8f };
```

That is the whole setup. The attachment registers itself and applies every frame.

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
| `PositionOverride` | none | A `UiPosition` used instead of the side/align/gap trio. |
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

Matching is written as `SizeConstraints`, not as `Size`, and that is what makes the axes independent. A `Size` is both axes at once: matching only the width would still have to write *some* height, and the only height available to write is the one it last wrote. That pins the axis nobody asked to match to itself, forever — a feedback loop rather than a setting. A minimum and maximum can speak per axis, so a matched axis is the two meeting and a free axis spans nothing to everything, which is the same "no constraint" Dalamud writes itself.

### Taking the window, and giving it back

The attachment holds a window's position and size constraints only while it is actually placing it. Turn `Enabled` off, turn both `MatchWidth` and `MatchHeight` off, or let the game window go off screen, and whatever was taken over is handed back exactly as it was found. Nothing moves on release: Dalamud only applies a position or size that is set at all, so giving them back simply stops them being reasserted.

This matters because Dalamud reapplies a position and a set of constraints every single frame they are set, the position with `ImGuiCond.Always`. An attachment that merely *stopped writing* would leave the window frozen wherever it was last put, undraggable and unresizable for the rest of the session, with nothing on screen to say why.

### Visibility is decided before the frame, not during it

`FollowVisibility` is applied on the framework tick rather than from `Apply`. Dalamud tests whether a window is open, then calls its `PreDraw` and draws it, in that order and in one pass — so a window closed from `PreDraw` has already been let through the test and draws once anyway. Deciding a tick earlier is what makes a window that cannot be shown never appear at all, rather than flashing up for a single frame.

That leaves one thing for the caller. Nothing can intercept `window.IsOpen = true`, so opening a window whose game window is absent still closes it again immediately, which looks like the button doing nothing. Ask first:

```csharp
if (attach.FollowVisibility && !attach.IsAddonVisible)
    NoireToast.Error($"{attach.AddonName} is not on screen.");
else
    myWindow.IsOpen = true;
```

### Keeping up with a drag

Apply it from the window's own `PreDraw` when it has to track a game window being dragged:

```csharp
public override void PreDraw() => attach.Apply();
```

Dalamud applies a window's position immediately after `PreDraw` returns, so this is frame-for-frame. The automatic pass runs elsewhere in the frame and can land one frame behind, which shows up only during a drag.

---

## Labels in the world (NoireWorldLabel)

A label pinned to a place rather than to the screen, projected every frame.

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

Every one of these belongs to the label it is set on. There is no page-wide or global equivalent: a plugin marking three different things wants three different markers, and they are configured one at a time.

A world label draws itself: it starts at an explicit `AutoDraw = true`, because a label pinned to the world has no place inside one of your windows to be drawn from. Set `AutoDraw = false` and call `Draw()` yourself to place it in your own draw order, or `null` to follow the `NoireUI.AutoDraw` master default. See [Automatic drawing](#automatic-drawing).

Two things about it are not adjustable, because getting either wrong is worse than any setting:

- **What it follows is read on the framework thread** and reduced to a position there. A game object can be freed between the frame that found it and the frame that draws it, and reading one from the draw thread is an access violation rather than a wrong number.
- **The label takes no input at all** until `OnClick` or `Tooltip` is set. Something drawn over the world that silently eats clicks is indistinguishable from a broken game.

**Off screen, only the direction is read.** The game's `WorldToScreen` divides by the absolute value of the clip-space w, so a point behind the camera comes back already reflected through the centre of the screen: the direction from the centre is the true one and stays continuous as a point crosses the camera plane, while the distance means nothing. A label that has left the view is therefore cast out from the centre along that direction until it meets the edge, and pinned there by its centre. Clamping the projected point cannot do this job, because a point behind the camera routinely projects *inside* the viewport (something exactly behind you lands on the centre) and clamping leaves it exactly where it was. The arrow follows the same direction outward, so a marker for something behind you points off the bottom of the screen rather than back into the middle of it.

**Two ways to shrink.** `Perspective` is the reference distance over the real one, which is how the world itself shrinks: authored size at `ScaleReferenceDistance`, half of it at twice that. `Ramp` shrinks evenly between `ShrinkFromDistance` and `ShrinkToDistance`, which is the same pair of numbers as the distance fade and answers "where does it stop shrinking" outright. Both clamp to `MinScale` and `MaxScale`. `BaseScale` multiplies on top of whichever you pick, and applies just as well with `ScaleWithDistance` off, so it is the knob for a label that is simply bigger than the rest.

**Scaling is stepped, so the text stays sharp.** `NoireText` draws a size by building a real font at it; a label scaled smoothly would want one per pixel of distance, and each is a full glyph atlas out of a deliberately small cache. `ScaleStep` rounds the distance part of the scale so the whole range costs a handful of sizes, every one of them rasterized rather than resampled. Set it to zero for a smooth ramp and accept the stretch. `BaseScale` multiplies after the stepping, so it stays a free-form number without adding a size per value it could take in between. The body draws inside one `NoireText` scope, so a `Renderer` or `Content` body picks the size up too.

**`AlwaysOnTop` moves two orders, not one.** ImGui decides what is drawn in front and what receives the mouse separately, and promoting only the first gives you a marker plainly visible above a window and completely dead under it. This moves both, so a label that takes input stays clickable where it overlaps a window. It is off by default: a marker that covers the window you are trying to read is worse than one behind it, and a world label appears wherever the world puts it.

`UiWorldProjection` holds the arithmetic (distance fade and scale, scale stepping, the off-screen direction, edge pinning, arrow geometry) and is public, so a marker NoireUI does not ship can be built on the same pieces.

---

## Gauges and sparklines (NoireGauges)

Small readouts that show a number as a shape. Immediate and stateless: each one draws at the cursor, reserves what it used, and remembers nothing, so it drops into a row, a table cell, a button or a world label without either side knowing about the other.

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

Every gauge takes a fraction from 0 to 1 and clamps it, so no caller has to guard the edges.

**Thresholds** apply at or below their value, and the lowest matching one wins. That reads the way the things being measured read: under a quarter is critical, under a half is a warning, above is fine. A gauge counting the other way reaches the same result with the values inverted.

**Countdowns empty rather than fill.** Filling reads as progress toward something; time being spent is the opposite of that. `Timer` takes a `TimeSpan` pair and labels itself through `DurationHelper.Format` unless the style already carries a label.

**Sparkline bounds default to the data**, which is the wrong default for anything you plan to compare: left to itself, every trace fills its own box, so a flat line and a violent one draw the same picture. Pin `Min` and `Max` when two sparklines sit near each other. A flat series and an empty one both get a usable range rather than a division by zero.

Rings are drawn from `NoireShapes.Wedge`, so an open dial is two settings away:

```csharp
new RingStyle { StartTurns = 0.625f, SweepTurns = 0.75f }   // the speedometer sweep
```

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

With `FilterEnabled = true`, the dropdown shows a text input at the top, **automatically focused when the dropdown opens**.

**Typing filters fuzzily by default**: the characters need only appear in order, so `dkn` finds `Dark Knight`, and the options are reordered so the best match leads. The characters that matched are picked out in the accent, which is what makes the reordering something a user can account for rather than something that looks like guessing. The scorer is [`FuzzyMatcher`](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/Fuzzy/README.md).

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

`FilterPredicate` takes the decision over completely, **including from `FilterFuzzy`**: a predicate answers yes or no and has no score to order by, so the options keep the order they were given and nothing is highlighted.

`FilterText` is public: read it to show or save what the user narrowed the list to, set it to put one back. Setting it rebuilds the matches immediately.

### Keeping the search

`ClearFilterOnOpen = false` keeps the search between openings of one live combo. `FilterMemory` is the stronger form, surviving the widget itself:

```csharp
combo.FilterMemory = UiMemoryScope.Session;     // until the plugin reloads
combo.FilterMemory = UiMemoryScope.Persisted;   // across reloads; needs a stable id
combo.WheelCycleFiltered = true;                // the wheel then walks only what the search matches
```

Anything but `None` implies `ClearFilterOnOpen` being off, because a search restored and then cleared on the first opening would have been restored for nothing.

`Session` uses [`NoireUiSession`](#session-only-memory-noireuisession) and needs **no** stable id, since the memory expires with the generated id it is keyed on. `Persisted` uses `NoireUiState` and **does**: a combo constructed without an id gets a fresh GUID every session, so nothing keyed on it could ever be read back, and it refuses with a single log line rather than filling the state file with entries nothing will restore. `HasGeneratedId` tells you which kind you have.

`WheelCycleFiltered` is the pairing that makes a remembered search worth having: narrow a long list once, then wheel through those few entries on the closed combo without opening it again. With nothing typed it changes nothing, since everything matches. When the current selection is not itself a match, cycling enters the matches at one end rather than refusing, so the shortcut always goes somewhere.

The filter is **pinned above the options** by default, so it stays put while they scroll. Set `FilterPinned = false` to let it scroll away with them:

```csharp
combo.FilterPinned = true;   // Default: only the option list scrolls
combo.FilterPinned = false;  // The whole dropdown scrolls, filter included
```

The dropdown shows **exactly one scrollbar**, in either mode, and **none at all while every option fits**.

That second half is a **measurement, not a calculation**, and the distinction took five attempts to arrive at. ImGui decides the scrollbar with `ContentSize + WindowPadding * 2 > SizeFull.y`, and floors `SizeFull` whenever a size constraint is present at all — which for a combo popup is always, since `BeginCombo` sets its own when you set none. So a height worked out in advance has to come out equal to a value that is then rounded down, and every fraction anywhere in the layout — the padding, the row step, the filter row, the spacing — is a scrollbar over a list that fits. Each correction to that arithmetic fixed one configuration and left another.

So the dropdown and its option list both **record what ImGui reported needing** and ask for it back, rounded up, as a size *minimum* on the next frame. That is the same quantity the scrollbar test uses, so it cannot disagree with it, whatever the layout turns out to contain. The cap on `VisibleItemCount` still applies when the list is genuinely longer than the dropdown, which is the one case where a pixel of error is invisible because a scrollbar belongs there.

**The general lesson, if you are sizing anything against ImGui's own measurement:** do not predict the number, read it back. A constraint that has to exactly equal what the framework measured is a constraint that will be wrong at some UI scale you are not developing at.

### Long lists

Past `VirtualizeThreshold` options (100 by default) the list draws through a clipper, so only the rows on screen cost anything. A dropdown over every item in the game is forty thousand rows, and drawing all of them every frame to show fifteen is what makes a picker unusable.

```csharp
combo.Virtualize = null;          // Default: on past the threshold
combo.VirtualizeThreshold = 100;  // Default
combo.Virtualize = false;         // Force off, for rows of genuinely varying height
```

It requires **every row to be the same height**, because rows are positioned arithmetically rather than by measuring them. That is free for ordinary text options; a renderer drawing something taller declares it through `ItemHeight`. A row that does not match its declared height slides out of step with the scrollbar as the list scrolls.

Arrow-key navigation keeps working through a virtualized list: the highlighted row is force-included in the drawn range, so the list still scrolls to follow it once it leaves the visible window.

### Custom rows

`ItemRenderer` paints an option yourself. The combo keeps everything about the row that is not paint: its size, its hit testing, its selection and keyboard state, its filtering and its scrolling.

```csharp
combo.ItemHeight = 22f;                       // Logical pixels; needed once rows are taller than a line
combo.ItemRenderer = option =>
{
    DrawIcon(option.Item);
    ImGui.SameLine();
    option.DrawLabel();                       // The combo's own text, filter highlighting included
};
```

`UiComboItemDraw<T>` carries the item, its index, its display text, and whether it is selected or arrow-key highlighted. Call `DrawLabel()` rather than reimplementing the label, or the filter highlighting silently stops applying to your rows. An exception thrown in a renderer is caught and logged rather than taking the frame down.

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

ImGui draws a combo's box from its own style, which is one flat colour and a rounding, so a ramped surface, a chamfered corner or a bevel is not reachable. `BoxStyle` hands the box to `NoireShapes` instead. The frame background **and its border** are pushed transparent: ImGui draws the border rounded from its own style, so leaving it lit puts a rounded outline around a square plate.

```csharp
combo.BoxStyle = new PlateStyle
{
    Fill = ink, FillTo = inkDeep, FillAxis = GradientAxis.Vertical,
    BorderColor = gold, BorderSize = 1f,
};
combo.BoxArrowColor = gold;
```

`PopupStyle` does the same for the dropdown, and the two belong together: restyling the closed box and leaving the dropdown as ImGui's grey popup is worse than restyling neither, because the two are seen one after the other and the mismatch reads as the styling having failed. It carries the surface, border, rounding, padding, row padding and spacing, hovered and selected row colours, text, and the scrollbar — pushed as ImGui style around the popup, so the filter box and the scrollbar follow it without the combo drawing either.

The plate is drawn first and ImGui's own frame is pushed transparent over it, so the preview text, the hit box, the popup, the filter and the keyboard all keep working exactly as they did. The rectangle is worked out before the combo is submitted rather than read back afterwards, because a plate drawn after the combo would cover its own preview text; `ImGui.CalcItemWidth()` is the width ImGui itself is about to use, so the two cannot disagree.

The arrow becomes NoireUI's too, because ImGui draws its own in the text colour and a gold chevron beside ivory text is not reachable from one colour. `BoxArrowColor` sets it; `ImGuiComboFlags.NoArrowButton` removes it.

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

A hint tooltip (drawn with `NoireTooltip`) is shown automatically when hovering the combo: each key of the shortcut as a keycap, then "+ Scroll", or "Scroll to cycle" when no binding has to be held. It is generated from the binding actually in effect, so it follows a rebinding on its own. Keycaps and text rather than mouse and arrow glyphs, which come from the icon font and are the one part of a hint a consumer's own styling cannot reach; one cap per key, since "Ctrl + G" in a single tile reads as a key called "Ctrl + G". `WheelCycleHintContent` still takes anything a `NoireContent` can hold.

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

**Pasting is the reason this exists over a text box.** A run containing any of `Separators` (comma, semicolon, newline, tab by default) comes apart into one chip each, empty pieces dropped, so a trailing comma does not produce an empty tag. People paste comma-separated lists constantly, and a field that swallows one as a single tag is the thing they then undo by hand.

**Backspace on the empty input takes the last chip back for editing** rather than deleting it. Deleting is what the cross on a chip is for; backspace is for fixing a typo in something already committed.

**Every refusal comes back named**, as a `TagRejection`: `Empty`, `Duplicate`, `TooLong`, `Full`, or `Invalid` from your own `Validate`. A tag that simply vanishes when the user presses Enter reads as a broken widget whichever rule actually rejected it, so `LastRejection` and `LastError` say which, and the field shakes (honouring `ReducedMotion`).

```csharp
tags.AllowDuplicates = false;      // Default; matched with Comparer, case-insensitive by default
tags.MaxTags = 10;                 // null for no limit
tags.MaxTagLength = 64;
tags.TrimWhitespace = true;        // Default
```

`TryAdd(tag, out var rejection)` is the full path; `Add` is the shorthand; `AddRange(text)` splits and adds. `SetTags` restores a persisted list and drops anything the rules refuse, so a saved list that no longer passes validation cannot smuggle itself back in.

**`RemoveAt(index)` removes a position; `Remove(tag)` removes the first tag that matches.** With `AllowDuplicates` on those are different chips, and the position is the one the user clicked. The chips themselves are keyed on their index for the same reason: two chips carrying the same text would otherwise share one ImGui id, and only the first of them would ever be clickable.

Suggestions are ranked with [`FuzzyMatcher`](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/Fuzzy/README.md) and shown under the field while there is text to match, with tags already held dropped from the list.

## Picking several at once (NoireMultiCombo)

A dropdown that does not close when you pick something, because choosing four things should not mean reopening the list three times. Every option is a tick box and the closed widget summarises what is chosen.

```csharp
var categories = new NoireMultiCombo<string>("categories", allCategories);

if (categories.Draw())
    config.Enabled = categories.Selected.ToArray();
```

**Selection is held by value, not by index**, so replacing the option list keeps whatever still applies and drops what no longer exists. An index-based set would keep pointing at whatever moved into those slots. Pass an `IEqualityComparer<T>` to the constructor when the items need one.

```csharp
combo.Toggle(item);                  // returns whether it is now selected
combo.Set(item, true);               // returns whether anything actually changed
combo.SetSelection(saved);           // restore a persisted set outright
combo.SelectAll();
combo.ClearSelection();
```

`Selected` comes back in the order the options were given, because a set has no order of its own and the option list is the one a reader expects.

The preview names up to `PreviewMaxItems` items and summarises the rest (`PreviewOverflowFormat`, default `+{0} more`). `PreviewFunc` replaces it entirely; if yours throws, the built-in preview is used rather than the widget breaking.

The **All** and **None** shortcuts are scoped to what the filter is currently showing, which is what "all" means with a search box directly above it. `ShowSelectAll = false` hides them.

Everything the single-select combo does about filtering applies here too: fuzzy matching, matched-character highlighting, and a clipper past `VirtualizeThreshold`. The dropdown shows **exactly one scrollbar**, around the option list: the popup is capped at the full row budget and the list is sized to what the filter actually leaves, so a short list shrinks rather than padding out with dead space.

`CloseOnSelect = true` turns it back into an ordinary combo, which is the thing it exists not to do.

## Game data pickers (NoireExcelPicker)

A searchable, icon-rich dropdown over any sheet of game data, in one line.

```csharp
var items = new NoireExcelPicker<Item>("itemPicker", row => row.Name.ExtractText())
{
    Icon = row => row.Icon,
    Include = row => row.Icon != 0,      // which rows are offered; null means all of them
};

if (items.Draw())
    config.ItemId = items.SelectedRowId; // the row id is what you persist
```

`Select(rowId)` restores a persisted value, and is **safe to call before the sheet has been read**: the request is remembered and applied when the rows arrive, so restoring a saved id on plugin load does not have to wait for anything.

**The sheet is read once, on a background thread**, and the picker draws a disabled stand-in saying so rather than freezing the frame that opened it. Excel data is static content on disk; this never touches the object table, which is the game state that genuinely has to be read on the framework thread. Reads are serialized across pickers, because Lumina loads a sheet's pages on demand and two pickers starting at once would be two threads walking that lazily.

**Display names are built once, when the sheet is read.** That is the part that matters more than the drawing: the fuzzy filter scores every row on every keystroke, so reading names on demand would decode forty thousand SeStrings per character typed. The clipper only saves the drawing; precomputing saves the filtering.

`Include` is a predicate rather than a fixed set of categories, so the decision stays yours: equippable items, unlocked emotes, worlds on one data centre are all this callback and nothing else. `SkipEmptyNames` is on by default, because most game sheets are mostly blank — unused ids, placeholders and internal entries all carry an empty name.

Changing `Language` reloads on its own. Changing `Display`, `Icon` or `Include` needs a `Reload()`, since the picker cannot notice a callback being reassigned.

`Combo` is the `NoireComboBox<ExcelPickerEntry<TRow>>` underneath and is fully public: reach through it for the wheel-cycle shortcut, the filter's pinning, the visible option count, or a renderer of your own. The picker only fills it and draws it.

## Settings fields (NoireInputs)

The fields a settings window is mostly made of, each of them a small thing every plugin writes again and gets slightly wrong.

```csharp
NoireInputs.Number("Interval", ref config.IntervalMs, unit: "ms");
NoireInputs.Duration("Cooldown", ref config.Cooldown);
NoireInputs.HexColor("Accent", ref config.Accent);
```

Immediate and stateless from your side: pass the value by reference, take the return as "changed this frame".

**A number carries its unit inside the field**, not in a label beside it, because a unit in a separate label is a unit that drifts away from its number the first time the row is laid out differently. `NumberStyle` has the stepper, the range and the decimals; the `int` overloads share the same drawing, so the unit and stepper behave identically.

**A duration is typed the way people write one.** The value is a `TimeSpan`, so nothing downstream has to know which unit it was entered in:

```csharp
NoireInputs.Duration("Cooldown", ref config.Cooldown, new DurationStyle
{
    Default = TimeSpan.FromSeconds(30),
    BareUnit = DurationUnit.Milliseconds,   // what a number typed with no unit means
});
```

`90s`, `1m30s`, `1h30`, `2m 30s`, `1.5h` and `1:30` all read. A bare tail takes the unit below the one before it, which is what makes `1h30` ninety minutes. The reading is shown beside the field while you type, and is **written only when the field loses focus**: half of `1m30s` is itself a valid duration, and committing per keystroke would take the setting to one minute on the way to ninety seconds. `DurationHelper` is the parser on its own, and is as useful behind a command argument as behind a field.

**A colour takes a pasted hex**, in either shorthand or full, with a swatch that opens a picker. `ColorHelper.TryHexToVector4` is the form that does not throw, which is what a box being typed into needs.

**A row lays itself out inside the column it is in, not the window.** Labels are padded to a shared `NoireInputs.LabelWidth` (110 logical px, a minimum rather than a fixed width, so a longer label pushes its own field along instead of being clipped) and aligned to the frame padding so they sit level with the text in the field beside them. The field takes the rest of the column, and the column comes from `NoireLayout.ContentWidth()` rather than `GetContentRegionAvail()`, which reports the *window's* right edge and would run a field straight past a page that centres its content.

**Give a `Default` and the modified dot appears.** Move a value off it and a dot shows beside the field; click the dot and the shipped value comes back. The dot's column is reserved whether or not there is a dot in it, so a column of settings does not shuffle sideways as values change. `NoireInputs.ResetDot` is the same affordance for a widget this class does not ship.

**Refusals report, they do not block.** A `Validate` callback returns a message and the message slides in under the field; the value is still written. A field that silently swallows a keystroke is a field the user fights. A refusal is **held until the field is typed in again**: a `Validate` message is recomputed from the value every frame and persists on its own, but text that could not be parsed fails on exactly one frame, when focus is lost, and would otherwise slide straight back out as a flicker. `NoireInputs.Validated` wraps any drawing of your own in the same treatment:

```csharp
NoireInputs.Validated("port", port < 1024 ? "Ports under 1024 need administrator rights." : null, () =>
    ImGui.InputInt("Port", ref port));
```

## Data grids (NoireTable)

`BeginTable` already does the hard half, which is the layout: resizable, reorderable, hideable columns that behave. This is the other half.

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

**Column filters match the same way the search does**, fuzzily or not, and whatever narrowed the table picks its matched characters out in the cell.

A column needs a header and a `Text`, and everything follows from it: the column sorts on that text, the search reads it, a per-column filter matches it, the CSV export writes it.

**`SortKey` is for when the text does not sort the way the data does.** A level written `"100"` sorts before `"80"` as text; a duration written `"1m30s"` sorts after `"1h"`. Return the underlying value and the column sorts on that while still showing the text. `Sort` takes a full `Comparison<T>` when neither is enough.

**The table never copies your rows.** It holds the list you gave it and works in indices into it, so the row a renderer or a selection sees is the one you own. Filtering and sorting run when something changes rather than every frame — editing the list in place is the one thing it cannot notice, so call `Invalidate()`.

**The rightmost column takes whatever width is left over**, so the table always fills itself and its cells reach the edge. Its own `Width` is ignored for that reason, and it carries no resize grip, there being nothing to its right to hand width to. Which column that is follows the *display* order, so dragging a header somewhere else takes the behaviour with it.

**Every other column keeps a width of its own.** That is what makes the header menu's "size column to fit" mean anything: auto-fitting a fixed column sets an exact pixel width, while auto-fitting a stretch column sets a weight ImGui then renormalises against every other column, so each use nudges all of them by a pixel or two. Dragging a border is fine either way.

**Ties break on the source index.** `List.Sort` is an introsort and promises nothing about equal elements, so sorting on a column where hundreds of rows tie would reshuffle them every time anything else changed. Sorting is deterministic here, and the order inside a group is the order the rows arrived in.

**The search filters; it does not reorder.** This is deliberately unlike `NoireComboBox`, whose fuzzy filter ranks by score. A table's order is one the user chose by clicking a header, and quietly re-ranking it would take that away.

**Selection is held by value**, not by index, for the same reason it is in `NoireMultiCombo`: an index keeps pointing at whatever moves into that slot when the rows are replaced. A plain click selects, ctrl or shift adds.

**Aggregates are computed over the rows showing**, never over all of them. A total that ignores the filter above it totals something nobody is looking at.

**The footer is pinned to the bottom of the table, not to the end of the list.** A table has one scroll region and ImGui can only freeze rows at the *top*, so a totals row inside the body is one you have to scroll past a hundred thousand rows to read. Body and footer are two tables inside a single bordered frame, so they read as one table with a row pinned to the bottom of it; the footer's columns take the widths the body's columns actually have that frame, read in display order so they follow a column you resized or dragged elsewhere.

```csharp
new TableColumn<Item> { Header = "Weight", Text = i => $"{i.Weight:0.0}", SortKey = i => i.Weight,
                        Aggregate = shown => $"{shown.Sum(i => i.Weight):0.0} total" }
```

**`ToCsv()` exports what is on screen**: the visible columns, the surviving rows, the chosen order, quoted per RFC 4180 so a field with a comma in it survives the trip into a spreadsheet. An export that quietly hands back the unfiltered table is the one thing a user cannot check by looking.

**Long lists draw only what is on screen**, past `VirtualizeThreshold` (100 rows) or whenever `Virtualize` says so. `DrawnRowCount` reports how many rows were actually drawn, which is the honest way to show that it is working.

A `Renderer` on a column paints a cell instead of the plain text, and follows the same shape as every other custom-draw hook here: the table keeps the sizing, the sort, the filtering and the selection, and the hook is handed a `UiTableCellDraw<T>` with everything it needs, `DrawText()` included.

## Reorderable lists (NoireReorderableList)

A list whose rows can be dragged into a different order, which ImGui has no answer for at all.

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

**The list is yours.** The widget reorders it in place and tells you it did; it never holds a copy, so what you persist afterwards is the list you passed in.

**The drag starts on the grip, not anywhere on the row.** A row that carries its own controls would otherwise move every time one of them was used. `DragAnywhere` turns that off for rows that are only a label.

**The list holds still while a drag is in flight.** The row you picked up dims to the hole it left, a ghost follows the pointer, and an outlined gap shows where it lands; the move happens when the button comes up. Reordering as the pointer passes each row means aiming at a list that is moving out from under you.

**Where a drop lands is worked out from the pointer, not from which row reports itself hovered.** During a drag the dragged row is ImGui's active item and no other item is given the hover, so a hover-driven target only resolves in whichever direction happens to keep the pointer inside the row it started on: dragging works one way and not the other. `ResolveSlot` is pure and unit-tested in both directions.

**Reordering is a tested pure function, not a side effect of the drag.** Every off-by-one in a drag-to-reorder list lives in what "dropped at index 4" means when the row came from above rather than below it, and a drag is the one thing that cannot demonstrate it. `MoveItem` is unit-tested over every from/to pair in a list, against the invariant that matters more than any single ordering: **a reorder never loses or invents a row.**

**A drag that ends past the end means "put it last"**, which is the one thing the user was unambiguously asking for, so the target is clamped rather than the move refused.

**`AllowKeyboard` moves the focused row with the arrow keys.** Click a row to focus it, then press up or down; `KeyboardModifier` adds a required modifier if you want one, and defaults to none, because someone who has clicked a row and pressed an arrow has already said what they meant. The focus also follows a row you have just dropped, so a drag and then a nudge is one continuous gesture.

**The reorder keys are read the way a hotkey is, not through ImGui.** ImGui only receives key events the host forwards, and the host forwards them only when ImGui says it wants the keyboard, which with no text field active it does not: the game takes the arrow keys and the widget is never told anything happened. `KeybindsHelper.IsBindingHeld` reads the key state directly, which is the same route `NoireHotkeyManager` takes and why a hotkey works anywhere.

**So the keys are a `HotkeyBinding`, with the same two modes as the combo box's wheel-cycle shortcut**: a local binding, or a hotkey id so the user can rebind them.

```csharp
list.MoveUpBinding = VirtualKey.PRIOR;                              // local
list.BindReorderHotkeys(hotkeys, "list.moveUp", "list.moveDown");   // rebindable
```

Modifiers are matched exactly, so the default fires on the bare arrow and not on ctrl with it, and `ResolvedMoveUpBinding` reports whichever is actually in force.

**With hotkeys attached, the list swallows the key from the game only while the shortcut is live**: a row focused, and the window focused. The defaults are the game's own movement keys, and a hotkey left blocking permanently would take the arrow keys away for as long as the plugin is loaded, which is not a trade a reorderable list is entitled to make on anyone's behalf. `BlockGameInputWhileActive` turns the whole behaviour off.

**The key is taken through `HotkeyEntry.SuppressGameInput`, never by writing `BlockGameInput`.** That option is a persisted setting belonging to whoever registered the hotkey: a stored hotkey overrides the values it is registered with on the next load, so a widget writing its own momentary state there does not merely override an answer that is not its to give, it saves that state as the hotkey's standing one and the key stays swallowed on every launch afterwards, with no way left to turn it off. A suppression is runtime only and reference-counted, so the most a mistake can cost is the rest of the session.

**The block expires on its own, and that is what makes it safe.** Blocking works by clearing the key out of the game's key state on every framework tick, so a raised block that nothing lowers swallows that key until the plugin unloads — and the case that most needs it lowered is the list *not being drawn*, which is exactly when a call inside `Draw` cannot run. So a raised block is renewed one frame at a time and released by a watchdog once the frames stop coming: closing the window, switching tab, emptying the list, detaching the hotkeys or turning `BlockGameInputWhileActive` off all hand the keys straight back. A frame of slack is allowed first, because the watchdog runs on the framework tick and the renewal on the draw, and a tick landing between the two would otherwise take the keys back from a list still being worked in. The focus is dropped with it, so a list that comes back into view comes back neutral rather than silently holding the keys again.

**Clicking anywhere outside the list drops the focus**, tested against the rows' own bounds rather than against whether ImGui reports something hovered. Clicking another control is the ordinary way to stop working in a list, and a hover test counts that as still being in it — which would leave the keys held while the user is plainly somewhere else. Dragging is awkward in a long list and unavailable to some people entirely; the keyboard path costs one branch and is the difference between a reorderable list and a reorderable list somebody can use.

**`Duplicate` matters for anything mutable.** Without it the copy and the original are the same object, and editing either edits both. A record needs `step with { ... }`; a class needs a real copy.

```csharp
list.Duplicate = step => step with { Name = $"{step.Name} (copy)" };
```

A `Renderer` paints a row instead of its label, inside the space between the grip and the buttons, and is handed a `UiReorderRowDraw<T>` with `DrawLabel()` on it.

**Flat lists only.** Trees are a different widget with different rules and are deliberately out of scope: everything that makes reordering pleasant here, one insertion point and one index, stops being true the moment a row can be dropped *into* another one.

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

**Why this exists.** ImGui has a perfectly good tab bar and a genuinely bad story for opening a tab from code. The only lever is `ImGuiTabItemFlags.SetSelected`, and it has to be set for **exactly one frame**: leave it set and the tab is welded open with the user unable to click away, clear it on the wrong frame and the switch silently does not happen. So every plugin that wants "the changelog button opens the What's New tab" hand-rolls a `pendingTab` field and a flag-clearing dance, and most get the edge cases wrong.

`SwitchTab` is that dance done once, and the edge cases are the feature:

| You do this | It does this |
|---|---|
| Switch to the tab already open | Nothing. It is not a switch. |
| Switch twice before a frame runs | Keeps the last request, not both. |
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
| `ScrollWhenCrowded` | off | Scroll rather than shrink when they do not fit. |
| `WheelScrolls` / `WheelScrollStep` | on / `80` | Wheel over the strip scrolls it. See below. |
| `Width` | `0` | How wide the bar may be. Zero fits the column it is in. |
| `EmptyState` | none | Drawn when there are no tabs at all. |

Each `UiTab` carries its own `Body`, so nothing runs for a closed tab and there is no end call to forget. `Label` may change every frame, length included, without the tab losing its identity or its place: ImGui is keyed on `Id`, which never changes.

**A tab disabled while it is open stays open.** `Enabled` gates *reaching* a tab, not what it shows. Closing it under the user would move them somewhere they did not ask to go, and blanking it would leave them looking at nothing with no way to tell what happened. Set `DisabledReason` whenever you set `Enabled` — a control that is dead for no stated reason reads as broken.

**On reordering.** `Reorderable` lets the user drag tabs, but ImGui owns the order it draws them in and does not report it back, so `Tabs` is left exactly as you wrote it. A reordering is for that session and is not something to persist.

**The wheel scrolls the strip.** ImGui's own tab bar ignores it, so reaching a tab that has scrolled off means clicking the little arrows, or selecting the last visible tab so the bar creeps one along and repeating — which changes the open tab as the price of looking for another one. `WheelScrolls` (on by default) makes the wheel move the strip while the pointer is over it, selecting nothing. It does nothing while every tab already fits, so it costs nothing to leave on.

It also stops the windows behind it scrolling on the same notch, and that has to be a **refusal rather than an undo**. ImGui hands the wheel to the hovered window inside `NewFrame`, before a single widget has drawn, so by the time a tab bar could notice, the scrolling has already happened. Undoing it afterwards does not work either, because the window that moved is usually not the one the bar is drawn in: ImGui walks up from the hovered window to the first ancestor that can actually scroll, which for a bar inside a non-scrolling column is the page behind it. So while the pointer is over the strip, the bar marks that whole ancestor chain as not scrolling with the mouse, which is what makes the same walk find nothing willing to move. It is set a frame ahead, which costs nothing: a pointer rests on the strip for many frames before a notch arrives. Nothing has to be restored, because `Begin` reassigns a window's flags from its own arguments every frame.

**`Width` keeps it inside your column.** ImGui builds a tab bar out to the window's right edge and takes no width at all, so a bar inside a page that centres its content in a narrower column runs past the column and out the other side. Left at `0` the bar asks `NoireLayout.ContentWidth()`, which answers for the column rather than the window; set it to hold the bar to a width of your own. It only ever narrows — a bar cannot be given more room than the window it is in.

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

Both are immediate and stateless, and both draw **over** a rectangle you already have rather than wrapping anything, so they compose with any widget without it knowing.

**A badge costs no layout.** It writes straight to the draw list and submits no ImGui item, so it never moves the cursor, never widens the row, and never changes the line's height. That is what lets it be dropped after any widget, including a tab header, without the things around it shifting. Drawing the number with an ordinary text call would not do: an ImGui text call *is* an item, so it advances the cursor and grows the current line's bounding box, and everything after it on the row moves across and up.

**States and events are different things.** `Pulse` and `Glow` are states: they run for as long as the condition holds and you pass that condition every frame, so nothing is registered and nothing has to be stopped. `Shake`, `Bounce` and `Flash` are events: fired once by id, they play themselves out and return to zero on their own.

A shake or a bounce moves where a widget is *drawn*, not where it thinks it is. `ApplyOffset` nudges the cursor before the widget, so the widget moves without knowing it did and nothing can end up somewhere the mouse is not.

| `BadgeStyle` | Default | What it does |
|---|---|---|
| `Scale` | `1` | One knob for the whole badge. See below. |
| `Color` / `TextColor` | danger / text | Danger, because a badge exists to be seen before anything else on the element. |
| `MaxCount` | `99` | Above it, the badge reads `99+` rather than growing until it swallows the button. Zero shows everything. |
| `Anchor` / `Offset` | top right | Which point of the element it straddles, and the nudge from it. |
| `OutlineThickness` | `1.5` | A ring in the surrounding colour, so it reads against a busy element. |
| `Pulse` | off | A slow fade to catch the eye without moving anything. |
| `DotSize` / `MinSize` / `PaddingX` | `7` / `15` / `4` | Sizing, in logical pixels. |

A count of zero or less draws nothing, so `NoireBadge.OnLast(count)` can be called unconditionally rather than wrapped in an `if`.

**A badge is never moved to fit.** It straddles the corner it is anchored to and stays there, wherever the element goes. Somewhere it may not overflow, clip rather than reposition: a badge belongs to its element, so it should leave with it. `NoireTabBar` clips to the ends of its bar, which is why a tab scrolled halfway off has half a badge and one scrolled off entirely has none — the same thing the tab itself does. Pushing the badge back inside instead would strand it at the edge, still showing a count for a tab that is no longer there.

**`Scale` is the size knob.** Every measurement below it is also settable on its own, but growing a badge that way means keeping five numbers in proportion by hand:

```csharp
NoireBadge.OnLast(unread, new BadgeStyle { Scale = 2f });   // twice the size, still in proportion
```

It moves the text, the padding, the minimum size, the dot, the outline and the offset from the anchor together, and multiplies with `NoireUI.Scale` rather than replacing it, so a badge sized here still follows the user's own interface scale. The text is drawn with a font built at the size it works out to, so each distinct value in use is a distinct font size — a few are free, one that varies per badge across dozens of them is not.

**All of it is decoration, so all of it stops under `NoireUI.ReducedMotion`** while what is underneath keeps working: a pulsing button is still a button, a shaken field still holds its text. The one exception is `Glow`, which holds at full strength rather than disappearing — marking the element is the point, and that survives losing the movement.

## Keyboard focus (NoireFocus)

Marks the control the keyboard is pointed at, so there is always somewhere on screen saying where typing and the arrow keys will go. **Every widget NoireUI ships draws it itself**, so a plugin gets focus indication by using the widgets and setting nothing:

```csharp
NoireFocus.Style.Shape = FocusShape.Corners;   // everywhere, once
NoireFocus.Enabled = false;                    // or not at all

ImGui.InputText("##notes", ref notes, 256);
NoireFocus.OnLast();                           // a control the library does not provide
```

**Focus and selection have to differ in kind, not in degree.** Hover, selection and emphasis are drawn with soft marks: a glow, a tint, a lit plate. Focus is drawn hard edged, and that is the whole design. Two marks that differ only in brightness are read as "this one is selected harder", which is not a thing an interface can mean — and a glow spent on selection is the loudest mark in the vocabulary spent on the quietest state. The natures differ too, which is what decides who gets which: focus is singular, transient and moves on every keystroke, while selection is plural, persistent and moves rarely.

| `FocusShape` | What it is | Where it fits |
|---|---|---|
| `Ring` | A hairline outline following the whole edge | The default. Unambiguous at any size or proportion |
| `Corners` | A short elbow inside each corner | Quieter, and lighter on a busy surface |
| `Brackets` | A matched `[` and `]`, one each side | The most decorative, and the one needing the most room |
| `Underline` | A bar along the bottom edge alone | Quietest. Reads naturally on a text field, which already has a frame |
| `None` | Nothing | How one widget opts out while the rest keep their mark |

**Three levels of control, all optional.** `NoireFocus.Enabled = false` turns the mark off everywhere. Every widget that draws one takes a style of its own — `NumberStyle.Focus`, `DurationStyle.Focus`, `HexColorStyle.Focus`, `NoireComboBox.FocusStyle`, `NoireTagInput.FocusStyle` — so one field can differ from the rest, or go unmarked with `Shape = FocusShape.None`. And `FocusStyle.CustomDraw` replaces the painter outright:

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

The hook is handed the rect with the spread and arrival already applied, the faded colour, the control's own rectangle, and `Arrival` from 0 to 1, so a custom mark can animate with the arrival rather than against it. `DrawShape()` paints the shipped look, for a hook adding to it rather than replacing it; a hook that draws nothing is another way to suppress one widget's mark.

**The movement runs on arrival and never at rest.** `ArrivalSeconds` (0.12 by default) is how long the mark takes to settle onto a control that has just taken focus, drifting in from `ArrivalSpread` further out and fading up as it lands. An arrival is either focus moving to a different control or focus returning to one it left — the second is detected from a gap in the frames the mark was drawn on, since nothing tells a stateless surface that focus went away. Seeing *where focus went* is the hard part of keyboard navigation, and a short movement answers it; a mark that kept moving would be animating underneath the text the user is in the middle of typing, and would collide with `NoireAttention.Pulse`, which already means "this needs attention" rather than "this is where you are".

**It is the one mark that survives `NoireUI.ReducedMotion`.** Everything in `NoireAttention` stops there, deliberately. Focus does not: the arrival simply does not run, and the mark is placed instantly at full strength. The people navigating by keyboard are exactly the people who need to see where the keyboard is, so dropping the signal along with the motion would fail its own audience. `NoireFocus.Enabled = false` is available and is a real accessibility loss, not a cosmetic preference.

Arms on `Corners` and `Brackets` are sized by `ArmRatio`, a fraction of the control's shorter side, so one style reads correctly on a text field, a tall list box and a small icon button alike; `ArmLength` overrides it with a fixed distance where that is wanted, the way `SunburstStyle.InnerSize` overrides `InnerRatio`. Either is clamped so two arms on one edge cannot meet, since a mark that closes is a frame drawn the expensive way and stops reading as corners at all.

Nothing here submits an ImGui item. Like a badge, the mark is painted over the layout rather than added to it, so it never moves what is around it.

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

**If you are styling a flagged window of your own, push the field the flag actually selects.** ImGui picks between the window, popup and child style fields by window flag, and the branches are not the same test for every property:

| | Border size | Rounding | Background |
|---|---|---|---|
| Ordinary window | `WindowBorderSize` | `WindowRounding` | `WindowBg` |
| Popup | `PopupBorderSize` | `PopupRounding` | `PopupBg` |
| Tooltip | `PopupBorderSize` | `WindowRounding` | `PopupBg` |
| Child | `ChildBorderSize` | `ChildRounding` | `ChildBg` |

Pushing the wrong one is silent: the window draws with whatever the host style happened to have, whatever you asked for. A popup styled with `WindowRounding` and a tooltip styled with `WindowBorderSize` are both no-ops.

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

`Draw`, `Colored`, `Muted`, `Disabled`, `Wrapped`, `Bullet`, `Centered`, `Highlighted`, `CalcSize`, `LineHeight`, `CenterOffset`, and the `At` scopes. Sizes are logical pixels at 100%, like every other measurement here.

### Ask by role, not by number

`TextSize` has four steps: `Display`, `Heading`, `Body`, `Caption`. They resolve through `NoireTheme`, and every step except the body derives from the body size by a shipped proportion, so one number moves the whole scale:

```csharp
NoireTheme.Current.BodySize = 20f;      // the whole scale grows with it
NoireTheme.Current.HeadingSize = 24f;   // this step opts out; the others keep following
NoireTheme.Current.HeadingSize = null;  // and back onto the proportion
```

`BodySize` left unset is the host's own default font size (`NoireTheme.DefaultBodySize`), so an untouched theme is indistinguishable from ordinary `ImGui.TextUnformatted` beside it, and costs no atlas space at all.

An explicit `NoireText.Draw(text, 22f)` is there when you need it, and is the thing to avoid at thirty call sites: a number at a call site is a number the next thirty will each pick differently.

### Lining a drawn shape up with a label

`CalcSize` and `LineHeight` answer for the em box, and letters do not sit in the middle of it. The box reserves room under the baseline for descenders most labels never use, so a shape centred on the line sits one to two pixels above the words beside it: a tick box against a row label, a cross against a tag, an icon against a caption. It is small and it is the difference between a row that looks set and one that looks assembled.

`NoireText.CenterOffset()` is how far down the line the text actually looks centred, read off the font's capital band:

```csharp
var middle = rowTop + NoireText.CenterOffset();          // where the label looks centred
var half = side * 0.5f;

NoireShapes.Rect(new Vector2(x, middle - half), new Vector2(x + side, middle + half), color);
```

It is measured on the capital band rather than on the string being drawn, deliberately. Centring on the string's own ink would move a chip whenever its tag happened to contain a `g`, and a row of chips would stop sharing a baseline. Every label at a given size gets the same offset.

### What a size costs to build

Rasterizing is per glyph and per size, so the only thing that makes a type scale fast is not rasterizing glyphs nobody is going to draw. A complete font is several thousand: the whole default range, plus FontAwesome's ~1400 icons, plus the extra glyphs for the user's language. Building three sizes of that takes seconds, which is a long time to look at a heading in the wrong font.

So NoireText re-sizes the user's **own** font specification and gives it a glyph range: Latin with its accents, the punctuation real prose uses, currency, arrows and common symbols. Around seven hundred glyphs instead of several thousand. The extra glyphs for the user's Dalamud language are attached on top, so this is fast for someone reading English rather than broken for someone reading Japanese.

What it drops is the icon font and the parts of Unicode you are not about to put in a heading. Two knobs put them back:

```csharp
// Wider glyphs: Greek and Cyrillic on top of the usual Latin.
NoireText.GlyphRanges = [0x0020, 0x00FF, 0x0370, 0x03FF, 0x0400, 0x04FF, 0];

// Or take over the build entirely. This line is exactly what NoireText did before it was made fast:
// everything the default font has, icons included, and slower for it.
NoireText.FontBuilder = (toolkit, sizePx) => toolkit.AddDalamudDefaultFont(sizePx);
```

Set either before the first size is built (next to `Prewarm`), since a size already rasterized is not rasterized again.

### A type scale that changes while you watch

A plugin that offers the type scale as a setting hands NoireText a different size on every frame of a slider drag. Building each one would spend a rasterization on every step of the gesture and fill the size cache with values the user only passed through.

So a size that is not built yet is only rasterized once the scale has held still for `NoireText.RebuildSettleDelay` (120 ms by default). A whole sweep costs one build, at the size the user stopped on. While it is moving, text draws at the right size with the stretched stand-in, which is what a slider wants to show anyway: the size is the thing being chosen, and it tracks exactly. Drag back over a size that is already built and it is sharp immediately, because that is a cache hit rather than a build.

Sizes are cached at whole pixels. Glyphs are rasterized onto a pixel grid, so a tenth of a pixel is not a different font; at whole pixels a slider sweep asks for a couple of dozen distinct sizes instead of several hundred. Sizes that fall out of the scale and go unused are dropped after 20 seconds, so a session spent fiddling with the setting does not accumulate them.

### The size limit

Every distinct size is a full glyph atlas. NoireUI builds them into an atlas of its own, so adding a heading never forces the host plugin's fonts to rebuild alongside it, and caches one entry per size for the life of the plugin.

**The cache is bounded at 16 distinct sizes.** Past that it refuses to build more, draws at the nearest size it already has, and logs once naming the limit. It refuses rather than evicting because something may be mid-draw with the handle it would have thrown away. An interface with more than sixteen genuinely different text sizes has a type scale that has stopped being one, and running out of texture memory is the wrong place to find that out. `NoireUI.Diagnostics.Snapshot().TextFontSizes` reports the count.

### While a size is still building

Rasterizing a size takes a moment, and NoireUI does two things so you never watch it happen.

**The whole scale is built in one rebuild.** Registering a font asks the atlas to rebuild, and a rebuild re-rasterizes *every* font in that atlas, so four steps registered one at a time cost four full rebuilds. Any miss now registers every step of the current theme's scale inside one suppression scope, so they cost one. It cannot be made free: these are real glyphs rasterized at a real size, and how long that takes depends on how many glyph ranges your Dalamud language settings pull in. The time it actually took is logged, so it is a number rather than a guess.

**The wait is the right size, not the right sharpness.** Until the real font is ready the text is drawn by stretching the font already loaded to the size asked for. That is the blurry scaling this whole section exists to replace, used deliberately and briefly, because the alternative is worse in the way that shows: text that starts small and jumps when its font arrives takes the layout around it along with it. Right size and briefly soft beats right sharpness and briefly wrong.

**`NoireText.Prewarm()` moves the cost to load, and `Prewarm(wait: true)` removes the transition entirely.** Without `wait`, the build starts at load and runs in the background. With it, the call blocks until the sizes are rasterized, so your plugin finishes loading with its fonts ready rather than finishing sooner and showing a stand-in for the first seconds.

```csharp
public Plugin()
{
    NoireLibMain.Initialize(PluginInterface, this);
    NoireTheme.Current = NoireTheme.FromAccent("#C8A96A");   // set the theme first: it decides the sizes
    NoireText.Prewarm(wait: true);
}
```

It is a real trade rather than a speed-up. The rasterization takes as long as it takes; `wait` only decides whether it is spent on your load or on your first frames. Use it from a constructor, never from a draw callback, where the time would come out of the frame.

With `wait`, the glyphs are rasterized on the calling thread and are finished when the call returns. That detail is the whole reason it works: an atlas left to rebuild itself is driven by an event Dalamud raises **on the main thread**, and in a constructor no frame has run yet, so there is nothing under way to wait for and the build quietly starts later, on your first frame.

Safe to call repeatedly: a size already built is not built again.

**`CalcSize` measures whatever would draw.** It pushes the same font first, stand-in included, so a layout built on it cannot end up a few pixels wrong everywhere with neither font looking like the one lying.

---

### Letter-spacing

ImGui draws a string in one call at the font's own advances, so there is no notion of tracking. `NoireText.Tracked` places each glyph instead.

```csharp
NoireText.Tracked("OVERLAYS", NoireText.CapsTracking, TextSize.Caption);
var width = NoireText.TrackedSize("OVERLAYS", NoireText.CapsTracking, TextSize.Caption).X;
```

`Tracked` returns the size it drew, so a caller placing something beside a tracked label does not need the second call. Each glyph's advance is measured once per font size and remembered, not re-measured on every frame the label is drawn, and neither call allocates.

**Never measure text outside a frame.** `CalcSize`, `Draw`, `Tracked` and the rest push a font handle and call into ImGui, both of which need a frame in progress — reaching for one from a plugin or window constructor to warm the cache is a crash, not a warm cache. `NoireText.Request(sizePx)` is the frame-safe call: it only tells the cache a size is wanted, so it can be built before the frame that needs it. Use it for sizes a host can switch to at runtime, such as a reader-facing type scale; `Prewarm` already covers the sizes the interface always draws.

**`UiFontCache.MaxSizes` bounds how many distinct sizes exist.** Every size is an atlas entry and every rebuild re-rasterizes all of them, so it is a real budget. The default of 16 suits one type scale; a host offering the reader several scales has steps times scale many sizes and should raise it deliberately, rather than have its largest heading silently drawn at its second-largest size.

**Tracking is in ems, a fraction of the size the text is drawn at, exactly as CSS letter-spacing works.** That is what makes one value right at every step of the type scale and at every UI scale, so it is never scaled and never restated. `CapsTracking` is the shipped default: capitals have no ascenders or descenders to separate them and need noticeably more room than lower case to stop reading as one block.

The run is drawn onto the draw list and reserved with a single `Dummy`, rather than as one text item per character, so item spacing cannot creep in between the glyphs and a label always measures exactly what it draws. The trailing gap after the last character is not part of the run, or every tracked label would sit a gap left of where a centred or right-aligned layout put it.

---

## Drawing shapes (NoireShapes)

Widgets get you a plugin that works. Getting one that looks like it was designed means drawing the surface it sits on, and ImGui's draw list stops short in a specific place: it has rounded rectangles but no chamfered ones, one axis-aligned square-cornered gradient, no bevel, no glow, and no way to put a gradient on any shape but that one rectangle.

`NoireShapes` is those missing shapes. Nothing here is a widget: there is no state, no id, no hit testing. You give it a rectangle and it paints.

```csharp
var min = ImGui.GetCursorScreenPos();
var max = min + new Vector2(320f, 90f) * NoireUI.Scale;

NoireShapes.Plate(min, max, new PlateStyle { CornerShape = CornerShape.Notched, CornerSize = 12f, BevelSize = 2f });
NoireShapes.Frame(min, max, new FrameStyle { TickLength = 14f, Inset = 6f });
```

**Which numbers are logical and which are real.** The same rule as everywhere else, and it is worth stating because this is the one place both kinds sit side by side. A coordinate or size you pass as an *argument* is **real pixels**: it came from `GetCursorScreenPos`, from `GetItemRectMin`, or from arithmetic on those, and scaling it would corrupt an expression that reads as correct. A value on a `PlateStyle` or a `FrameStyle` is **logical**, written at 100%, and scaled where it resolves, because those are numbers NoireUI ships a default for. See [The UI scale](#the-ui-scale).

**Antialiasing is NoireUI's, not the host's.** It is a draw list flag rather than a per-call argument, so it is normally whatever was last left set somewhere else in the process. `NoireShapes` sets it around its own drawing and puts it back afterwards, because whether a shape comes out smooth or visibly stepped should not depend on a setting it does not own. `NoireShapes.AntiAlias` turns it off for these shapes alone.

This has nothing to do with the Draw3D renderer, which paints the game world through D3D11. The two share no code and no concepts.

### Where it draws

By default, the current window's draw list. `NoireShapes.On` redirects a block of drawing somewhere else, and nests:

```csharp
NoireShapes.On(ImGui.GetBackgroundDrawList(), () =>
{
    NoireShapes.Sunburst(centre, 400f, glow);       // behind every window, across the whole screen
});
```

`NoireShapes.DrawList` is the list currently being painted into, public so a block can mix these shapes with raw `ImDrawListPtr` calls and be sure both land in the same place. Inside an `On` scope, the current window's list is not the answer, and this is.

### Plates

A plate is the surface a panel, card, masthead or button face is made of. One call paints the fill or gradient, the bevel, the border and the glow underneath.

```csharp
NoireShapes.Plate(min, max, new PlateStyle
{
    CornerShape = CornerShape.Notched,
    CornerSize = 14f,
    Corners = RectCorners.Diagonal,       // chamfer two corners, not four
    Fill = accent with { W = 0.22f },
    FillTo = accent with { W = 0.02f },   // unset for a flat plate
    BevelSize = 2f,
    GlowSpread = 10f,
});
```

Everything is off by default except the fill and the theme's own border, so `NoireShapes.Plate(min, max)` with no style is a surface that matches the interface around it.

`CornerShape` is `Square`, `Rounded` or `Notched`, and `RectCorners` picks which corners it applies to. Cutting two corners rather than four is most of what separates a deliberate shape from a rounded box, so the diagonal pairs are named rather than left to be spelled out.

**The bevel works on any shape, including the ones with arcs in them.** It is not two offset outlines: each edge of the path is lit by how far its outward normal faces the light, so a chamfer catches the light on its diagonal cut and a rounded corner turns smoothly from lit to shaded around the arc. `BevelDirection` moves the light; the default is above and to the left.

### Gradients over anything

ImGui's `AddRectFilledMultiColor` is one axis-aligned rectangle with four square corners. That is why the gradient here is a **scope over arbitrary drawing** rather than one more shape: it shades whatever the body drew.

```csharp
// One ramp across a plate and the ring inside it, so they cannot disagree about it.
NoireShapes.Gradient(min, max, GradientAxis.Horizontal, accent, warning, () =>
{
    NoireShapes.Rect(min, max, Vector4.One, CornerShape.Rounded, 20f);
    NoireShapes.Ring((min + max) * 0.5f, 22f, Vector4.One, 3f);
});
```

Pass two points instead of a `GradientAxis` for any angle at all. `NoireShapes.GradientRect` is the shorthand for the ordinary case.

**Colour is replaced and alpha is multiplied.** That is deliberate and worth knowing, because it decides how you draw the body: ImGui carries its antialiasing in the alpha of the outer vertices of every shape, so replacing alpha outright would give every shaded shape hard, jagged edges. The practical consequences are that a body drawn in white takes the gradient exactly, a body drawn in a colour is tinted by it, and a gradient that fades to zero alpha fades the shape out.

Nesting works: an inner gradient shades only what it drew, and the outer one then shades that again.

### Frames and corner ticks

A rectangle with a short bracket set inside each corner reads as drawn rather than as a border, and it is the cheapest thing that makes a panel look composed.

```csharp
NoireShapes.Frame(min, max, new FrameStyle
{
    Inset = 6f,          // set the frame off the content without moving the content
    DoubleGap = 3f,      // a second line inside the first
    TickLength = 16f,
    TickCorners = RectCorners.Diagonal,
});
```

Set `TickLength` to zero and this is an ordinary outline again.

**Corner ticks suppress themselves when they would meet.** Two brackets crossing in the middle read as a smaller frame rather than as corners, so below twice the tick length on either axis the frame draws none. That is the right answer for a rectangle that has merely become small and the wrong one for a strip, which loses its edge entirely — a window collapsed to a title bar, say. `TickFallback.Brackets` draws a full-height bracket at each end instead, at the inset, length, thickness and colour the ticks would have had, so a frame moving between the two shapes keeps its marks where they were:

```csharp
new FrameStyle { TickLength = 16f, TickFallback = TickFallback.Brackets };
```

The same brackets are public on their own, and so are the corner ticks, for anything drawing its own edge:

```csharp
NoireShapes.Brackets(min, max, gold, armLength: 7f, thickness: 1.5f);
NoireShapes.Bracket(min, max, gold, 7f, 1.5f, BracketSide.Right);   // just the "]"
NoireShapes.CornerTicks(min, max, gold, length: 7f, thickness: 1.5f);
NoireShapes.CornerTicks(min, max, gold, 7f, 1.5f, RectCorners.TopLeft | RectCorners.BottomRight);
```

Each tick is one three-point path rather than two lines meeting at a point. A line is drawn centred on its own path, so two of them sharing an end leave the outer corner uncovered by half the thickness: a square notch exactly where the tick is supposed to turn.

### Arcs, rings and wedges

**Angles are turns, not radians.** Zero is twelve o'clock and a quarter is three o'clock, so a gauge reading sixty eight percent is written as `0.68` and there is no question which way zero points. That matches how every other fraction in NoireUI is expressed.

```csharp
NoireShapes.Wedge(centre, radius - 12f, radius, 0.125f, 0.875f, track);        // the empty track
NoireShapes.Wedge(centre, radius - 12f, radius, 0.125f, 0.125f + 0.75f * value, accent);
NoireShapes.Arc(centre, radius + 5f, 0.125f, 0.875f, accent, 2f);
NoireShapes.Ring(centre, radius, hairline);
```

A wedge given an inner radius is drawn as a thick arc rather than as a filled band, which keeps the ends square and the edges antialiased however far round it goes. With an inner radius of zero it is a filled pie, split internally when it passes half a turn because a slice wider than that is no longer convex.

**A full turn closes cleanly.** `NoireShapes.ArcPath` is the round counterpart to `RectPath` and is public for the same reason. It reports whether the sweep came all the way round, and a path that did **stops one step short of its own first point**, because the edge back to the start is the one stroking or filling adds for itself. Pass the `closed` flag straight on to `Stroke`:

```csharp
Span<Vector2> points = stackalloc Vector2[NoireShapes.MaxArcPathPoints];
var count = NoireShapes.ArcPath(points, centre, radius, 0f, value, out var closed);
NoireShapes.Stroke(points[..count], accent, 4f, closed);
```

That detail is load-bearing rather than cosmetic. A repeated first point leaves the closing edge zero length, and a zero-length edge has no direction to build a join from, so a thick ring draws a spike at twelve o'clock instead of nothing. A full-turn wedge is a disc for the matching reason: it carries no centre vertex, which would otherwise fold the fan back through the middle and leave a seam along the radius.

### Pattern fills

Two patterns, both drawn as geometry rather than rendered into a cached texture.

```csharp
NoireShapes.Sunburst(centre, 240f, accent with { W = 0.5f }, new SunburstStyle { Rays = 32, Duty = 0.35f });
NoireShapes.Guilloche(centre, 120f, accent, new GuillocheStyle { Lobes = 9, Rings = 3, RingRotationTurns = 0.5f / 9f });
```

The guilloche is the interlaced rosette engraved on banknotes and watch dials: a hypotrochoid, the curve a pen traces through a hole in a small circle rolling inside a larger one. `Lobes` is the ratio between the two circles and `Depth` is how far out the pen sits. Turning each ring by half a lobe against the one outside it is what produces the interlacing the pattern is named for.

**The sunburst's rays have soft sides** (`SunburstStyle.Softness`, 0.35 by default). A filled shape only gets one pixel of antialiasing, which is enough for a handful of wide rays and visibly stepped once there are twenty narrow ones. Each ray is therefore drawn as a few layers narrowing inwards, so its sides fade instead of ending. Set it to zero for hard edges. It is also simply the more truthful look: light does not have an edge.

**The hole in the middle is a ratio or a distance.** `InnerRatio` starts the rays at a fraction of the radius, so the hole scales with the burst. `InnerSize` states it as a distance at 100% instead and takes precedence when set, for lining the burst up with something drawn at a fixed radius inside it: a ratio would pull the hole away from an ornament with a size of its own the moment the burst's radius changed.

Neither is cached to a texture, and that was a decision rather than an omission. Both are a few hundred points, so there is nothing to cache that would be cheaper than drawing them; geometry also stays sharp at every scale and costs no texture memory, where a cached bitmap would have to be rebuilt whenever the size or the UI scale changed. A pattern that genuinely needs per-pixel maths is a texture you build yourself and draw with [`UiImageSource`](#images-uiimagesource).

### Glows, clipping and sweeps

`Glow` grows a **rectangle**, so a shape that is not one gets a rectangular halo: a lit diamond comes out sitting in a glowing square. `GlowPath` grows the path itself.

```csharp
NoireShapes.Diamond(centre, 6f, gold, glow: goldHi, glowSpread: 8f);   // sugar over both
NoireShapes.GlowPath(myPoints, goldHi, 8f);                           // any convex, clockwise path
```

Each vertex moves along the bisector of its two edges, by the distance that keeps both edges parallel to where they started. That is a real outward offset rather than a scale about the centre, which only agrees with one for shapes that happen to be regular. The miter is floored so a sharp corner is blunted rather than shooting a spike across the interface.

**`Clipped` keeps a whole composition inside a box.** A painted background is drawn from its centre outwards and has no idea where the block holding it ends: a sunburst reaching the corners of a masthead reaches just as far past it, over whatever comes next. It is a scope for the same reason the gradient is — one call contains however many shapes the composition turns out to be.

```csharp
NoireShapes.Clipped(min, max, () => { PaintSunburst(); PaintRosette(); });
```

**`SweepLine` is a line with a bright band travelling along it.** It cannot be a `Gradient`: that ramps between two colours across the whole span, and this is three stops with the bright one moving. The band runs off both ends rather than bouncing, because a highlight that reverses reads as a scanner and one that wraps mid-line flickers.

`FadeIn`, `Diamond`, `DiamondOutline` and `DiamondPath` are the small marks a deco interface repeats; the diamond ones exist because a hand-written diamond is four points that have to be clockwise for `Fill` and `GlowPath` to behave, and neither fails loudly.

### Shapes NoireUI does not ship

Every shape above is drawn by the same three public calls over a path, so a shape NoireUI has never heard of gets the same bevel and the same gradient:

```csharp
Span<Vector2> tag = [ /* your own points, clockwise */ ];

NoireShapes.Fill(tag, fill);
NoireShapes.Bevel(tag, light, shadow, 2f);
NoireShapes.Stroke(tag, border);
```

`NoireShapes.RectPath` and `NoireShapes.ArcPath` are public for the same reason: generate the outline NoireUI would have used, adjust it, and draw that instead. A buffer of `NoireShapes.MaxRectPathPoints` or `MaxArcPathPoints` is always large enough.

Two properties are load-bearing and both hold for every path `RectPath` produces. `Fill` needs the path **convex**, because a path that turns back on itself renders as overlapping fans rather than as the shape you drew; a concave shape is drawn as two or more convex pieces. `Bevel` needs it wound **clockwise**, because that is how an edge works out which way it faces.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Hotkey Manager Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/HotkeyManager/README.md)
