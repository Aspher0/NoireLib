using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Utility.Raii;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;

namespace NoireDraw3DDemoPlugin.Windows.Sections;

/// <summary>
/// A build-your-own-scene playground and object browser. The tab is a two-level navigator: a scene list (create /
/// open / dispose scenes) and, inside a scene, its editor + gizmo configuration, spawn controls, the object list, and a
/// full per-node inspector. Everything is built on the public Draw3D API. Owns the demo scenes and frees them on
/// <see cref="Dispose"/>.
/// </summary>
public sealed class ScenesSection : IDisposable
{
    private enum Primitive { Box, Sphere, Cylinder, Cone, Torus, Quad, Disc, Ring, Arrow }

    private readonly List<DemoScene> scenes = new();
    private readonly NodeInspector inspector = new();
    private DemoScene? open;        // null = scene-list view; else the open scene's detail view
    private SceneNode? inspected;   // the node the inspector edits (mirrors the scene's primary selection)
    private bool mainSceneAdded;
    private int spawnCounter;

    // Primitive spawn controls.
    private Vector4 primColor = new(0.85f, 0.60f, 0.40f, 1f);
    private bool primLit = true;

    // Decal spawn controls.
    private int decalShapeIdx;
    private int decalSurfaceIdx;
    private int decalProjIdx;
    private Vector4 decalColor = new(0.30f, 0.70f, 1f, 0.9f);
    private float decalSize = 4f;
    private float decalOutline = 0.08f;

    // Per-scene gizmo/editor UI state.
    private Vector4 selectionOutlineColor = new(1f, 0.85f, 0.2f, 1f);

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        EnsureMainScene();
        PruneDisposedScenes();

        if (open is { Scene.IsDisposed: false })
            DrawSceneDetail(open);
        else
        {
            open = null;
            DrawSceneList();
        }
    }

    // ---------------------------------------------------------------- scene list

    private void DrawSceneList()
    {
        ImGui.TextWrapped("Scenes you can open and edit. Create a scene, open it to spawn primitives and decals, browse its objects, tune its gizmo, and edit any node. The permanent MainScene is always listed.");
        ImGui.Separator();

        if (ImGui.Button("New scene"))
            NewScene();
        ImGui.SameLine();
        using (SectionUi.Disabled(!AnyOwned()))
        {
            if (ImGui.Button("Dispose all demo scenes"))
                DisposeOwnedScenes();
        }

        ImGui.Spacing();
        using var table = ImRaii.Table("scenelist", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders);
        if (!table)
            return;

        ImGui.TableSetupColumn("Scene", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Objects", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Visible", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableHeadersRow();

        foreach (var demo in scenes)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(demo.Label);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(demo.Scene.NodeCount.ToString());
            ImGui.TableNextColumn();
            SectionUi.Toggle($"##vis{demo.Label}", () => demo.Scene.Visible, v => demo.Scene.Visible = v);
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"Open##{demo.Label}"))
                OpenScene(demo);
            if (demo.Owned)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Dispose##{demo.Label}"))
                {
                    demo.TearDown();
                    // list is pruned next frame; if this was open, fall back to the list
                }
            }
        }
    }

    // ---------------------------------------------------------------- scene detail

    private void DrawSceneDetail(DemoScene demo)
    {
        if (ImGui.Button("< Back to scenes"))
        {
            open = null;
            inspected = null;
            return;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"Scene: {demo.Label}  ({demo.Scene.NodeCount} nodes)");

        var name = demo.Scene.Name ?? string.Empty;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.InputText("Scene name", ref name, 128))
            demo.Scene.Name = string.IsNullOrWhiteSpace(name) ? null : name;
        SectionUi.Toggle("Scene visible", () => demo.Scene.Visible, v => demo.Scene.Visible = v);
        ImGui.SameLine();
        if (ImGui.Button("Clear scene"))
        {
            demo.Scene.Clear();
            inspected = null;
        }

        var editor = demo.EnsureEditor();

        if (ImGui.CollapsingHeader("Editor & gizmo", ImGuiTreeNodeFlags.DefaultOpen))
            DrawGizmoConfig(editor);

        if (ImGui.CollapsingHeader("Spawn", ImGuiTreeNodeFlags.DefaultOpen))
            DrawSpawnControls(demo);

        if (ImGui.CollapsingHeader("Objects", ImGuiTreeNodeFlags.DefaultOpen))
            DrawObjectList(demo);
    }

    private void DrawGizmoConfig(SceneEditor editor)
    {
        var gizmo = editor.Gizmo;

        SectionUi.Toggle("Editor enabled", () => editor.Enabled, v => editor.Enabled = v,
            "When off the gizmo neither draws nor interacts (the selection still tracks).");
        SectionUi.Toggle("Multi-select", () => editor.MultiSelect, v => editor.MultiSelect = v,
            "Lets picks build a multi-node selection (with the toggle / add modifiers from Global settings). The gizmo then edits the whole group around its centroid.");

        var outlineOn = editor.SelectionOutline.HasValue;
        if (ImGui.Checkbox("Selection outline", ref outlineOn))
            editor.SelectionOutline = outlineOn ? selectionOutlineColor : null;
        SectionUi.Hint("Draws a real silhouette outline around selected nodes (in addition to the gizmo and hover tint).");
        if (ImGui.ColorEdit4("Selection outline color", ref selectionOutlineColor) && editor.SelectionOutline.HasValue)
            editor.SelectionOutline = selectionOutlineColor;
        SectionUi.DragFloat("Selection outline width (px)", () => editor.OutlineWidth, v => editor.OutlineWidth = v, 0.2f, 1f, 20f,
            "Outline thickness in screen pixels.");

        SectionUi.SeparatorText("Operations");
        ImGui.TextDisabled("Which transform handles the gizmo shows:");
        var op = gizmo.Op;
        var t = (op & GizmoOp.Translate) != 0;
        var r = (op & GizmoOp.Rotate) != 0;
        var s = (op & GizmoOp.Scale) != 0;
        var changed = ImGui.Checkbox("Translate", ref t);
        ImGui.SameLine(); changed |= ImGui.Checkbox("Rotate", ref r);
        ImGui.SameLine(); changed |= ImGui.Checkbox("Scale", ref s);
        if (changed)
            gizmo.Op = (t ? GizmoOp.Translate : 0) | (r ? GizmoOp.Rotate : 0) | (s ? GizmoOp.Scale : 0);

        SectionUi.SeparatorText("Handles");
        SectionUi.EnumComboChanged("Space", () => gizmo.Space, v => gizmo.Space = v,
            "Frame the translate / rotate handles align to: World (axis-aligned) or Local (the object's own rotation). Scale handles are always object-local.");
        SectionUi.EnumComboChanged("Backend", () => gizmo.Backend, v => gizmo.Backend = v,
            "Native = in-world depth handles hit-tested in screen space (occlude correctly, never wobble). ImGuizmo = the classic flat 2D handles, always on top.");
        SectionUi.EnumComboChanged("Depth", () => gizmo.Depth, v => gizmo.Depth = v,
            "Native backend only: OnTopOfObjects (occluded by walls but over other 3D objects - the editor default), AlwaysOnTop (full x-ray), or Occluded (fully depth-tested).");
        SectionUi.Toggle("Visible", () => gizmo.Visible, v => gizmo.Visible = v,
            "Draw + interact only while on (independent of Editor enabled).");
        SectionUi.Toggle("Show drag feedback", () => gizmo.Options.ShowDragFeedback, v => gizmo.Options.ShowDragFeedback = v,
            "Draws the drag preview overlay: an anchor at the pre-drag center, a guide line, and the live amount moved / rotated / scaled.");
        SectionUi.DragFloat("Handle length (px)", () => gizmo.Options.HandlePixelLength, v => gizmo.Options.HandlePixelLength = v, 1f, 20f, 400f,
            "On-screen handle arm length in pixels (kept constant regardless of camera distance).");
        SectionUi.DragFloat("Handle thickness (px)", () => gizmo.Options.HandlePixelThickness, v => gizmo.Options.HandlePixelThickness = v, 0.1f, 1f, 20f,
            "Handle line / arrow thickness in screen pixels.");
        SectionUi.DragFloat("Grab tolerance (px)", () => gizmo.Options.GrabPixelTolerance, v => gizmo.Options.GrabPixelTolerance = v, 0.2f, 1f, 40f,
            "How close (in pixels) the cursor must be to a handle to grab it.");

        SectionUi.SeparatorText("Snapping");
        SectionUi.DragFloat("Translate snap", () => gizmo.Snap, v => gizmo.Snap = v, 0.01f, 0f, 10f,
            "Grid the movement snaps to, in world units (0 = free). Applied to all three axes.");
        SectionUi.DragFloat("Rotate snap (deg)", () => gizmo.RotateSnapDeg, v => gizmo.RotateSnapDeg = v, 0.5f, 0f, 90f,
            "Rotation snap increment in degrees (0 = free).");
        SectionUi.DragFloat("Scale snap", () => gizmo.ScaleSnap, v => gizmo.ScaleSnap = v, 0.01f, 0f, 5f,
            "Scale snap increment (0 = free).");

        SectionUi.SeparatorText("Selection");
        SectionUi.LabelValue("Count", editor.Selection.Count.ToString());
        SectionUi.LabelValue("Primary", editor.Selection.Primary?.Name ?? "(none)");
        SectionUi.LabelValue("Dragging", gizmo.IsDragging ? gizmo.HoveredHandle.ToString() : "no");
        if (ImGui.SmallButton("Clear selection"))
            editor.Selection.Clear();
    }

    private void DrawSpawnControls(DemoScene demo)
    {
        ImGui.ColorEdit4("Color##prim", ref primColor);
        ImGui.Checkbox("Lit (off = additive unlit)", ref primLit);
        if (ImGui.Button("Box")) SpawnPrimitive(demo, Primitive.Box);
        ImGui.SameLine(); if (ImGui.Button("Sphere")) SpawnPrimitive(demo, Primitive.Sphere);
        ImGui.SameLine(); if (ImGui.Button("Cylinder")) SpawnPrimitive(demo, Primitive.Cylinder);
        ImGui.SameLine(); if (ImGui.Button("Cone")) SpawnPrimitive(demo, Primitive.Cone);
        ImGui.SameLine(); if (ImGui.Button("Torus")) SpawnPrimitive(demo, Primitive.Torus);
        if (ImGui.Button("Quad")) SpawnPrimitive(demo, Primitive.Quad);
        ImGui.SameLine(); if (ImGui.Button("Disc")) SpawnPrimitive(demo, Primitive.Disc);
        ImGui.SameLine(); if (ImGui.Button("Ring")) SpawnPrimitive(demo, Primitive.Ring);
        ImGui.SameLine(); if (ImGui.Button("Arrow")) SpawnPrimitive(demo, Primitive.Arrow);

        SectionUi.SeparatorText("Decal (wall / ground / both)");
        SectionUi.EnumCombo<DecalShape>("Shape", ref decalShapeIdx);
        SectionUi.EnumCombo<DecalSurface>("Surface", ref decalSurfaceIdx);
        SectionUi.EnumCombo<DecalProjection>("Projection", ref decalProjIdx);
        ImGui.ColorEdit4("Color##decal", ref decalColor);
        ImGui.SliderFloat("Footprint size (m)", ref decalSize, 1f, 12f);
        ImGui.SliderFloat("Outline width", ref decalOutline, 0f, 0.3f);
        if (ImGui.Button("Spawn decal at player"))
            SpawnDecal(demo);
    }

    private void DrawObjectList(DemoScene demo)
    {
        demo.PruneDestroyed();

        // Follow the scene's in-world selection when the list has no explicit pick.
        if (inspected is null or { IsDestroyed: true } || !ReferenceEquals(inspected.Scene, demo.Scene))
            inspected = demo.Selection.Primary;

        using (var child = ImRaii.Child("nodelist", new Vector2(0f, 150f), true))
        {
            if (child)
            {
                if (demo.Nodes.Count == 0)
                    ImGui.TextDisabled("No demo objects yet - spawn some above.");

                for (var i = 0; i < demo.Nodes.Count; i++)
                {
                    var node = demo.Nodes[i];
                    var label = $"{node.Name ?? "(unnamed)"}##node{i}";
                    if (ImGui.Selectable(label, ReferenceEquals(inspected, node)))
                    {
                        inspected = node;
                        demo.Selection.SetSingle(node);
                    }
                }
            }
        }

        ImGui.Separator();
        if (inspected is { IsDestroyed: false } target && ReferenceEquals(target.Scene, demo.Scene))
        {
            if (!inspector.Draw(demo, target))
                inspected = null;
        }
        else
        {
            ImGui.TextDisabled("Select an object above (or click one in the world) to edit it.");
        }
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

        var mat = Material.Decal(shape, decalColor, outlineWidth: decalOutline, surface: surface) with { Projection = projection };
        var node = demo.Scene.AddBox(mat, pos, "Decal", keepCpuData: true)
             .Scale(new Vector3(decalSize, decalSize, decalSize))
             .MakeSelectable()
             .ExcludeObjects(static o => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc);
        demo.Track(node);
        demo.Selection.SetSingle(node); // select it so the gizmo attaches and the inspector opens on it
        inspected = node;
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

    private bool AnyOwned()
    {
        foreach (var d in scenes)
            if (d.Owned && !d.Scene.IsDisposed)
                return true;
        return false;
    }

    private void DisposeOwnedScenes()
    {
        for (var i = scenes.Count - 1; i >= 0; i--)
        {
            if (!scenes[i].Owned)
                continue;

            if (ReferenceEquals(open, scenes[i]))
            {
                open = null;
                inspected = null;
            }

            scenes[i].TearDown();
            scenes.RemoveAt(i);
        }

        spawnCounter = 0;
    }

    private void PruneDisposedScenes()
    {
        for (var i = scenes.Count - 1; i >= 0; i--)
        {
            if (scenes[i].Owned && scenes[i].Scene.IsDisposed)
            {
                if (ReferenceEquals(open, scenes[i]))
                {
                    open = null;
                    inspected = null;
                }

                scenes.RemoveAt(i);
            }
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
