using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
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
/// The build-your-own workspace. One scene is always open (the permanent <c>MainScene</c> until you make another), and
/// the work is split into tabs so that spawning, editing the gizmo, and inspecting an object never compete for the same
/// column: <b>Objects</b> is a list beside a full inspector, and the rest are ordinary settings panels. Owns the demo
/// scenes and frees them on <see cref="Dispose"/>.
/// </summary>
public sealed class ScenesPage : IDisposable
{
    private enum Primitive { Box, Sphere, Cylinder, Cone, Torus, Quad, Disc, Ring, Arrow }

    private const float ObjectListWidth = 250f;

    private readonly List<DemoScene> scenes = new();
    private readonly NodeInspector inspector = new();

    private DemoScene? open;        // the scene being worked on; never null once EnsureMainScene has run
    private SceneNode? inspected;   // the object the inspector edits (follows the scene's primary selection)
    private bool mainSceneAdded;
    private int spawnCounter;
    private int sceneIdx;

    // Primitive spawn controls.
    private Vector4 primColor = new(0.85f, 0.60f, 0.40f, 1f);
    private bool primLit = true;

    // Decal spawn controls.
    private int decalShapeIdx;
    private int decalSurfaceIdx;
    private int decalProjIdx;
    private bool decalAdditive;
    private Vector4 decalColor = new(0.30f, 0.70f, 1f, 0.9f);
    private bool decalCustomOutlineColor;
    private Vector4 decalOutlineColor = new(1f, 0.95f, 0.55f, 1f);
    private bool decalShowVolume;
    private float decalSize = 4f;
    private float decalOutline = 0.08f;

    // Editor UI state.
    private Vector4 selectionOutlineColor = new(1f, 0.85f, 0.2f, 1f);

    // World-geometry / glTF import controls.
    private string modelPath = string.Empty;
    private bool modelVertexColors;
    private float worldRadius = 20f;
    private bool worldAnalytic = true;
    private string spawnStatus = string.Empty;

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        EnsureMainScene();
        PruneDisposedScenes();

        if (open is null or { Scene.IsDisposed: true })
            OpenScene(scenes[0]);

        var demo = open!;

        // Bar and tab strip are drawn straight into the page, outside any scroll region, so they stay pinned; each tab
        // then scrolls its own body.
        DrawSceneBar(demo);

        using var tabs = ImRaii.TabBar("##scenetabs");
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem("Objects"))
        {
            if (tab)
                DrawObjects(demo); // already a pair of side-by-side children, each scrolling itself
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

    // ---------------------------------------------------------------- scene bar

    /// <summary>
    /// The always-visible scene strip: which scene the tabs below act on, and the lifecycle buttons. A picker rather than
    /// a list-then-detail navigation, so switching scenes never costs the place you were in.
    /// </summary>
    private void DrawSceneBar(DemoScene demo)
    {
        var names = new string[scenes.Count];
        for (var i = 0; i < scenes.Count; i++)
            names[i] = $"{scenes[i].Label}  ({scenes[i].Scene.NodeCount} objects)";

        sceneIdx = Math.Clamp(scenes.IndexOf(demo), 0, Math.Max(0, scenes.Count - 1));

        // A free-standing toolbar rather than a form row: a picker plus three buttons is wider than a form's control
        // cell, and a table cell clips rather than wraps.
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Working on");
        Ui.HelpMarker("Every tab below acts on this scene. Scenes render independently and hold their own selection, so two can overlap in the world without interfering.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(260f * Ui.Scale);
        if (Ui.Combo("##scenepick", names, ref sceneIdx))
            OpenScene(scenes[sceneIdx]);

        ImGui.SameLine();
        if (ImGui.Button("New scene"))
            OpenScene(NewScene());
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Creates an extra retained scene through NoireDraw3D.CreateScene, rendered after the main one. It is a self-contained unit: its own objects, selection, editor and gizmo.");

        ImGui.SameLine();
        using (Ui.Disabled(!demo.Owned))
        {
            if (ImGui.Button("Dispose"))
            {
                demo.TearDown();
                open = null;
            }
        }

        if (!demo.Owned && ImGui.IsItemHovered())
            Ui.Tooltip("MainScene is permanent and cannot be disposed - it belongs to the library, not the demo. Use \"Clear objects\" instead, or make a new scene.");

        ImGui.SameLine();
        if (ImGui.Button("Clear objects"))
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

    // ---------------------------------------------------------------- objects

    /// <summary>
    /// The object list beside the inspector. This is the pairing the old single-column layout could not express: pick on
    /// the left, edit on the right, with the world selection kept in sync both ways.
    /// </summary>
    private void DrawObjects(DemoScene demo)
    {
        demo.PruneDestroyed();

        // Follow the scene's in-world selection unless the list has an explicit, still-valid pick.
        if (inspected is null or { IsDestroyed: true } || !ReferenceEquals(inspected.Scene, demo.Scene))
            inspected = demo.Selection.Primary;

        using (var list = ImRaii.Child("##objects", new Vector2(ObjectListWidth * Ui.Scale, 0f), true))
        {
            if (list)
                DrawObjectList(demo);
        }

        ImGui.SameLine();

        // The pane itself never scrolls: it holds the inspector's own pinned header and tab strip, and the tab bodies
        // inside scroll on their own.
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

            // The list shows what the world shows: hovering an entry is not selection, so the tint marks what the scene
            // selection actually holds, and the inspector target is what the entry highlight tracks.
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGold, selected);
            if (ImGui.Selectable($"{node.Name ?? "(unnamed)"}##node{i}", ReferenceEquals(inspected, node)))
            {
                inspected = node;
                demo.Selection.SetSingle(node);
            }
        }
    }

    // ---------------------------------------------------------------- spawn

    private void DrawSpawn(DemoScene demo)
    {
        Ui.Section("Primitives");
        using (Ui.Form("scenes.prim"))
        {
            Ui.Color4("Color", () => primColor, v => primColor = v);
            Ui.Toggle("Lit", () => primLit, v => primLit = v,
                "On: Material.Lit, shaded against the Lighting page. Off: Material.Unlit + additive, which reads as a glow.");
        }

        Ui.Gap();
        if (ImGui.Button("Box")) SpawnPrimitive(demo, Primitive.Box);
        ImGui.SameLine(); if (ImGui.Button("Sphere")) SpawnPrimitive(demo, Primitive.Sphere);
        ImGui.SameLine(); if (ImGui.Button("Cylinder")) SpawnPrimitive(demo, Primitive.Cylinder);
        ImGui.SameLine(); if (ImGui.Button("Cone")) SpawnPrimitive(demo, Primitive.Cone);
        ImGui.SameLine(); if (ImGui.Button("Torus")) SpawnPrimitive(demo, Primitive.Torus);
        ImGui.SameLine(); if (ImGui.Button("Quad")) SpawnPrimitive(demo, Primitive.Quad);
        ImGui.SameLine(); if (ImGui.Button("Disc")) SpawnPrimitive(demo, Primitive.Disc);
        ImGui.SameLine(); if (ImGui.Button("Ring")) SpawnPrimitive(demo, Primitive.Ring);
        ImGui.SameLine(); if (ImGui.Button("Arrow")) SpawnPrimitive(demo, Primitive.Arrow);

        Ui.Section("Decal");
        Ui.Note("A box whose volume the shape is painted inside, projected onto whatever the depth buffer says is there.");
        Ui.Gap();
        using (Ui.Form("scenes.decal"))
        {
            Ui.Enum<DecalShape>("Shape", ref decalShapeIdx,
                "The footprint painted: Circle, Ring (inner radius from ShapeParams.X), Sector (a pie slice), Rect, or Texture (stamps the material's texture).");
            Ui.Enum<DecalSurface>("Surface", ref decalSurfaceIdx,
                "Locks the box's orientation. Ground stays horizontal and projects down onto the floor; Wall stays vertical and projects into the wall it faces (grow it so it reaches); Both rotates freely and its orientation decides.\n\nSpawn one, then rotate it with the gizmo to feel the lock.");
            Ui.Enum<DecalProjection>("Projection", ref decalProjIdx,
                "AllSurfaces paints everything inside the box. HighestOnly paints only the topmost surface per column - a tabletop, not the floor under it - and needs the collision height-map on (Decals page).");
            Ui.Color4("Color", () => decalColor, v => decalColor = v);
            Ui.Toggle("Additive", () => decalAdditive, v => decalAdditive = v,
                "Blends additively, so stacked coloured decals sum their light toward white - overlap a red, green and blue one to see white. Off is the standard translucent blend.");
            Ui.Slider("Footprint size (m)", () => decalSize, v => decalSize = v, 1f, 12f,
                "Scales the projection box: footprint and vertical sweep. The outline rim keeps a constant world thickness no matter how large this gets.");
            Ui.Slider("Outline width", () => decalOutline, v => decalOutline = v, 0f, 0.3f,
                "Rim thickness, held constant in world space regardless of the footprint size above. 0 is a flat fill.");
            Ui.Toggle("Custom border color", () => decalCustomOutlineColor, v => decalCustomOutlineColor = v,
                "Off, the border is the decal's own colour and only its opacity differs from the fill (the classic look). On, the border gets its own colour.");
            using (Ui.Disabled(!decalCustomOutlineColor))
                Ui.Color4("Border color", () => decalOutlineColor, v => decalOutlineColor = v);
            Ui.Toggle("Show volume box", () => decalShowVolume, v => decalShowVolume = v,
                "Spawns it with its projection box drawn - the volume the SDF is evaluated in, and the limit of what it can paint. Handy while sizing the vertical sweep. Toggle it per object later in the inspector, or for every decal at once on the Renderer page.");
        }

        Ui.Gap();
        if (ImGui.Button("Spawn decal at my feet", new Vector2(220f * Ui.Scale, 0f)))
            SpawnDecal(demo);
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Spawns it where you stand, already excluding characters and NPCs so they are not painted over.");
    }

    // ---------------------------------------------------------------- editor & gizmo

    private void DrawEditor(SceneEditor editor)
    {
        var gizmo = editor.Gizmo;

        Ui.Section("Editor");
        using (Ui.Form("scenes.editor"))
        {
            Ui.Toggle("Editor enabled", () => editor.Enabled, v => editor.Enabled = v,
                "Off, the gizmo neither draws nor interacts. The selection still tracks.");
            Ui.Toggle("Multi-select", () => editor.MultiSelect, v => editor.MultiSelect = v,
                "Lets picks build a multi-object selection, using the toggle / add modifiers from the Interaction page. The gizmo then edits the whole group around its centroid.\n\nThis is a scoped setting: it is restored when the editor or scene is disposed, so it leaves no global behind.");

            Ui.Row("Selection outline");
            var outlineOn = editor.SelectionOutline.HasValue;
            if (ImGui.Checkbox("##outlineon", ref outlineOn))
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
                "Native: in-world depth handles hit-tested in screen space - they occlude correctly and never wobble.\n\nImGuizmo: the classic flat 2D handles, always on top.");
            Ui.Enum("Depth", () => gizmo.Depth, v => gizmo.Depth = v,
                "Native backend only.\n\nOnTopOfObjects (the editor default): occluded by the world but drawn over other 3D objects, so a handle is never buried inside what it edits.\n\nAlwaysOnTop: full x-ray.\n\nOccluded: fully depth-tested.");
            Ui.Toggle("Drag feedback", () => gizmo.Options.ShowDragFeedback, v => gizmo.Options.ShowDragFeedback = v,
                "Draws the drag preview: an anchor at the pre-drag centre, a guide line, and the live amount moved / rotated / scaled.");
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
            if (ImGui.Button("Clear selection"))
                editor.Selection.Clear();
        }
    }

    // ---------------------------------------------------------------- world & models

    /// <summary>
    /// The world-collision and imported-model spawns: <see cref="Draw3DWorld.SpawnWorldGeometry"/> /
    /// <see cref="Draw3DWorld.SpawnWorldDecal"/> (framework-thread only) and the glTF importer.
    /// </summary>
    private void DrawWorldAndModels(DemoScene demo)
    {
        Ui.Section("Model");
        using (Ui.Form("scenes.model"))
        {
            Ui.Text("Model file", () => modelPath, v => modelPath = v, @"Absolute path to a .gltf / .glb", 512,
                "An absolute path. Surrounding quotes are stripped, so Explorer's \"Copy as path\" pastes straight in.");
            Ui.Toggle("Import vertex colors", () => modelVertexColors, v => modelVertexColors = v,
                "Off by default, and it usually should be: FFXIV-derived exports store shader data (wetness and wind masks) in COLOR_0 rather than albedo, so importing it as a tint paints the model in psychedelic colours.");
        }

        Ui.Gap();
        if (ImGui.Button("Load model", new Vector2(220f * Ui.Scale, 0f)))
            LoadModel(demo);

        Ui.Section("World collision");
        Ui.Note("Reads the live collision scene, so both run on the framework thread and fail soft with nothing under you.");
        Ui.Gap();
        using (Ui.Form("scenes.world"))
        {
            Ui.Slider("Query radius (m)", () => worldRadius, v => worldRadius = v, 5f, 60f,
                "Half-size of the cubic query around you.");
            Ui.Toggle("Include analytic colliders", () => worldAnalytic, v => worldAnalytic = v,
                "Also collect box / cylinder / sphere / plane colliders - invisible walls and trigger volumes - not just mesh models.");
        }

        Ui.Gap();
        if (ImGui.Button("Spawn world geometry", new Vector2(220f * Ui.Scale, 0f)))
            NoireService.Framework.RunOnFrameworkThread(() => SpawnWorldGeometry(demo));
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Turns the real collision around you into a translucent scene mesh - the same surface ground decals project onto. A debugging and preview aid.");

        ImGui.SameLine();
        if (ImGui.Button("Spawn world decal", new Vector2(220f * Ui.Scale, 0f)))
            NoireService.Framework.RunOnFrameworkThread(() => SpawnWorldDecal(demo));
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Projects a footprint onto the REAL collision surface, so it drapes over terrain slopes and furniture - unlike the screen-space Material.Decal, which projects onto the depth buffer.");

        Ui.Gap();
        Ui.Status(spawnStatus);
    }

    // ---------------------------------------------------------------- spawning

    private void SpawnPrimitive(DemoScene demo, Primitive kind)
    {
        var scene = demo.Scene;
        var pos = NextSpawnPos();
        var mat = primLit ? Material.Lit(primColor) : Material.Unlit(primColor) with { Blend = BlendMode.Additive };
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
        demo.Selection.SetSingle(node); // select it so the gizmo attaches and the inspector opens on it
        inspected = node;
    }

    private void SpawnDecal(DemoScene demo)
    {
        var pos = PlayerPos();
        var shape = Enum.GetValues<DecalShape>()[decalShapeIdx];
        var surface = Enum.GetValues<DecalSurface>()[decalSurfaceIdx];
        var projection = Enum.GetValues<DecalProjection>()[decalProjIdx];

        var mat = Material.Decal(shape, decalColor, outlineWidth: decalOutline, surface: surface, projection: projection, additive: decalAdditive,
                                 outlineColor: decalCustomOutlineColor ? decalOutlineColor : null);
        var node = demo.Scene.AddBox(mat, pos, "Decal", keepCpuData: true)
             .Scale(new Vector3(decalSize, decalSize, decalSize))
             .MakeSelectable()
             .ExcludeObjects(static o => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc);

        if (decalShowVolume)
            node.ShowDecalVolume();

        demo.Track(node);
        demo.Selection.SetSingle(node); // select it so the gizmo attaches and the inspector opens on it
        inspected = node;
    }

    /// <summary>Imports a glTF/glb off-thread; the scene owns the result, and the model root joins the object list.</summary>
    private void LoadModel(DemoScene demo)
    {
        var path = modelPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            spawnStatus = $"Model file not found: '{path}'. Pass an absolute path to a .gltf / .glb.";
            return;
        }

        var scene = demo.Scene;
        var at = PlayerPos() + new Vector3(0f, 1f, 4f);
        spawnStatus = $"Loading '{Path.GetFileName(path)}' - it appears in front of you when ready (errors go to /xllog).";
        scene.LoadModelAsync(path, at, Path.GetFileNameWithoutExtension(path), keepCpuData: true, importVertexColors: modelVertexColors)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    NoireLogger.LogError(task.Exception!, $"Draw3D demo: glTF import failed for '{path}'.", "Draw3D Demo");
                    spawnStatus = $"Import failed for '{Path.GetFileName(path)}' - see /xllog.";
                    return;
                }

                // The load can finish after the scene was disposed / closed; re-check before touching it.
                if (scene.IsDisposed || !ReferenceEquals(demo.Scene, scene))
                    return;

                demo.Track(task.Result.Root.MakeSelectable());
                spawnStatus = $"Imported '{Path.GetFileName(path)}'.";
            }, TaskScheduler.Default);
    }

    /// <summary>Spawns the game's real collision around the player as a translucent mesh. Framework thread only.</summary>
    private void SpawnWorldGeometry(DemoScene demo)
    {
        if (demo.Scene.IsDisposed)
            return;

        var mat = Material.Lit(new Vector4(0.35f, 0.75f, 1f, 0.4f)) with { Cull = CullMode.None, Blend = BlendMode.Premultiplied };
        var node = demo.Scene.SpawnWorldGeometry(PlayerPos(), worldRadius, mat, worldAnalytic, "WorldGeometry", keepCpuData: true);
        if (node == null)
        {
            spawnStatus = "No collision found near you (open area / airborne, or the read faulted - see /xllog).";
            return;
        }

        demo.Track(node.MakeSelectable());
        spawnStatus = "Spawned the real collision around you, translucent blue.";
    }

    /// <summary>Projects a decal footprint onto the real collision surface under the player. Framework thread only.</summary>
    private void SpawnWorldDecal(DemoScene demo)
    {
        if (demo.Scene.IsDisposed)
            return;

        var mat = Material.Unlit(decalColor) with { Cull = CullMode.None };
        var node = demo.Scene.SpawnWorldDecal(PlayerPos(), Vector3.UnitY, decalSize, decalSize, mat, depth: 3f, name: "WorldDecal");
        if (node == null)
        {
            spawnStatus = "Nothing under the footprint to project onto.";
            return;
        }

        demo.Track(node.MakeSelectable());
        spawnStatus = "Projected a decal onto the real world surface (it drapes over slopes and furniture).";
    }

    private static Vector3 PlayerPos() => NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;

    private Vector3 NextSpawnPos()
    {
        var p = PlayerPos();
        var i = spawnCounter++;
        return p + new Vector3((i % 5) * 2f - 4f, 1f, 4f + i / 5 * 2f);
    }

    // ---------------------------------------------------------------- scene bookkeeping

    private void EnsureMainScene()
    {
        if (mainSceneAdded)
            return;

        mainSceneAdded = true;
        scenes.Insert(0, new DemoScene(NoireDraw3D.MainScene, owned: false, "MainScene"));
    }

    private DemoScene NewScene()
    {
        var scene = NoireDraw3D.CreateScene($"demo{scenes.Count}");
        var demo = new DemoScene(scene, owned: true, $"demo{scenes.Count}");
        demo.EnsureEditor(GizmoOp.Universal);
        scenes.Add(demo);
        return demo;
    }

    private void OpenScene(DemoScene demo)
    {
        open = demo;
        inspected = null;
        demo.EnsureEditor();
    }

    private void PruneDisposedScenes()
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

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var demo in scenes)
            demo.TearDown();

        scenes.Clear();
        open = null;
        inspected = null;
    }
}
