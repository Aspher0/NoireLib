using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
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

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// The showcase scene: (almost) every Draw3D feature in one disposable scene, built entirely on the public API and laid
/// out as stations around the player so features can be found and compared in the world. Owns its scene, editor, render
/// view and icon texture; frees them all on <see cref="Clear"/> / <see cref="Dispose"/>.
/// </summary>
public sealed class ShowcasePage : IDisposable
{
    private const string PulsePipeline = "NoireShowcasePulse";
    private static bool pulseRegistered;

    private Scene3D? scene;
    private SceneEditor? editor;
    private RenderView? view;         // the render-to-texture mirror/portal source
    private Vector3 center;
    private SceneNode? portalNode;    // shows view's texture (material swapped in once it exists)
    private SceneNode? iconQuad;      // shows a game-icon texture (material swapped in once loaded)
    private SceneNode? iconDecal;     // a DecalShape.Texture decal (material swapped in once loaded)
    private GpuTexture? icon;         // the loaded game-icon texture, disposed with the page
    private bool portalReady;

    private string modelPath = string.Empty;
    private string status = string.Empty;

    /// <summary>Whether the showcase scene is currently in the world.</summary>
    public bool IsSpawned => scene is { IsDisposed: false };

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        Ui.Section("Scene");
        if (Ui.IconButton(FontAwesomeIcon.Cubes, IsSpawned ? "Respawn here" : "Spawn here", 150f))
            Spawn();
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Builds the gallery around where you are standing. Respawning moves it to you.");

        ImGui.SameLine();
        using (Ui.Disabled(!IsSpawned))
        {
            if (Ui.IconButton(FontAwesomeIcon.TrashAlt, "Clear", 110f))
                Clear();
        }

        if (!IsSpawned)
        {
            Ui.Gap();
            Ui.Note("Every primitive, every decal footprint, the material families, a custom pipeline, a textured quad, an "
                    + "RTT portal and the immediate layer - one scene, all of it selectable.");
            return;
        }

        ImGui.SameLine();
        Ui.Status(status);

        Ui.Section("Stations");
        using (Ui.Form("showcase.stations", 110f))
        {
            Station("North", "Mesh primitives",
                "Box, sphere, cylinder, cone, torus, disc, ring, arrow, sector, an extruded path, and one mesh combining two builders. All Lit, so the Lighting page moves them together.");
            Station("South", "Decal footprints",
                "Circle, ring, sector, rect, and a texture stamp, each scaled differently but all sharing one constant-thickness rim. All cut around characters standing in them. The circle is HighestOnly, so it needs the collision height-map on to skip the floor under a table. Behind them, three additive circles overlap to white.");
            Station("Far south", "Immediate layer",
                "Donut, sweeping pie, orbiting additive orb, spinning line. Redrawn every frame from OnPrepareFrame, so there is no node to select.");
            Station("West", "Materials",
                "Additive sphere, a depth-faded quad softening where it meets the ground, and a box on a custom HLSL pipeline pulsing with EyePosTime.w.");
            Station("West, up", "RTT portal",
                "This scene rendered from a second camera above it. The texture only exists after the view's first frame, so it starts as a dark placeholder.");
            Station("East", "Depth, textures",
                "A stack of rotated opaque boxes occluding each other through Draw3D's private depth, and a quad stamped with a game icon.");
        }

        if (editor != null)
        {
            Ui.Section("Gizmo");
            using (Ui.Form("showcase.gizmo"))
            {
                Ui.Enum("Backend", () => editor.Gizmo.Backend, v => editor.Gizmo.Backend = v,
                    "Same API either way.\n\nNative: real in-world geometry hit-tested in screen space - occludes correctly, never wobbles.\n\nImGuizmo: the classic flat handles, always on top.\n\nThis scene runs AlwaysOnTop depth, so Native handles stay visible through walls.");
                Ui.Value("Attached to", editor.Selection.Count switch
                {
                    0 => "-",
                    1 => editor.Selection.Primary?.Name ?? "?",
                    var n => $"{n} objects, primary {editor.Selection.Primary?.Name ?? "?"}",
                }, "Click an object in the world; Shift-click to add more.");
            }
        }

        Ui.Section("Model");
        using (Ui.Form("showcase.model"))
        {
            Ui.Text("glTF / glb", () => modelPath, v => modelPath = v, @"Absolute path", 512,
                "Blender: File > Export > glTF 2.0. Base colour and textures import; PBR maps, skins and animations are skipped and logged. Surrounding quotes are stripped.");
        }

        if (Ui.IconButton(FontAwesomeIcon.FileImport, "Load", 110f))
            SpawnModel(modelPath);
    }

    /// <summary>One station row: where it is, and what it demonstrates.</summary>
    /// <param name="where">Direction from the spawn point.</param>
    /// <param name="what">The feature group.</param>
    /// <param name="detail">What it proves.</param>
    private static void Station(string where, string what, string detail)
    {
        Ui.Row(where);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(what);
        Ui.HelpMarker(detail);
    }

    /// <summary>Spawns the showcase scene around the player (or the world origin when no player is present).</summary>
    public void Spawn()
    {
        Clear();

        center = NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
        var s = scene = NoireDraw3D.CreateScene("showcase");
        s.OnPrepareFrame += OnPrepareFrame; // render-thread per-frame work (portal swap + immediate markers)

        // Register the custom pulse pipeline once (used by the custom-shader station); a compile failure disables only it.
        if (!pulseRegistered)
            pulseRegistered = NoireDraw3D.RegisterPipeline(PulsePipeline, PulseHlsl);

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

        // ---- Station 2: every ground-decal footprint shape (Texture added when the icon loads, in LoadIcon). Every setting
        // rides the Material.Decal(...) factory in one call, and each footprint is scaled differently on purpose - the rim
        // keeps a constant world thickness regardless, so a 4m and a 12m decal read with the same edge.
        var dz = center.Z - 7f;
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0.30f, 0.70f, 1f, 0.9f), projection: DecalProjection.HighestOnly), new Vector3(center.X - 9f, center.Y, dz), "Decal.Circle", keepCpuData: true)
         .Scale(new Vector3(4f, 4f, 4f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Ring, new Vector4(1f, 0.55f, 0.10f, 0.9f), new Vector4(0.6f, 0f, 0f, 0.5f)), new Vector3(center.X - 3.5f, center.Y, dz), "Decal.Ring", keepCpuData: true)
         .Scale(new Vector3(5f, 4f, 5f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Sector, new Vector4(0.90f, 0.15f, 0.15f, 0.9f), new Vector4(MathF.PI / 4f, 0f, 0f, 0.55f)), new Vector3(center.X + 2f, center.Y, dz), "Decal.Sector", keepCpuData: true)
         .Scale(new Vector3(6f, 4f, 6f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        // The rect carries its own border colour (amber rim on a violet fill) - the rim is not tied to the decal colour.
        s.AddBox(Material.Decal(DecalShape.Rect, new Vector4(0.60f, 0.35f, 1f, 0.9f), outlineColor: new Vector4(1f, 0.75f, 0.25f, 1f)), new Vector3(center.X + 7f, center.Y, dz), "Decal.Rect", keepCpuData: true)
         .Scale(new Vector3(4f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);

        // Additive decals: three coloured circles overlapping on the ground. Where all three meet, their light sums to
        // white - the plainest proof that the additive blend now survives the decal path.
        var addZ = dz - 4f;
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(1f, 0f, 0f, 0.9f), additive: true), new Vector3(center.X - 1.1f, center.Y, addZ + 0.6f), "Decal.Add.R", keepCpuData: true)
         .Scale(new Vector3(3f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0f, 1f, 0f, 0.9f), additive: true), new Vector3(center.X + 1.1f, center.Y, addZ + 0.6f), "Decal.Add.G", keepCpuData: true)
         .Scale(new Vector3(3f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0f, 0f, 1f, 0.9f), additive: true), new Vector3(center.X, center.Y, addZ - 1.2f), "Decal.Add.B", keepCpuData: true)
         .Scale(new Vector3(3f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);

        // ---- Station 3: material families + blending + the custom pulse pipeline (a cluster to the west). -----------
        s.AddSphere(0.75f, Material.Unlit(new Vector4(0.20f, 0.60f, 1f, 0.8f)) with { Blend = BlendMode.Additive }, new Vector3(center.X - 9f, center.Y + 1.5f, center.Z + 1f), "Mat.Additive", keepCpuData: true).MakeSelectable();
        s.AddQuad(4f, 4f, Material.Unlit(new Vector4(0.30f, 1f, 0.50f, 0.5f), depthFade: 0.35f) with { Cull = CullMode.None }, new Vector3(center.X - 9f, center.Y + 0.05f, center.Z - 2f), "Mat.DepthFadeQuad", keepCpuData: true).MakeSelectable();
        var pulseMat = pulseRegistered
            ? Material.Custom(PulsePipeline, new Vector4(1f, 0.40f, 0.80f, 1f))
            : Material.Unlit(new Vector4(1f, 0.40f, 0.80f, 1f)); // fallback if the pipeline failed to compile
        s.AddBox(new Vector3(1.3f, 1.3f, 1.3f), pulseMat, new Vector3(center.X - 13f, center.Y + 1f, center.Z), "Mat.CustomPulse", keepCpuData: true).MakeSelectable();

        // Opaque box stack (private-depth occlusion between Draw3D meshes), east.
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
        iconDecal.MakeSelectable().ExcludeObjects(ActorExclusion);
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

        status = "Spawned around you. Left-click an object to select it, then drag the gizmo handles.";
    }

    /// <summary>The showcase decals' actor-exclusion predicate: characters, monsters and NPCs are skipped.</summary>
    private static bool ActorExclusion(IGameObject o)
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
                NoireLogger.LogError(task.Exception!, "Draw3D showcase: game-icon texture load failed.", "Draw3D Demo");
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

    /// <summary>Loads a glTF/glb model from disk into the running showcase scene (spawned in front of the player, selectable).</summary>
    private void SpawnModel(string path)
    {
        if (scene is not { IsDisposed: false } s)
        {
            status = "Spawn the scene first, then load a model.";
            return;
        }

        path = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            status = $"Model file not found: '{path}'. Pass an absolute path to a .gltf / .glb.";
            return;
        }

        var spawnAt = center + new Vector3(0f, 1f, 13f);
        s.LoadModelAsync(path, spawnAt, "Showcase.Model", keepCpuData: true).ContinueWith(task =>
        {
            if (task.IsFaulted)
                NoireLogger.LogError(task.Exception!, $"Draw3D showcase: glTF import failed for '{path}'.", "Draw3D Demo");
        }, TaskScheduler.Default);
        status = $"Loading model '{Path.GetFileName(path)}' - it appears in front of you when ready (errors go to /xllog).";
    }

    /// <summary>
    /// Per-frame showcase work (render thread, via <see cref="Scene3D.OnPrepareFrame"/>): swaps the render view's
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

    /// <summary>Removes the showcase scene: one <see cref="Scene3D.Dispose"/> frees its nodes, owned meshes, view and editor.</summary>
    public void Clear()
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
        icon?.Dispose();     // the game-icon texture is page-owned, not scene-owned
        icon = null;
        status = "Scene cleared.";
    }

    /// <inheritdoc/>
    public void Dispose() => Clear();

    /// <summary>
    /// The custom pipeline HLSL for the pulse box: an unlit shader whose brightness pulses with time (<c>EyePosTime.w</c>),
    /// premultiplied and world-depth tested, over the standard vertex layout - the minimal shape of a
    /// <see cref="NoireDraw3D.RegisterPipeline"/> shader.
    /// </summary>
    private const string PulseHlsl = """
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
