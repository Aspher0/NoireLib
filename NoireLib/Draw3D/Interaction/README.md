# NoireInteract & NoireGizmo

The interaction layer for [NoireDraw3D](../README.md). The renderer reads no input and only exposes `NoireDraw3D.Pick(screenPx)`. This layer reads the mouse on the UI thread, tracks gestures across frames, and raises **hover / click / drag** events on scene nodes and gizmos.

It is the only part of `Draw3D/` that touches ImGui. A contract test enforces it.

**One front door.** Hover, click and selection live on `scene`, `node` and `editor`. The global input knobs are on **`NoireDraw3D.Interaction`**: gestures, obstacle occlusion, deselect rules, multi-select modifiers, debug, custom interactors. Each forwards to `NoireInteract`.

## The two problems it exists to solve

**1. A camera pan is never a click.** A gesture belongs to its owner **from the press**. A press on empty world is the game's camera pan, even if it later drags across an object. A left press past the drag threshold is a drag.

**2. A drag owns the mouse.** Grabbing a draggable target claims the mouse on the first frame. The camera never pans underneath.

The decision table is unit-tested headlessly (`InteractionArbiter`).

## Clickable objects

`MakeSelectable` opts a node in, adds a hover highlight, and routes a left-click into the node's **scene selection**. `MakeInteractable` is the hover and click variant without selection. Your own `OnHoverEnter` / `OnClick` handlers are kept:

```csharp
var node = scene.Spawn(MeshBuilder.Box(), material, pos, "switch")
                .MakeSelectable();              // hover highlight + click-to-select
node.OnClick = h => Toggle(h.Node);            // still yours; runs alongside select
```

> The highlight restores the node's resting tint on exit and never stacks. Pass your own transform (`MakeSelectable(t => t * 2f)`), return the input for no feedback, or call `ClearHoverHighlight()`.

A model made of several meshes selects as **one object** through `SelectionProxy`: parent the parts under a group node and point each proxy at it. Clicking a part selects and moves the whole. Hover and `OnClick` stay on the part. The editor's `SelectionOutline` covers the selected node's subtree.

```csharp
var root = scene.CreateNode("chair");
foreach (var part in parts)
{
    part.SetParent(root);
    part.MakeSelectable().SelectionProxy = root;
}
```

Or opt in by hand with callbacks:

```csharp
node.Interactable = true;                       // starts NoireInteract automatically
node.Selectable   = false;                      // hover/click only, no selection (MakeInteractable does this)
node.OnHoverEnter = h => Highlight(h.Node);
node.OnClick      = h => Toggle(h.Node);        // h.WorldPoint / h.TriangleIndex tell you exactly where
node.OnRightClick = h => radial.OpenAtMouse();
```

`InteractHit` carries the node, the world hit point, the triangle (for `keepCpuData` meshes), the ray and the screen position.

A draggable node blocks the camera while dragged:

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

`scene.Selection` (an `InteractSelection`) is what is selected in that scene. A left-click on a selectable node updates it (globally `NoireDraw3D.Interaction.SelectOnClick`, per node `node.Selectable`). Scenes select independently. Read or drive it with `Selection.Nodes`, `Selection.Primary`, `Selection.Count`, `Selection.Contains(node)`, the `Selection.Changed` event, and `SetSingle` / `Add` / `Remove` / `Clear`:

```csharp
scene.Selection.Mode = SelectionMode.Multi;   // allow more than one (or editor.MultiSelect, see below)
scene.Selection.MaxCount = 8;                 // cap it (0 = unlimited)
onDeletePressed = () =>
{
    foreach (var n in scene.Selection.Nodes.ToArray())
        n.Destroy();
    scene.Selection.Clear();
};
```

**Multi-select.** `Selection.Mode` is `Single` or `Multi`. `Selection.MaxCount` drops the oldest past the cap. The modifiers are predicates on `NoireDraw3D.Interaction`:

- `ToggleSelectionHeld` (default **Ctrl**): a click toggles the node.
- `AddSelectionHeld` (default **Shift**): a click adds the node. `() => true` adds on every click.

**Deselecting** is `NoireDraw3D.Interaction.DeselectOn`, a `[Flags]` `DeselectMode`. It clears every scene's selection:

- `ClickEmpty` (default): a left click on empty world, not over UI and not a camera pan.
- `Key`: `DeselectKeyHeld` (default **Escape**). Off unless added.
- `None`: never.

Combine them (`DeselectMode.ClickEmpty | DeselectMode.Key`). Clearing raises `Selection.Changed`. A bound gizmo detaches.

**`DeselectKeyHeld` is a held test.** The press edge is detected for you. Read the key **from the OS**:

```csharp
NoireDraw3D.Interaction.DeselectOn = DeselectMode.ClickEmpty | DeselectMode.Key;
NoireDraw3D.Interaction.DeselectKeyHeld = () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.DELETE);
```

Dalamud forwards non-modifier keys to ImGui only while a text field is focused. `ImGui.IsKeyPressed(ImGuiKey.Delete)` is **always false while playing**. Modifiers are always forwarded. `KeybindsHelper.IsAsyncKeyDown` reads the hardware state. The key only counts while the game window is in front and the cursor is over the viewport.

## SceneEditor: the packaged controller

"Click to select, gizmo follows" is one object, **owned by** the scene:

```csharp
var editor = scene.CreateEditor(GizmoOp.Universal);
editor.MultiSelect = true;                 // scoped: restored when the editor/scene is disposed
editor.Gizmo.Space = GizmoSpace.Local;     // configure via the flattened gizmo surface
editor.Gizmo.Snap  = 0.5f;
editor.SelectionOutline = new Vector4(1f, 0.8f, 0.2f, 1f);   // optional: outline selected nodes
foreach (var n in scene.Roots) n.MakeSelectable();
// teardown: nothing; scene.Dispose() disposes the editor too (editor.Dispose() is optional early teardown).
```

The editor attaches its `Gizmo` to the current pick: one node or the whole group. When scenes overlap, the front-most hit wins and only its scene's editor updates. `MultiSelect` drives the selection mode, restored on dispose.

## NoireGizmo: move / rotate / scale

Grab any node or world matrix with axis, plane and center handles.

The common knobs are on the gizmo and delegate to `Options`. A scalar `Snap` covers the usual case:

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

`scene.CreateEditor` usually makes the gizmo for you. Use a bare `NoireGizmo` to drive a matrix that is not a scene node.

**Groups.** `AttachGroup` shows one gizmo at the set's centroid and transforms every member around it. A group of one behaves like `Attach`.

**Scaling is relative to the size at bind.** Repeated scaling does not compound, and an axis dragged near zero can grow back.

**Drag feedback.** While dragging, the gizmo draws an anchor at the pre-drag center, a guide line to the current center, and the live amount. Both backends draw it. The readout measures along the gizmo's press-time axes. ImGuizmo's own text is hidden for the manipulate call only, since it reports a world-space delta. `Options.ShowDragFeedback = false` turns it off.

Translation snaps per axis (`Snap`, a `Vector3`). Rotation (`RotateSnapDeg`) and scale (`ScaleSnap`) take one value. Both backends honour all three.

### Two backends, the consumer picks (`gizmo.Options.Backend`)

Same API either way. Both honour `Options.Space` (World / Local). Scale handles are always object-local.

- **`GizmoBackend.Native`: in-world gizmos.** Handles are geometry drawn through `Im` on the render thread, hit-tested in screen space against the render-time camera, constant in screen size. `Options.Depth` (`GizmoDepth`) sets occlusion: `OnTopOfObjects` (default) draws over other 3D objects but behind the game world, `AlwaysOnTop` is x-ray, `Occluded` is fully depth-tested. `Options.OcclusionHeld` (for example `() => ImGui.GetIO().KeyAlt`) occludes while held and overrides `Depth`. In `Local` space the handles rotate with the object. The drag solver uses the press-time basis. It is an `IPointerInteractor`: handles out-rank scene nodes, and a handle behind an obstacle is ungrabbable unless the depth mode is `AlwaysOnTop`.
- **`GizmoBackend.ImGuizmo` (default): the classic ImGui gizmo.** `Dalamud.Bindings.ImGuizmo` draws inside a fullscreen `NoInputs` window. It is **self-driven**: it runs before scene picking, and a hovered or dragged handle wins over the object behind it. It blocks the camera with `SetNextFrameWantCaptureMouse`, because ImGuizmo drops hover whenever another window is hovered. The game's reversed-Z infinite-far projection breaks ImGuizmo's ray unprojection. Its Z column is rebuilt finite. Scale uses `Scaleu`, which keeps translate and rotate in the chosen space. For a group, `Local` aligns to the first selected node. It **falls back to `Native`** when its binding fails or the fallback camera is in use. `NoireInteract.DebugLog` logs a one-time `[Gizmo]` line saying which.

Dispose the gizmo to remove it.

## How input is arbitrated

Every frame `NoireInteract.Update()`, driven from `UiBuilder.Draw`, runs the `InteractionArbiter`, and shows a fullscreen invisible window **only while interacting**. Hovering it sets `WantCaptureMouse`, and Dalamud withholds the mouse from the game.

**Outside the game window nothing reacts.** When the cursor is off the viewport or the game is not in front, nothing hovers, picks, selects, deselects, drags or captures.

**Self-driven interactors run first.** An interactor with `SelfDriven` (the ImGuizmo backend) reads ImGui IO through its own passthrough window. While it owns the mouse, scene picking is skipped. It blocks the camera with `SetNextFrameWantCaptureMouse`.

**UI blocks interaction.** Over another UI surface nothing hovers, picks or captures (`ForeignUiHasMouse`): a **foreign ImGui window** (from `WantCaptureMouse`, our own capture window discounted), or **native game UI** (from `AddonHelper.HitTest`). Game UI is tested on its **collision nodes**. The empty margins around HUD elements do not block. Near-fullscreen overlay roots (nameplates, fly text) are excluded. `NoireDraw3D.Interaction.GameUiBlocksInteraction = false` turns game-UI blocking off. With `DebugLog` on, `[Interact/Gate]` logs why a spot is blocked, naming the addon.

**Obstacles (`ObstacleOcclusionMode`).** `Off` by default. `HoldToClickThrough` makes whatever stands in front of an object block it, unless `ClickThroughHeld` (default **Alt**) is held. `Always` never clicks through. The occluder is read from the **game depth buffer**: walls, terrain, props, characters, mounts and NPCs. When depth is unreadable it falls back to the collision raycast. The ground can occlude an object at grazing angles. Native gizmo handles follow the same rule unless their depth mode is `AlwaysOnTop`.

**Decals pick their shape.** A ground decal is hit-tested against its rendered footprint on the ground, like `GroundDecal.hlsl`. The hole of a ring misses.

- `BlockGameMouseOnHover` (default **false**): hovering never claims the mouse. The camera still pans and zooms. A left-click still selects and also reaches the world behind. A draggable target still owns its drag. **true** claims the mouse on hover and consumes the click, and blocks the camera while the cursor rests on an object.
- `NoireDraw3D.Interaction.DragThresholdPixels` (default 4): movement past this turns a left press into a drag.
- Custom interactors: implement `IPointerInteractor` and call `NoireDraw3D.Interaction.RegisterInteractor`.

## Extension points

- **Custom interactors**: `IPointerInteractor` + `NoireDraw3D.Interaction.RegisterInteractor`.
- **Manual driving**: `NoireDraw3D.Interaction.AutoRun = false`, then call `NoireDraw3D.Interaction.Update()` from your own draw code.
