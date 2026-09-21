using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using NoireDraw3DDemoPlugin.Helpers;
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

namespace NoireDraw3DDemoPlugin.Services;

internal sealed class ShowcaseSceneService : IDisposable
{
    private const string PulsePipeline = "NoireShowcasePulse";
    private static bool pulseRegistered;

    private Scene3D? scene;
    private SceneEditor? editor;
    private RenderView? view;
    private Vector3 center;
    private SceneNode? portalNode;    // material swapped in once the view exists
    private SceneNode? iconQuad;      // material swapped in once loaded
    private SceneNode? iconDecal;     // material swapped in once loaded
    private GpuTexture? icon;
    private bool portalReady;

    public bool IsSpawned => scene is { IsDisposed: false };

    /// <summary>The editor driving the showcase gizmo, once <see cref="Spawn"/> has run.</summary>
    public SceneEditor? Editor => editor;

    /// <summary>The status line the Scene section shows next to the spawn/clear buttons.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Spawns the showcase scene around the player (or the world origin when no player is present).</summary>
    public void Spawn()
    {
        Clear();

        center = DemoPlayerHelper.Position();
        var s = scene = NoireDraw3D.CreateScene("showcase");
        s.OnPrepareFrame += OnPrepareFrame;

        // A failed compile disables only that station.
        if (!pulseRegistered)
            pulseRegistered = NoireDraw3D.RegisterPipeline(PulsePipeline, PulseHlsl);

        // Primitives.
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

        // Decal footprint shapes.
        var dz = center.Z - 7f;
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0.30f, 0.70f, 1f, 0.9f), projection: DecalProjection.HighestOnly), new Vector3(center.X - 9f, center.Y, dz), "Decal.Circle", keepCpuData: true)
         .Scale(new Vector3(4f, 4f, 4f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Ring, new Vector4(1f, 0.55f, 0.10f, 0.9f), new Vector4(0.6f, 0f, 0f, 0.5f)), new Vector3(center.X - 3.5f, center.Y, dz), "Decal.Ring", keepCpuData: true)
         .Scale(new Vector3(5f, 4f, 5f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Sector, new Vector4(0.90f, 0.15f, 0.15f, 0.9f), new Vector4(MathF.PI / 4f, 0f, 0f, 0.55f)), new Vector3(center.X + 2f, center.Y, dz), "Decal.Sector", keepCpuData: true)
         .Scale(new Vector3(6f, 4f, 6f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        // Rect's outline color is independent of its fill.
        s.AddBox(Material.Decal(DecalShape.Rect, new Vector4(0.60f, 0.35f, 1f, 0.9f), outlineColor: new Vector4(1f, 0.75f, 0.25f, 1f)), new Vector3(center.X + 7f, center.Y, dz), "Decal.Rect", keepCpuData: true)
         .Scale(new Vector3(4f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);

        // Additive red, green and blue sum to white where they meet.
        var addZ = dz - 4f;
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(1f, 0f, 0f, 0.9f), additive: true), new Vector3(center.X - 1.1f, center.Y, addZ + 0.6f), "Decal.Add.R", keepCpuData: true)
         .Scale(new Vector3(3f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0f, 1f, 0f, 0.9f), additive: true), new Vector3(center.X + 1.1f, center.Y, addZ + 0.6f), "Decal.Add.G", keepCpuData: true)
         .Scale(new Vector3(3f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);
        s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0f, 0f, 1f, 0.9f), additive: true), new Vector3(center.X, center.Y, addZ - 1.2f), "Decal.Add.B", keepCpuData: true)
         .Scale(new Vector3(3f, 4f, 3f)).MakeSelectable().ExcludeObjects(ActorExclusion);

        // Materials and blending.
        s.AddSphere(0.75f, Material.Unlit(new Vector4(0.20f, 0.60f, 1f, 0.8f)) with { Blend = BlendMode.Additive }, new Vector3(center.X - 9f, center.Y + 1.5f, center.Z + 1f), "Mat.Additive", keepCpuData: true).MakeSelectable();
        s.AddQuad(4f, 4f, Material.Unlit(new Vector4(0.30f, 1f, 0.50f, 0.5f), depthFade: 0.35f) with { Cull = CullMode.None }, new Vector3(center.X - 9f, center.Y + 0.05f, center.Z - 2f), "Mat.DepthFadeQuad", keepCpuData: true).MakeSelectable();
        var pulseMat = pulseRegistered
            ? Material.Custom(PulsePipeline, new Vector4(1f, 0.40f, 0.80f, 1f))
            : Material.Unlit(new Vector4(1f, 0.40f, 0.80f, 1f)); // the pipeline failed to compile
        s.AddBox(new Vector3(1.3f, 1.3f, 1.3f), pulseMat, new Vector3(center.X - 13f, center.Y + 1f, center.Z), "Mat.CustomPulse", keepCpuData: true).MakeSelectable();

        // Opaque boxes occlude each other through the private depth.
        for (var i = 0; i < 3; i++)
            s.AddBox(Material.Lit(new Vector4(0.8f - i * 0.2f, 0.4f + i * 0.25f, 0.35f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X + 7f, center.Y + 0.5f + i * 1.05f, center.Z), $"Stack.Box{i}", keepCpuData: true)
             .RotateY(i * 0.4f).MakeSelectable();

        // Render-to-texture portal.
        view = NoireDraw3D.CreateRenderView(s, new Camera3D(center + new Vector3(0f, 7f, 15f), center + new Vector3(0f, 1f, 0f)), 512, 384);
        s.Own(view);
        portalNode = s.AddQuad(5f, 3.75f, Material.Unlit(new Vector4(0.05f, 0.05f, 0.08f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X - 14f, center.Y + 2.5f, center.Z + 4f), "Portal", keepCpuData: true)
             .RotateX(MathF.PI * 0.5f);
        portalNode.MakeSelectable();
        portalReady = false;

        // Async-loaded icon texture.
        iconQuad = s.AddQuad(3f, 3f, Material.Unlit(new Vector4(0.25f, 0.25f, 0.28f, 1f)) with { Cull = CullMode.None }, new Vector3(center.X + 12f, center.Y + 1.6f, center.Z + 2f), "Tex.IconQuad", keepCpuData: true)
             .RotateX(MathF.PI * 0.5f);
        iconQuad.MakeSelectable();
        iconDecal = s.AddBox(Material.Decal(DecalShape.Circle, new Vector4(0.5f, 0.5f, 0.55f, 0.7f)), new Vector3(center.X + 12f, center.Y, dz), "Decal.Texture", keepCpuData: true)
             .Scale(new Vector3(4f, 4f, 4f));
        iconDecal.MakeSelectable().ExcludeObjects(ActorExclusion);
        LoadIcon(s, 60074u);

        var e = editor = s.CreateEditor(GizmoOp.Universal);
        e.MultiSelect = true;
        e.Gizmo.Space = GizmoSpace.Local;
        e.Gizmo.Depth = GizmoDepth.AlwaysOnTop;
        e.Gizmo.Snap = 0.05f;
        e.Gizmo.ScaleSnap = 0.05f;
        e.Gizmo.RotateSnapDeg = 15f;
        e.SelectionOutline = new Vector4(1f, 0.85f, 0.2f, 1f);
        e.OutlineWidth = 4f;

        Status = "Spawned around you. Left-click an object to select it, then drag the gizmo handles.";
    }

    private static bool ActorExclusion(IGameObject o)
        => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc;

    // Material assignment is an atomic reference swap.
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
                tex.Dispose(); // the scene went away while loading
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
    public void SpawnModel(string path)
    {
        if (scene is not { IsDisposed: false } s)
        {
            Status = "Spawn the scene first, then load a model.";
            return;
        }

        path = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = $"Model file not found: '{path}'. Pass an absolute path to a .gltf / .glb.";
            return;
        }

        var spawnAt = center + new Vector3(0f, 1f, 13f);
        s.LoadModelAsync(path, spawnAt, "Showcase.Model", keepCpuData: true).ContinueWith(task =>
        {
            if (task.IsFaulted)
                NoireLogger.LogError(task.Exception!, $"Draw3D showcase: glTF import failed for '{path}'.", "Draw3D Demo");
        }, TaskScheduler.Default);
        Status = $"Loading model '{Path.GetFileName(path)}'. It appears in front of you when ready (errors go to /xllog).";
    }

    private void OnPrepareFrame(FrameContext frame)
    {
        if (scene is not { IsDisposed: false })
            return;

        // The render view has no texture until it renders once.
        if (!portalReady && view?.Texture is { } viewTex && portalNode?.Renderer is { } portalRenderer)
        {
            portalReady = true;
            portalRenderer.Material = Material.UnlitTextured(viewTex) with { Cull = CullMode.None };
        }

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
        editor = null;       // owned by the scene
        view = null;         // owned by the scene
        portalNode = null;
        iconQuad = null;
        iconDecal = null;
        portalReady = false;
        var s = scene;
        scene = null;        // a pending async load sees null
        s?.Dispose();
        icon?.Dispose();
        icon = null;
        Status = "Scene cleared.";
    }

    public void Dispose() => Clear();

    // Brightness pulses with EyePosTime.w. Premultiplied, depth-tested.
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
