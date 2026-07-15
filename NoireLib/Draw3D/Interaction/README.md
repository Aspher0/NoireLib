# NoireInteract & NoireGizmo

The interaction layer for [NoireDraw3D](../README.md). The renderer is **deaf by design** (Law 11: it draws, it reads no input); it only exposes `NoireDraw3D.Pick(screenPx)`, a raw ray query you call yourself. This layer is the half the renderer refuses to own: a UI-thread state machine that reads the mouse, tracks gestures across frames, and turns raw input into **hover / click / drag** events on scene nodes and gizmos.

It is the single file group under `Draw3D/` allowed to touch ImGui; the renderer core stays ImGui-free, enforced by a contract test.

**One front door.** You rarely name `NoireInteract` directly: hover / click / selection live on the objects they act on (`scene`, `node`, `editor`), and the genuinely-global input knobs are grouped on **`NoireDraw3D.Interaction`** (gestures, wall-occlusion, deselect rules, multi-select modifiers, debug, custom-interactor registration). `NoireInteract` remains the engine and an advanced alias; every `NoireDraw3D.Interaction.X` below forwards to it.

## The two problems it exists to solve

**1. A click is a click, not a camera pan.** In FFXIV you move the camera by clicking and dragging. That must never register as a click on a 3D object. A gesture is bound to its owner **at press time**: press on an object makes it yours; press on empty world makes it the game's camera pan, and it *stays* the game's even if it later drags across your object. A left press that moves past the drag threshold is a drag, never a click.

**2. A drag takes the lead of input.** Grabbing a draggable target (a gizmo handle, a movable node) claims the mouse from the game on the very first frame, so the camera never pans underneath the drag.

Both are guaranteed regardless of frame-rate, and the whole decision table is unit-tested headlessly (`InteractionArbiter`).

## Clickable objects

The easy path: spawn a node and make it selectable in one call. `MakeSelectable` opts the node into interaction, adds a built-in hover highlight (brightens the tint ×1.2 by default), and routes a left-click into the node's **scene selection**. `MakeInteractable` is the hover/click-only variant (no selection). Both **compose** over any `OnHoverEnter` / `OnClick` you set, and never clobber them:

```csharp
var node = scene.Spawn(MeshBuilder.Box(), material, pos, "switch")
                .MakeSelectable();              // hover highlight + click-to-select
node.OnClick = h => Toggle(h.Node);            // still yours; runs alongside select
```

The open path stays available - opt in by hand and give it callbacks; the node behaves like a button in the world:

```csharp
node.Interactable = true;                       // starts NoireInteract automatically
node.Selectable   = false;                      // hover/click only, no selection (MakeInteractable does this)
node.OnHoverEnter = h => Highlight(h.Node);
node.OnClick      = h => Toggle(h.Node);        // h.WorldPoint / h.TriangleIndex tell you exactly where
node.OnRightClick = h => radial.OpenAtMouse();
```

`InteractHit` carries the node, the world point the ray met it, the exact triangle (for meshes created with `keepCpuData`), the ray, and the screen position: enough for "which face did I click", decal-stamp-at-cursor, or spawning a child exactly where clicked.

Make a node draggable and the camera will not pan while you drag it:

```csharp
node.Draggable  = true;
node.OnDragStart = ctx => { /* the camera is already blocked */ };
node.OnDrag      = ctx =>
{
    // helpers turn the cursor ray into a usable delta:
    if (ctx.TryPlaneDelta(Vector3.UnitY, out var move))   // move across the ground plane
        node.LocalPosition = _pressPos + move;
};
```

## Selection is per-scene

Selection is a property of the **scene**, not a process-global: `scene.Selection` (an `InteractSelection`) is the source of truth for what is selected in that scene, updated automatically when a left-click lands on a selectable node in it (toggle globally with `NoireDraw3D.Interaction.SelectOnClick`, or per node with `node.Selectable`). Multiple scenes select independently; there is no global mode to set and reset. Read/drive it: `Selection.Nodes` (ordered, read-only), `Selection.Primary`, `Selection.Count`, `Selection.Contains(node)`, the `Selection.Changed` event, and `SetSingle` / `Add` / `Remove` / `Clear`:

```csharp
scene.Selection.Mode = SelectionMode.Multi;   // allow more than one (or editor.MultiSelect - see below)
scene.Selection.MaxCount = 8;                 // cap it (0 = unlimited)
onDeletePressed = () =>
{
    foreach (var n in scene.Selection.Nodes.ToArray())
        n.Destroy();
    scene.Selection.Clear();
};
```

**Multi-select is configurable.** `Selection.Mode` (`Single` / `Multi`) decides whether more than one node can be held; `Selection.MaxCount` caps it (the oldest is dropped when a new add would exceed it). Which modifier extends the set is a predicate on `NoireDraw3D.Interaction`, so it can be any key or always-on:

- `NoireDraw3D.Interaction.ToggleSelectionHeld` (default **Ctrl**): while held, a click toggles the node in/out of the set.
- `NoireDraw3D.Interaction.AddSelectionHeld` (default **Shift**): while held, a click adds the node. Set it to `() => true` for add-on-every-click.

**Deselecting is configurable** (`NoireDraw3D.Interaction.DeselectOn`, a `[Flags]` `DeselectMode`); "deselect" is global to the pointer (it clears every scene's selection), while the selections themselves stay per-scene:

- `ClickEmpty` (default): a left click on empty world (no object under the cursor, not over UI) that *is not* a camera pan clears the selection. A click-and-drag (the FFXIV camera pan) never deselects; the arbiter tells the two apart by the drag threshold, so this is safe to leave on during normal play.
- `Key`: clears on `DeselectKey` (default **Escape**; point it at any key). Off unless you add the flag.
- `None`: never auto-deselect; you own the selection.

Combine them (`DeselectMode.ClickEmpty | DeselectMode.Key`). Clearing the selection raises `Selection.Changed`, so a bound gizmo detaches on its own.

## SceneEditor: the packaged controller

"Click to select, gizmo follows the selection" is one object, created from and **owned by** the scene:

```csharp
var editor = scene.CreateEditor(GizmoOp.Universal);
editor.MultiSelect = true;                 // scoped: restored when the editor/scene is disposed
editor.Gizmo.Space = GizmoSpace.Local;     // configure via the flattened gizmo surface
editor.Gizmo.Snap  = 0.5f;
editor.SelectionOutline = new Vector4(1f, 0.8f, 0.2f, 1f);   // optional: outline selected nodes
foreach (var n in scene.Roots) n.MakeSelectable();
// teardown: nothing - scene.Dispose() disposes the editor too (editor.Dispose() is optional early teardown).
```

The editor subscribes to its scene's `Selection.Changed` and attaches its `Gizmo` to the current pick - one node, or the whole group - so you never write the count-0/1/many follow branch. Because a scene's selection only holds that scene's nodes, an editor naturally reacts to picks in its own scene only (when scenes overlap on screen, the front-most hit across all scenes wins the pick, and only the owning scene's editor updates). `MultiSelect` drives the selection mode as a scoped setting, restored on dispose - no lingering global.

## NoireGizmo: move / rotate / scale

Grab any node (or any world matrix) with axis / plane / center handles.

The common knobs are surfaced directly on the gizmo (they delegate to `Options`), so object-initializers work and a scalar `Snap` covers the usual case; the full `Options` struct is there for everything else:

```csharp
var gizmo = new NoireGizmo(GizmoOp.Universal)     // Translate | Rotate | Scale
{
    Space = GizmoSpace.World,                     // World | Local
    Snap  = 0.5f,                                 // uniform translate snap (SnapPerAxis for a Vector3)
    RotateSnapDeg = 15f,                          // rotation snap in degrees
    ScaleSnap     = 0.5f,                          // scale snap increment
};
gizmo.Attach(node);
gizmo.OnEditEnd += g => Commit();                 // one transaction per drag; pair with your undo

// or bind to any matrix you own:
gizmo.AttachMatrix(() => transform, m => transform = m);

// or edit several nodes at once around one pivot:
gizmo.AttachGroup(scene.Selection.Nodes);
```

Most of the time you don't construct a gizmo directly - `scene.CreateEditor` makes one and follows the selection for you (see above). Reach for a bare `NoireGizmo` when you want to drive a matrix that isn't a scene node, or a fully custom controller.

**Groups.** `AttachGroup` shows a single gizmo at the centroid of the set and moves / rotates / scales every member together around it (a group of one behaves like `Attach`). A common wiring is to follow the selection: bind a single node when one is selected, a group when several are.

**Scaling is relative to the original size.** A scale gesture adds increments measured against the size captured when the gizmo bound the target, not the current size, so repeated scaling does not compound and an axis dragged to near-zero can always grow back (multiplying the current size would lock a zeroed axis at zero).

**Drag feedback.** While dragging, the gizmo draws a fixed anchor marking where the target's center was before the drag (glued to that world point as the camera moves), a guide line to the current center, and the live amount moved / rotated / scaled. Both backends draw the same overlay; on the ImGuizmo backend it replaces ImGuizmo's built-in text, which is suppressed because that text reports a *world-space* delta and so reads wrong in `Local` space (dragging a rotated object's local axis would print the world projection, e.g. `0.353` for a `0.50` move at 45°). The readout instead measures the movement along the gizmo's own press-time axes, so a local-axis drag reads its true distance. ImGuizmo's text is hidden only for the span of the gizmo's own manipulate call (its shared style is restored immediately), so any other consumer of ImGuizmo is unaffected. Turn the overlay off with `Options.ShowDragFeedback = false`.

Snapping is split by the natural shape of each operation: translation snaps per axis (`Snap`, a `Vector3`, since a grid can differ along X/Y/Z), while rotation (`RotateSnapDeg`) and scale (`ScaleSnap`) are single values (an angle and a ratio). Both backends honour all three identically.

### Two backends, the consumer picks (`gizmo.Options.Backend`)

Same API surface either way; flip one field without touching call sites. Both honour `Options.Space` (World / Local) the same way, and scale handles are always object-local.

- **`GizmoBackend.Native` (default): in-world depth gizmos.** Handles are **real geometry** drawn through `Im`, and hit-tested in **screen space** against the render-time camera, so detection stays reliable at any camera angle and never wobbles under camera motion. Occlusion is set by `Options.Depth` (`GizmoDepth`): the default `OnTopOfObjects` keeps them on top of other 3D objects (never buried in the object they edit) but **occluded by the game world**, so a handle hides behind a wall like real geometry; `AlwaysOnTop` restores full x-ray; `Occluded` is fully depth-tested. For a *hold-to-occlude key*, set `Options.OcclusionHeld` (for example `() => ImGui.GetIO().KeyAlt`): occluded while held, x-ray otherwise, overriding `Depth`. Handles draw on a high layer so they paint over translucent scene objects (a fading ground plane) rather than under them. Translate/rotate handles follow `GizmoSpace` (World by default); **scale handles are always object-local**. The handles are drawn from a live basis every frame, even mid-drag, so they stay a constant on-screen size through camera distance/zoom and, in `Local` space, rotate with the object as you turn it (the drag *solver* uses the frozen press-time basis, so nothing slips). Their screen-constant size comes from an analytic projection derivative rather than a depth round-trip, so it holds steady even up close (no size flicker near the camera). The handle geometry is emitted on the **render thread** (via `NoireDraw3D`'s render-overlay hook) with the live frame, so its sizing tracks the camera without lag; hit-testing and the drag solver run on the UI thread. Supports per-axis translation snapping. This is a client of `NoireInteract` (an `IPointerInteractor`): it shares the one mouse-capture authority, so grabbing a handle blocks the camera and handles out-rank scene nodes under the cursor. Unless the depth mode is `AlwaysOnTop`, a handle behind a wall is not grabbable (see `WallOcclusionMode`).
- **`GizmoBackend.ImGuizmo`: the classic ImGui gizmo.** Drawn by `Dalamud.Bindings.ImGuizmo` inside a fullscreen, **always-`NoInputs`** host window (ImGuizmo reads ImGui IO directly, so it hit-tests and manipulates through a passthrough window). It is **self-driven**: it runs in a pre-pass *before* scene picking and reports when a handle is hovered/dragged, which makes the frame a hard pass for scene picking (so a handle over a 3D object grabs the handle, not the object). It always `Enable(true)`s (a pure overlay is never greyed by what is under it) and blocks the game camera itself with `SetNextFrameWantCaptureMouse`, deliberately **not** an extra ImGui window: ImGuizmo gates every handle's hover on "is any other window hovered", so a capture window (or toggling the host's own input flags) flickers the highlight on and off every frame, while a windowless capture flag does not. The renderer's view/projection are fed through with the projection's Z column **rebuilt to a finite-far, non-reversed range**: the game's own projection is reversed-Z *and* infinite-far, which collapses ImGuizmo's cursor-ray unprojection (translate reads 0, rotate NaNs the object, handles shrink to a dot); only clip.z is changed, so the gizmo still overlays the object pixel-for-pixel. It follows the gizmo's Local/World space and snaps translate, rotate and scale. A universal gizmo is driven as **two manipulations** - translate+rotate in the chosen space, scale always object-local - because ImGuizmo forces the whole gizmo into local space whenever the operation includes scale; splitting keeps world translate/rotate genuinely world-aligned. For a group, `Local` aligns the handles to the first selected node. The backend **auto-falls-back to `Native`** so it never silently vanishes when its binding cannot initialise or a frame used the wholesale view-projection fallback camera; turn on `NoireInteract.DebugLog` and a one-time `[Gizmo]` line in the log says which.

Dispose the gizmo to remove it.

## How input is arbitrated

Every frame `NoireInteract.Update()` (auto-driven from `UiBuilder.Draw`) runs the pure `InteractionArbiter`, then shows a fullscreen invisible ImGui window **only while interacting**. Hovering that window makes ImGui set `WantCaptureMouse`, which is exactly what tells Dalamud to withhold the mouse from the game, so the camera cannot pan and nothing is targeted. The rest of the time the game keeps the mouse untouched.

**Outside the game window is a hard pass.** When the cursor is not over the game viewport, or the game is not the foreground window, nothing hovers, picks, selects, deselects, drags, or captures. A click in another application never reaches a 3D object.

**Self-driven interactors run first.** An interactor may set `SelfDriven` (the ImGuizmo backend does): instead of being ray-hit-tested, it reads ImGui IO through its own always-passthrough window and reports, in a pre-pass *before* hover resolution, whether it owns the mouse this frame. While one does, the frame is a hard pass for scene picking. It blocks the game camera itself with `SetNextFrameWantCaptureMouse` rather than a capture window.

**UI is a hard pass.** Whenever the cursor is over another UI surface, Draw3D neither hovers, picks, nor captures; the object *behind* the UI is never touched (`ForeignUiHasMouse`). Two surfaces count: a **foreign ImGui window** (another plugin's, detected from `WantCaptureMouse`, with our own capture window discounted so we never mistake ourselves for foreign), and **native game UI** (a HUD window, inventory, friend list, detected from the game via `NoireDraw3D.IsCursorOverGameUi`, since native addons are not ImGui and never set `WantCaptureMouse`). Game-UI detection tests the addon's **collision nodes** (the game's own hit regions), not its padded bounding box, so the transparent margin around a HUD element (the gaps beside action-bar slots, a window's padding) does not falsely block a 3D object behind it; near-fullscreen transparent overlay roots (nameplates, fly-text) are excluded so they never blanket the viewport. Turn game-UI blocking off entirely with `NoireDraw3D.Interaction.GameUiBlocksInteraction = false`. With `NoireDraw3D.Interaction.DebugLog` on, a `[Interact/Gate]` log line prints (on change) exactly why a spot is a hard pass, naming the game addon when it is the cause.

**Walls can be a hard pass (`WallOcclusionMode`).** By default the mode is `Off`, so objects are always hoverable/clickable and picking is reliable at every camera angle. Opt into `HoldToClickThrough` to have a wall / house / terrain / **furnishing** in front of a 3D object block hovering and clicking it; hold the click-through key (`ClickThroughHeld`, default **Alt**; Ctrl/Shift are the selection modifiers) to reach objects behind geometry. `Always` never clicks through. The occluding surface is read from the **game depth buffer** (throttled, and cached between reads since the depth resource copies whole), so *every* rendered surface counts - static meshes, fences, decorations - not just the collision meshes the game's screen raycast would return; it falls back to that raycast only on frames where depth is unreadable. Note that once enabled, the ground an object rests on can occlude it at grazing camera angles. Native gizmo handles obey the same rule unless their depth mode is `AlwaysOnTop`, in which case they stay grabbable through walls.

**Decals pick their shape, not their box.** A ground-decal node is hit-tested against its rendered footprint SDF (the ring's annulus, the sector's wedge, the rect) on the real ground surface (mirroring `GroundDecal.hlsl`), so hovering the hole of a ring or outside a sector's arc correctly misses.

- `BlockGameMouseOnHover` (default **false**): the playable default. Hovering a plain object never claims the mouse, so the camera still pans/zooms and the world stays clickable straight through a highlighted object. A plain left-click still selects (fires `OnClick`) but coexists with the game (the click also reaches the world behind), and a **draggable** target (a gizmo handle) always still takes the lead of its drag so the camera cannot move under it. Set **true** for the aggressive, ImGui-consistent mode where hovering claims the mouse and consumes the click; tidy for a modal editor, but it blocks camera/zoom while the cursor rests on an object.
- `NoireDraw3D.Interaction.DragThresholdPixels` (default 4): movement past this turns a left press into a drag.
- Custom interactors: implement `IPointerInteractor` and `NoireDraw3D.Interaction.RegisterInteractor` to add your own grabbable geometry (invisible hotspots, custom widgets) into the same arbitration.

## Extension points

- **Custom interactors**: anything above the scene graph (the gizmo is one) via `IPointerInteractor` + `NoireDraw3D.Interaction.RegisterInteractor`.
- **Manual driving**: set `NoireDraw3D.Interaction.AutoRun = false` and call `NoireDraw3D.Interaction.Update()` from your own ImGui draw code for explicit ordering.
