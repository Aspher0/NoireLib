using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;

namespace NoireDraw3DDemoPlugin.Windows.Sections;

/// <summary>
/// A build-your-own-scene playground: create scenes, drop primitives into the newest one, and spawn decals with any
/// shape / <b>surface filter (wall / ground / both)</b> / projection. Every node is selectable and each scene carries a
/// gizmo editor, so spawned objects can be moved. Owns the demo scenes and frees them on <see cref="Dispose"/>.
/// </summary>
public sealed class ScenesSection : IDisposable
{
    private enum Primitive { Box, Sphere, Cylinder, Cone, Torus }

    private readonly List<Scene3D> scenes = new();
    private int spawnCounter;

    // Primitive controls.
    private Vector4 primColor = new(0.85f, 0.60f, 0.40f, 1f);
    private bool primLit = true;

    // Decal controls (applied when "Spawn decal" is pressed).
    private int decalShapeIdx;   // index into DecalShape
    private int decalSurfaceIdx; // index into DecalSurface (Both / Ground / Wall)
    private int decalProjIdx;    // index into DecalProjection
    private Vector4 decalColor = new(0.30f, 0.70f, 1f, 0.9f);
    private float decalSize = 4f;
    private float decalOutline = 0.08f;

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        ImGui.TextWrapped("Create scenes and populate the newest one with primitives and decals. Nodes are selectable - left-click one and drag the gizmo. Decals exercise the wall / ground / both surface filter.");
        ImGui.Separator();

        ImGui.TextUnformatted($"Active demo scenes: {ActiveCount()}");
        if (ImGui.Button("New scene"))
            NewScene();
        ImGui.SameLine();
        using (SectionUi.Disabled(scenes.Count == 0))
        {
            if (ImGui.Button("Clear all"))
                Clear();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Primitives (into the newest scene)");
        ImGui.ColorEdit4("Color##prim", ref primColor);
        ImGui.Checkbox("Lit (off = additive unlit)", ref primLit);
        if (ImGui.Button("Box")) SpawnPrimitive(Primitive.Box);
        ImGui.SameLine(); if (ImGui.Button("Sphere")) SpawnPrimitive(Primitive.Sphere);
        ImGui.SameLine(); if (ImGui.Button("Cylinder")) SpawnPrimitive(Primitive.Cylinder);
        ImGui.SameLine(); if (ImGui.Button("Cone")) SpawnPrimitive(Primitive.Cone);
        ImGui.SameLine(); if (ImGui.Button("Torus")) SpawnPrimitive(Primitive.Torus);

        ImGui.Separator();
        ImGui.TextUnformatted("Decal (wall / ground / both)");
        SectionUi.EnumCombo<DecalShape>("Shape", ref decalShapeIdx);
        SectionUi.EnumCombo<DecalSurface>("Surface", ref decalSurfaceIdx);
        SectionUi.EnumCombo<DecalProjection>("Projection", ref decalProjIdx);
        ImGui.ColorEdit4("Color##decal", ref decalColor);
        ImGui.SliderFloat("Footprint size (m)", ref decalSize, 1f, 12f);
        ImGui.SliderFloat("Outline width", ref decalOutline, 0f, 0.3f);
        if (ImGui.Button("Spawn decal at player"))
            SpawnDecal();
        ImGui.TextDisabled("Surface locks the box's orientation: Ground stays horizontal (can't be tipped onto a wall), Wall stays vertical (grow it to reach a wall), Both rotates freely and its orientation decides the surface. Select a decal and rotate it with the gizmo to feel the lock.");
    }

    private void SpawnPrimitive(Primitive kind)
    {
        var scene = EnsureScene();
        var pos = NextSpawnPos();
        var mat = primLit ? Material.Lit(primColor) : Material.Unlit(primColor) with { Blend = BlendMode.Additive };
        var node = kind switch
        {
            Primitive.Box => scene.AddBox(new Vector3(1.2f, 1.2f, 1.2f), mat, pos, "Demo.Box", keepCpuData: true),
            Primitive.Sphere => scene.AddSphere(0.7f, mat, pos, "Demo.Sphere", keepCpuData: true),
            Primitive.Cylinder => scene.AddCylinder(0.6f, 1.4f, mat, pos, "Demo.Cylinder", keepCpuData: true),
            Primitive.Cone => scene.AddCone(0.7f, 1.4f, mat, pos, "Demo.Cone", keepCpuData: true),
            _ => scene.AddTorus(0.7f, 0.25f, mat, pos, "Demo.Torus", keepCpuData: true),
        };
        node.MakeSelectable();
    }

    private void SpawnDecal()
    {
        var scene = EnsureScene();
        var pos = PlayerPos();
        var shape = Enum.GetValues<DecalShape>()[decalShapeIdx];
        var surface = Enum.GetValues<DecalSurface>()[decalSurfaceIdx];
        var projection = Enum.GetValues<DecalProjection>()[decalProjIdx];

        var mat = Material.Decal(shape, decalColor, outlineWidth: decalOutline, surface: surface) with { Projection = projection };
        scene.AddBox(mat, pos, "Demo.Decal", keepCpuData: true)
             .Scale(new Vector3(decalSize, decalSize, decalSize))
             .MakeSelectable()
             .ExcludeObjects(static o => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc);
    }

    private static Vector3 PlayerPos() => NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;

    private Vector3 NextSpawnPos()
    {
        var p = PlayerPos();
        var i = spawnCounter++;
        return p + new Vector3((i % 5) * 2f - 4f, 1f, 4f + i / 5 * 2f);
    }

    private int ActiveCount()
    {
        var n = 0;
        foreach (var s in scenes)
            if (!s.IsDisposed)
                n++;
        return n;
    }

    private Scene3D? Newest()
    {
        for (var i = scenes.Count - 1; i >= 0; i--)
            if (!scenes[i].IsDisposed)
                return scenes[i];
        return null;
    }

    private Scene3D EnsureScene() => Newest() ?? NewScene();

    private Scene3D NewScene()
    {
        var scene = NoireDraw3D.CreateScene($"demo{scenes.Count}");
        var editor = scene.CreateEditor(GizmoOp.Universal); // scene-owned; disposed with the scene
        editor.MultiSelect = true;
        editor.SelectionOutline = new Vector4(1f, 0.85f, 0.2f, 1f);
        scenes.Add(scene);
        return scene;
    }

    private void Clear()
    {
        foreach (var s in scenes)
            if (!s.IsDisposed)
                s.Dispose();
        scenes.Clear();
        spawnCounter = 0;
    }

    /// <inheritdoc/>
    public void Dispose() => Clear();
}
