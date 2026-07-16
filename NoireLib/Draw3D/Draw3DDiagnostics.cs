using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D;

/// <summary>
/// The runtime diagnostics toolkit behind <c>/noire3d</c>, exposed programmatically so any consumer can
/// run it even when another plugin owns the command name. Results go to the plugin log.<br/>
/// <c>RunValidate</c>: projection parity vs the game's own WorldToScreen (the wobble-class killer).<br/>
/// <c>RunProbe</c>: reads actual depth-buffer values back and compares them against expectation.<br/>
/// <c>SpawnSmokeScene</c>: the reference QA scene from the regression matrix.
/// </summary>
public sealed unsafe class Draw3DDiagnostics
{
    private int validateFramesRemaining;
    private float validateMaxDelta;
    private double validateDeltaSum;
    private int validateSamples;
    private float validateMaxMatrixDelta;
    private bool probePending;

    private Scene3D? smokeScene;
    private SceneEditor? smokeEditor;
    private RenderView? smokeView;      // the render-to-texture mirror/portal source
    private Vector3 smokeCenter;
    private SceneNode? smokePortalNode;  // shows smokeView's texture (material swapped in once it exists)
    private SceneNode? smokeIconQuad;    // shows a game-icon texture (material swapped in once loaded)
    private SceneNode? smokeIconDecal;   // a DecalShape.Texture decal (material swapped in once loaded)
    private GpuTexture? smokeIcon;       // the loaded game-icon texture, disposed with the scene
    private bool smokePortalReady;
    private Scene3D? worldGeoScene;      // the '/noire3d worldgeo' collision-preview scene (toggled independently of smoke)

    /// <summary>The custom pipeline used by the smoke scene's "custom shader" station (registered once).</summary>
    private const string SmokePulsePipeline = "NoireSmokePulse";
    private static bool smokePulseRegistered;

    internal Draw3DDiagnostics() { }

    /// <summary>Arms the projection parity validator for the next 10 rendered frames (results logged). Gate: max ≤ 1 px.</summary>
    public void RunValidate()
    {
        NoireDraw3D.EnsureInitialized();
        validateFramesRemaining = 10;
        validateMaxDelta = 0f;
        validateDeltaSum = 0;
        validateSamples = 0;
        validateMaxMatrixDelta = 0f;
    }

    /// <summary>
    /// Arms the ground-truth depth probe for the next rendered frame (results logged): the analytic depth
    /// map rendering uses, a diagnostic-only raycast fit, a per-point depth table for both candidate
    /// buffers, and the UI-mask alpha health. Read-only - it never disturbs the live depth state.
    /// Gate: ≥ 90 % of hit points within 1e-3.
    /// </summary>
    public void RunProbe()
    {
        NoireDraw3D.EnsureInitialized();
        probePending = true;
    }

    /// <summary>Toggles wireframe rasterization of the scene pass. Returns the new state.</summary>
    public bool ToggleWireframe() => NoireDraw3D.Wireframe = !NoireDraw3D.Wireframe;

    /// <summary>Formats the current stats snapshot.</summary>
    public string GetStatsText() => NoireDraw3D.Stats.ToString();

    /// <summary>
    /// Spawns the reference QA scene around the player - a hands-on gallery of (almost) every Draw3D feature in one
    /// disposable scene, so you can eyeball them all at once:
    /// <list type="bullet">
    /// <item>every <see cref="MeshBuilder"/> primitive (box, sphere, cylinder, cone, torus, disc, ring, arrow, sector,
    /// extruded path, and an appendable-builder combined mesh), each a <see cref="Material.Lit"/> node;</item>
    /// <item>every ground-decal footprint shape (circle, ring, sector, rect, and - once the icon loads - a textured
    /// decal), each excluding actors that stand in it;</item>
    /// <item>the material families: additive glow, a depth-faded translucent quad, an opaque box stack (V2↔V2 depth),
    /// and a <b>custom-pipeline</b> pulsing box (<see cref="NoireDraw3D.RegisterPipeline"/>);</item>
    /// <item>a game-icon <b>textured</b> quad, a render-to-texture <b>mirror/portal</b> quad (<see cref="RenderView"/>),
    /// and a live <b>immediate-layer</b> marker set animated every frame;</item>
    /// <item>the whole interaction spine: every node is <see cref="SceneNode.MakeSelectable"/>, and a
    /// <see cref="SceneEditor"/> follows the selection so you can move/rotate/scale with the gizmo.</item>
    /// </list>
    /// glTF import is on-demand via <c>/noire3d model &lt;path&gt;</c> (needs a file); external/browser textures need a
    /// shared handle you own (<see cref="ExternalTexture.FromSharedHandle"/>). One <see cref="Scene3D.Dispose"/> (via
    /// <see cref="ClearSmokeScene"/> or <c>/noire3d clear</c>) frees the whole thing - nodes, owned meshes, view, editor.
    /// </summary>
    public void SpawnSmokeScene()
    {
        ClearSmokeScene();

        var center = smokeCenter = NoireService.ObjectTable.LocalPlayer?.Position
            ?? (NoireDraw3D.LastFrameValid ? NoireDraw3D.LastFrame.EyePos : Vector3.Zero);

        var scene = smokeScene = NoireDraw3D.CreateScene("smoke");

        // Register the custom pulse pipeline once (used by the custom-shader station); a compile failure disables only it.
        if (!smokePulseRegistered)
            smokePulseRegistered = NoireDraw3D.RegisterPipeline(SmokePulsePipeline, SmokePulseHlsl);

        // ---- Station 1: every MeshBuilder primitive, one Lit node each, all selectable (a row to the north). --------
        var pz = center.Z + 9f;
        var x = center.X - 12f;
        SceneNode Row(SceneNode n) { n.MakeSelectable(); x += 2.5f; return n; }
        Row(scene.AddBox(new Vector3(1.4f, 1.4f, 1.4f), Material.Lit(new Vector4(0.90f, 0.50f, 0.40f, 1f)), new Vector3(x, center.Y + 0.9f, pz), "Prim.Box", keepCpuData: true));
        Row(scene.AddSphere(0.8f, Material.Lit(new Vector4(0.50f, 0.80f, 0.55f, 1f)), new Vector3(x, center.Y + 1f, pz), "Prim.Sphere", keepCpuData: true));
        Row(scene.AddCylinder(0.7f, 1.5f, Material.Lit(new Vector4(0.50f, 0.60f, 0.90f, 1f)), new Vector3(x, center.Y + 0.75f, pz), "Prim.Cylinder", keepCpuData: true));
        Row(scene.AddCone(0.8f, 1.6f, Material.Lit(new Vector4(0.90f, 0.80f, 0.40f, 1f)), new Vector3(x, center.Y + 0.1f, pz), "Prim.Cone", keepCpuData: true));
        Row(scene.AddTorus(0.8f, 0.30f, Material.Lit(new Vector4(0.80f, 0.50f, 0.90f, 1f)), new Vector3(x, center.Y + 1f, pz), "Prim.Torus", keepCpuData: true));
        Row(scene.AddDisc(0.9f, Material.Lit(new Vector4(0.55f, 0.90f, 0.90f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.Disc", keepCpuData: true));
        Row(scene.AddRing(0.4f, 0.9f, Material.Lit(new Vector4(0.90f, 0.60f, 0.60f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.Ring", keepCpuData: true));
        Row(scene.AddArrow(1.6f, Material.Lit(new Vector4(0.90f, 0.90f, 0.50f, 1f)), new Vector3(x, center.Y + 0.2f, pz), "Prim.Arrow", keepCpuData: true));
        Row(scene.Spawn(MeshBuilder.Sector(MathF.PI / 4f, 0.3f, 1f), Material.Lit(new Vector4(1f, 0.70f, 0.30f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.Sector", keepCpuData: true));
        var ribbon = new List<Vector3> { new(-1f, 0f, 0f), new(-0.3f, 0f, 0.6f), new(0.3f, 0f, -0.6f), new(1f, 0f, 0f) };
        Row(scene.Spawn(MeshBuilder.ExtrudePath(ribbon, 0.25f), Material.Lit(new Vector4(0.70f, 1f, 0.70f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.ExtrudePath", keepCpuData: true));
        var combined = new MeshBuilder().AddBox(new Vector3(1f, 0.4f, 1f)).AddSphere(0.45f, new Vector3(0f, 0.6f, 0f)).ToMeshData();
        Row(scene.Spawn(combined, Material.Lit(new Vector4(0.80f, 0.80f, 0.88f, 1f)), new Vector3(x, center.Y + 0.6f, pz), "Prim.Combined", keepCpuData: true));

        // ---- Station 2: every ground-decal footprint shape (Texture added when the icon loads, in LoadSmokeIconAsync).
        var dz = center.Z - 7f;
        scene.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0.30f, 0.70f, 1f, 0.9f)), new Vector3(center.X - 9f, center.Y, dz), "Decal.Circle", keepCpuData: true)
             .Scale(new Vector3(4f, 4f, 4f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        scene.AddBox(Material.Decal(DecalShape.Ring, new Vector4(1f, 0.55f, 0.10f, 0.9f), new Vector4(0.6f, 0f, 0f, 0.5f)), new Vector3(center.X - 3.5f, center.Y, dz), "Decal.Ring", keepCpuData: true)
             .Scale(new Vector3(5f, 4f, 5f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        scene.AddBox(Material.Decal(DecalShape.Sector, new Vector4(0.90f, 0.15f, 0.15f, 0.9f), new Vector4(MathF.PI / 4f, 0f, 0f, 0.55f)), new Vector3(center.X + 2f, center.Y, dz), "Decal.Sector", keepCpuData: true)
             .Scale(new Vector3(6f, 4f, 6f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        scene.AddBox(Material.Decal(DecalShape.Rect, new Vector4(0.60f, 0.35f, 1f, 0.9f)), new Vector3(center.X + 7f, center.Y, dz), "Decal.Rect", keepCpuData: true)
             .Scale(new Vector3(4f, 4f, 3f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);

        // ---- Station 3: material families + blending + the custom pulse pipeline (a cluster to the west). -----------
        scene.AddSphere(0.75f, Material.Unlit(new Vector4(0.20f, 0.60f, 1f, 0.8f)) with { Blend = BlendMode.Additive }, new Vector3(center.X - 9f, center.Y + 1.5f, center.Z + 1f), "Mat.Additive", keepCpuData: true).MakeSelectable();
        scene.AddQuad(4f, 4f, Material.Unlit(new Vector4(0.30f, 1f, 0.50f, 0.5f), depthFade: 0.35f) with { Cull = CullMode.None }, new Vector3(center.X - 9f, center.Y + 0.05f, center.Z - 2f), "Mat.DepthFadeQuad", keepCpuData: true).MakeSelectable();
        var pulseMat = smokePulseRegistered
            ? Material.Custom(SmokePulsePipeline, new Vector4(1f, 0.40f, 0.80f, 1f))
            : Material.Unlit(new Vector4(1f, 0.40f, 0.80f, 1f)); // fallback if the pipeline failed to compile
        scene.AddBox(new Vector3(1.3f, 1.3f, 1.3f), pulseMat, new Vector3(center.X - 13f, center.Y + 1f, center.Z), "Mat.CustomPulse", keepCpuData: true).MakeSelectable();

        // Opaque box stack (private-depth V2↔V2 occlusion), east.
        for (var i = 0; i < 3; i++)
            scene.AddBox(Material.Lit(new Vector4(0.8f - i * 0.2f, 0.4f + i * 0.25f, 0.35f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X + 7f, center.Y + 0.5f + i * 1.05f, center.Z), $"Stack.Box{i}", keepCpuData: true)
                 .RotateY(i * 0.4f).MakeSelectable();

        // ---- Station 4: render-to-texture mirror/portal. The view renders THIS scene from a raised camera; its texture
        // is swapped onto the quad once it exists (null on frame 0) in OnSmokeFrame. Feeding it back in is legal (1-frame
        // latency), so you get a recursive "screen showing the room". The quad starts as a dark placeholder. ----------
        smokeView = NoireDraw3D.CreateRenderView(scene, new Camera3D(center + new Vector3(0f, 7f, 15f), center + new Vector3(0f, 1f, 0f)), 512, 384);
        scene.Own(smokeView);
        smokePortalNode = scene.AddQuad(5f, 3.75f, Material.Unlit(new Vector4(0.05f, 0.05f, 0.08f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X - 14f, center.Y + 2.5f, center.Z + 4f), "Portal", keepCpuData: true)
             .RotateX(MathF.PI * 0.5f);
        smokePortalNode.MakeSelectable();
        smokePortalReady = false;

        // ---- Station 5: a game-icon texture on a quad AND a DecalShape.Texture decal (loaded async; materials swapped
        // in once ready in LoadSmokeIconAsync). They start as neutral placeholders. -------------------------------------
        smokeIconQuad = scene.AddQuad(3f, 3f, Material.Unlit(new Vector4(0.25f, 0.25f, 0.28f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X + 12f, center.Y + 1.6f, center.Z + 2f), "Tex.IconQuad", keepCpuData: true)
             .RotateX(MathF.PI * 0.5f);
        smokeIconQuad.MakeSelectable();
        smokeIconDecal = scene.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0.5f, 0.5f, 0.55f, 0.7f)), new Vector3(center.X + 12f, center.Y, dz), "Decal.Texture", keepCpuData: true)
             .Scale(new Vector3(4f, 4f, 4f));
        smokeIconDecal.MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        LoadSmokeIcon(scene, 60074u);

        // The editor follows the selection: left-click any object to select it, then drag the handles (the camera stays
        // put while you drag). Multi-select (Ctrl toggles, Shift adds) is scoped - restored when the scene is disposed.
        var editor = smokeEditor = scene.CreateEditor(GizmoOp.Universal);
        editor.MultiSelect = true;
        editor.Gizmo.Space = GizmoSpace.Local;
        editor.Gizmo.Depth = GizmoDepth.AlwaysOnTop;
        editor.Gizmo.Snap = 0.05f;
        editor.Gizmo.ScaleSnap = 0.05f;
        editor.Gizmo.RotateSnapDeg = 1f;
        editor.SelectionOutline = new Vector4(1f, 0.85f, 0.2f, 1f);
        editor.OutlineWidth = 4f;
    }

    /// <summary>The smoke decals' actor-exclusion predicate: characters, monsters and NPCs (players, battle NPCs, event NPCs) are skipped.</summary>
    private static bool SmokeActorExclusion(IGameObject o)
        => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc;
    //=> false;

    /// <summary>
    /// Loads a game-icon texture off-thread, then swaps it onto the icon quad + textured decal (a material-reference
    /// assignment is atomic, so it is safe from any thread). The texture is owned by the diagnostics and freed in
    /// <see cref="ClearSmokeScene"/>. Guarded against the scene being cleared/re-spawned mid-load. Uses a continuation
    /// rather than <c>await</c> because this type is <c>unsafe</c> (async methods cannot be).
    /// </summary>
    private void LoadSmokeIcon(Scene3D scene, uint iconId)
    {
        TextureLoader.FromGameIconAsync(iconId).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                NoireLogger.LogError(task.Exception!, "Draw3D smoke: game-icon texture load failed.", "Draw3D");
                return;
            }

            var tex = task.Result;
            if (tex == null)
                return;

            if (scene.IsDisposed || !ReferenceEquals(smokeScene, scene))
            {
                tex.Dispose(); // the scene went away (or was re-spawned) while loading
                return;
            }

            smokeIcon = tex;
            if (smokeIconQuad?.Renderer is { } quadRenderer)
                quadRenderer.Material = Material.UnlitTextured(tex) with { Cull = CullMode.None };
            if (smokeIconDecal?.Renderer is { } decalRenderer)
                decalRenderer.Material = Material.Decal(DecalShape.Texture, new Vector4(1f, 1f, 1f, 0.95f)) with { Texture = tex };
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Loads a glTF/glb model from disk into the running smoke scene (spawned in front of the player, selectable). The
    /// <c>/noire3d model &lt;path&gt;</c> hook - the one Draw3D feature that needs a file. FBX is not supported directly:
    /// convert once with Blender or FBX2glTF. Loads off the framework thread; scene-graph mutation is thread-safe.
    /// </summary>
    public void SpawnSmokeModel(string path)
    {
        if (smokeScene is not { } scene || scene.IsDisposed)
        {
            Report("Draw3D: run '/noire3d smoke' first, then '/noire3d model <path>'.");
            return;
        }

        path = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Report($"Draw3D: model file not found: '{path}'. Pass an absolute path to a .gltf/.glb.");
            return;
        }

        var spawnAt = smokeCenter + new Vector3(0f, 1f, 13f);
        scene.LoadModelAsync(path, spawnAt, "Smoke.Model", keepCpuData: true).ContinueWith(task =>
        {
            if (task.IsFaulted)
                NoireLogger.LogError(task.Exception!, $"Draw3D smoke: glTF import failed for '{path}'.", "Draw3D");
        }, TaskScheduler.Default);
        Report($"Draw3D: loading model '{Path.GetFileName(path)}' - it appears in front of you when ready (errors go to /xllog).");
    }

    /// <summary>
    /// Per-frame smoke-scene work: swaps the render view's texture onto the portal quad once it exists, and draws the
    /// animated immediate-layer markers (grounded decals + a flat additive orb + a rotating line). Called from
    /// <see cref="OnFrame"/> before the validator, so the <c>Im</c> calls land this frame.
    /// </summary>
    private void OnSmokeFrame(in FrameContext frame)
    {
        if (smokeScene is not { IsDisposed: false })
            return;

        // Deferred: the render view has no texture until it has rendered once. Swap it onto the portal quad then.
        if (!smokePortalReady && smokeView?.Texture is { } viewTex && smokePortalNode?.Renderer is { } portalRenderer)
        {
            smokePortalReady = true;
            portalRenderer.Material = Material.UnlitTextured(viewTex) with { Cull = CullMode.None };
        }

        // Immediate layer: redraws every frame. Two grounded decals (hug the terrain), a flat additive orbiting orb, and
        // a rotating flat line - demonstrating ImShapeStyle placement/blending. Anything not re-requested vanishes.
        var im = NoireDraw3D.Im;
        var t = frame.Time;
        var c = smokeCenter + new Vector3(0f, 0f, -13f);
        var (s, co) = MathF.SinCos(t);
        im.DrawDonut(c, 2.2f, 2.7f, new Vector4(1f, 0.6f, 0.1f, 0.85f));
        im.DrawSector(c, t, MathF.PI / 5f, 0f, 6f, new Vector4(1f, 0.2f, 0.2f, 0.45f));
        im.DrawSphere(c + new Vector3(co * 3f, 2f, s * 3f), 0.35f, new Vector4(0.3f, 0.85f, 1f, 1f), new ImShapeStyle { Additive = true });
        im.DrawLine(c + new Vector3(co * 4f, 0.6f, s * 4f), c + new Vector3(-co * 4f, 0.6f, -s * 4f), 0.1f, new Vector4(0.6f, 1f, 0.7f, 0.9f), new ImShapeStyle { Placement = ImShapePlacement.Flat });
    }

    /// <summary>
    /// Flips the smoke scene's gizmo between the native (in-world depth) and ImGuizmo (classic 2D) backends so both can
    /// be compared in-game without a recompile. Select an object afterwards to see the switch; the ImGuizmo init/draw
    /// diagnostics land in /xllog. No-op with a message when the smoke scene isn't up.
    /// </summary>
    public string ToggleSmokeGizmoBackend()
    {
        if (smokeEditor == null)
            return "Draw3D: no smoke scene - run '/noire3d smoke' first.";

        var gizmo = smokeEditor.Gizmo;
        gizmo.Backend = gizmo.Backend == GizmoBackend.ImGuizmo ? GizmoBackend.Native : GizmoBackend.ImGuizmo;
        return $"Draw3D: smoke gizmo backend = {gizmo.Backend}. Select an object to see it - if ImGuizmo doesn't appear, check /xllog for the '[Gizmo]' lines.";
    }

    /// <summary>Removes the smoke scene: one <see cref="Scene3D.Dispose"/> frees its nodes, owned meshes, view and editor.</summary>
    public void ClearSmokeScene()
    {
        NoireDraw3D.Interaction.DebugLog = false;
        smokeEditor = null;        // owned by the scene; disposed by scene.Dispose() below
        smokeView = null;          // ditto (scene.Own)
        smokePortalNode = null;
        smokeIconQuad = null;
        smokeIconDecal = null;
        smokePortalReady = false;
        var scene = smokeScene;
        smokeScene = null;         // cleared first so any in-flight async load bails instead of touching it
        scene?.Dispose();
        smokeIcon?.Dispose();      // the game-icon texture is diagnostics-owned, not scene-owned
        smokeIcon = null;
    }

    /// <summary>
    /// Toggles the <c>/noire3d worldgeo</c> preview: the game's real collision world near the player
    /// (streamed terrain, background models, housing furniture and dynamic objects that register a collider),
    /// pulled straight from <see cref="World.WorldGeometry"/> and drawn as translucent shaded shells so you can
    /// see exactly what a world-projected decal has to conform to. Pair with <c>/noire3d wire</c> for a wireframe.
    /// Runs on the framework thread (command dispatch), where the collision scene is safe to read.
    /// </summary>
    public string ToggleWorldGeometryPreview()
    {
        NoireDraw3D.EnsureInitialized();

        if (worldGeoScene is { IsDisposed: false } existing)
        {
            existing.Dispose();
            worldGeoScene = null;
            return "Draw3D: world-geometry preview off.";
        }

        var center = NoireService.ObjectTable.LocalPlayer?.Position
            ?? (NoireDraw3D.LastFrameValid ? NoireDraw3D.LastFrame.EyePos : Vector3.Zero);

        var scene = worldGeoScene = NoireDraw3D.CreateScene("worldgeo");
        var mat = Material.Lit(new Vector4(0.35f, 0.75f, 1f, 0.4f)) with { Cull = CullMode.None, Blend = BlendMode.Premultiplied };
        var node = scene.SpawnWorldGeometry(center, 20f, mat, includeAnalytic: true, name: "WorldGeo");
        if (node == null)
        {
            scene.Dispose();
            worldGeoScene = null;
            return "Draw3D: no collision found near you (open area / airborne, or the read faulted - see /xllog).";
        }

        return "Draw3D: world-geometry preview ON - the real collision (terrain, furniture, walls, dynamic objects) around you, translucent blue. '/noire3d worldgeo' again to remove, '/noire3d wire' for wireframe.";
    }

    /// <summary>
    /// The custom pipeline HLSL for the smoke scene's pulse box: an unlit shader whose brightness pulses with time
    /// (<c>EyePosTime.w</c>), premultiplied and world-depth tested, over the standard vertex layout - the minimal shape
    /// of a <see cref="NoireDraw3D.RegisterPipeline"/> shader (fxc-validated at ps_5_0 with warnings-as-errors).
    /// </summary>
    private const string SmokePulseHlsl = """
        #include "Common.hlsli"

        struct VsIn  { float3 pos : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR0; };
        struct PsIn  { float4 svPos : SV_Position; float4 color : COLOR0; float2 clipZW : TEXCOORD1; };

        PsIn vs(VsIn v)
        {
            PsIn o;
            float4 wp = mul(float4(v.pos, 1.0), World);
            o.svPos  = mul(wp, ViewProj);
            o.color  = v.color * BaseColor;
            o.clipZW = o.svPos.zw;
            return o;
        }

        float4 ps(PsIn i) : SV_Target
        {
            float pulse = 0.35 + 0.65 * (0.5 + 0.5 * sin(EyePosTime.w * 3.0));
            float4 c = i.color;
            c.rgb *= pulse;
            float vis = DepthVisibility(DisplayUv(i.svPos), i.clipZW.y, 0.0);
            c.a *= vis;
            return float4(c.rgb * c.a, c.a);
        }
        """;

    // ---------------------------------------------------------------- frame hooks (called by the hub)

    internal void OnFrame(in FrameContext frame, in GameRenderSources.CameraData cam, bool hasDepth)
    {
        if (smokeScene != null)
            OnSmokeFrame(in frame); // portal-texture swap + animated immediate-layer markers while the QA scene is up

        if (validateFramesRemaining <= 0)
            return;

        validateFramesRemaining--;

        // Sample world points: around the player, plus points pushed forward through screen rays.
        Span<Vector3> points = stackalloc Vector3[24];
        var count = 0;

        var player = NoireService.ObjectTable.LocalPlayer?.Position;
        if (player is { } p)
        {
            for (var i = 0; i < 8; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / 8f);
                points[count++] = p + new Vector3(cos * 3f, (i % 3) * 0.8f, sin * 3f);
            }
        }

        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (frame.TryScreenToRay(screen, out var origin, out var dir))
                    points[count++] = origin + dir * (5f + gx * 12f + gy * 3f);
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (!frame.TryWorldToScreen(points[i], out var ours))
                continue;
            if (!NoireService.GameGui.WorldToScreen(points[i], out var theirs))
                continue;

            var delta = Vector2.Distance(ours, theirs);
            validateMaxDelta = MathF.Max(validateMaxDelta, delta);
            validateDeltaSum += delta;
            validateSamples++;
        }

        // Cross-check View·Proj against the game's own combined matrix.
        if (cam.HasRenderCamera && cam.HasControlViewProj)
        {
            var ours = cam.View * cam.Proj;
            var theirs = cam.ControlViewProj;
            var maxDelta = 0f;
            var a = ours;
            var b = theirs;
            Span<float> av = stackalloc float[16] { a.M11, a.M12, a.M13, a.M14, a.M21, a.M22, a.M23, a.M24, a.M31, a.M32, a.M33, a.M34, a.M41, a.M42, a.M43, a.M44 };
            Span<float> bv = stackalloc float[16] { b.M11, b.M12, b.M13, b.M14, b.M21, b.M22, b.M23, b.M24, b.M31, b.M32, b.M33, b.M34, b.M41, b.M42, b.M43, b.M44 };
            for (var i = 0; i < 16; i++)
                maxDelta = MathF.Max(maxDelta, MathF.Abs(av[i] - bv[i]));
            validateMaxMatrixDelta = MathF.Max(validateMaxMatrixDelta, maxDelta);
        }

        if (validateFramesRemaining == 0)
        {
            var mean = validateSamples > 0 ? validateDeltaSum / validateSamples : 0;
            var verdict = validateMaxDelta <= 1.0f ? "PASS" : "FAIL";
            var report = $"Draw3D validate [{verdict}]: {validateSamples} samples over 10 frames - max {validateMaxDelta:F3} px, mean {mean:F3} px (gate: max ≤ 1 px). " +
                         $"VP cross-check max element delta: {validateMaxMatrixDelta:E2}. Camera fallback active: {frame.UsedFallbackCamera}. " +
                         "Repeat in the §7.5 poses: orbit, side-on grazing, wall-collision camera, first-person, max zoom.";
            NoireService.ChatGui.Print($"Draw3D validate: {verdict} - max {validateMaxDelta:F3} px (details in log).");
            NoireLogger.LogInfo(report, "Draw3D");
        }
    }

    internal void OnFrameRendered(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        if (!probePending)
            return;

        probePending = false;

        try
        {
            RunProbeNow(device, in frame, sceneDepth);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D depth probe failed.", "Draw3D");
        }
    }

    private void RunProbeNow(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        // Gather ground-truth points: screen positions raycast into the world by the game itself.
        var screens = new List<Vector2>();
        var worlds = new List<Vector3>();
        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (NoireService.GameGui.ScreenToWorld(screen, out var world))
                {
                    screens.Add(screen);
                    worlds.Add(world);
                }
            }
        }

        if (screens.Count == 0)
        {
            Report("Draw3D probe: no raycast hits (nothing under the sampled screen points?). Aim the camera at terrain and retry.");
            return;
        }

        // Expected buffer value from the analytic depth map (what rendering actually uses), plus our
        // reconstructed device-z as a cross-check. Both come from the camera, never from a fit.
        var cam = NoireDraw3D.LastCameraData;
        var near = cam.NearPlane > 1e-6f ? cam.NearPlane : 0.1f;
        var map = DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane);
        var expectedMap = new float[screens.Count];
        var expectedProjZ = new float[screens.Count];
        var clipWs = new float[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            var clip = Vector4.Transform(new Vector4(worlds[i], 1f), frame.ViewProj);
            clipWs[i] = clip.W;
            expectedMap[i] = clip.W > 1e-6f ? map.X + map.Y / clip.W : float.NaN;
            expectedProjZ[i] = clip.W > 1e-6f ? clip.Z / clip.W : float.NaN;
        }

        // Actual values from both candidate depth buffers.
        float[]? actualMain = null, actualSwap = null;
        string mainDesc = "unavailable", swapDesc = "unavailable";
        if (GameRenderSources.TryGetDepthTexture(out var mainInfo))
            actualMain = DepthReadback.TryReadAtPoints(device, in mainInfo, screens, frame.ViewportSize, out mainDesc);
        if (GameRenderSources.TryGetSwapChainDepthTexture(out var swapInfo))
            actualSwap = DepthReadback.TryReadAtPoints(device, in swapInfo, screens, frame.ViewportSize, out swapDesc);

        // An informational least-squares fit of the same raycast points (diagnostic ONLY - never used for
        // rendering). A gap between this and the analytic map is collision-vs-rendered-surface disagreement,
        // which is exactly why fitting was abandoned in favour of the analytic map.
        var fitXs = new List<float>(screens.Count);
        var fitYs = new List<float>(screens.Count);
        if (actualMain != null)
        {
            for (var i = 0; i < screens.Count; i++)
            {
                if (clipWs[i] > 1e-6f && !float.IsNaN(actualMain[i]) && actualMain[i] is >= 0f and <= 1f)
                {
                    fitXs.Add(1f / clipWs[i]);
                    fitYs.Add(actualMain[i]);
                }
            }
        }

        var fitDesc = DepthCalibration.TrySolve(fitXs, fitYs, out var fitA, out var fitB, out var fitResid, out var fitInliers)
            ? $"z={fitA:E2}{(fitB >= 0 ? "+" : "")}{fitB:F5}/w ({fitInliers} pts, resid {fitResid:E1})"
            : "unfittable this frame";

        var details = new StringBuilder();
        details.AppendLine($"Draw3D probe: {screens.Count} raycast points. Active source: {sceneDepth?.Description ?? "none"}.");
        details.AppendLine($"  analytic map (used by rendering): z={map.X:E2}{(map.Y >= 0 ? "+" : "")}{map.Y:F5}/w");
        details.AppendLine($"  raycast fit (diagnostic only):    {fitDesc}");
        details.AppendLine($"  RenderTargetManager depth: {mainDesc}");
        details.AppendLine($"  SwapChain depth:           {swapDesc}");
        details.AppendLine("  point | expected(map) expected(projZ) | actual(RTM) actual(Swap)");
        for (var i = 0; i < screens.Count; i++)
            details.AppendLine($"  {i,2}: {Fmt(expectedMap, i)} {Fmt(expectedProjZ, i)} | {Fmt(actualMain, i)} {Fmt(actualSwap, i)}");

        var mainVsMap = CountMatches(expectedMap, actualMain);
        var swapVsMap = CountMatches(expectedMap, actualSwap);
        details.AppendLine($"  matches within 1e-3: RTM×map {mainVsMap}/{screens.Count}, Swap×map {swapVsMap}/{screens.Count}");

        // UI-mask alpha health. Per-pixel game-UI-on-top depends on the backbuffer alpha channel holding
        // native-UI coverage; in FFXIV the native UI often writes NO alpha there, in which case these read
        // ~0, the per-pixel mask is inert, and the layer draws over the HUD (a known engine limitation).
        var health = NoireDraw3D.UiMaskHealthState;
        details.AppendLine($"  ui mask: {health?.Description ?? "off"}");
        if (health?.LastSamples is { } alphas)
            details.AppendLine($"  ui alpha samples: {string.Join(" ", Array.ConvertAll(alphas, a => a.ToString("F2")))}");

        var gate = (int)MathF.Ceiling(screens.Count * 0.9f);
        var verdict = mainVsMap >= gate ? "PASS"
            : swapVsMap >= gate ? "FAIL - scene depth lives in the SwapChain buffer at present time (paste the log)"
            : "FAIL - the analytic map does not match the RTM buffer; paste the point table "
              + "(mismatched rows are usually collision-vs-rendered-surface disagreement, harmless if few)";

        Report($"Draw3D probe [{verdict.Split(' ')[0]}]: RTM×map {mainVsMap}/{screens.Count} (gate ≥ {gate}). {(verdict.Contains('-') ? verdict[(verdict.IndexOf('-') + 2)..] : "Analytic depth mapping confirmed against ground truth.")}");
        NoireLogger.LogInfo($"Draw3D probe details:\n{details}", "Draw3D");
    }

    private static string Fmt(float[]? values, int i)
        => values == null || float.IsNaN(values[i]) ? "   n/a  " : values[i].ToString("F6");

    private static int CountMatches(float[] expected, float[]? actual)
    {
        if (actual == null)
            return 0;

        var matches = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!float.IsNaN(expected[i]) && !float.IsNaN(actual[i]) && MathF.Abs(expected[i] - actual[i]) <= 1e-3f)
                matches++;
        }

        return matches;
    }

    private static void Report(string message)
    {
        NoireService.ChatGui.Print(message);
        NoireLogger.LogInfo(message, "Draw3D");
    }
}
