using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Assets;
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
using System.Threading.Tasks;

namespace NoireDraw3DDemoPlugin.Windows.Sections;

/// <summary>
/// The reference QA "smoke" scene - a hands-on gallery of (almost) every Draw3D feature in one disposable scene, built
/// entirely on the public API. Formerly baked into the library's <c>Draw3DDiagnostics</c>; it now lives here so the
/// library ships no showcase. Owns its scene, editor, render view and icon texture, and frees them all on
/// <see cref="Clear"/> / <see cref="Dispose"/>.
/// </summary>
public sealed class SmokeSceneSection : IDisposable
{
    private const string SmokePulsePipeline = "NoireSmokePulse";
    private static bool smokePulseRegistered;

    private Scene3D? scene;
    private SceneEditor? editor;
    private RenderView? view;         // the render-to-texture mirror/portal source
    private Vector3 center;
    private SceneNode? portalNode;    // shows view's texture (material swapped in once it exists)
    private SceneNode? iconQuad;      // shows a game-icon texture (material swapped in once loaded)
    private SceneNode? iconDecal;     // a DecalShape.Texture decal (material swapped in once loaded)
    private GpuTexture? icon;         // the loaded game-icon texture, disposed with the section
    private bool portalReady;

    private string modelPath = string.Empty;
    private string status = "No smoke scene spawned.";

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        ImGui.TextWrapped("A gallery of nearly every Draw3D feature: all mesh primitives, every decal footprint, the material families, a custom-pipeline pulse box, a textured quad, a render-to-texture portal, animated immediate-layer markers, and the selection/gizmo editor - all selectable and movable.");
        ImGui.Separator();

        var spawned = scene is { IsDisposed: false };
        if (ImGui.Button(spawned ? "Respawn smoke scene" : "Spawn smoke scene"))
            Spawn();
        ImGui.SameLine();
        using (ImRaiiDisabled(!spawned))
        {
            if (ImGui.Button("Clear"))
                Clear();
        }

        ImGui.Spacing();
        ImGui.TextDisabled(status);

        if (!spawned)
            return;

        ImGui.Separator();
        ImGui.TextUnformatted("glTF / glb model import");
        ImGui.SetNextItemWidth(320f);
        ImGui.InputTextWithHint("##smokeModelPath", @"Absolute path to a .gltf / .glb", ref modelPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Load model"))
            SpawnModel(modelPath);

        ImGui.Separator();
        SectionUi.Toggle("Show decal shape outlines", static () => NoireDraw3D.Diagnostics.DecalShapeOutlines, static v => NoireDraw3D.Diagnostics.DecalShapeOutlines = v,
            "Traces the shape every ground decal actually paints as a closed 3D line on its plane - circle, ring, pie or rect, straight from the SDF.\n\nGlobal, so it covers the immediate-layer donut and pie by the orbiting orb too, which have no node to toggle.\n\nA placement / sizing aid.");

        ImGui.Separator();
        if (editor != null)
        {
            ImGui.TextUnformatted($"Gizmo backend: {editor.Gizmo.Backend}");
            ImGui.SameLine();
            if (ImGui.Button("Toggle backend"))
            {
                editor.Gizmo.Backend = editor.Gizmo.Backend == GizmoBackend.ImGuizmo ? GizmoBackend.Native : GizmoBackend.ImGuizmo;
                status = $"Gizmo backend = {editor.Gizmo.Backend}. Select an object to see it.";
            }
        }
    }

    /// <summary>Spawns the smoke scene around the player (or the world origin when no player is present).</summary>
    private void Spawn()
    {
        Clear();

        center = NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
        var s = scene = NoireDraw3D.CreateScene("smoke");
        s.OnPrepareFrame += OnPrepareFrame; // render-thread per-frame work (portal swap + immediate markers)

        // Register the custom pulse pipeline once (used by the custom-shader station); a compile failure disables only it.
        if (!smokePulseRegistered)
            smokePulseRegistered = NoireDraw3D.RegisterPipeline(SmokePulsePipeline, SmokePulseHlsl);

        // ---- Station 1: every MeshBuilder primitive, one Lit node each, all selectable (a row to the north). --------
        var pz = center.Z + 9f;
        var x = center.X - 12f;
        SceneNode Row(SceneNode n) { n.MakeSelectable(); x += 2.5f; return n; }
        Row(s.AddBox(new Vector3(1.4f, 1.4f, 1.4f), Material.Lit(new Vector4(0.90f, 0.50f, 0.40f, 1f)), new Vector3(x, center.Y + 0.9f, pz), "Prim.Box", keepCpuData: true));
        Row(s.AddSphere(0.8f, Material.Lit(new Vector4(0.50f, 0.80f, 0.55f, 1f)), new Vector3(x, center.Y + 1f, pz), "Prim.Sphere", keepCpuData: true));
        Row(s.AddCylinder(0.7f, 1.5f, Material.Lit(new Vector4(0.50f, 0.60f, 0.90f, 1f)), new Vector3(x, center.Y + 0.75f, pz), "Prim.Cylinder", keepCpuData: true));
        Row(s.AddCone(0.8f, 1.6f, Material.Lit(new Vector4(0.90f, 0.80f, 0.40f, 1f)), new Vector3(x, center.Y + 0.1f, pz), "Prim.Cone", keepCpuData: true));
        Row(s.AddTorus(0.8f, 0.30f, Material.Lit(new Vector4(0.80f, 0.50f, 0.90f, 1f)), new Vector3(x, center.Y + 1f, pz), "Prim.Torus", keepCpuData: true));
        Row(s.AddDisc(0.9f, Material.Lit(new Vector4(0.55f, 0.90f, 0.90f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.Disc", keepCpuData: true));
        Row(s.AddRing(0.4f, 0.9f, Material.Lit(new Vector4(0.90f, 0.60f, 0.60f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.Ring", keepCpuData: true));
        Row(s.AddArrow(1.6f, Material.Lit(new Vector4(0.90f, 0.90f, 0.50f, 1f)), new Vector3(x, center.Y + 0.2f, pz), "Prim.Arrow", keepCpuData: true));
        Row(s.Spawn(MeshBuilder.Sector(MathF.PI / 4f, 0.3f, 1f), Material.Lit(new Vector4(1f, 0.70f, 0.30f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.Sector", keepCpuData: true));
        var ribbon = new List<Vector3> { new(-1f, 0f, 0f), new(-0.3f, 0f, 0.6f), new(0.3f, 0f, -0.6f), new(1f, 0f, 0f) };
        Row(s.Spawn(MeshBuilder.ExtrudePath(ribbon, 0.25f), Material.Lit(new Vector4(0.70f, 1f, 0.70f, 1f)) with { Cull = CullMode.None }, new Vector3(x, center.Y + 1f, pz), "Prim.ExtrudePath", keepCpuData: true));
        var combined = new MeshBuilder().AddBox(new Vector3(1f, 0.4f, 1f)).AddSphere(0.45f, new Vector3(0f, 0.6f, 0f)).ToMeshData();
        Row(s.Spawn(combined, Material.Lit(new Vector4(0.80f, 0.80f, 0.88f, 1f)), new Vector3(x, center.Y + 0.6f, pz), "Prim.Combined", keepCpuData: true));

        // ---- Station 2: every ground-decal footprint shape (Texture added when the icon loads, in LoadIcon).
        var dz = center.Z - 7f;
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0.30f, 0.70f, 1f, 0.9f)) with { Surface = DecalSurface.Ground, Projection = DecalProjection.HighestOnly }, new Vector3(center.X - 9f, center.Y, dz), "Decal.Circle", keepCpuData: true)
         .Scale(new Vector3(4f, 4f, 4f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Ring, new Vector4(1f, 0.55f, 0.10f, 0.9f), new Vector4(0.6f, 0f, 0f, 0.5f)) with { Surface = DecalSurface.Ground }, new Vector3(center.X - 3.5f, center.Y, dz), "Decal.Ring", keepCpuData: true)
         .Scale(new Vector3(5f, 4f, 5f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Sector, new Vector4(0.90f, 0.15f, 0.15f, 0.9f), new Vector4(MathF.PI / 4f, 0f, 0f, 0.55f)) with { Surface = DecalSurface.Ground }, new Vector3(center.X + 2f, center.Y, dz), "Decal.Sector", keepCpuData: true)
         .Scale(new Vector3(6f, 4f, 6f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Rect, new Vector4(0.60f, 0.35f, 1f, 0.9f)) with { Surface = DecalSurface.Ground }, new Vector3(center.X + 7f, center.Y, dz), "Decal.Rect", keepCpuData: true)
         .Scale(new Vector3(4f, 4f, 3f)).MakeSelectable().ExcludeObjects(SmokeActorExclusion);

        // ---- Station 3: material families + blending + the custom pulse pipeline (a cluster to the west). -----------
        s.AddSphere(0.75f, Material.Unlit(new Vector4(0.20f, 0.60f, 1f, 0.8f)) with { Blend = BlendMode.Additive }, new Vector3(center.X - 9f, center.Y + 1.5f, center.Z + 1f), "Mat.Additive", keepCpuData: true).MakeSelectable();
        s.AddQuad(4f, 4f, Material.Unlit(new Vector4(0.30f, 1f, 0.50f, 0.5f), depthFade: 0.35f) with { Cull = CullMode.None }, new Vector3(center.X - 9f, center.Y + 0.05f, center.Z - 2f), "Mat.DepthFadeQuad", keepCpuData: true).MakeSelectable();
        var pulseMat = smokePulseRegistered
            ? Material.Custom(SmokePulsePipeline, new Vector4(1f, 0.40f, 0.80f, 1f))
            : Material.Unlit(new Vector4(1f, 0.40f, 0.80f, 1f)); // fallback if the pipeline failed to compile
        s.AddBox(new Vector3(1.3f, 1.3f, 1.3f), pulseMat, new Vector3(center.X - 13f, center.Y + 1f, center.Z), "Mat.CustomPulse", keepCpuData: true).MakeSelectable();

        // Opaque box stack (private-depth V2↔V2 occlusion), east.
        for (var i = 0; i < 3; i++)
            s.AddBox(Material.Lit(new Vector4(0.8f - i * 0.2f, 0.4f + i * 0.25f, 0.35f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X + 7f, center.Y + 0.5f + i * 1.05f, center.Z), $"Stack.Box{i}", keepCpuData: true)
             .RotateY(i * 0.4f).MakeSelectable();

        // ---- Station 4: render-to-texture mirror/portal. The view renders THIS scene from a raised camera; its texture
        // is swapped onto the quad once it exists (null on frame 0) in OnPrepareFrame. The quad starts as a dark placeholder.
        view = NoireDraw3D.CreateRenderView(s, new Camera3D(center + new Vector3(0f, 7f, 15f), center + new Vector3(0f, 1f, 0f)), 512, 384);
        s.Own(view);
        portalNode = s.AddQuad(5f, 3.75f, Material.Unlit(new Vector4(0.05f, 0.05f, 0.08f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X - 14f, center.Y + 2.5f, center.Z + 4f), "Portal", keepCpuData: true)
             .RotateX(MathF.PI * 0.5f);
        portalNode.MakeSelectable();
        portalReady = false;

        // ---- Station 5: a game-icon texture on a quad AND a DecalShape.Texture decal (loaded async; materials swapped
        // in once ready in LoadIcon). They start as neutral placeholders. --------------------------------------------
        iconQuad = s.AddQuad(3f, 3f, Material.Unlit(new Vector4(0.25f, 0.25f, 0.28f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X + 12f, center.Y + 1.6f, center.Z + 2f), "Tex.IconQuad", keepCpuData: true)
             .RotateX(MathF.PI * 0.5f);
        iconQuad.MakeSelectable();
        iconDecal = s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0.5f, 0.5f, 0.55f, 0.7f)), new Vector3(center.X + 12f, center.Y, dz), "Decal.Texture", keepCpuData: true)
             .Scale(new Vector3(4f, 4f, 4f));
        iconDecal.MakeSelectable().ExcludeObjects(SmokeActorExclusion);
        LoadIcon(s, 60074u);

        // The editor follows the selection: left-click any object to select it, then drag the handles.
        var e = editor = s.CreateEditor(GizmoOp.Universal);
        e.MultiSelect = true;
        e.Gizmo.Space = GizmoSpace.Local;
        e.Gizmo.Depth = GizmoDepth.AlwaysOnTop;
        e.Gizmo.Snap = 0.05f;
        e.Gizmo.ScaleSnap = 0.05f;
        e.Gizmo.RotateSnapDeg = 15f;
        e.SelectionOutline = new Vector4(1f, 0.85f, 0.2f, 1f);
        e.OutlineWidth = 4f;

        status = "Smoke scene spawned. Left-click an object to select it, then drag the gizmo handles.";
    }

    /// <summary>The smoke decals' actor-exclusion predicate: characters, monsters and NPCs are skipped.</summary>
    private static bool SmokeActorExclusion(IGameObject o)
        => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc;

    /// <summary>
    /// Loads a game-icon texture off-thread, then swaps it onto the icon quad + textured decal (a material-reference
    /// assignment is atomic, so it is safe from any thread). Guarded against the scene being cleared/re-spawned mid-load.
    /// </summary>
    private void LoadIcon(Scene3D forScene, uint iconId)
    {
        TextureLoader.FromGameIconAsync(iconId).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                NoireLogger.LogError(task.Exception!, "Draw3D smoke: game-icon texture load failed.", "Draw3D Demo");
                return;
            }

            var tex = task.Result;
            if (tex == null)
                return;

            if (forScene.IsDisposed || !ReferenceEquals(scene, forScene))
            {
                tex.Dispose(); // the scene went away (or was re-spawned) while loading
                return;
            }

            icon = tex;
            if (iconQuad?.Renderer is { } quadRenderer)
                quadRenderer.Material = Material.UnlitTextured(tex) with { Cull = CullMode.None };
            if (iconDecal?.Renderer is { } decalRenderer)
                decalRenderer.Material = Material.Decal(DecalShape.Texture, new Vector4(1f, 1f, 1f, 0.95f)) with { Texture = tex };
        }, TaskScheduler.Default);
    }

    /// <summary>Loads a glTF/glb model from disk into the running smoke scene (spawned in front of the player, selectable).</summary>
    private void SpawnModel(string path)
    {
        if (scene is not { IsDisposed: false } s)
        {
            status = "Spawn the smoke scene first, then load a model.";
            return;
        }

        path = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            status = $"Model file not found: '{path}'. Pass an absolute path to a .gltf / .glb.";
            return;
        }

        var spawnAt = center + new Vector3(0f, 1f, 13f);
        s.LoadModelAsync(path, spawnAt, "Smoke.Model", keepCpuData: true).ContinueWith(task =>
        {
            if (task.IsFaulted)
                NoireLogger.LogError(task.Exception!, $"Draw3D smoke: glTF import failed for '{path}'.", "Draw3D Demo");
        }, TaskScheduler.Default);
        status = $"Loading model '{Path.GetFileName(path)}' - it appears in front of you when ready (errors go to /xllog).";
    }

    /// <summary>
    /// Per-frame smoke-scene work (render thread, via <see cref="Scene3D.OnPrepareFrame"/>): swaps the render view's
    /// texture onto the portal quad once it exists, and draws the animated immediate-layer markers.
    /// </summary>
    private void OnPrepareFrame(FrameContext frame)
    {
        if (scene is not { IsDisposed: false })
            return;

        // Deferred: the render view has no texture until it has rendered once. Swap it onto the portal quad then.
        if (!portalReady && view?.Texture is { } viewTex && portalNode?.Renderer is { } portalRenderer)
        {
            portalReady = true;
            portalRenderer.Material = Material.UnlitTextured(viewTex) with { Cull = CullMode.None };
        }

        // Immediate layer: redraws every frame. Two grounded decals, a flat additive orbiting orb, and a rotating line.
        var im = NoireDraw3D.Im;
        var t = frame.Time;
        var c = center + new Vector3(0f, 0f, -13f);
        var (sn, co) = MathF.SinCos(t);
        im.DrawDonut(c, 2.2f, 2.7f, new Vector4(1f, 0.6f, 0.1f, 0.85f));
        im.DrawSector(c, t, MathF.PI / 5f, 0f, 6f, new Vector4(1f, 0.2f, 0.2f, 0.45f));
        im.DrawSphere(c + new Vector3(co * 3f, 2f, sn * 3f), 0.35f, new Vector4(0.3f, 0.85f, 1f, 1f), new ImShapeStyle { Additive = true });
        im.DrawLine(c + new Vector3(co * 4f, 0.6f, sn * 4f), c + new Vector3(-co * 4f, 0.6f, -sn * 4f), 0.1f, new Vector4(0.6f, 1f, 0.7f, 0.9f), new ImShapeStyle { Placement = ImShapePlacement.Flat });
    }

    /// <summary>Removes the smoke scene: one <see cref="Scene3D.Dispose"/> frees its nodes, owned meshes, view and editor.</summary>
    private void Clear()
    {
        editor = null;       // owned by the scene; disposed by scene.Dispose() below
        view = null;         // ditto (scene.Own)
        portalNode = null;
        iconQuad = null;
        iconDecal = null;
        portalReady = false;
        var s = scene;
        scene = null;        // cleared first so any in-flight async load bails instead of touching it
        s?.Dispose();
        icon?.Dispose();     // the game-icon texture is section-owned, not scene-owned
        icon = null;
        status = "Smoke scene cleared.";
    }

    /// <inheritdoc/>
    public void Dispose() => Clear();

    // A tiny local Disabled scope so the section needn't take a dependency on the exact ImRaii Disabled overload name.
    private static IDisposable ImRaiiDisabled(bool disabled) => Dalamud.Interface.Utility.Raii.ImRaii.Disabled(disabled);

    /// <summary>
    /// The custom pipeline HLSL for the pulse box: an unlit shader whose brightness pulses with time (<c>EyePosTime.w</c>),
    /// premultiplied and world-depth tested, over the standard vertex layout - the minimal shape of a
    /// <see cref="NoireDraw3D.RegisterPipeline"/> shader.
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
}
