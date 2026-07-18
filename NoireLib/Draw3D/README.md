# NoireDraw3D

A real D3D11 world renderer for Dalamud plugins. It draws real 3D geometry into the game's frame - glowless and color-exact (the world's post-processing has already run), hardware-clipped at the screen edges, and always under your plugin windows. By default it composites **under the game's native UI** so HUD and nameplates read on top (this uses a render-thread hook on the present composition); set `NativeUi.Layering = OverEverything` to composite over everything with no hook at all. There is no ImGui and no 2D-projected fallback anywhere in it: when it cannot render correctly, it renders nothing and tells you why.

Full design rationale, invariants and acceptance gates live in [`docs/Draw3D V2 Proposal.md`](https://github.com/Aspher0/NoireLib/blob/main/docs/Draw3D%20V2%20Proposal.md).

## Quick start - markers in three lines

The immediate layer redraws every frame; anything you stop requesting vanishes. Call it from any per-frame callback:

```csharp
// e.g. inside your plugin's UiBuilder.Draw handler:
NoireDraw3D.Im.DrawDonut(player.Position, innerRadius: 3f, outerRadius: 5f, new Vector4(1f, 0.6f, 0.1f, 0.9f));
NoireDraw3D.Im.DrawSector(boss.Position, boss.Rotation, MathF.PI / 4f, 0f, 20f, new Vector4(1f, 0.2f, 0.2f, 0.8f));
NoireDraw3D.Im.DrawLine(a, b, width: 0.1f, new Vector4(0.3f, 0.8f, 1f, 1f));
```

Shapes default to **ground decals**: they project onto the terrain and hug stairs and slopes exactly like the game's own telegraphs. Style them with `ImShapeStyle`:

```csharp
NoireDraw3D.Im.DrawCircle(pos, 4f, color, new ImShapeStyle
{
    Placement = ImShapePlacement.Flat,  // flat mesh instead of terrain projection
    Additive = true,                    // energy-glow blending
    IgnoreDepth = true,                 // x-ray through walls (flat shapes only)
    OutlineWidth = 0.12f,               // strong decal rim (decals)
});
```

> **Zero-latency rule:** `Im` calls made inside `Scene3D.OnPrepareFrame` or an `ISceneFeature` render *this* frame; calls made elsewhere render at most one frame late. For markers you will never notice - it is documented so nobody debugs it as a bug.

> **What you may do there:** those two callbacks run on the **render thread**, and on the default under-UI path they run *mid-frame, from inside one of the game's own D3D calls*. Touch the scene graph, `Im`, and your own state - nothing else. Reading game state, printing to chat, or calling a Dalamud game service from there re-enters the game underneath itself; do that work on the framework thread and leave the result somewhere the callback can read.

## Retained scenes - the "FF14 Blender"

For long-lived content, build nodes once and mutate them. `scene.Spawn` (and the `Add*` primitive shortcuts) collapse "create node → build mesh → attach → track for disposal" into one call - the node **owns** the mesh, so there is nothing to track:

```csharp
var scene = NoireDraw3D.MainScene;

var donut = scene.AddTorus(2f, 0.3f, Material.Lit(new Vector4(0.9f, 0.9f, 1f, 1f)), somePosition, "waymark");
// equivalently: scene.Spawn(MeshBuilder.Torus(2f, 0.3f), material, somePosition, "waymark");

// Fluent transforms chain off the returned node:
donut.At(somePosition).RotateY(angle).Scale(1.2f);

// Later, from any thread:
donut.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
donut.Visible = someCondition;

// When done: destroying the node frees its owned mesh; or dispose the whole scene at once.
donut.Destroy();
```

**The scene is an ownership scope.** `Scene3D` is `IDisposable`: `scene.Dispose()` destroys every node (freeing owned meshes), disposes everything handed to `scene.Own(...)` (a shared mesh, a texture, an imported model, an editor) and removes the scene from the renderer - no parallel bookkeeping lists. Create extra scenes with `NoireDraw3D.CreateScene("name")`; `MainScene` is permanent. The manual model is intact for the power case - `scene.Spawn(sharedMesh, material, ...)` references a mesh you own (one mesh, many nodes - the instancing path).

`MeshBuilder` ships the full shape catalog - `Quad`, `Box`, `Disc`, `Ring`, `Sector`, `Sphere`, `Cylinder`, `Cone`, `Torus`, `Arrow`, `ExtrudePath` - all unit-sized, +Y up, ready to scale via the node; there is also an appendable `new MeshBuilder()` instance form to mix primitives and raw geometry into one mesh, and raw-vertex `scene.Spawn(vertices, indices, ...)` for anything not in the catalog. Identical mesh+material combinations are automatically instanced into single draw calls.

### Materials

Immutable records - share them freely, derive variants with `with`:

```csharp
var decal     = Material.Decal(DecalShape.Ring, new Vector4(1f, 0.5f, 0f, 0.9f),
                               shapeParams: new Vector4(0.7f, 0f, 0f, 0.6f)); // x = inner ratio, w = fill opacity
var glass     = Material.Unlit(new Vector4(0.4f, 0.8f, 1f, 0.35f), depthFade: 0.4f); // soft seam where it meets walls
var solid     = Material.Lit(new Vector4(1f, 1f, 1f, 1f));                            // opaque, z-tested against other meshes
var textured  = Material.UnlitTextured(myTexture) with { Cull = CullMode.None };
var custom    = Material.Custom("myPipeline", new Vector4(0f, 1f, 1f, 1f));           // your HLSL via RegisterPipeline
```

> A ground decal paints its shape onto the world surface, hugging terrain, stairs and walls (reconstructed from the game depth). **Characters** you list with `ExcludeObjects(pred)` are cut out along their **exact game-stencil silhouette** — legs, feet and tail included — with no volume, no collision: the decal simply is not painted on them. The `ExcludeObjects` cylinders are only a coarse gate picking *which* characters (the stencil is the cut), so the radius is safe to widen (`radiusScale`) and never holes the ground. A character you *don't* list is painted over. The stencil value that marks characters is `NoireDraw3D.CharacterStencilValue` (default `0x08`, discoverable via `/noire3d stencil`; set 0 to disable). Non-character targets (furniture, terrain) share the world's stencil value, so this excludes characters only.

- `Surface` **locks the decal to a surface by constraining how its box may be oriented** (the projection itself is a single rule — the shape lives in the box's footprint and sweeps along the box's local Y; the box's orientation decides which surface it lands on). The mode just forbids rotating the box out of its plane, keeping heading (yaw), scale and position:
  - `DecalSurface.Ground` (default): the box is kept **horizontal** — projects straight down onto the floor/terrain; rotating it toward vertical has no effect. The classic ground decal.
  - `DecalSurface.Wall`: the box is kept **vertical** — projects horizontally into the wall it faces (aim it with yaw); rotating it toward flat has no effect. Size the box so it reaches the wall.
  - `DecalSurface.Both`: **free** — rotate the box however you like and its orientation decides the surface (upright = ground, tipped 90° = wall, in between = a hybrid).
- `Projection = DecalProjection.HighestOnly` paints only the **topmost** surface within the decal box per column (a tabletop, not the floor beneath it). Needs `CollisionHeightMap` on (the default), `TopSurfaceThreshold` above 0, and the covering object to have collision. It is the *only* consumer of those two - they do nothing to any other decal.
- `DepthFade` feathers the edge where translucent shapes intersect world geometry.
- `Depth = DepthMode.Ignore` draws through walls; `WhenDepthUnavailable` decides what happens on frames where the game's depth buffer can't be read.
- `UnorderedBatching = true` lets hundreds of identical translucent markers collapse into one instanced draw.

> **Seeing the shape.** Call `node.ShowDecalShape()` to trace what the decal actually paints - the same circle / ring / pie / rect its SDF evaluates - as a closed 3D line lying on the decal's own plane, and `node.HideDecalShape()` to turn it back off. It follows `Shape`, `ShapeParams` and the `Surface` constraint live, so it tracks the decal through any edit. It is a per-frame overlay driven for you (no plumbing); default color is the decal's own, or pass one: `node.ShowDecalShape(new Vector4(1f, 1f, 0f, 1f))`.
>
> It traces the shape, not the projection box, on purpose: that box's footprint is the SDF's *bounding square* and its sweep runs well above and below the surface, so for anything but a full-footprint circle it is much larger than the paint and centred where the paint is not (a pie's box is centred on its apex and spans twice its radius). It reads as stray lines crossing the view rather than as the decal.
>
> `ShowDecalShape()` is per-node. For **every** decal at once - including the immediate layer's grounded shapes, which have no node to opt in with - use `NoireDraw3D.Diagnostics.DecalShapeOutlines` (or `/noire3d decalshapes`).

## Importing models (glTF)

```csharp
var model = await GltfLoader.LoadAsync(@"C:\models\prop.glb");
model.AttachTo(NoireDraw3D.MainScene);      // O(1), any thread
model.Root.LocalPosition = spawnPosition;
// ...
model.Dispose();                            // detaches and releases its meshes/textures
```

Blender → *File → Export → glTF 2.0* just works (base color + texture; PBR maps/skins/animations are skipped and logged). The import logs one summary line - primitive count, textured vs. flat materials, decode failures - so a wrong-looking model is self-diagnosing. **FBX:** convert once with `FBX2glTF` or Blender - NoireLib will never ship the FBX SDK.

> **Vertex colors are off by default.** FFXIV-derived character exports carry a per-vertex `COLOR_0` channel the game uses as shader *data* (wetness / wind / blend masks), not albedo - importing it as a tint paints the model in psychedelic colors. Pass `importVertexColors: true` (on `LoadAsync` / `scene.LoadModel`) only for assets that genuinely author vertex colors.

**Level of detail (opt-in).** Pass `generateLods: true` when loading a model to build a chain of progressively coarser meshes (a quadric-error decimation, logged in the summary line); the renderer then draws the level that fits the object's size on screen, so a heavy model shrinking into the distance stops paying for triangles it no longer resolves. It is **off by default** - a full-detail model renders cheaply, so LOD is there for scenes with *many* heavy models at once, not for a single one. Culling, picking and bounds always use the full-resolution mesh.

## Performance

Everything here lives on `NoireDraw3D.Performance` (or a `Configure(c => c.Performance...)` batch). Every knob is opt-in and off the default path:

```csharp
var model = await scene.LoadModelAsync(path, generateLods: true); // build the LOD chain at import

NoireDraw3D.Performance.Lod = true;             // use LOD chains where present (default on; no-op without a chain)
NoireDraw3D.Performance.LodBias = 1f;           // >1 = drop detail sooner; <1 = keep it longer
NoireDraw3D.Performance.LodScreenRadii = new[]{160f, 60f, 22f}; // px radii where each LOD takes over

NoireDraw3D.Performance.MaxDrawDistance = 0f;   // 0 = unlimited; else skip objects past this (world units)
NoireDraw3D.Performance.MinScreenPixels = 0f;   // 0 = off; else skip objects smaller than this on screen

NoireDraw3D.Performance.Supersample = 1f;       // 1 = off; 2 = 2x2 SSAA (fixes distance shimmer, 4x the layer fill)
```

LOD and the culls all default off, so nothing is swapped or disappears unexpectedly. Reach for LOD in scenes with many heavy models; the culls pay off with many far or tiny objects (`MinScreenPixels = 1` is a near-free win there, and outlined/selected objects are exempt so a highlight never vanishes). All of it applies to the main game view only, never to a render-to-texture pass.

> **Anti-aliasing.** The 3D layer has no MSAA of its own (the game world does), so a *dense* mesh - a detailed glTF model - shimmers along its edges at a distance where the anti-aliased world does not. `Performance.Supersample = 2` renders the layer at 2x and box-downsamples it at composite, which removes the shimmer at the cost of 4x the layer's fill and VRAM (opt-in for that reason). Model LOD is the lighter alternative: thinning distant geometry also removes the aliasing, trading detail for fill instead.

> **Picking is BVH-accelerated.** Hover/click hit-testing against a `keepCpuData` mesh uses a bounding-volume hierarchy built once per mesh, so hovering a dense imported model (hundreds of thousands of triangles) costs an O(log n) ray query per frame, not a scan of every triangle.

## Textures

```csharp
var icon = await TextureLoader.FromGameIconAsync(60073);
var png  = await TextureLoader.FromFileAsync(path);
var raw  = TextureLoader.FromRgba(pixels, width, height);
var live = ExternalTexture.FromSharedHandle(handle, ntHandle: true); // another process renders it (browser, etc.)
```

Every returned `GpuTexture` is yours to dispose. External shared-handle textures make "a live browser screen on a quad in the world" an ordinary textured material.

## Render-to-texture

```csharp
var view = NoireDraw3D.CreateRenderView(scene, new Camera3D(camPos, lookAt), 512, 512);
material = Material.UnlitTextured(view.Texture!); // minimap portals, mirrors, thumbnails
```

> A render view re-renders **this scene** from a second camera - it shows your 3D objects, not the game world. There is no way to re-photograph the game world from a different angle (the world only exists as pixels already composited for the game camera). The closest is rendering the collision proxy below into the view.

## World-projected decals (real collision)

The screen-space `Material.Decal` projects onto whatever is in the depth buffer. When you want a decal that clips to the **actual world surface** - draping over terrain slopes, climbing onto walls and furniture, never "cut" by an actor standing in front - project it onto the game's real collision geometry instead. Everything here is **framework-thread only** (it reads the live collision scene) and fails soft (no surface ⇒ `null`):

```csharp
// A decal that conforms to the real ground/walls/furniture under `pos`, facing up:
scene.SpawnWorldDecal(pos, Vector3.UnitY, width: 6f, height: 6f, Material.UnlitTextured(tex), depth: 3f);

// The raw collision near a point, as a mesh (debug/preview, or feed your own logic):
scene.SpawnWorldGeometry(pos, radius: 20f, Material.Lit(new Vector4(0.4f, 0.8f, 1f, 0.4f)) with { Cull = CullMode.None });

// Or go lower: get the geometry / projected footprint yourself.
var geo   = WorldGeometry.Collect(pos, radius: 20f);                       // terrain + models + furniture + dynamic objects
var decal = WorldGeometry.ProjectDecal(pos, Vector3.UnitY, 6f, 6f);        // clipped, UV-mapped MeshData
```

The source is the same collision world a navmesh tool walks (streamed terrain, placed background models, housing furniture, and any dynamic object that registers a collider). `includeAnalytic: true` also pulls in box/cylinder/sphere/plane colliders (invisible walls, trigger volumes). `/noire3d worldgeo` toggles a live preview of it around you.

## Picking

```csharp
NoireDraw3D.PickInputGate = () => !myUiWantsTheMouse; // you decide when the mouse is free
var hits = NoireDraw3D.Pick(mousePos);                // nearest first; exact triangles for meshes built with keepCpuData
```

## Layer controls

| Property | What it does |
|---|---|
| `NoireDraw3D.Enabled` | Master switch (also re-arms the renderer after a fault). |
| `NoireDraw3D.LayerOpacity` | 0–1 fade of the whole 3D layer. |
| `NoireDraw3D.NativeUi.Layering` | **Default `UnderGameUi`.** Where the layer lands in the game's frame. `UnderGameUi` composites via a render-thread hook on the present composition, before the game draws its UI, so the UI is always on top. `OverEverything` composites over the backbuffer at present time, which is the only mode that can decide *per element* what the layer covers. Falls back to `OverEverything` on any frame the injection can't run. |
| `NoireDraw3D.NativeUi.KeepUiOnTop` | **Default true. Only applies under `OverEverything`.** Masks the layer per-pixel so the HUD, addons and nameplates read on top. Letter-exact and rectangle-free: the mask is the *difference* between the present buffer photographed before and after the game drew its UI into it. Rides the same render-thread hook, so a frame with no injection point has no "before" and composites unmasked. |
| `NoireDraw3D.NativeUi.Nameplates` | **Default `DepthAware`. Honoured under both layering modes.** Whether the game's own nameplates are occluded by 3D objects in front of them. Under the game UI it stamps depth for the game's plate pass to test; over everything it gates where the `KeepUiOnTop` mask applies. `Covered` requires `OverEverything`. Fail-soft. |
| `NoireDraw3D.NativeUi.NameplateDim` | **Default 0. Only applies under `OverEverything`** with `KeepUiOnTop` on, and only to a plate `Nameplates` decided is covered. How much a covered plate still shows through: 0 = fully covered, toward 1 = faintly readable. |
| `NoireDraw3D.KeepDrawingWhenUiHidden` | Keep **the 3D layer** rendering in cutscenes/GPose/UI-hide. Affects only the layer - your windows are yours (see below). |
| `NoireDraw3D.IsGameUiHidden` | Whether the game UI is hidden (user toggle / cutscene / GPose), read from the game rather than Dalamud - so it stays truthful whatever the overrides are doing. |
| `NoireDraw3D.Lighting` | Ambient + directional half-Lambert parameters for `Lit` materials. |
| `NoireDraw3D.OnFault` | Raised when the self-disable ladder trips (a pipeline, feature, or the renderer disabled itself). |

> **The two layering modes keep the UI readable by opposite means, and both are letter-exact.**
>
> - **`UnderGameUi`** composites *before* the game draws its UI, so the game paints its HUD over the layer by itself. Nothing to configure, nothing to mask, no cost. The trade is that the UI always wins: the layer can never cover any of it.
> - **`OverEverything`** composites *after*, so the UI is already there - which is exactly what makes it decidable. `KeepUiOnTop` masks the layer back off the UI, and because the UI now exists, a nameplate can be `Covered` outright or dimmed rather than merely occluded or not.
>
> **Where `OverEverything`'s mask comes from.** Not the backbuffer's alpha channel - FFXIV writes no UI coverage there, which is why an earlier design that read it was inert in every frame it ever ran. Instead Draw3D photographs the game's present buffer at the pre-UI injection point and again at present time, and **differences the two**: wherever the image changed, the UI painted. Same texture both times, so the snapshots always agree on format and resolution, and antialiased glyph edges come out as partial coverage for free.
>
> Two consequences worth knowing. The mask rides the same render-thread hook the under-UI path uses, so a frame whose injection point cannot fire has no "before" photo and composites unmasked. And a UI pixel that blends to exactly the colour beneath it reads as no-UI - correct, since it is invisible either way.
>
> `/noire3d uimask` reports whether the difference is finding the UI at all, and the sampled grid it is looking at. If protection ever looks inert, that command answers it in one line rather than leaving you guessing.

> **UI-hide, and your windows.** The 3D layer renders inside `UiBuilder.Draw`, and Dalamud's four `Disable*UiHide` flags are the only way to keep that callback firing. NoireDraw3D therefore **holds them for the layer's lifetime** and decides for itself whether to draw, so `KeepDrawingWhenUiHidden` means only what it says.
>
> The consequence is that **Dalamud will not auto-hide your windows** - that call is yours now, and it is one line:
>
> ```csharp
> public override bool DrawConditions() => !NoireDraw3D.IsGameUiHidden;
> ```
>
> The two are fully independent: 3D on with windows hidden, windows up with no 3D, either, or neither. `/noire3d stats` reports `skipped (ui-hidden N)` when the layer sits a frame out.

## Custom shaders

```csharp
NoireDraw3D.RegisterPipeline("MyPulse", hlslSource);   // #include "Common.hlsli", vs/ps entry points
var mat = new Material { CustomPipeline = "MyPulse", Color = ... };
```

A compile error disables only that pipeline and logs the full compiler output.

## Diagnostics - `/noire3d`

| Command | Purpose |
|---|---|
| `/noire3d validate` | Projection parity vs the game's own WorldToScreen over 10 frames (gate: ≤ 1 px). |
| `/noire3d probe` | Forces a fresh depth calibration, then reads real depth-buffer values back and compares them to the calibrated prediction (gate: ≥ 90 % within 1e-3). |
| `/noire3d stats` | Frame/draw/skip counters + GPU timings - "why is nothing drawing" is always answerable. |
| `/noire3d wire` | Wireframe toggle. Ground decals carry no mesh to wireframe (their shape lives in the pixel shader), so they trace the outline of what they paint instead - the same line `ShowDecalShape()` draws. |
| `/noire3d decalshapes` | Traces what **every** decal paints as an outline, over normal rendering - retained decals and immediate-layer grounded shapes alike. The global "where is this decal actually landing"; an `ImDraw3D` shape has no node to call `ShowDecalShape()` on, so this is the only way to outline one. Implied by `wire`. |
| `/noire3d camtrace [frames]` | Camera-phase "swim" trace: pixel-anchored residuals for the struct-camera history and the captured GPU camera (the `cap` row), plus the inject-vs-fallback split and the capture state (run it while panning/zooming hard). |
| `/noire3d cbprobe [frames]` | Camera-constant discovery report: every constant buffer observed on the upload paths, its update mechanism and VS slot, and the candidate camera windows with match errors. The answer to "is the capture locked, and on what". |
| `/noire3d gpucam` | A/B toggle between the captured GPU camera constants (default, swim-free) and the struct snapshot. Turning it off deliberately reintroduces the old load-scaled swim for comparison. |
| `/noire3d heightmap` | Toggles `CollisionHeightMap` - the top-down collision height-map. Only `DecalProjection.HighestOnly` reads it, so with no `HighestOnly` decal on screen there is nothing to see. It does **not** cut characters out of decals (that is `ExcludeObjects` + `CharacterStencilValue`). |
| `/noire3d reset` | Resets counters and re-arms the renderer. |
| `/noire3d ontop` | Toggles `NativeUi.Layering` (under the game UI vs over everything). |
| `/noire3d platedepth` | Toggles `NativeUi.Nameplates` (depth-aware vs always-visible nameplates). |
| `/noire3d uimask` | Reports the over-everything UI mask: whether the render-thread hook is landing its pre-UI snapshot, the health verdict, and the per-sample difference grid. The answer to "is keeping the UI on top actually doing anything". |
| `/noire3d plates` | Per-nameplate policy factors from last frame, with the distances that decided them. Separates the two ways nameplate layering looks broken on screen but is not the same bug: factor 1 on a covered plate means the mask never found its pixels; factor 0 on a plate that should read on top means the occlusion test decided wrongly. |
| `/noire3d rtlog` | Captures one frame's render-target bind sequence to the log (injection-point diagnostics). |

Commands are global across plugins; everything is also available programmatically via `NoireDraw3D.Diagnostics`.

The **visual showcase** - the showcase gallery scene, the world-geometry collision preview, glTF import, and the gizmo-backend toggle - lives in the standalone **`NoireDraw3DDemoPlugin`** (in this repo's solution), an ImGui front-end built entirely on this public API. It also exposes a scenes/decals playground (including the wall/ground/both surface filter), a full per-object inspector, and a live editor for every global knob, one page per area.

## Rules of the road

- **Ownership:** whoever creates a `Mesh`/`GpuTexture` disposes it. Scenes and nodes only reference assets; disposing an asset in use is safe (draws skip it, counted, never a crash).
- **Threading:** scene mutation and asset creation are safe from any thread. `Im` calls belong in draw-cycle callbacks.
- **Camera:** the layer projects with the **exact camera constants the GPU rasterized the frame with**, self-discovered from the game's own constant-buffer uploads (never a fixed offset), so world-anchored content stays pixel-locked to the world during violent camera motion at any frame-rate and any load. Falls back to a struct snapshot automatically; `/noire3d gpucam` A/Bs the two and `/noire3d cbprobe` reports the discovery.
- **Depth:** world occlusion is **self-calibrating** - the depth buffer's value convention is derived analytically from the game's own projection, never assumed, so a patch that changes the projection is handled automatically instead of producing inverted visuals. `/noire3d probe` cross-checks the calibration against the game's collision surfaces.
- **Failure:** everything fails soft, loudly once - a broken shader, unreadable depth buffer, or missing camera degrades the narrowest feature and never takes your plugin down.
