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
- [Session-only memory (NoireUiSession)](#session-only-memory-noireuisession)
- [Diagnostics](#diagnostics)
- [Theming (NoireTheme)](#theming-noiretheme)
- [Buttons (NoireButtons)](#buttons-noirebuttons)
- [Layout structures](#layout-structures)
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
- [Custom Tooltips](#custom-tooltips)
- [Images (UiImageSource)](#images-uiimagesource)
- [The UI scale](#the-ui-scale)
- [Text at any size (NoireText)](#text-at-any-size-noiretext)
- [Drawing shapes (NoireShapes)](#drawing-shapes-noireshapes)
  - [Where it draws](#where-it-draws)
  - [Plates](#plates)
  - [Gradients over anything](#gradients-over-anything)
  - [Frames and corner ticks](#frames-and-corner-ticks)
  - [Arcs, rings and wedges](#arcs-rings-and-wedges)
  - [Pattern fills](#pattern-fills)
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

`NoireLayout.ContentWidth()` is that same answer on its own, and is what any widget defaulting to "the space available" should ask instead of `GetContentRegionAvail()`. The difference only shows up inside a page that centres its content in a narrower column: the content region still reports the window's edge, so a field sized from it runs past the end of everything around it.

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

The dropdown always shows **exactly one scrollbar**, in either mode: it is sized to hold `VisibleItemCount` options plus the filter row, and shrinks to fit when there are fewer options.

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

**With hotkeys attached, the list swallows the key from the game only while the shortcut is live**: a row focused, and the window focused. The defaults are the game's own movement keys, and a hotkey left blocking permanently would take the arrow keys away for as long as the plugin is loaded, which is not a trade a reorderable list is entitled to make on anyone's behalf. Whatever each entry's `BlockGameInput` was set to is remembered and restored, so a hotkey a plugin deliberately blocks with keeps blocking when the list is not using it. `BlockGameInputWhileActive` turns the whole behaviour off. Dragging is awkward in a long list and unavailable to some people entirely; the keyboard path costs one branch and is the difference between a reorderable list and a reorderable list somebody can use.

**`Duplicate` matters for anything mutable.** Without it the copy and the original are the same object, and editing either edits both. A record needs `step with { ... }`; a class needs a real copy.

```csharp
list.Duplicate = step => step with { Name = $"{step.Name} (copy)" };
```

A `Renderer` paints a row instead of its label, inside the space between the grip and the buttons, and is handed a `UiReorderRowDraw<T>` with `DrawLabel()` on it.

**Flat lists only.** Trees are a different widget with different rules and are deliberately out of scope: everything that makes reordering pleasant here, one insertion point and one index, stops being true the moment a row can be dropped *into* another one.

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

Neither is cached to a texture, and that was a decision rather than an omission. Both are a few hundred points, so there is nothing to cache that would be cheaper than drawing them; geometry also stays sharp at every scale and costs no texture memory, where a cached bitmap would have to be rebuilt whenever the size or the UI scale changed. A pattern that genuinely needs per-pixel maths is a texture you build yourself and draw with [`UiImageSource`](#images-uiimagesource).

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
