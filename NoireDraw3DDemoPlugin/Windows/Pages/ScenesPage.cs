using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using NoireDraw3DDemoPlugin.Models;
using NoireDraw3DDemoPlugin.Services;
using NoireLib;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Scene;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// The build-your-own workspace. One scene is always open (the permanent <c>MainScene</c> until you make another).
/// Owns the demo scenes and frees them on <see cref="Dispose"/>.
/// </summary>
public sealed class ScenesPage : IDisposable
{
    private const float ObjectListWidth = 250f;

    private readonly SceneSpawnService spawnService = new();
    private readonly NodeInspector inspector = new();

    private DemoScene? open;        // never null once EnsureMainScene has run
    private SceneNode? inspected;   // follows the scene's primary selection
    private int sceneIdx;

    private Vector4 selectionOutlineColor = new(1f, 0.85f, 0.2f, 1f);

    public void Draw()
    {
        spawnService.EnsureMainScene();
        spawnService.PruneDisposedScenes(ref open, ref inspected);

        if (open is null or { Scene.IsDisposed: true })
            OpenScene(spawnService.Scenes[0]);

        var demo = open!;

        // Drawn outside any scroll region to stay pinned.
        DrawSceneBar(demo);

        using var tabs = ImRaii.TabBar("##scenetabs");
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem("Objects"))
        {
            if (tab)
                DrawObjects(demo);
        }

        using (var tab = ImRaii.TabItem("Spawn"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##spawnbody");
                if (body)
                    DrawSpawn(demo);
            }
        }

        using (var tab = ImRaii.TabItem("Editor & gizmo"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##editorbody");
                if (body)
                    DrawEditor(demo.EnsureEditor());
            }
        }

        using (var tab = ImRaii.TabItem("World & models"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##worldbody");
                if (body)
                    DrawWorldAndModels(demo);
            }
        }
    }

    private void DrawSceneBar(DemoScene demo)
    {
        var scenes = spawnService.Scenes;
        var names = new string[scenes.Count];
        for (var i = 0; i < scenes.Count; i++)
            names[i] = $"{scenes[i].Label}  ({scenes[i].Scene.NodeCount} objects)";

        var idx = -1;
        for (var i = 0; i < scenes.Count; i++)
        {
            if (ReferenceEquals(scenes[i], demo))
            {
                idx = i;
                break;
            }
        }

        sceneIdx = Math.Clamp(idx, 0, Math.Max(0, scenes.Count - 1));

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Working on");
        Ui.HelpMarker("Every tab below acts on this scene. Scenes render independently and hold their own selection. Two can overlap in the world without interfering.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(260f * Ui.Scale);
        if (Ui.Combo("##scenepick", names, ref sceneIdx))
            OpenScene(scenes[sceneIdx]);

        ImGui.SameLine();
        if (Ui.Button("New scene"))
            OpenScene(spawnService.NewScene());
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Creates an extra retained scene through NoireDraw3D.CreateScene, rendered after the main one. It is a self-contained unit: its own objects, selection, editor and gizmo.");

        ImGui.SameLine();
        using (Ui.Disabled(!demo.Owned))
        {
            if (Ui.Button("Dispose"))
            {
                demo.TearDown();
                open = null;
            }
        }

        if (!demo.Owned && ImGui.IsItemHovered())
            Ui.Tooltip("MainScene is permanent and cannot be disposed. It belongs to the library. Use \"Clear objects\", or make a new scene.");

        ImGui.SameLine();
        if (Ui.Button("Clear objects"))
        {
            demo.Scene.Clear();
            inspected = null;
        }

        if (ImGui.IsItemHovered())
            Ui.Tooltip("Destroys every object in this scene, keeping the scene itself.");

        using (Ui.Form("scenes.bar"))
        {
            Ui.Text("Scene name", () => demo.Scene.Name ?? string.Empty, v => demo.Scene.Name = string.IsNullOrWhiteSpace(v) ? null : v);
            Ui.Toggle("Scene visible", () => demo.Scene.Visible, v => demo.Scene.Visible = v,
                "Objects keep their own Visible flag underneath.");
        }

        Ui.Gap();
    }

    private void DrawObjects(DemoScene demo)
    {
        demo.PruneDestroyed();

        // Follows the in-world selection unless the list holds a valid pick.
        if (inspected is null or { IsDestroyed: true } || !ReferenceEquals(inspected.Scene, demo.Scene))
            inspected = demo.Selection.Primary;

        using (var list = ImRaii.Child("##objects", new Vector2(ObjectListWidth * Ui.Scale, 0f), true))
        {
            if (list)
                DrawObjectList(demo);
        }

        ImGui.SameLine();

        // The pane never scrolls. The tab bodies scroll on their own.
        using var detail = ImRaii.Child("##inspector", Vector2.Zero, true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!detail)
            return;

        if (inspected is { IsDestroyed: false } target && ReferenceEquals(target.Scene, demo.Scene))
        {
            if (!inspector.Draw(demo, target))
                inspected = null;

            return;
        }

        Ui.Note("Nothing selected. Pick one on the left, or click one in the world.");
    }

    private void DrawObjectList(DemoScene demo)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3))
            ImGui.TextUnformatted($"OBJECTS ({demo.Nodes.Count})");
        ImGui.Separator();

        if (demo.Nodes.Count == 0)
        {
            Ui.Note("Empty. See the Spawn tab.");
            return;
        }

        for (var i = 0; i < demo.Nodes.Count; i++)
        {
            var node = demo.Nodes[i];
            var selected = demo.Selection.Contains(node);

            // The tint marks the scene selection. The highlight tracks the inspector target.
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGold, selected);
            if (ImGui.Selectable($"{node.Name ?? "(unnamed)"}##node{i}", ReferenceEquals(inspected, node)))
            {
                inspected = node;
                demo.Selection.SetSingle(node);
            }
        }
    }

    private void DrawSpawn(DemoScene demo)
    {
        Ui.Section("Primitives");
        using (Ui.Form("scenes.prim"))
        {
            Ui.Color4("Color", () => spawnService.PrimColor, v => spawnService.PrimColor = v);
            Ui.Toggle("Lit", () => spawnService.PrimLit, v => spawnService.PrimLit = v,
                "On uses Material.Lit, shaded against the Lighting page. Off uses Material.Unlit plus additive, which reads as a glow.");
        }

        Ui.Gap();
        if (Ui.Button("Box")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Box);
        ImGui.SameLine(); if (Ui.Button("Sphere")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Sphere);
        ImGui.SameLine(); if (Ui.Button("Cylinder")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Cylinder);
        ImGui.SameLine(); if (Ui.Button("Cone")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Cone);
        ImGui.SameLine(); if (Ui.Button("Torus")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Torus);
        ImGui.SameLine(); if (Ui.Button("Quad")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Quad);
        ImGui.SameLine(); if (Ui.Button("Disc")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Disc);
        ImGui.SameLine(); if (Ui.Button("Ring")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Ring);
        ImGui.SameLine(); if (Ui.Button("Arrow")) inspected = spawnService.SpawnPrimitive(demo, SceneSpawnService.Primitive.Arrow);

        Ui.Section("Decal");
        Ui.Note("A box whose volume the shape is painted inside, projected onto whatever the depth buffer says is there.");
        Ui.Gap();
        using (Ui.Form("scenes.decal"))
        {
            Ui.Enum<DecalShape>("Shape", ref spawnService.DecalShapeIdx,
                "The footprint painted: Circle, Ring (inner radius from ShapeParams.X), Sector (a pie slice), Rect, or Texture (stamps the material's texture).");
            Ui.Enum<DecalSurface>("Surface", ref spawnService.DecalSurfaceIdx,
                "Ground projects down, Wall projects into the wall it faces, Both rotates freely.");
            Ui.Enum<DecalProjection>("Projection", ref spawnService.DecalProjIdx,
                "AllSurfaces paints everything in the box. HighestOnly paints only the topmost surface per column (needs the height-map, Decals page).");
            Ui.Color4("Color", () => spawnService.DecalColor, v => spawnService.DecalColor = v);
            Ui.Toggle("Additive", () => spawnService.DecalAdditive, v => spawnService.DecalAdditive = v,
                "Additive blend: stacked decals sum toward white. Off is the standard translucent blend.");
            Ui.Slider("Footprint size (m)", () => spawnService.DecalSize, v => spawnService.DecalSize = v, 1f, 12f,
                "Scales the projection box: footprint and vertical sweep.");
            Ui.Slider("Outline width", () => spawnService.DecalOutline, v => spawnService.DecalOutline = v, 0f, 0.3f,
                "Rim thickness, held constant in world space regardless of the footprint size above. 0 is a flat fill.");
            Ui.Toggle("Custom border color", () => spawnService.DecalCustomOutlineColor, v => spawnService.DecalCustomOutlineColor = v,
                "On, the border color is set independently below.");
            using (Ui.Disabled(!spawnService.DecalCustomOutlineColor))
                Ui.Color4("Border color", () => spawnService.DecalOutlineColor, v => spawnService.DecalOutlineColor = v);
            Ui.Toggle("Show volume box", () => spawnService.DecalShowVolume, v => spawnService.DecalShowVolume = v,
                "Spawns it with its projection box drawn.");
        }

        Ui.Gap();
        if (Ui.Button("Spawn decal at my feet", new Vector2(220f * Ui.Scale, 0f)))
            inspected = spawnService.SpawnDecal(demo);
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Spawns it where you stand, already excluding characters and NPCs from being painted over.");
    }

    private void DrawEditor(SceneEditor editor)
    {
        var gizmo = editor.Gizmo;

        Ui.Section("Editor");
        using (Ui.Form("scenes.editor"))
        {
            Ui.Toggle("Editor enabled", () => editor.Enabled, v => editor.Enabled = v,
                "Off, the gizmo neither draws nor interacts. The selection still tracks.");
            Ui.Toggle("Multi-select", () => editor.MultiSelect, v => editor.MultiSelect = v,
                "Lets picks build a multi-object selection. The gizmo then edits the group around its centroid.");

            Ui.Row("Selection outline");
            var outlineOn = editor.SelectionOutline.HasValue;
            if (Ui.Check("##outlineon", ref outlineOn))
                editor.SelectionOutline = outlineOn ? selectionOutlineColor : null;

            using (Ui.Disabled(!editor.SelectionOutline.HasValue))
            {
                Ui.Color4("Outline color", () => selectionOutlineColor, v =>
                {
                    selectionOutlineColor = v;
                    if (editor.SelectionOutline.HasValue)
                        editor.SelectionOutline = v;
                });
                Ui.Drag("Outline width (px)", () => editor.OutlineWidth, v => editor.OutlineWidth = v, 0.2f, 1f, 20f);
            }
        }

        Ui.Section("Handles");
        using (Ui.Form("scenes.handles"))
        {
            Ui.Flags("Operations", () => gizmo.Op, v => gizmo.Op = v);
            Ui.Toggle("Visible", () => gizmo.Visible, v => gizmo.Visible = v,
                "Independent of 'Editor enabled'.");
            Ui.Enum("Space", () => gizmo.Space, v => gizmo.Space = v,
                "The frame the translate and rotate handles align to: World (axis-aligned) or Local (the object's own rotation). Scale handles are always object-local.");
            Ui.Enum("Backend", () => gizmo.Backend, v => gizmo.Backend = v,
                "Native: in-world handles that occlude correctly. ImGuizmo: flat 2D handles, always on top.");
            Ui.Enum("Depth", () => gizmo.Depth, v => gizmo.Depth = v,
                "OnTopOfObjects: hidden by walls, drawn over objects. AlwaysOnTop: x-ray. Occluded: fully depth-tested.");
            Ui.Toggle("Drag feedback", () => gizmo.Options.ShowDragFeedback, v => gizmo.Options.ShowDragFeedback = v,
                "Draws the drag preview, an anchor at the pre-drag centre, a guide line, and the live amount moved / rotated / scaled.");
            Ui.Drag("Handle length (px)", () => gizmo.Options.HandlePixelLength, v => gizmo.Options.HandlePixelLength = v, 1f, 20f, 400f,
                "Held constant regardless of camera distance.");
            Ui.Drag("Handle thickness (px)", () => gizmo.Options.HandlePixelThickness, v => gizmo.Options.HandlePixelThickness = v, 0.1f, 1f, 20f);
            Ui.Drag("Grab tolerance (px)", () => gizmo.Options.GrabPixelTolerance, v => gizmo.Options.GrabPixelTolerance = v, 0.2f, 1f, 40f);
        }

        Ui.Section("Snapping");
        Ui.Note("0 is free. Both backends honour all three identically.");
        Ui.Gap();
        using (Ui.Form("scenes.snap"))
        {
            Ui.Drag("Translate snap", () => gizmo.Snap, v => gizmo.Snap = v, 0.01f, 0f, 10f, "World units, all three axes.");
            Ui.Drag("Rotate snap (deg)", () => gizmo.RotateSnapDeg, v => gizmo.RotateSnapDeg = v, 0.5f, 0f, 90f);
            Ui.Drag("Scale snap", () => gizmo.ScaleSnap, v => gizmo.ScaleSnap = v, 0.01f, 0f, 5f);
        }

        Ui.Section("Selection");
        using (Ui.Form("scenes.sel"))
        {
            Ui.Value("Count", editor.Selection.Count.ToString());
            Ui.Value("Primary", editor.Selection.Primary?.Name ?? "-", "The most recently picked; a single-object gizmo binds to it.");
            Ui.Value("Dragging", gizmo.IsDragging ? gizmo.HoveredHandle.ToString() : "-");

            Ui.Row("Clear");
            if (Ui.Button("Clear selection"))
                editor.Selection.Clear();
        }
    }

    private void DrawWorldAndModels(DemoScene demo)
    {
        Ui.Section("Model");
        using (Ui.Form("scenes.model"))
        {
            Ui.Text("Model file", () => spawnService.ModelPath, v => spawnService.ModelPath = v, @"Absolute path to a .gltf / .glb", 512,
                "An absolute path. Surrounding quotes are stripped for Explorer's \"Copy as path\" to paste straight in.");
            Ui.Toggle("Import vertex colors", () => spawnService.ModelVertexColors, v => spawnService.ModelVertexColors = v,
                "Off by default. FFXIV-derived exports use this channel for shader masks.");
            Ui.Toggle("Generate LODs", () => spawnService.ModelGenerateLods, v => spawnService.ModelGenerateLods = v,
                "Builds coarser levels for large primitives at import, drawn as they shrink on screen. The lever for many copies of a heavy model.");
        }

        Ui.Gap();
        if (Ui.Button("Load model", new Vector2(220f * Ui.Scale, 0f)))
            spawnService.LoadModel(demo);

        Ui.Gap();

        Ui.Section("World collision");
        Ui.Note("Reads the live collision scene. Both run on the framework thread and fail soft with nothing under you.");
        Ui.Gap();
        using (Ui.Form("scenes.world"))
        {
            Ui.Slider("Query radius (m)", () => spawnService.WorldRadius, v => spawnService.WorldRadius = v, 5f, 60f,
                "Half-size of the cubic query around you.");
            Ui.Toggle("Include analytic colliders", () => spawnService.WorldAnalytic, v => spawnService.WorldAnalytic = v,
                "Also collects box / cylinder / sphere / plane colliders, such as invisible walls and trigger volumes, in addition to mesh models.");
        }

        Ui.Gap();
        if (Ui.Button("Spawn world geometry", new Vector2(220f * Ui.Scale, 0f)))
            NoireService.Framework.RunOnFrameworkThread(() => spawnService.SpawnWorldGeometry(demo));
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Turns the real collision around you into a translucent scene mesh, the same surface ground decals project onto. A debugging and preview aid.");

        ImGui.SameLine();
        if (Ui.Button("Spawn world decal", new Vector2(220f * Ui.Scale, 0f)))
            NoireService.Framework.RunOnFrameworkThread(() => spawnService.SpawnWorldDecal(demo));
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Projects a footprint onto the collision surface, draping over slopes and furniture.");

        Ui.Gap();
        Ui.Status(spawnService.Status);
    }

    private void OpenScene(DemoScene demo)
    {
        open = demo;
        inspected = null;
        demo.EnsureEditor();
    }

    public void Dispose()
    {
        spawnService.Dispose();
        open = null;
        inspected = null;
    }
}
