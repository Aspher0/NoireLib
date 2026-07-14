# NoireInteract & NoireGizmo

The interaction layer for [NoireDraw3D](../README.md). The renderer is **deaf by design** (Law 11: it draws, it reads no input) — it only exposes `NoireDraw3D.Pick(screenPx)`, a raw ray query you must call yourself. This layer is the half the renderer refuses to own: a UI-thread state machine that reads the mouse, tracks gestures across frames, and turns raw input into **hover / click / drag** events on scene nodes and gizmos.

It is the single file group under `Draw3D/` allowed to touch ImGui — the renderer core stays ImGui-free, enforced by a contract test.

## The two problems it exists to solve

**1. A click is a click, not a camera pan.** In FFXIV you move the camera by clicking and dragging. That must never register as a click on a 3D object. A gesture is bound to its owner **at press time**: press on an object → it's yours; press on empty world → it's the game's camera pan, and it *stays* the game's even if it later drags across your object. A left press that moves past the drag threshold is a drag, never a click.

**2. A drag takes the lead of input.** Grabbing a draggable target (a gizmo handle, a movable node) claims the mouse from the game on the very first frame, so the camera never pans underneath the drag.

Both are guaranteed regardless of frame-rate, and the whole decision table is unit-tested headlessly (`InteractionArbiter`).

## Clickable objects

Opt a node in and give it callbacks — it behaves like a button in the world:

```csharp
var node = NoireDraw3D.MainScene.CreateNode("switch");
node.SetMesh(mesh, material);

node.Interactable = true;                       // starts NoireInteract automatically
node.OnHoverEnter = h => Highlight(h.Node);
node.OnClick      = h => Toggle(h.Node);        // h.WorldPoint / h.TriangleIndex tell you exactly where
node.OnRightClick = h => radial.OpenAtMouse();
```

`InteractHit` carries the node, the world point the ray met it, the exact triangle (for meshes created with `keepCpuData`), the ray, and the screen position — enough for "which face did I click," decal-stamp-at-cursor, or spawning a child exactly where clicked.

Make a node draggable and the camera won't pan while you drag it:

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

## Selection

`NoireInteract.Selection` is a `Single`/`Multi` set with Ctrl-toggle / Shift-add, updated automatically on left-click (toggle with `SelectOnClick`). The gizmo and any editor read from it.

**Deselecting is configurable** (`NoireInteract.DeselectOn`, a `[Flags]` `DeselectMode`):

- `ClickEmpty` (default) — a left click on empty world (no object under the cursor, not over UI) that *isn't* a camera pan clears the selection. A click-and-drag (the FFXIV camera pan) never deselects — the arbiter tells the two apart by the drag threshold, so this is safe to leave on during normal play.
- `Key` — clears on `DeselectKey` (default **Escape**; point it at any key). Off unless you add the flag.
- `None` — never auto-deselect; you own the selection.

Combine them (`DeselectMode.ClickEmpty | DeselectMode.Key`). Clearing the selection raises `Selection.Changed`, so a bound gizmo detaches on its own.

## NoireGizmo — move / rotate / scale

Grab any node (or any world matrix) with axis / plane / screen handles.

```csharp
var gizmo = new NoireGizmo(GizmoOp.Universal);   // Translate | Rotate | Scale
gizmo.Attach(node);
gizmo.Options.Space  = GizmoSpace.World;         // World | Local | Screen
gizmo.Options.Snap   = new Vector3(0.5f);        // translate snap; RotateSnapDeg / ScaleSnap too
gizmo.OnEditEnd     += g => Commit();            // one transaction per drag — pair with your undo

// … or bind to any matrix you own:
gizmo.AttachMatrix(() => transform, m => transform = m);
```

### Two backends, the consumer picks — `gizmo.Options.Backend`

Same API surface either way; flip one field without touching call sites.

- **`GizmoBackend.Native` (default) — in-world depth gizmos.** Handles are **real geometry** drawn through `Im` and hit-tested with the render-time camera, so they never wobble under camera motion. Occlusion is set by `Options.Depth` (`GizmoDepth`): the default `OnTopOfObjects` keeps them on top of other 3D objects (never buried in the object they edit) but **occluded by the game world**, so a handle hides behind a wall like real geometry; `AlwaysOnTop` restores full x-ray; `Occluded` is fully depth-tested. For a *hold-to-occlude key*, set `Options.OcclusionHeld` (e.g. `() => ImGui.GetIO().KeyAlt`) — occluded while held, x-ray otherwise, overriding `Depth`. Handles also draw on a high layer so they paint over translucent scene objects (a fading ground plane) rather than under them. Translate/rotate handles follow `GizmoSpace` (World by default); **scale handles are always object-local**. The handles are drawn from a live basis every frame — even mid-drag — so they stay a constant on-screen size through camera distance/zoom and, in `Local` space, rotate with the object as you turn it (the drag *solver* uses the frozen press-time basis, so nothing slips). The handle geometry is emitted on the **render thread** (via `NoireDraw3D`'s render-overlay hook) with the live frame, not from `UiBuilder.Draw`, so its screen-constant sizing tracks the camera zero-latency instead of lagging a frame during a zoom (what used to read as a "swim"); hit-testing and the drag solver still run on the UI thread. Supports `GizmoSpace.Screen` and per-axis universal snapping. This is a client of `NoireInteract` (an `IPointerInteractor`): it shares the one mouse-capture authority, so grabbing a handle blocks the camera and handles out-rank scene nodes under the cursor (still grabbable even where a wall visually occludes them).
- **`GizmoBackend.ImGuizmo` — the classic ImGui gizmo.** Drawn by `Dalamud.Bindings.ImGuizmo` inside a fullscreen, **always-`NoInputs`** host window (ImGuizmo reads ImGui IO directly, so it hit-tests and manipulates through a passthrough window). It is **self-driven**: it runs in a pre-pass *before* scene picking and reports when a handle is hovered/dragged, which makes the frame a hard pass for scene picking (so a handle over a 3D object grabs the handle, not the object). It always `Enable(true)`s (a pure overlay is never greyed by what's under it) and blocks the game camera itself with `SetNextFrameWantCaptureMouse` — deliberately **not** an extra ImGui window: ImGuizmo gates every handle's hover on "is any other window hovered", so a capture window (or toggling the host's own input flags) flickers the highlight on and off every frame; a windowless capture flag doesn't. The renderer's view/projection are fed through with the projection's Z column **rebuilt to a finite-far, non-reversed range** — the game's own projection is reversed-Z *and* infinite-far, which collapses ImGuizmo's cursor-ray unprojection (translate reads 0, rotate NaNs the object, handles shrink to a dot); only clip.z is changed, so the gizmo still overlays the object pixel-for-pixel. Familiar flat look, always on top, no Screen space, coarser universal snapping. It needs a **separate view and projection**: the binding's native table is bound lazily on first use (Dalamud initialises ImGui but not ImGuizmo), and if a frame falls back to the wholesale view-proj camera there's nothing to feed it. In either case the gizmo **auto-falls-back to `Native`** so it never silently vanishes — turn on `NoireInteract.DebugLog` and a one-time `[Gizmo]` line in the log says which (`apiReady`, `fallbackCamera`).

Dispose the gizmo to remove it.

## How input is arbitrated

Every frame `NoireInteract.Update()` (auto-driven from `UiBuilder.Draw`) runs the pure `InteractionArbiter`, then shows a fullscreen invisible ImGui window **only while interacting**. Hovering that window makes ImGui set `WantCaptureMouse`, which is exactly what tells Dalamud to withhold the mouse from the game — so the camera can't pan and nothing is targeted. The rest of the time the game keeps the mouse untouched.

**Self-driven interactors run first.** An interactor may set `SelfDriven` (the ImGuizmo backend does): instead of being ray-hit-tested, it reads ImGui IO through its own always-passthrough window and reports, in a pre-pass *before* hover resolution, whether it owns the mouse this frame. While one does, the frame is a hard pass for scene picking (this is why an ImGuizmo handle over a 3D object grabs the handle, not the object). It blocks the game camera itself with `SetNextFrameWantCaptureMouse` rather than a capture window — a second hovered window would trip ImGuizmo's internal hover-gate and flicker it.

**UI is a hard pass.** Whenever the cursor is over another UI surface, Draw3D neither hovers, picks, nor captures — the object *behind* the UI is never touched (`ForeignUiHasMouse`). Two surfaces count: a **foreign ImGui window** (another plugin's — detected from `WantCaptureMouse`, with our own capture window discounted so we never mistake ourselves for foreign), and **native game UI** (a HUD window, inventory, friend list — detected from the game via `NoireDraw3D.IsCursorOverGameUi`, since native addons are not ImGui and never set `WantCaptureMouse`). Near-fullscreen transparent overlay roots (nameplates, fly-text) are excluded so they don't blanket the viewport.

**Walls are a hard pass too (`WallOcclusionMode`).** By default (`HoldToClickThrough`) a wall / house / terrain in front of a 3D object blocks hovering and clicking it — the pick compares the object's distance against the game's own screen raycast to the nearest world surface. Hold the click-through key (`ClickThroughHeld`, default **Alt** — Ctrl/Shift are the selection modifiers) to reach objects behind geometry, or set the mode to `Off` (always click through / x-ray) or `Always` (never click through). Gizmo handles are exempt from pick occlusion — they stay grabbable even where a wall visually occludes them.

**Decals pick their shape, not their box.** A ground-decal node is hit-tested against its rendered footprint SDF (the ring's annulus, the sector's wedge, the rect) on the real ground surface — mirroring `GroundDecal.hlsl` — so hovering the hole of a ring or outside a sector's arc correctly misses.

- `BlockGameMouseOnHover` (default **false**): the playable default — hovering a plain object never claims the mouse, so the camera still pans/zooms and the world stays clickable straight through a highlighted object. A plain left-click still selects (fires `OnClick`) but coexists with the game (the click also reaches the world behind), and a **draggable** target (a gizmo handle) always still takes the lead of its drag so the camera can't move under it. Set **true** for the aggressive, ImGui-consistent mode where hovering claims the mouse and consumes the click — tidy for a modal editor, but it blocks camera/zoom while the cursor rests on an object.
- `DragThresholdPixels` (default 4): movement past this turns a left press into a drag.
- Custom interactors: implement `IPointerInteractor` and `NoireInteract.RegisterInteractor` to add your own grabbable geometry (invisible hotspots, custom widgets) into the same arbitration.

## Extension points

- **Custom interactors** — anything above the scene graph (the gizmo is one) via `IPointerInteractor`.
- **Manual driving** — set `NoireInteract.AutoRun = false` and call `Update()` from your own ImGui draw code for explicit ordering.
