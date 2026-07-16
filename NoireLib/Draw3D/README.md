# NoireDraw3D

A real D3D11 world renderer for Dalamud plugins. It draws real 3D geometry into the game's frame - glowless and color-exact (the world's post-processing has already run), hardware-clipped at the screen edges, and always under your plugin windows. By default it composites **under the game's native UI** so HUD and nameplates read on top (this uses a render-thread hook on the present composition); set `RenderUnderNativeUi = false` to composite over everything with no hook at all. There is no ImGui and no 2D-projected fallback anywhere in it: when it cannot render correctly, it renders nothing and tells you why.

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
- `Projection = DecalProjection.HighestOnly` paints only the **topmost** surface within the decal box per column (a tabletop, not the floor beneath it). Needs `WorldOccludedDecals` and the covering object to have collision.
- `DepthFade` feathers the edge where translucent shapes intersect world geometry.
- `Depth = DepthMode.Ignore` draws through walls; `WhenDepthUnavailable` decides what happens on frames where the game's depth buffer can't be read.
- `UnorderedBatching = true` lets hundreds of identical translucent markers collapse into one instanced draw.

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
| `NoireDraw3D.RenderUnderNativeUi` | **Default true.** Composite the layer under the game's native UI (HUD/nameplates read on top) via a render-thread hook on the present composition; falls back to the over-everything composite on any frame the injection can't run. Off = draw over everything, no hook. |
| `NoireDraw3D.NativeUiDepthWrite` | **Default true** (needs `RenderUnderNativeUi`). Write the layer's opaque depth into the game's own scene-depth buffer so nameplates behind your 3D objects get occluded - real depth-aware nameplates. Fail-soft. |
| `NoireDraw3D.ProtectGameUi` | The game's native UI always draws on top of the 3D layer - **per pixel** (nameplate letters, window drop shadows, chat transparency), via the backbuffer's UI-coverage alpha. Default true. Off = the layer sits above the game UI (still under plugin windows). |
| `NoireDraw3D.NativeUiProtection` | Nameplate layering, always letter-exact: `DepthAware` (default - a plate in front of your shape reads on top, a plate behind it is covered, like real occlusion), `AlwaysVisible` (letters always on top), `Off` (the layer covers plates). |
| `NoireDraw3D.NativeUiProtectionDimFactor` | How much a plate that is *behind* your content still shows through it: 0 (default) = fully covered, toward 1 = faintly readable. |
| `NoireDraw3D.KeepDrawingWhenUiHidden` | Keep rendering in cutscenes/GPose (**plugin-wide** UiBuilder side effect - read the XML doc). |
| `NoireDraw3D.Lighting` | Ambient + directional half-Lambert parameters for `Lit` materials. |
| `NoireDraw3D.OnFault` | Raised when the self-disable ladder trips (a pipeline, feature, or the renderer disabled itself). |

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
| `/noire3d probe` | Forces a fresh depth calibration, then reads real depth-buffer values back and compares them to the calibrated prediction (gate: ≥ 90 % within 1e-3). Also reports the UI-mask alpha health. |
| `/noire3d stats` | Frame/draw/skip counters + GPU timings - "why is nothing drawing" is always answerable. |
| `/noire3d wire` | Wireframe toggle. |
| `/noire3d camtrace [frames]` | Camera-phase "swim" trace: measures overlay-vs-world drift under camera motion, plus the inject-vs-fallback frame split (run it while panning/zooming hard). |
| `/noire3d worldocclude` | Toggles `WorldOccludedDecals` (ground decals skip characters via the collision world vs. the legacy `ExcludeVolumes` cylinder). |
| `/noire3d reset` | Resets counters and re-arms the renderer. |
| `/noire3d ontop` | Toggles `RenderUnderNativeUi` (under the game UI vs over everything). |
| `/noire3d platedepth` | Toggles `NativeUiDepthWrite` (depth-aware nameplates). |
| `/noire3d rtlog` | Captures one frame's render-target bind sequence to the log (injection-point diagnostics). |

Commands are global across plugins; everything is also available programmatically via `NoireDraw3D.Diagnostics`.

The **visual showcase** - the smoke scene, the world-geometry collision preview, glTF import, and the gizmo-backend toggle - lives in the standalone **`NoireDraw3DDemoPlugin`** (in this repo's solution), an ImGui front-end built entirely on this public API. It also exposes a scenes/decals playground (including the wall/ground/both surface filter) and a live editor for every global knob.

## Rules of the road

- **Ownership:** whoever creates a `Mesh`/`GpuTexture` disposes it. Scenes and nodes only reference assets; disposing an asset in use is safe (draws skip it, counted, never a crash).
- **Threading:** scene mutation and asset creation are safe from any thread. `Im` calls belong in draw-cycle callbacks.
- **Depth:** world occlusion **self-calibrates** at runtime - the depth buffer's value convention is fitted from the game's own raycasts, never assumed, so a patch that changes the projection degrades to a one-off recalibration instead of inverted visuals. `/noire3d stats` shows the live fit.
- **Failure:** everything fails soft, loudly once - a broken shader, unreadable depth buffer, or missing camera degrades the narrowest feature and never takes your plugin down.
