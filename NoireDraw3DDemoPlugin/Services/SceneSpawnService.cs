using Dalamud.Game.ClientState.Objects.Enums;
using NoireDraw3DDemoPlugin.Helpers;
using NoireDraw3DDemoPlugin.Models;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

namespace NoireDraw3DDemoPlugin.Services;

internal sealed class SceneSpawnService : IDisposable
{
    public enum Primitive { Box, Sphere, Cylinder, Cone, Torus, Quad, Disc, Ring, Arrow }

    private readonly List<DemoScene> scenes = new();
    private bool mainSceneAdded;
    private int spawnCounter;

    public IReadOnlyList<DemoScene> Scenes => scenes;

    public Vector4 PrimColor { get; set; } = new(0.85f, 0.60f, 0.40f, 1f);
    public bool PrimLit { get; set; } = true;

    // Ui.Enum binds a dropdown by ref.
    private int decalShapeIdx;
    private int decalSurfaceIdx;
    private int decalProjIdx;

    public ref int DecalShapeIdx => ref decalShapeIdx;
    public ref int DecalSurfaceIdx => ref decalSurfaceIdx;
    public ref int DecalProjIdx => ref decalProjIdx;

    public bool DecalAdditive { get; set; }
    public Vector4 DecalColor { get; set; } = new(0.30f, 0.70f, 1f, 0.9f);
    public bool DecalCustomOutlineColor { get; set; }
    public Vector4 DecalOutlineColor { get; set; } = new(1f, 0.95f, 0.55f, 1f);
    public bool DecalShowVolume { get; set; }
    public float DecalSize { get; set; } = 4f;
    public float DecalOutline { get; set; } = 0.08f;

    public string ModelPath { get; set; } = string.Empty;
    public bool ModelVertexColors { get; set; }
    public bool ModelGenerateLods { get; set; }
    public float WorldRadius { get; set; } = 20f;
    public bool WorldAnalytic { get; set; } = true;

    /// <summary>The status line the Scenes tab shows under the world/model actions.</summary>
    public string Status { get; set; } = string.Empty;

    public void EnsureMainScene()
    {
        if (mainSceneAdded)
            return;

        mainSceneAdded = true;
        scenes.Insert(0, new DemoScene(NoireDraw3D.MainScene, owned: false, "MainScene"));
    }

    public DemoScene NewScene()
    {
        var scene = NoireDraw3D.CreateScene($"demo{scenes.Count}");
        var demo = new DemoScene(scene, owned: true, $"demo{scenes.Count}");
        demo.EnsureEditor(GizmoOp.Universal);
        scenes.Add(demo);
        return demo;
    }

    public void PruneDisposedScenes(ref DemoScene? open, ref SceneNode? inspected)
    {
        for (var i = scenes.Count - 1; i >= 0; i--)
        {
            if (!scenes[i].Owned || !scenes[i].Scene.IsDisposed)
                continue;

            if (ReferenceEquals(open, scenes[i]))
            {
                open = null;
                inspected = null;
            }

            scenes.RemoveAt(i);
        }
    }

    public SceneNode SpawnPrimitive(DemoScene demo, Primitive kind)
    {
        var scene = demo.Scene;
        var pos = NextSpawnPos();
        var mat = PrimLit ? Material.Lit(PrimColor) : Material.Unlit(PrimColor) with { Blend = BlendMode.Additive };
        var node = kind switch
        {
            Primitive.Box => scene.AddBox(new Vector3(1.2f, 1.2f, 1.2f), mat, pos, "Box", keepCpuData: true),
            Primitive.Sphere => scene.AddSphere(0.7f, mat, pos, "Sphere", keepCpuData: true),
            Primitive.Cylinder => scene.AddCylinder(0.6f, 1.4f, mat, pos, "Cylinder", keepCpuData: true),
            Primitive.Cone => scene.AddCone(0.7f, 1.4f, mat, pos, "Cone", keepCpuData: true),
            Primitive.Torus => scene.AddTorus(0.7f, 0.25f, mat, pos, "Torus", keepCpuData: true),
            Primitive.Quad => scene.AddQuad(1.4f, 1.4f, mat with { Cull = CullMode.None }, pos, "Quad", keepCpuData: true),
            Primitive.Disc => scene.AddDisc(0.8f, mat with { Cull = CullMode.None }, pos, "Disc", keepCpuData: true),
            Primitive.Ring => scene.AddRing(0.4f, 0.8f, mat with { Cull = CullMode.None }, pos, "Ring", keepCpuData: true),
            _ => scene.AddArrow(1.4f, mat, pos, "Arrow", keepCpuData: true),
        };

        demo.Track(node.MakeSelectable());
        demo.Selection.SetSingle(node);
        return node;
    }

    public SceneNode SpawnDecal(DemoScene demo)
    {
        var pos = DemoPlayerHelper.Position();
        var shape = Enum.GetValues<DecalShape>()[decalShapeIdx];
        var surface = Enum.GetValues<DecalSurface>()[decalSurfaceIdx];
        var projection = Enum.GetValues<DecalProjection>()[decalProjIdx];

        var mat = Material.Decal(shape, DecalColor, outlineWidth: DecalOutline, surface: surface, projection: projection, additive: DecalAdditive,
                                 outlineColor: DecalCustomOutlineColor ? DecalOutlineColor : null);
        var node = demo.Scene.AddBox(mat, pos, "Decal", keepCpuData: true)
             .Scale(new Vector3(DecalSize, DecalSize, DecalSize))
             .MakeSelectable()
             .ExcludeObjects(static o => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc);

        if (DecalShowVolume)
            node.ShowDecalVolume();

        demo.Track(node);
        demo.Selection.SetSingle(node);
        return node;
    }

    public void LoadModel(DemoScene demo)
    {
        var path = ModelPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = $"Model file not found: '{path}'. Pass an absolute path to a .gltf / .glb.";
            return;
        }

        var scene = demo.Scene;
        var at = DemoPlayerHelper.Position() + new Vector3(0f, 1f, 4f);
        Status = $"Loading '{Path.GetFileName(path)}'. It appears in front of you when ready (errors go to /xllog).";
        scene.LoadModelAsync(path, at, Path.GetFileNameWithoutExtension(path), keepCpuData: true, importVertexColors: ModelVertexColors, generateLods: ModelGenerateLods)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    NoireLogger.LogError(task.Exception!, $"Draw3D demo: glTF import failed for '{path}'.", "Draw3D Demo");
                    Status = $"Import failed for '{Path.GetFileName(path)}'. See /xllog.";
                    return;
                }

                // The scene may have been disposed while loading.
                if (scene.IsDisposed || !ReferenceEquals(demo.Scene, scene))
                    return;

                demo.Track(task.Result.Root.MakeSelectable());
                Status = $"Imported '{Path.GetFileName(path)}'.";
            }, TaskScheduler.Default);
    }

    /// <summary>Spawns the game's real collision around the player as a translucent mesh. Framework thread only.</summary>
    public void SpawnWorldGeometry(DemoScene demo)
    {
        if (demo.Scene.IsDisposed)
            return;

        var mat = Material.Lit(new Vector4(0.35f, 0.75f, 1f, 0.4f)) with { Cull = CullMode.None, Blend = BlendMode.Premultiplied };
        var node = demo.Scene.SpawnWorldGeometry(DemoPlayerHelper.Position(), WorldRadius, mat, WorldAnalytic, "WorldGeometry", keepCpuData: true);
        if (node == null)
        {
            Status = "No collision found near you (open area / airborne, or the read faulted, see /xllog).";
            return;
        }

        demo.Track(node.MakeSelectable());
        Status = "Spawned the real collision around you, translucent blue.";
    }

    /// <summary>Projects a decal footprint onto the real collision surface under the player. Framework thread only.</summary>
    public void SpawnWorldDecal(DemoScene demo)
    {
        if (demo.Scene.IsDisposed)
            return;

        var mat = Material.Unlit(DecalColor) with { Cull = CullMode.None };
        var node = demo.Scene.SpawnWorldDecal(DemoPlayerHelper.Position(), Vector3.UnitY, DecalSize, DecalSize, mat, depth: 3f, name: "WorldDecal");
        if (node == null)
        {
            Status = "Nothing under the footprint to project onto.";
            return;
        }

        demo.Track(node.MakeSelectable());
        Status = "Projected a decal onto the real world surface (it drapes over slopes and furniture).";
    }

    private Vector3 NextSpawnPos()
    {
        var p = DemoPlayerHelper.Position();
        var i = spawnCounter++;
        return p + new Vector3((i % 5) * 2f - 4f, 1f, 4f + i / 5 * 2f);
    }

    public void Dispose()
    {
        foreach (var demo in scenes)
            demo.TearDown();

        scenes.Clear();
    }
}
