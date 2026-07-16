using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;

namespace NoireDraw3DDemoPlugin.Windows.Sections;

/// <summary>
/// The per-node editor: everything the public <see cref="SceneNode"/> / <see cref="MeshRenderer"/> / <see cref="Material"/>
/// surface exposes for a single object or decal - transform, material, tint, outline, decal projection box, interaction
/// flags, selection routing and destroy. Draws into the currently open scene's detail view.
/// </summary>
internal sealed class NodeInspector
{
    private float rotStepDeg = 15f;
    private Vector4 outlineColor = new(1f, 0.85f, 0.2f, 1f);
    private float outlineWidth = 4f;
    private Vector4 decalBoxColor = new(1f, 1f, 0f, 1f);

    /// <summary>Draws the inspector for <paramref name="node"/> inside <paramref name="demo"/>. Returns false when the node was destroyed here (caller should drop it).</summary>
    public bool Draw(DemoScene demo, SceneNode node)
    {
        var selected = demo.Selection.Contains(node);
        ImGui.TextUnformatted($"Editing: {node.Name ?? "(unnamed)"}{(selected ? "  [selected]" : string.Empty)}");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Node", ImGuiTreeNodeFlags.DefaultOpen))
            DrawNodeBasics(node);

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            DrawTransform(node);

        if (node.Renderer is { } renderer)
        {
            if (ImGui.CollapsingHeader("Material", ImGuiTreeNodeFlags.DefaultOpen))
                DrawMaterial(renderer);

            if (ImGui.CollapsingHeader("Outline & decal box"))
                DrawOverlays(node, renderer);
        }

        if (ImGui.CollapsingHeader("Interaction"))
            DrawInteraction(node);

        if (node.Renderer?.Material.Domain == MaterialDomain.GroundDecal && ImGui.CollapsingHeader("Decal exclusions"))
            DrawExclusions(node);

        ImGui.Separator();
        DrawSelectionRow(demo, node);

        ImGui.Spacing();
        if (ImGui.Button("Destroy node"))
        {
            demo.DestroyNode(node);
            return false;
        }

        return true;
    }

    private static void DrawNodeBasics(SceneNode node)
    {
        var name = node.Name ?? string.Empty;
        if (ImGui.InputText("Name", ref name, 128))
            node.Name = string.IsNullOrWhiteSpace(name) ? null : name;

        SectionUi.Toggle("Visible", () => node.Visible, v => node.Visible = v);
        SectionUi.IntInput("Layer", () => node.Layer, v => node.Layer = v);
        SectionUi.LabelValue("Has renderer", node.HasRenderer ? "yes" : "no");
    }

    private void DrawTransform(SceneNode node)
    {
        SectionUi.DragVec3("Position", () => node.LocalPosition, v => node.LocalPosition = v, 0.05f);

        var scale = node.LocalScale;
        if (ImGui.DragFloat3("Scale", ref scale, 0.05f, 0.01f, 1000f))
            node.LocalScale = new Vector3(MathF.Max(0.001f, scale.X), MathF.Max(0.001f, scale.Y), MathF.Max(0.001f, scale.Z));

        // Rotation is edited incrementally (exact, no euler round-trip) via the fluent Rotate API.
        ImGui.SetNextItemWidth(120f);
        ImGui.DragFloat("Rotate step (deg)", ref rotStepDeg, 1f, 1f, 180f);
        var step = rotStepDeg * MathF.PI / 180f;
        ImGui.TextUnformatted("Rotate:");
        ImGui.SameLine(); if (ImGui.SmallButton("X-")) node.Rotate(Vector3.UnitX, -step);
        ImGui.SameLine(); if (ImGui.SmallButton("X+")) node.Rotate(Vector3.UnitX, step);
        ImGui.SameLine(); if (ImGui.SmallButton("Y-")) node.Rotate(Vector3.UnitY, -step);
        ImGui.SameLine(); if (ImGui.SmallButton("Y+")) node.Rotate(Vector3.UnitY, step);
        ImGui.SameLine(); if (ImGui.SmallButton("Z-")) node.Rotate(Vector3.UnitZ, -step);
        ImGui.SameLine(); if (ImGui.SmallButton("Z+")) node.Rotate(Vector3.UnitZ, step);
        ImGui.SameLine(); if (ImGui.SmallButton("Reset")) node.LocalRotation = Quaternion.Identity;

        if (ImGui.SmallButton("Look at player"))
            node.LookAt(NoireLib.NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero);
    }

    private static void DrawMaterial(MeshRenderer renderer)
    {
        SectionUi.EnumComboChanged("Domain", () => renderer.Material.Domain, v => renderer.Material = renderer.Material with { Domain = v });
        SectionUi.Color4("Color", () => renderer.Material.Color, c => renderer.Material = renderer.Material with { Color = c });
        SectionUi.Color4("Tint (per-node)", () => renderer.Tint, c => renderer.Tint = c);
        SectionUi.EnumComboChanged("Blend", () => renderer.Material.Blend, v => renderer.Material = renderer.Material with { Blend = v });
        SectionUi.EnumComboChanged("Depth", () => renderer.Material.Depth, v => renderer.Material = renderer.Material with { Depth = v });
        SectionUi.EnumComboChanged("Cull", () => renderer.Material.Cull, v => renderer.Material = renderer.Material with { Cull = v });
        SectionUi.EnumComboChanged("When depth unavailable", () => renderer.Material.WhenDepthUnavailable, v => renderer.Material = renderer.Material with { WhenDepthUnavailable = v });
        SectionUi.DragFloat("Depth fade (m)", () => renderer.Material.DepthFade, v => renderer.Material = renderer.Material with { DepthFade = v }, 0.01f, 0f, 2f);
        SectionUi.Toggle("Unordered batching", () => renderer.Material.UnorderedBatching, v => renderer.Material = renderer.Material with { UnorderedBatching = v });
        SectionUi.Toggle("Casts into private depth", () => renderer.CastsIntoPrivateDepth, v => renderer.CastsIntoPrivateDepth = v);

        if (renderer.Material.Domain != MaterialDomain.GroundDecal)
            return;

        SectionUi.SeparatorText("Ground decal");
        SectionUi.EnumComboChanged("Shape", () => renderer.Material.Shape, v => renderer.Material = renderer.Material with { Shape = v });
        SectionUi.EnumComboChanged("Surface", () => renderer.Material.Surface, v => renderer.Material = renderer.Material with { Surface = v });
        SectionUi.EnumComboChanged("Projection", () => renderer.Material.Projection, v => renderer.Material = renderer.Material with { Projection = v });
        SectionUi.DragFloat("Outline width (SDF)", () => renderer.Material.OutlineWidth, v => renderer.Material = renderer.Material with { OutlineWidth = v }, 0.005f, 0f, 0.5f);
        SectionUi.DragFloat("Height fade", () => renderer.Material.HeightFade, v => renderer.Material = renderer.Material with { HeightFade = v }, 0.02f, 0f, 1f);

        var shapeParams = renderer.Material.ShapeParams;
        if (ImGui.DragFloat4("Shape params (x/y/z/fill)", ref shapeParams, 0.02f))
            renderer.Material = renderer.Material with { ShapeParams = shapeParams };
    }

    private void DrawOverlays(SceneNode node, MeshRenderer renderer)
    {
        // Outline (public MeshRenderer.OutlineColor / OutlineWidthPixels, driven by ShowOutline / HideOutline).
        var hasOutline = node.HasOutline;
        if (ImGui.Checkbox("Outline", ref hasOutline))
        {
            if (hasOutline)
                node.ShowOutline(outlineColor, outlineWidth);
            else
                node.HideOutline();
        }

        if (ImGui.ColorEdit4("Outline color", ref outlineColor) && node.HasOutline)
            node.ShowOutline(outlineColor, outlineWidth);
        if (ImGui.DragFloat("Outline width (px)", ref outlineWidth, 0.2f, 1f, 20f) && node.HasOutline)
            node.ShowOutline(outlineColor, outlineWidth);
        if (renderer.Material.Domain == MaterialDomain.GroundDecal)
            ImGui.TextDisabled("(Ground-decal outlines are inert - use the decal box below.)");

        ImGui.Spacing();

        // Decal projection box wireframe (ShowDecalBox / HideDecalBox).
        var hasBox = node.HasDecalBox;
        if (ImGui.Checkbox("Decal projection box", ref hasBox))
        {
            if (hasBox)
                node.ShowDecalBox(decalBoxColor);
            else
                node.HideDecalBox();
        }

        if (ImGui.ColorEdit4("Box color", ref decalBoxColor) && node.HasDecalBox)
            node.ShowDecalBox(decalBoxColor);
    }

    private static void DrawInteraction(SceneNode node)
    {
        SectionUi.Toggle("Interactable", () => node.Interactable, v => node.Interactable = v);
        SectionUi.Toggle("Draggable", () => node.Draggable, v => node.Draggable = v);
        SectionUi.Toggle("Selectable", () => node.Selectable, v => node.Selectable = v);
        SectionUi.LabelValue("Hovered", node.IsHovered ? "yes" : "no");

        if (ImGui.SmallButton("MakeSelectable()"))
            node.MakeSelectable();
        ImGui.SameLine();
        if (ImGui.SmallButton("MakeInteractable()"))
            node.MakeInteractable();
    }

    private static void DrawExclusions(SceneNode node)
    {
        ImGui.TextWrapped("Ground decals can skip painting on actors (characters / monsters / NPCs) that stand in them.");
        if (ImGui.SmallButton("Exclude characters"))
            node.ExcludeObjects(static (IGameObject o) => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear exclusions"))
            node.ClearExclusions();
    }

    private static void DrawSelectionRow(DemoScene demo, SceneNode node)
    {
        var selection = demo.Selection;
        var selected = selection.Contains(node);
        if (ImGui.Button(selected ? "Deselect" : "Select"))
        {
            if (selected)
                selection.Remove(node);
            else
                selection.SetSingle(node);
        }

        ImGui.SameLine();
        if (ImGui.Button("Add to selection"))
            selection.Add(node);
    }
}
