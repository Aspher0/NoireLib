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
    private Vector4 decalShapeColor = new(1f, 1f, 0f, 1f);

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
        SectionUi.Hint("Debug / lookup name. Shown in the object list above.");

        SectionUi.Toggle("Visible", () => node.Visible, v => node.Visible = v,
            "Whether this node and its whole subtree render (ANDs down the hierarchy).");
        SectionUi.IntInput("Layer", () => node.Layer, v => node.Layer = v,
            "Draw layer: orders ground decals against each other and feeds the sort key. Higher layers draw later within a bucket.");
        SectionUi.LabelValue("Has renderer", node.HasRenderer ? "yes" : "no");
    }

    private void DrawTransform(SceneNode node)
    {
        SectionUi.DragVec3("Position", () => node.LocalPosition, v => node.LocalPosition = v, 0.05f,
            hint: "Local position relative to the parent (world position for a scene root). Drag, or use the gizmo in-world.");

        var scale = node.LocalScale;
        if (ImGui.DragFloat3("Scale", ref scale, 0.05f, 0.01f, 1000f))
            node.LocalScale = new Vector3(MathF.Max(0.001f, scale.X), MathF.Max(0.001f, scale.Y), MathF.Max(0.001f, scale.Z));
        SectionUi.Hint("Local scale per axis. For a decal this is its projection box: X/Z are the footprint, Y the vertical sweep depth.");

        // Rotation is edited incrementally (exact, no euler round-trip) via the fluent Rotate API.
        ImGui.SetNextItemWidth(120f);
        ImGui.DragFloat("Rotate step (deg)", ref rotStepDeg, 1f, 1f, 180f);
        SectionUi.Hint("How many degrees each +/- button below turns the node.");
        var step = rotStepDeg * MathF.PI / 180f;
        ImGui.TextUnformatted("Rotate:");
        ImGui.SameLine(); if (ImGui.SmallButton("X-")) node.Rotate(Vector3.UnitX, -step);
        ImGui.SameLine(); if (ImGui.SmallButton("X+")) node.Rotate(Vector3.UnitX, step);
        ImGui.SameLine(); if (ImGui.SmallButton("Y-")) node.Rotate(Vector3.UnitY, -step);
        ImGui.SameLine(); if (ImGui.SmallButton("Y+")) node.Rotate(Vector3.UnitY, step);
        ImGui.SameLine(); if (ImGui.SmallButton("Z-")) node.Rotate(Vector3.UnitZ, -step);
        ImGui.SameLine(); if (ImGui.SmallButton("Z+")) node.Rotate(Vector3.UnitZ, step);
        ImGui.SameLine(); if (ImGui.SmallButton("Reset")) node.LocalRotation = Quaternion.Identity;
        SectionUi.Hint("Rotation is applied incrementally (exact - no euler round-trip). A Ground/Wall decal ignores everything but yaw: its Surface mode locks the box to its plane.");

        if (ImGui.SmallButton("Look at player"))
            node.LookAt(NoireLib.NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero);
        SectionUi.Hint("Orients the node so its local +Z (forward) points at you.");
    }

    private static void DrawMaterial(MeshRenderer renderer)
    {
        SectionUi.EnumComboChanged("Domain", () => renderer.Material.Domain, v => renderer.Material = renderer.Material with { Domain = v },
            "Which shader family draws this: Unlit (flat color/texture), Lit (stylized half-Lambert against the global Lighting), or GroundDecal (projects its shape onto world surfaces through the mesh's box volume).");
        SectionUi.Color4("Color", () => renderer.Material.Color, c => renderer.Material = renderer.Material with { Color = c },
            "The material's base color, straight alpha. Shared by every node using this material reference.");
        SectionUi.Color4("Tint (per-node)", () => renderer.Tint, c => renderer.Tint = c,
            "A per-node multiplier on top of the material color - cheap per-instance variation without a new material. Note: hovering the object in-world temporarily brightens this and restores it on exit.");
        SectionUi.EnumComboChanged("Blend", () => renderer.Material.Blend, v => renderer.Material = renderer.Material with { Blend = v },
            "Opaque (z-tested against other Draw3D meshes), Premultiplied (standard translucent), or Additive (emissive/glow, order-independent).");
        SectionUi.EnumComboChanged("Depth", () => renderer.Material.Depth, v => renderer.Material = renderer.Material with { Depth = v },
            "Vs the game world: TestOnly (hidden behind walls), Ignore (x-ray through everything), WorldOnly (hidden by walls but drawn over other Draw3D objects - the gizmo mix). Ignored by decals: projection replaces testing.");
        SectionUi.EnumComboChanged("Cull", () => renderer.Material.Cull, v => renderer.Material = renderer.Material with { Cull = v },
            "Which triangle faces rasterize. Back is the default; None for planes/ribbons seen from both sides; Front is what decal volume boxes use.");
        SectionUi.EnumComboChanged("When depth unavailable", () => renderer.Material.WhenDepthUnavailable, v => renderer.Material = renderer.Material with { WhenDepthUnavailable = v },
            "What to do on frames where the game's depth buffer can't be read: Ignore (draw anyway) or Hide (skip).");
        SectionUi.DragFloat("Depth fade (m)", () => renderer.Material.DepthFade, v => renderer.Material = renderer.Material with { DepthFade = v }, 0.01f, 0f, 2f,
            "Softens the seam where a translucent shape intersects world geometry, in world units. 0 = hard edge. Blended materials only.");
        SectionUi.Toggle("Unordered batching", () => renderer.Material.UnorderedBatching, v => renderer.Material = renderer.Material with { UnorderedBatching = v },
            "Lets translucent draws with this material render in any order so the renderer can instance them hard. Great for hundreds of identical markers; leave off when shapes visibly overlap each other.");
        SectionUi.Toggle("Casts into private depth", () => renderer.CastsIntoPrivateDepth, v => renderer.CastsIntoPrivateDepth = v,
            "Whether opaque draws write Draw3D's private depth buffer so other Draw3D meshes occlude correctly. Ignored for blended materials.");

        if (renderer.Material.Domain != MaterialDomain.GroundDecal)
            return;

        SectionUi.SeparatorText("Ground decal");
        SectionUi.EnumComboChanged("Shape", () => renderer.Material.Shape, v => renderer.Material = renderer.Material with { Shape = v },
            "The projected footprint: Circle, Ring, Sector, Rect, or Texture (stamps the material's texture over the footprint).");
        SectionUi.EnumComboChanged("Surface", () => renderer.Material.Surface, v => renderer.Material = renderer.Material with { Surface = v },
            "Locks the box's orientation: Ground (kept horizontal, projects down), Wall (kept vertical, projects into the wall it faces), Both (free - orientation decides). Try rotating with the gizmo after changing this.");
        SectionUi.EnumComboChanged("Projection", () => renderer.Material.Projection, v => renderer.Material = renderer.Material with { Projection = v },
            "AllSurfaces paints everything in the box; HighestOnly paints only the topmost surface per column (a tabletop, not the floor beneath). HighestOnly needs 'Collision height-map' on and 'Top-surface threshold' above 0 in Global settings, plus real collision on the covering object.");
        SectionUi.DragFloat("Outline width (SDF)", () => renderer.Material.OutlineWidth, v => renderer.Material = renderer.Material with { OutlineWidth = v }, 0.005f, 0f, 0.5f,
            "Width of the bright rim, in SDF units (0..1 of the footprint). 0 = no outline.");
        SectionUi.DragFloat("Height fade", () => renderer.Material.HeightFade, v => renderer.Material = renderer.Material with { HeightFade = v }, 0.02f, 0f, 1f,
            "How strongly the decal feathers out near the top/bottom of its box volume. 0 = none, 1 = full.");

        var shapeParams = renderer.Material.ShapeParams;
        if (ImGui.DragFloat4("Shape params (x/y/z/fill)", ref shapeParams, 0.02f))
            renderer.Material = renderer.Material with { ShapeParams = shapeParams };
        SectionUi.Hint("Shape-specific tuning. Ring: X = inner radius as a ratio of the outer (0..1). Sector: X = half-angle in radians, Y = inner ratio. W (fill) = fill opacity relative to the outline, for every shape.");
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

        SectionUi.Hint("A real screen-space silhouette outline (a post-process rim from a coverage mask, not a second mesh), so it traces the object's actual outline.");
        if (ImGui.ColorEdit4("Outline color", ref outlineColor) && node.HasOutline)
            node.ShowOutline(outlineColor, outlineWidth);
        if (ImGui.DragFloat("Outline width (px)", ref outlineWidth, 0.2f, 1f, 20f) && node.HasOutline)
            node.ShowOutline(outlineColor, outlineWidth);
        SectionUi.Hint("Outline thickness in screen pixels.");
        if (renderer.Material.Domain == MaterialDomain.GroundDecal)
            ImGui.TextDisabled("(Ground-decal outlines are inert - use the decal box below.)");

        ImGui.Spacing();

        // Decal shape outline (ShowDecalShape / HideDecalShape).
        var hasShape = node.HasDecalShape;
        if (ImGui.Checkbox("Decal shape outline", ref hasShape))
        {
            if (hasShape)
                node.ShowDecalShape(decalShapeColor);
            else
                node.HideDecalShape();
        }

        SectionUi.Hint("Traces the shape this node's decal actually paints - the same circle / ring / pie / rect the shader's SDF evaluates - as a closed 3D line on the decal's plane. It follows Shape, ShapeParams and the Surface constraint live. A placement / sizing aid.");
        if (ImGui.ColorEdit4("Outline color", ref decalShapeColor) && node.HasDecalShape)
            node.ShowDecalShape(decalShapeColor);
    }

    private static void DrawInteraction(SceneNode node)
    {
        SectionUi.Toggle("Interactable", () => node.Interactable, v => node.Interactable = v,
            "Whether this node responds to the pointer at all (hover / click / drag). A non-interactable node is invisible to picking.");
        SectionUi.Toggle("Draggable", () => node.Draggable, v => node.Draggable = v,
            "Whether a left press on this node begins a drag rather than only a click. While dragging, the mouse is taken from the game so the camera can't pan underneath. Implies Interactable.");
        SectionUi.Toggle("Selectable", () => node.Selectable, v => node.Selectable = v,
            "Whether a left-click routes into the scene's selection (and so attaches the gizmo). Only consulted while 'Select on click' is on in Global settings.");
        SectionUi.LabelValue("Hovered", node.IsHovered ? "yes" : "no", "Live: whether the cursor is over this node right now.");

        if (ImGui.SmallButton("MakeSelectable()"))
            node.MakeSelectable();
        SectionUi.Hint("One-call opt-in: interactable + selectable + the built-in hover tint highlight. Calling it again just replaces the highlight - it never stacks.");
        ImGui.SameLine();
        if (ImGui.SmallButton("MakeInteractable()"))
            node.MakeInteractable();
        SectionUi.Hint("Like MakeSelectable, but hover/click only - a click never touches the selection.");
        ImGui.SameLine();
        if (ImGui.SmallButton("ClearHoverHighlight()"))
            node.ClearHoverHighlight();
        SectionUi.Hint("Drops the built-in hover tint feedback (clicks still select). Use it when you drive the tint yourself.");
    }

    private static void DrawExclusions(SceneNode node)
    {
        ImGui.TextWrapped("Ground decals can skip painting on actors (characters / monsters / NPCs) that stand in them - the decal paints the ground normally and the actor is simply not painted on, cut along their exact game-stencil silhouette.");
        if (ImGui.SmallButton("Exclude characters"))
            node.ExcludeObjects(static (IGameObject o) => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc);
        SectionUi.Hint("Refreshed by the library each frame on the framework thread - no per-frame plumbing needed.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear exclusions"))
            node.ClearExclusions();
        SectionUi.Hint("The decal paints over everything again, actors included.");
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
