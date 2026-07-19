using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Bindings.ImGui;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// Loads models straight out of the game's archives and draws them with this renderer. The file is read and decoded
/// here; nothing is spawned in the game and no game function is called, so what appears is inert geometry that only
/// this layer knows about.
/// </summary>
internal sealed class GameAssetsPage : IDisposable
{
    private const string DefaultPath = "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl";

    /// <summary>Paths verified to exist, covering both vertex layouts the decoder has to handle.</summary>
    private static readonly (string Label, string Path)[] Presets =
    [
        ("Furniture", "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl"),
        ("Furniture 2", "bgcommon/hou/indoor/general/0002/bgparts/fun_b0_m0002.mdl"),
        ("Body", "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl"),
        ("Equipment", "chara/equipment/e0001/model/c0101e0001_top.mdl"),
        ("Monster", "chara/monster/m0001/obj/body/b0001/model/m0001b0001.mdl"),
    ];

    private readonly List<SceneNode> spawned = [];

    private Scene3D? scene;
    private string path = DefaultPath;
    private int lod;
    private bool importVertexColors;
    private bool keepCpuData = true;
    private Vector4 tint = new(0.82f, 0.82f, 0.86f, 1f);
    private float distance = 4f;
    private string status = string.Empty;
    private bool failed;
    private bool loading;
    private GameModelMesh[]? loaded;

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        ConsumeLoaded();

        Ui.Section("Load a model");
        Ui.Note("Reads a .mdl from the game archives, decodes it here, and draws it in front of you. Reading a file is all that happens: no actor, no object table entry, nothing the game or its server knows about.");
        Ui.Gap();

        DrawPresets();
        Ui.Gap();

        using (Ui.Form("assets.load"))
        {
            Ui.Text("Game path", () => path, v => path = v, DefaultPath, 512,
                "Archive path of the model. Anything under bgcommon/ is a static prop; chara/ paths are character parts.");
            Ui.Int("Level of detail", () => lod, v => lod = Math.Clamp(v, 0, 2),
                "0 is the most detailed. Models carry up to three; asking for one a model does not have loads nothing.");
            Ui.Toggle("Import vertex colors", () => importVertexColors, v => importVertexColors = v,
                "Off by default. The game stores shader data in this channel (blend and wetness masks) rather than color, so applying it as a tint paints the model in false colors. Turn it on to see exactly that.");
            Ui.Toggle("Keep CPU data", () => keepCpuData, v => keepCpuData = v,
                "Retains the decoded geometry so exact per-triangle picking works on the spawned node.");
            Ui.Color4("Tint", () => tint, v => tint = v,
                "Materials are not resolved yet, so every piece draws with this one lit material.");
            Ui.Slider("Distance", () => distance, v => distance = v, 1f, 20f,
                "How far in front of you the model is placed.");
        }

        Ui.Gap();
        DrawActions();

        if (status.Length > 0)
        {
            Ui.Gap();
            if (failed)
                Ui.Callout(status, ImGuiColors.DalamudRed);
            else
                Ui.Status(status);
        }

        DrawSpawned();
    }

    private void DrawPresets()
    {
        for (var i = 0; i < Presets.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            if (ImGui.SmallButton(Presets[i].Label))
                path = Presets[i].Path;
        }

        ImGui.SameLine();
        Ui.HelpMarker("Paths confirmed to exist in a complete installation. The furniture entries are static meshes; the character entries carry skinning data the decoder currently ignores.");
    }

    private void DrawActions()
    {
        using (Ui.Disabled(loading || path.Length == 0))
        {
            if (Ui.IconButton(FontAwesomeIcon.Download, loading ? "Loading..." : "Load and spawn"))
                BeginLoad();
        }

        ImGui.SameLine();

        using (Ui.Disabled(spawned.Count == 0))
        {
            if (Ui.IconButton(FontAwesomeIcon.Trash, "Clear"))
                Clear();
        }
    }

    private void DrawSpawned()
    {
        if (loaded is null || loaded.Length == 0)
            return;

        Ui.Section("What was decoded");

        var vertices = 0;
        var indices = 0;
        foreach (var mesh in loaded)
        {
            vertices += mesh.Geometry.Vertices.Length;
            indices += mesh.Geometry.Indices.Length;
        }

        Ui.Mono($"{loaded.Length} mesh(es)   {vertices} vertices   {indices / 3} triangles");
        Ui.Gap();

        for (var i = 0; i < loaded.Length; i++)
        {
            var material = loaded[i].MaterialPath;
            Ui.Mono($"[{i}] {loaded[i].Geometry.Vertices.Length,6} verts   {(material.Length > 0 ? material : "(no material)")}",
                ImGuiColors.DalamudGrey3);
        }

        Ui.Gap();
        Ui.Note("Material paths are reported but not yet resolved. Character models store them relative, beginning with a slash; the folder they resolve against comes from the item's variant.");
    }

    private void BeginLoad()
    {
        loading = true;
        failed = false;
        status = string.Empty;

        var requested = path;
        var requestedLod = lod;
        var colors = importVertexColors;

        GameModelLoader.LoadAsync(requested, requestedLod, colors).ContinueWith(task =>
        {
            loading = false;

            if (task.IsFaulted)
            {
                failed = true;
                status = task.Exception?.GetBaseException().Message ?? "Load failed.";
                return;
            }

            if (task.Result.Length == 0)
            {
                failed = true;
                status = $"Nothing decoded from '{requested}'. The path may not exist, or it has no geometry at level {requestedLod}.";
                return;
            }

            loaded = task.Result;
        });
    }

    /// <summary>
    /// Spawns whatever the last load produced. Done from the draw loop rather than the load continuation so the scene is
    /// only ever touched from one thread, and so a load that finishes after this page is disposed has nothing to spawn into.
    /// </summary>
    private void ConsumeLoaded()
    {
        if (loaded is null || spawned.Count > 0)
            return;

        var target = EnsureScene();
        if (target is null)
            return;

        var origin = PlayerPos() + (Forward() * distance);
        var material = Material.Lit(tint);

        foreach (var mesh in loaded)
        {
            var node = target.Spawn(mesh.Geometry, material, origin, "GameAsset", keepCpuData);
            node.MakeSelectable();
            spawned.Add(node);
        }

        status = $"Spawned {spawned.Count} mesh(es) {distance:F0}m in front of you.";
    }

    private Scene3D? EnsureScene()
    {
        if (scene is { IsDisposed: false })
            return scene;

        scene = NoireDraw3D.CreateScene("Draw3DDemo.GameAssets");
        return scene;
    }

    private void Clear()
    {
        foreach (var node in spawned)
        {
            if (!node.IsDestroyed)
                node.Destroy();
        }

        spawned.Clear();
        loaded = null;
        status = string.Empty;
        failed = false;
    }

    private static Vector3 PlayerPos() => NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;

    private static Vector3 Forward()
    {
        var rotation = NoireService.ObjectTable.LocalPlayer?.Rotation ?? 0f;
        return new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var target = scene;
        scene = null;
        target?.Dispose();
        spawned.Clear();
    }
}
