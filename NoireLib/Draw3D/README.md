# NoireDraw3D

A real D3D11 world renderer for Dalamud plugins. It draws real 3D geometry into the game's frame — glowless and color-exact (the world's post-processing has already run), hardware-clipped at the screen edges, and always under your plugin windows. By default it composites **under the game's native UI** so HUD and nameplates read on top (this uses a render-thread hook on the present composition); set `RenderUnderNativeUi = false` to composite over everything with no hook at all. There is no ImGui and no 2D-projected fallback anywhere in it: when it cannot render correctly, it renders nothing and tells you why.

Full design rationale, invariants and acceptance gates live in [`docs/Draw3D V2 Proposal.md`](https://github.com/Aspher0/NoireLib/blob/main/docs/Draw3D%20V2%20Proposal.md).

## Quick start — markers in three lines

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
    OutlineWidth = 0.12f,               // strong telegraph rim (decals)
});
```

> **Zero-latency rule:** `Im` calls made inside `Scene3D.OnPrepareFrame` or an `ISceneFeature` render *this* frame; calls made elsewhere render at most one frame late. For markers you will never notice — it is documented so nobody debugs it as a bug.

## Retained scenes — the "FF14 Blender"

For long-lived content, build nodes once and mutate them:

```csharp
var scene = NoireDraw3D.MainScene;

var donut = scene.CreateNode("waymark");
donut.LocalPosition = somePosition;
var torusMesh = new Mesh(MeshBuilder.Torus(majorRadius: 2f, minorRadius: 0.3f));
donut.SetMesh(torusMesh, Material.Lit(new Vector4(0.9f, 0.9f, 1f, 1f)));

// Later, from any thread:
donut.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
donut.Visible = someCondition;

// When done: the node goes back to the scene, the mesh is YOURS to dispose (creator owns it).
donut.Destroy();
torusMesh.Dispose();
```

`MeshBuilder` ships the full shape catalog — `Quad`, `Box`, `Disc`, `Ring`, `Sector`, `Sphere`, `Cylinder`, `Cone`, `Torus`, `Arrow`, `ExtrudePath` — all unit-sized, +Y up, ready to scale via the node. Identical mesh+material combinations are automatically instanced into single draw calls.

### Materials

Immutable records — share them freely, derive variants with `with`:

```csharp
var telegraph = Material.Telegraph(DecalShape.Ring, new Vector4(1f, 0.5f, 0f, 0.9f),
                                   shapeParams: new Vector4(0.7f, 0f, 0f, 0.6f)); // x = inner ratio, w = fill opacity
var glass     = Material.Unlit(new Vector4(0.4f, 0.8f, 1f, 0.35f), depthFade: 0.4f); // soft seam where it meets walls
var solid     = Material.Lit(new Vector4(1f, 1f, 1f, 1f));                            // opaque, z-tested against other meshes
var textured  = Material.UnlitTextured(myTexture) with { Cull = CullMode.None };
```

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

Blender → *File → Export → glTF 2.0* just works (base color + texture; PBR maps/skins/animations are skipped and logged). **FBX:** convert once with `FBX2glTF` or Blender — NoireLib will never ship the FBX SDK.

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
| `NoireDraw3D.NativeUiDepthWrite` | **Default true** (needs `RenderUnderNativeUi`). Write the layer's opaque depth into the game's own scene-depth buffer so nameplates behind your 3D objects get occluded — real depth-aware nameplates. Fail-soft. |
| `NoireDraw3D.ProtectGameUi` | The game's native UI always draws on top of the 3D layer — **per pixel** (nameplate letters, window drop shadows, chat transparency), via the backbuffer's UI-coverage alpha. Default true. Off = the layer sits above the game UI (still under plugin windows). |
| `NoireDraw3D.NativeUiProtection` | Nameplate layering, always letter-exact: `DepthAware` (default — a plate in front of your shape reads on top, a plate behind it is covered, like real occlusion), `AlwaysVisible` (letters always on top), `Off` (the layer covers plates). |
| `NoireDraw3D.NativeUiProtectionDimFactor` | How much a plate that is *behind* your content still shows through it: 0 (default) = fully covered, toward 1 = faintly readable. |
| `NoireDraw3D.KeepDrawingWhenUiHidden` | Keep rendering in cutscenes/GPose (**plugin-wide** UiBuilder side effect — read the XML doc). |
| `NoireDraw3D.Lighting` | Ambient + directional half-Lambert parameters for `Lit` materials. |
| `NoireDraw3D.OnFault` | Raised when the self-disable ladder trips (a pipeline, feature, or the renderer disabled itself). |

## Custom shaders

```csharp
NoireDraw3D.RegisterPipeline("MyPulse", hlslSource);   // #include "Common.hlsli", vs/ps entry points
var mat = new Material { CustomPipeline = "MyPulse", Color = ... };
```

A compile error disables only that pipeline and logs the full compiler output.

## Diagnostics — `/noire3d`

| Command | Purpose |
|---|---|
| `/noire3d validate` | Projection parity vs the game's own WorldToScreen over 10 frames (gate: ≤ 1 px). |
| `/noire3d probe` | Forces a fresh depth calibration, then reads real depth-buffer values back and compares them to the calibrated prediction (gate: ≥ 90 % within 1e-3). Also reports the UI-mask alpha health. |
| `/noire3d stats` | Frame/draw/skip counters + GPU timings — "why is nothing drawing" is always answerable. |
| `/noire3d wire` | Wireframe toggle. |
| `/noire3d smoke` / `clear` | Spawns/removes the reference QA scene around you. |
| `/noire3d reset` | Resets counters and re-arms the renderer. |
| `/noire3d ontop` | Toggles `RenderUnderNativeUi` (under the game UI vs over everything). |
| `/noire3d platedepth` | Toggles `NativeUiDepthWrite` (depth-aware nameplates). |
| `/noire3d cam` | A/B the present-time camera source (`FrameworkSnapshot`/`DrawTime`); does not affect the injection path. |
| `/noire3d rtlog` | Captures one frame's render-target bind sequence to the log (injection-point diagnostics). |

Commands are global across plugins; everything is also available programmatically via `NoireDraw3D.Diagnostics`.

## Rules of the road

- **Ownership:** whoever creates a `Mesh`/`GpuTexture` disposes it. Scenes and nodes only reference assets; disposing an asset in use is safe (draws skip it, counted, never a crash).
- **Threading:** scene mutation and asset creation are safe from any thread. `Im` calls belong in draw-cycle callbacks.
- **Depth:** world occlusion **self-calibrates** at runtime — the depth buffer's value convention is fitted from the game's own raycasts, never assumed, so a patch that changes the projection degrades to a one-off recalibration instead of inverted visuals. `/noire3d stats` shows the live fit.
- **Failure:** everything fails soft, loudly once — a broken shader, unreadable depth buffer, or missing camera degrades the narrowest feature and never takes your plugin down.
