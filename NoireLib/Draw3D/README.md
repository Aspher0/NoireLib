# NoireDraw3D

A D3D11 world renderer for Dalamud plugins. It draws 3D geometry into the game's frame, color-exact after the world's post-processing, clipped at the screen edges, and always under plugin windows. By default it composites **under the game's native UI** through a render-thread hook on the present composition. `NativeUi.Layering = OverEverything` composites over everything at present time. When it cannot render correctly it renders nothing and logs why.

## Quick start: markers in three lines

The immediate layer redraws every frame. Anything no longer requested vanishes. Call it from any per-frame callback:

```csharp
// e.g. inside the plugin's UiBuilder.Draw handler:
NoireDraw3D.Im.DrawDonut(player.Position, innerRadius: 3f, outerRadius: 5f, new Vector4(1f, 0.6f, 0.1f, 0.9f));
NoireDraw3D.Im.DrawSector(boss.Position, boss.Rotation, MathF.PI / 4f, 0f, 20f, new Vector4(1f, 0.2f, 0.2f, 0.8f));
NoireDraw3D.Im.DrawLine(a, b, width: 0.1f, new Vector4(0.3f, 0.8f, 1f, 1f));
NoireDraw3D.Im.DrawChevron(pos, facingRad: 0f, new Vector2(0.45f, 0.32f), new Vector4(0.25f, 0.6f, 1f, 0.95f));
```

Shapes default to **ground decals** that project onto the terrain, stairs and slopes like the game's telegraphs. Style them with `ImShapeStyle`:

```csharp
NoireDraw3D.Im.DrawCircle(pos, 4f, color, new ImShapeStyle
{
    Placement = ImShapePlacement.Flat,  // flat mesh instead of terrain projection
    Additive = true,                    // energy-glow blending
    IgnoreDepth = true,                 // x-ray through walls (flat shapes only)
    OutlineWidth = 0.12f,               // strong decal rim (decals)
});
```

> **Zero latency:** `Im` calls inside `Scene3D.OnPrepareFrame` or an `ISceneFeature` render *this* frame. Calls elsewhere render at most one frame late.

> **Those callbacks run on the render thread**, and on the under-UI path *inside one of the game's own D3D calls*. Touch only the scene graph, `Im` and plugin state. Read game state on the framework thread and leave the result for the callback.

## Retained scenes, the "FF14 Blender"

For long-lived content, build nodes once and mutate them. `scene.Spawn` and the `Add*` shortcuts create the node, build the mesh, attach it and track it in one call. The node **owns** the mesh:

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

**The scene is an ownership scope.** `scene.Dispose()` destroys every node, disposes everything handed to `scene.Own(...)` and removes the scene from the renderer. Create more scenes with `NoireDraw3D.CreateScene("name")`. `MainScene` is permanent. `scene.Spawn(sharedMesh, material, ...)` references a caller-owned mesh, for instancing one mesh on many nodes.

`MeshBuilder` ships `Quad`, `Box`, `Disc`, `Ring`, `Sector`, `Sphere`, `Cylinder`, `Cone`, `Torus`, `Arrow` and `ExtrudePath`, unit-sized, +Y up. `new MeshBuilder()` merges primitives and raw geometry into one mesh, and `scene.Spawn(vertices, indices, ...)` takes raw vertices. Identical mesh and material combinations are instanced automatically.

### Materials

Immutable records, shared freely, with variants through `with`:

```csharp
var decal     = Material.Decal(DecalShape.Ring, new Vector4(1f, 0.5f, 0f, 0.9f),
                               shapeParams: new Vector4(0.7f, 0f, 0f, 0.6f)); // x = inner ratio, w = fill opacity
var glass     = Material.Unlit(new Vector4(0.4f, 0.8f, 1f, 0.35f), depthFade: 0.4f); // soft seam where it meets walls
var solid     = Material.Lit(new Vector4(1f, 1f, 1f, 1f));                            // opaque, z-tested against other meshes
var textured  = Material.UnlitTextured(myTexture) with { Cull = CullMode.None };
var custom    = Material.Custom("myPipeline", new Vector4(0f, 1f, 1f, 1f));           // custom HLSL via RegisterPipeline
```

> A ground decal paints its shape onto the world surface reconstructed from the game depth. **Characters** listed with `ExcludeObjects(pred)` are cut out along their **exact game-stencil silhouette**. The `ExcludeObjects` cylinders only pick which characters. The radius is safe to widen (`radiusScale`). The stencil value marking characters is `NoireDraw3D.CharacterStencilValue` (default `0x08`, see `/noire3d stencil`, 0 disables). Only characters are excluded.

- `Surface` **constrains how the decal's box may be oriented**. The shape sweeps along the box's local Y. Yaw, scale and position are kept:
  - `DecalSurface.Ground` (default): the box stays **horizontal** and projects straight down.
  - `DecalSurface.Wall`: the box stays **vertical** and projects into the wall it faces. Size the box to reach the wall.
  - `DecalSurface.Both`: **free**. Upright is ground, tipped 90 degrees is wall.
- `Projection = DecalProjection.HighestOnly` paints only the **topmost** surface per column, a tabletop and not the floor beneath. Needs `CollisionHeightMap` on (the default), `TopSurfaceThreshold` above 0, and collision on the covering object.
- `OutlineWidth` is the rim thickness, **constant in world space whatever the decal's scale**. `0` is a flat fill. Immediate-mode `Im.DrawCircle(...)` keeps a rim proportional to its radius.
- `outlineColor:` gives the border **its own colour** (`Material.Decal(shape, fill, outlineColor: rim)`, or `OutlineColor` via `with`). Unset, the rim is the decal's own colour. The immediate layer has `ImShapeStyle.OutlineColor`.
- `additive: true` on `Material.Decal(...)` (or `Blend = BlendMode.Additive`) blends additively. **Stacked coloured decals sum toward white.** A decal is never opaque.
- `DepthFade` feathers the edge where translucent shapes intersect world geometry.
- `Depth = DepthMode.Ignore` draws through walls. `WhenDepthUnavailable` decides what happens when the game's depth cannot be read.
- `UnorderedBatching = true` collapses identical translucent markers into one instanced draw.

> **Seeing the shape.** `node.ShowDecalShape()` traces what the decal paints as a closed line on its own plane. `node.HideDecalShape()` turns it off. It follows `Shape`, `ShapeParams` and `Surface` live. The color defaults to the decal's own: `node.ShowDecalShape(new Vector4(1f, 1f, 0f, 1f))`.
>
> The projection box is the SDF's bounding square and is usually much larger than the paint. A pie's box is centred on its apex and spans twice its radius.
>
> For every decal at once, immediate shapes included, use `NoireDraw3D.Diagnostics.DecalShapeOutlines` (or `/noire3d decalshapes`).

> **Seeing the volume.** `node.ShowDecalVolume()` / `node.HideDecalVolume()` draws the decal's **projection box**. Only what falls inside it can be painted. The master switch is `NoireDraw3D.Diagnostics.DecalVolumeOutlines` (or `/noire3d decalvolumes`). The two overlays compose.

## Importing models (glTF)

```csharp
var model = await GltfLoader.LoadAsync(@"C:\models\prop.glb");
model.AttachTo(NoireDraw3D.MainScene);      // O(1), any thread
model.Root.LocalPosition = spawnPosition;
// ...
model.Dispose();                            // detaches and releases its meshes/textures
```

Blender's *File > Export > glTF 2.0* works as is. **PBR materials are shaded**: authored metallic, a metallic-roughness or normal texture, an emissive factor or an alpha cutoff routes through `GltfPbrPipeline`. `KHR_materials_unlit` maps to the Unlit domain. Plain base-color materials stay on the instanced lit shader. Dropped and logged per file: emissive textures, separate occlusion maps, texture transforms, specular-glossiness (approximated), transmission and clearcoat, skins, animations. **FBX:** convert with `FBX2glTF` or Blender.

> **Vertex colors are off by default.** FFXIV-derived exports carry a `COLOR_0` channel the game uses as shader data (wetness, wind and blend masks). Pass `importVertexColors: true` only for assets that author vertex colors.

**Level of detail (opt-in).** `generateLods: true` builds a chain of coarser meshes by quadric-error decimation. The renderer draws the level fitting the object's screen size. Culling, picking and bounds use the full-resolution mesh.

## Game models, scenes and level layers

The game's files decode straight out of the archives. The parsers (`GameModelFile`, `GameMaterialFile`, `LayerGroupHelper`) live in `Helpers/GameData`. The loaders below turn them into meshes and materials.

```csharp
GameModelMesh[] meshes = GameModelLoader.Load("bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl");

// A scene places several models, nested scenes included, each at its scene-local transform.
GameScenePart[] parts = GameSceneLoader.Load("bgcommon/hou/indoor/general/0116/asset/fun_b0_m0116.sgb");

// A level file's parts stand at world positions; the filter picks its layers.
GameScenePart[] level = GameSceneLoader.Load("bg/ffxiv/sea_s1/twn/s1t1/level/bg.lgb",
    layer => layer.Name.Contains("ship", StringComparison.OrdinalIgnoreCase));
```

`GameSceneLoader` decodes each model once and shares its meshes. A whole `bg.lgb` is thousands of models: filter its layers.

## Performance

Everything lives on `NoireDraw3D.Performance` or a `Configure(c => c.Performance...)` batch. Every knob is opt-in:

```csharp
var model = await scene.LoadModelAsync(path, generateLods: true); // build the LOD chain at import

NoireDraw3D.Performance.Lod = true;             // use LOD chains where present (default on; no-op without a chain)
NoireDraw3D.Performance.LodBias = 1f;           // >1 = drop detail sooner; <1 = keep it longer
NoireDraw3D.Performance.LodScreenRadii = new[]{160f, 60f, 22f}; // px radii where each LOD takes over

NoireDraw3D.Performance.MaxDrawDistance = 0f;   // 0 = unlimited; else skip objects past this (world units)
NoireDraw3D.Performance.MinScreenPixels = 0f;   // 0 = off; else skip objects smaller than this on screen

NoireDraw3D.Performance.Supersample = 1f;       // 1 = off; 2 = 2x2 SSAA (fixes distance shimmer, 4x the layer fill)

NoireDraw3D.Performance.BatchedObjectConstants = true;  // default on: single draws ride the instanced route. The
                                                        // object CB re-uploads only when material params change
```

LOD and the culls default off. Outlined and selected objects are exempt from the size cull. They apply to the main view only.

> **Anti-aliasing.** The layer has no MSAA. A dense mesh shimmers at a distance. `Performance.Supersample = 2` renders the layer at 2x and box-downsamples it, at 4x the fill and VRAM. Model LOD is the lighter alternative.

> **Picking is BVH-accelerated.** A `keepCpuData` mesh builds a bounding-volume hierarchy once. The last pick's cost is in `Draw3DStats.LastPickMicros` and `/noire3d stats`.

> **Many unique objects.** `BatchedObjectConstants` (default on) routes single draws through the instanced pipeline. The object constant buffer re-uploads only when material parameters change. `Draw3DStats.ObjectCbUpdates` shows the effect. `/noire3d batchcb` flips it in game. Decals and custom pipelines keep the classic path.

## Textures

```csharp
var icon = await TextureLoader.FromGameIconAsync(60073);
var png  = await TextureLoader.FromFileAsync(path);
var raw  = TextureLoader.FromRgba(pixels, width, height);
var live = ExternalTexture.FromSharedHandle(handle, ntHandle: true); // another process renders it (browser, etc.)
```

Every returned `GpuTexture` is the caller's to dispose. External shared-handle textures put a live browser screen on a quad in the world.

## Render-to-texture

```csharp
var view = NoireDraw3D.CreateRenderView(scene, new Camera3D(camPos, lookAt), 512, 512);
material = Material.UnlitTextured(view.Texture!); // minimap portals, mirrors, thumbnails
```

> A render view re-renders **this scene** from a second camera. The game world cannot be re-photographed from another angle. The closest is rendering the collision proxy below into the view.

## World-projected decals (real collision)

`Material.Decal` projects onto the depth buffer. These decals project onto the game's real collision geometry instead: terrain slopes, walls and furniture, never cut by an actor in front. **Framework thread only.** No surface returns `null`:

```csharp
// A decal that conforms to the real ground, walls and furniture under `pos`, facing up:
scene.SpawnWorldDecal(pos, Vector3.UnitY, width: 6f, height: 6f, Material.UnlitTextured(tex), depth: 3f);

// The raw collision near a point, as a mesh (debug preview, or input to caller logic):
scene.SpawnWorldGeometry(pos, radius: 20f, Material.Lit(new Vector4(0.4f, 0.8f, 1f, 0.4f)) with { Cull = CullMode.None });

// Or take the geometry and projected footprint directly.
var geo   = WorldGeometry.Collect(pos, radius: 20f);                       // terrain + models + furniture + dynamic objects
var decal = WorldGeometry.ProjectDecal(pos, Vector3.UnitY, 6f, 6f);        // clipped, UV-mapped MeshData
```

The source is the collision world a navmesh tool walks, read through `GameCollisionHelper.CollectTrianglesInBox` across every layer. `includeAnalytic: true` adds box, cylinder, sphere and plane colliders.

## Lighting an object with the game's own lights

`DrawGameLit` draws a node into the **game's own G-buffer**, inside the game's geometry pass. The game's deferred lighting then lights it like the wall beside it: every lamp, the sun, the ambient term, shadows, tonemapping and exposure.

```csharp
// Once per frame, for as long as the node should be game-lit. Nothing is retained between frames.
NoireDraw3D.DrawGameLit(node);
```

The node's own draw is suppressed for each submitted frame and resumes when submission stops. **Never hide it with `Visible = false`.** Hiding also removes it from picking and hover.

Submit from `Scene3D.OnPrepareFrame`. A UI callback stops when its window closes.

**Limits.** Outlines, transparency, fade, ground decals and drawing above everything are unavailable. Deferred geometry is opaque.

**Shadow casting.** With `NoireDraw3D.GameLit.CastShadows` on, every game-lit mesh is also drawn depth-only into the game's shadow passes, with each light's view-projection read from the game's shadow draw constants. A cached map picks the object up on its next refresh.

Both injections do nothing until a caller opts in, and lapse a few frames after the last submission.

`NoireDraw3D.GameLit` holds what gets written into each channel. Every default is measured off the game's own geometry:

| Property | What it is |
|---|---|
| `Misc` | rtv3's four channels. Red and green are `0`, as on the game's furniture; blue scales the model's baked per-vertex occlusion. |
| `ShadingModelId` | rtv0's alpha: which of the game's shading models runs over these pixels. `128` is furniture and architecture, `32` is characters. |
| `MaterialParams`, `MaterialOverride` | rtv1's scalars, and how much they replace the specular map a material samples. Red is reflection strength, green moves and scales the highlight, blue darkens the surface. |
| `MaterialCeiling` | Crossing `0.999` on red switches rtv1 into a special mode that turns the reflection green, and a specular map reaches `1.0` in places. The channels are held below this. |
| `Stencil` | The mark stamped into the stencil plane. **The game's deferred light volumes test this**. Geometry carrying no mark receives no light at all. Defaults to `Draw3DGameLit.LitStencilMark`. |
| `AlbedoOverride` | Forces a flat albedo. Black separates a wrong G-buffer from a downstream pass that never reads it. |
| `WriteColor`, `WriteDepth` | Turn off each half of what the injection writes. With depth off, the world draws over the object and the colour does not survive the pass. |

## Picking

```csharp
NoireDraw3D.PickInputGate = () => !myUiWantsTheMouse; // the caller decides when the mouse is free
var hits = NoireDraw3D.Pick(mousePos);                // nearest first; exact triangles for meshes built with keepCpuData
```

For something the renderer does not own (the game's collision, a navmesh, caller geometry), take the ray. It is the same ray `Pick` uses, through last frame's camera.

```csharp
if (NoireDraw3D.TryScreenToRay(mousePos, out var origin, out var direction))
{
    var hit = GameCollisionHelper.Raycast(origin, direction);
}
```

## Layer controls

| Property | What it does |
|---|---|
| `NoireDraw3D.Enabled` | Master switch (also re-arms the renderer after a fault). |
| `NoireDraw3D.LayerOpacity` | 0 to 1 fade of the whole 3D layer. |
| `NoireDraw3D.NativeUi.Layering` | **Default `UnderGameUi`.** Where the layer lands in the game's frame. `UnderGameUi` composites through a render-thread hook before the game draws its UI. The UI is always on top. `OverEverything` composites over the backbuffer at present time and can decide *per element* what the layer covers. It is also the fallback when the injection cannot run. |
| `NoireDraw3D.NativeUi.KeepUiOnTop` | **Default true. Only applies under `OverEverything`.** Masks the layer per pixel under the HUD, addons and nameplates. The mask is the *difference* between the present buffer before and after the game drew its UI. A frame with no injection point composites unmasked. |
| `NoireDraw3D.NativeUi.Nameplates` | **Default `DepthAware`. Honoured under both layering modes.** Whether the game's own nameplates are occluded by 3D objects in front of them. Under the game UI it stamps depth for the game's plate pass to test; over everything it gates where the `KeepUiOnTop` mask applies. `Covered` requires `OverEverything`. Fail-soft. |
| `NoireDraw3D.NativeUi.NameplateDim` | **Default 0. Only applies under `OverEverything`** with `KeepUiOnTop` on, and only to a plate `Nameplates` decided is covered. How much a covered plate still shows through: 0 = fully covered, toward 1 = faintly readable. |
| `NoireDraw3D.KeepDrawingWhenUiHidden` | Keep **the 3D layer** rendering in cutscenes, GPose and UI-hide. Affects only the layer; plugin windows are unaffected (see below). |
| `NoireDraw3D.IsGameUiHidden` | Whether the game UI is hidden (user toggle, cutscene, GPose), read from the game state whatever the overrides do. |
| `NoireDraw3D.Lighting` | Ambient + directional half-Lambert parameters for `Lit` materials. |
| `NoireDraw3D.OnFault` | Raised when the self-disable ladder trips (a pipeline, feature, or the renderer disabled itself). |

> **`UnderGameUi`** composites before the game draws its UI. The UI paints over the layer and always wins. Nothing to configure.
>
> **`OverEverything`** composites after. `KeepUiOnTop` masks the layer off the UI, and a nameplate can be `Covered` or dimmed.
>
> **The mask.** FFXIV writes no UI coverage alpha. Draw3D snapshots the present buffer before and after the UI and **differences the two**. Antialiased glyph edges come out as partial coverage. A frame whose injection point cannot fire composites unmasked.
>
> `/noire3d uimask` reports whether the difference finds the UI.

> **UI-hide and plugin windows.** Only Dalamud's four `Disable*UiHide` flags keep `UiBuilder.Draw` firing. NoireDraw3D **holds them for the layer's lifetime**. `KeepDrawingWhenUiHidden` decides whether the layer draws.
>
> **Dalamud then no longer auto-hides the plugin's own windows.** One line restores it:
>
> ```csharp
> public override bool DrawConditions() => !NoireDraw3D.IsGameUiHidden;
> ```
>
> `/noire3d stats` reports `skipped (ui-hidden N)`.

## Custom shaders

```csharp
NoireDraw3D.RegisterPipeline("MyPulse", hlslSource);   // #include "Common.hlsli", vs/ps entry points
var mat = new Material { CustomPipeline = "MyPulse", Color = ... };
```

A compile error disables only that pipeline and logs the compiler output.

A custom pipeline gets these inputs:

| Material member | Shader | Notes |
|---|---|---|
| `Texture` | `BaseTex` (t1) | Also used by the standard textured variants. |
| `AuxTexture0` | `AuxTex0` (t4) | Ignored by the standard shaders. Unbound when null, never left over from the previous draw. |
| `AuxTexture1` | `AuxTex1` (t5) | As above. |
| `ShapeParams` | `Params0` | Shared with the decal shape parameters. |
| `SurfaceParams` | `Params2` | **Not available to `GroundDecal`**, whose shader needs that register for projection data. |

A disposed texture in any slot skips the draw.

## Diagnostics: `/noire3d`

| Command | Purpose |
|---|---|
| `/noire3d validate` | Projection parity vs the game's own WorldToScreen over 10 frames (gate: <= 1 px). |
| `/noire3d probe` | Reads real depth-buffer values back at raycast points and compares them to the analytic depth map's prediction (gate: >= 90 % within 1e-3). |
| `/noire3d stats` | Frame, draw and skip counters plus GPU timings. |
| `/noire3d wire` | Wireframe toggle. Ground decals carry no mesh to wireframe (their shape lives in the pixel shader). They trace the outline of what they paint instead, the same line `ShowDecalShape()` draws. |
| `/noire3d decalshapes` | Traces what **every** decal paints as an outline, over normal rendering: retained decals and immediate-layer grounded shapes alike, including `ImDraw3D` shapes. Implied by `wire`. |
| `/noire3d cbprobe [frames]` | Camera-constant discovery report: every constant buffer observed on the upload paths, its update mechanism and VS slot, and the candidate camera windows with match errors. |
| `/noire3d gpucam` | A/B toggle between the captured GPU camera constants (default, swim-free) and the control's view-projection. The latter can lag the drawn camera by a frame. |
| `/noire3d batchcb` | A/B toggle for `Performance.BatchedObjectConstants` (singles ride the instanced route; the object CB re-uploads only on material-param changes). |
| `/noire3d heightmap` | Toggles `CollisionHeightMap`, the top-down collision height-map. Only `DecalProjection.HighestOnly` reads it. With no `HighestOnly` decal on screen there is nothing to see. Cutting characters out of decals is `ExcludeObjects` plus `CharacterStencilValue`. |
| `/noire3d decalvolumes` | Draws every decal's **projection box**, the volume its SDF is evaluated in, retained and immediate alike. `SceneNode.ShowDecalVolume()` is the per-node version. Independent of `decalshapes`. |
| `/noire3d topsurface` | Reports every link in the `DecalProjection.HighestOnly` chain (how many decals asked for it, the master switch, the threshold, the cached collision, and whether the height-map drew) then names the missing one. |
| `/noire3d reset` | Resets counters and re-arms the renderer. |
| `/noire3d ontop` | Toggles `NativeUi.Layering` (under the game UI vs over everything). |
| `/noire3d platedepth` | Toggles `NativeUi.Nameplates` (depth-aware vs always-visible nameplates). |
| `/noire3d uimask` | Reports the over-everything UI mask: whether the render-thread hook is landing its pre-UI snapshot, the health verdict, and the per-sample difference grid. |
| `/noire3d plates` | Per-nameplate policy factors from last frame, with the distances that decided them. Factor 1 on a covered plate means the mask never found its pixels. Factor 0 on a plate that should read on top means the occlusion test was wrong. |
| `/noire3d rtlog` | Captures one frame's render-target bind sequence to the log, with every bind's pixel format (injection-point diagnostics). |
| `/noire3d framedump [sweep [count]\|<from> [count]]` | Writes out what render-target binds produced, as images, to find the first pass where a pixel is already wrong. `sweep` (the default) spreads them across the whole frame. Bind indices shift with what is on screen. Narrow with an explicit span from the bind table that run prints. Each dump stalls the frame. |

Commands are global across plugins. Everything is also on `NoireDraw3D.Diagnostics`.

**`NoireDraw3DDemoPlugin`** in this solution is the visual showcase on this public API: the showcase gallery, the collision preview, glTF import, the gizmo backends, a scenes and decals playground, a per-object inspector and every global knob.

## Rules of the road

- **Ownership:** whoever creates a `Mesh` or `GpuTexture` disposes it. Disposing an asset in use is safe: draws skip it.
- **Threading:** scene mutation and asset creation are safe from any thread. `Im` calls belong in draw-cycle callbacks.
- **Camera:** the layer projects with **the exact camera constants the GPU drew the frame with**, discovered from the game's constant-buffer uploads. Falls back to the control's view-projection. `/noire3d gpucam` compares the two, `/noire3d cbprobe` reports the discovery.
- **Depth:** the depth convention is derived from the game's own projection. `/noire3d probe` cross-checks it against collision.
- **Failure:** everything fails soft and logs once. A failure degrades the narrowest feature.
