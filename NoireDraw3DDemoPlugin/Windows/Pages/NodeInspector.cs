using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// The per-object editor: everything the public <see cref="SceneNode"/> / <see cref="MeshRenderer"/> /
/// <see cref="Material"/> surface exposes for one object - transform, material, overlays, interaction flags, decal
/// exclusions, selection and destroy.
/// <para>
/// It is deep, so it is split across its own tab strip rather than a stack of collapsing headers: one object has more
/// settings than fit a screen, and tabs make each group a fixed, findable place instead of something whose position
/// depends on what happens to be expanded above it.
/// </para>
/// </summary>
internal sealed class NodeInspector
{
    /// <summary>A tighter caption column than a full-width page uses: the inspector lives in a split pane, where the default would starve the controls.</summary>
    private const float InspectorLabelWidth = 150f;

    private float rotStepDeg = 15f;
    private Vector4 outlineColor = new(1f, 0.85f, 0.2f, 1f);
    private float outlineWidth = 4f;
    private Vector4 decalShapeColor = new(1f, 1f, 0f, 1f);
    private Vector4 decalVolumeColor = new(0.35f, 0.85f, 1f, 1f);

    /// <summary>Draws the inspector for <paramref name="node"/> inside <paramref name="demo"/>.</summary>
    /// <param name="demo">The scene the node belongs to.</param>
    /// <param name="node">The node to edit.</param>
    /// <returns>False when the node was destroyed here, so the caller drops its reference to it.</returns>
    public bool Draw(DemoScene demo, SceneNode node)
    {
        if (!DrawHeader(demo, node))
            return false;

        var isDecal = node.Renderer?.Material.Domain == MaterialDomain.GroundDecal;

        // Header above, tab strip here, scroll inside each tab: the strip stays reachable however deep a tab's body runs.
        using var tabs = ImRaii.TabBar("##inspectortabs");
        if (!tabs)
            return true;

        using (var tab = ImRaii.TabItem("Transform"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##insp.transform");
                if (body)
                {
                    DrawBasics(node);
                    DrawTransform(node);
                }
            }
        }

        if (node.Renderer is { } renderer)
        {
            using (var tab = ImRaii.TabItem("Material"))
            {
                if (tab)
                {
                    using var body = Ui.Scroll("##insp.material");
                    if (body)
                        DrawMaterial(renderer);
                }
            }

            using (var tab = ImRaii.TabItem("Overlays"))
            {
                if (tab)
                {
                    using var body = Ui.Scroll("##insp.overlays");
                    if (body)
                        DrawOverlays(node, renderer);
                }
            }
        }

        using (var tab = ImRaii.TabItem("Interaction"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##insp.pointer");
                if (body)
                    DrawInteraction(node);
            }
        }

        if (!isDecal)
            return true;

        using (var tab = ImRaii.TabItem("Exclusions"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##insp.exclusions");
                if (body)
                    DrawExclusions(node);
            }
        }

        return true;
    }

    /// <summary>The object's identity strip: what is being edited, whether it is selected, and the destructive action.</summary>
    /// <returns>False when the node was destroyed.</returns>
    private static bool DrawHeader(DemoScene demo, SceneNode node)
    {
        var selection = demo.Selection;
        var selected = selection.Contains(node);

        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudWhite))
            ImGui.TextUnformatted(node.Name ?? "(unnamed)");

        if (selected)
        {
            ImGui.SameLine();
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGold);
            ImGui.TextUnformatted("[selected]");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(selected ? "Deselect" : "Select"))
        {
            if (selected)
                selection.Remove(node);
            else
                selection.SetSingle(node);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Add to selection"))
            selection.Add(node);
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Adds this object to the selection without removing the others. Needs the scene's editor in multi-select mode to hold more than one.");

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
        {
            if (ImGui.SmallButton("Destroy"))
            {
                demo.DestroyNode(node);
                return false;
            }
        }

        ImGui.Separator();
        return true;
    }

    private static void DrawBasics(SceneNode node)
    {
        Ui.Section("Object");
        using var form = Ui.Form("insp.basics", InspectorLabelWidth);
        Ui.Text("Name", () => node.Name ?? string.Empty, v => node.Name = string.IsNullOrWhiteSpace(v) ? null : v, maxLength: 128);
        Ui.Toggle("Visible", () => node.Visible, v => node.Visible = v,
            "ANDs down the hierarchy - hiding a parent hides its children.");
        Ui.Int("Layer", () => node.Layer, v => node.Layer = v,
            "Orders ground decals against each other and feeds the sort key. Higher draws later within a bucket.");
        Ui.Value("Has renderer", node.HasRenderer ? "yes" : "no",
            "A model root or grouping node has none - only its children draw.");
    }

    private void DrawTransform(SceneNode node)
    {
        Ui.Section("Transform");
        using (Ui.Form("insp.transform", InspectorLabelWidth))
        {
            Ui.Drag3("Position", () => node.LocalPosition, v => node.LocalPosition = v, 0.05f,
                hint: "Local to the parent; world for a scene root.");

            Ui.Row("Scale", "For a decal this is the projection box: X/Z the footprint, Y the vertical sweep.");
            var scale = node.LocalScale;
            if (ImGui.DragFloat3("##scale", ref scale, 0.05f, 0.01f, 1000f))
                node.LocalScale = new Vector3(MathF.Max(0.001f, scale.X), MathF.Max(0.001f, scale.Y), MathF.Max(0.001f, scale.Z));
        }

        Ui.Section("Rotation");
        Ui.Note("Incremental, so it stays exact - no euler round-trip. A Ground/Wall decal ignores all but yaw.");
        Ui.Gap();
        using (Ui.Form("insp.rotation", InspectorLabelWidth))
        {
            Ui.Drag("Step (deg)", () => rotStepDeg, v => rotStepDeg = v, 1f, 1f, 180f);

            var step = rotStepDeg * MathF.PI / 180f;
            Ui.Row("Rotate");
            if (ImGui.SmallButton("X-")) node.Rotate(Vector3.UnitX, -step);
            ImGui.SameLine(); if (ImGui.SmallButton("X+")) node.Rotate(Vector3.UnitX, step);
            ImGui.SameLine(); if (ImGui.SmallButton("Y-")) node.Rotate(Vector3.UnitY, -step);
            ImGui.SameLine(); if (ImGui.SmallButton("Y+")) node.Rotate(Vector3.UnitY, step);
            ImGui.SameLine(); if (ImGui.SmallButton("Z-")) node.Rotate(Vector3.UnitZ, -step);
            ImGui.SameLine(); if (ImGui.SmallButton("Z+")) node.Rotate(Vector3.UnitZ, step);
            ImGui.SameLine(); if (ImGui.SmallButton("Reset")) node.LocalRotation = Quaternion.Identity;

            Ui.Row("Aim", "Points local +Z at you.");
            if (ImGui.SmallButton("Look at me"))
                node.LookAt(NoireLib.NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero);
        }
    }

    private static void DrawMaterial(MeshRenderer renderer)
    {
        Ui.Section("Shading");
        using (Ui.Form("insp.shading", InspectorLabelWidth))
        {
            Ui.Enum("Domain", () => renderer.Material.Domain, v => renderer.Material = renderer.Material with { Domain = v },
                "Which shader family draws this.\n\nUnlit: flat colour or texture.\n\nLit: stylized half-Lambert against the global Lighting settings.\n\nGroundDecal: projects its shape onto world surfaces through the mesh's box volume.");
            Ui.Color4("Color", () => renderer.Material.Color, c => renderer.Material = renderer.Material with { Color = c },
                "Shared by every object using this material reference.");
            Ui.Color4("Tint (this object)", () => renderer.Tint, c => renderer.Tint = c,
                "A per-object multiplier on top of the material colour - cheap variation without a new material.\n\nNote that hovering the object in the world temporarily brightens this and restores it on exit.");
        }

        Ui.Section("Blending & depth");
        using (Ui.Form("insp.blend", InspectorLabelWidth))
        {
            Ui.Enum("Blend", () => renderer.Material.Blend, v => renderer.Material = renderer.Material with { Blend = v },
                "Opaque (z-tested against other Draw3D meshes), Premultiplied (standard translucent), or Additive (emissive glow, order-independent).");
            Ui.Enum("Depth", () => renderer.Material.Depth, v => renderer.Material = renderer.Material with { Depth = v },
                "Against the game world.\n\nTestOnly: hidden behind walls.\n\nIgnore: x-ray through everything.\n\nWorldOnly: hidden by walls but drawn over other Draw3D objects - the gizmo mix.\n\nIgnored by decals: projection replaces testing.");
            Ui.Enum("Cull", () => renderer.Material.Cull, v => renderer.Material = renderer.Material with { Cull = v },
                "Which triangle faces rasterize. Back is the default; None for planes and ribbons seen from both sides; Front is what decal volume boxes use.");
            Ui.Enum("When depth unavailable", () => renderer.Material.WhenDepthUnavailable, v => renderer.Material = renderer.Material with { WhenDepthUnavailable = v });
            Ui.Drag("Depth fade (m)", () => renderer.Material.DepthFade, v => renderer.Material = renderer.Material with { DepthFade = v }, 0.01f, 0f, 2f,
                "Softens the seam where a translucent shape meets world geometry. Blended materials only.");
            Ui.Toggle("Casts into private depth", () => renderer.CastsIntoPrivateDepth, v => renderer.CastsIntoPrivateDepth = v,
                "Whether opaque draws write Draw3D's private depth buffer, so other Draw3D meshes occlude correctly against this one. Ignored for blended materials.");
            Ui.Toggle("Unordered batching", () => renderer.Material.UnorderedBatching, v => renderer.Material = renderer.Material with { UnorderedBatching = v },
                "Lets translucent draws with this material render in any order, so the renderer can instance them hard. Great for hundreds of identical markers; leave off when shapes visibly overlap each other.");
        }

        if (renderer.Material.Domain != MaterialDomain.GroundDecal)
            return;

        Ui.Section("Decal projection");
        using (Ui.Form("insp.decal", InspectorLabelWidth))
        {
            Ui.Enum("Shape", () => renderer.Material.Shape, v => renderer.Material = renderer.Material with { Shape = v },
                "The projected footprint: Circle, Ring, Sector, Rect, or Texture (stamps the material's texture over the footprint).");
            Ui.Enum("Surface", () => renderer.Material.Surface, v => renderer.Material = renderer.Material with { Surface = v },
                "Locks the box's orientation. Ground: kept horizontal, projects down. Wall: kept vertical, projects into the wall it faces. Both: free, and the orientation decides.\n\nTry rotating with the gizmo after changing this.");
            Ui.Enum("Projection", () => renderer.Material.Projection, v => renderer.Material = renderer.Material with { Projection = v },
                "AllSurfaces paints everything in the box. HighestOnly paints only the topmost surface per column - a tabletop, not the floor beneath - and needs the collision height-map on and the top-surface threshold above 0 (Decals page), plus real collision on the covering object.");
            Ui.Drag("Outline width", () => renderer.Material.OutlineWidth, v => renderer.Material = renderer.Material with { OutlineWidth = v }, 0.005f, 0f, 0.5f,
                "Width of the bright rim, held at a constant world thickness regardless of the decal's scale - scale the box and the rim stays put. 0 means no outline.");

            Ui.Row("Custom border color", "Off, the border is the decal's own colour and only its opacity differs from the fill. On, it gets its own colour.");
            var hasOutlineColor = renderer.Material.OutlineColor.W > 0f;
            if (ImGui.Checkbox("##decaloutlinecol", ref hasOutlineColor))
            {
                renderer.Material = renderer.Material with
                {
                    // Alpha 0 is the "unset" sentinel the shader reads, so clearing it restores the decal-coloured rim.
                    // Seeding from the decal colour at full alpha means enabling it changes nothing until you pick a colour.
                    OutlineColor = hasOutlineColor
                        ? new Vector4(renderer.Material.Color.X, renderer.Material.Color.Y, renderer.Material.Color.Z, 1f)
                        : default,
                };
            }

            using (Ui.Disabled(renderer.Material.OutlineColor.W <= 0f))
            {
                Ui.Color4("Border color", () => renderer.Material.OutlineColor, v => renderer.Material = renderer.Material with { OutlineColor = v });
            }

            Ui.Drag("Height fade", () => renderer.Material.HeightFade, v => renderer.Material = renderer.Material with { HeightFade = v }, 0.02f, 0f, 1f,
                "Feathering near the top and bottom of the box volume.");
            Ui.Drag4("Shape params", () => renderer.Material.ShapeParams, v => renderer.Material = renderer.Material with { ShapeParams = v }, 0.02f,
                "Shape-specific tuning (x / y / z / fill).\n\nRing: X is the inner radius as a ratio of the outer (0..1).\n\nSector: X is the half-angle in radians, Y the inner ratio.\n\nW (fill) is the fill opacity relative to the outline, for every shape.");
        }
    }

    private void DrawOverlays(SceneNode node, MeshRenderer renderer)
    {
        Ui.Section("Outline");
        using (Ui.Form("insp.outline", InspectorLabelWidth))
        {
            Ui.Row("Outline", "A post-process rim from a coverage mask, not a second mesh, so it traces the real silhouette.");
            var hasOutline = node.HasOutline;
            if (ImGui.Checkbox("##hasoutline", ref hasOutline))
            {
                if (hasOutline)
                    node.ShowOutline(outlineColor, outlineWidth);
                else
                    node.HideOutline();
            }

            using (Ui.Disabled(!node.HasOutline))
            {
                Ui.Color4("Outline color", () => outlineColor, v =>
                {
                    outlineColor = v;
                    if (node.HasOutline)
                        node.ShowOutline(outlineColor, outlineWidth);
                });
                Ui.Drag("Outline width (px)", () => outlineWidth, v =>
                {
                    outlineWidth = v;
                    if (node.HasOutline)
                        node.ShowOutline(outlineColor, outlineWidth);
                }, 0.2f, 1f, 20f, "Outline thickness in screen pixels.");
            }
        }

        if (renderer.Material.Domain == MaterialDomain.GroundDecal)
        {
            Ui.Gap();
            Ui.Callout("This object is a ground decal, and decal outlines are inert - a decal has no mesh silhouette to trace. Use the decal shape outline below instead.");
        }

        Ui.Section("Decal overlays");
        using (Ui.Form("insp.decalshape", InspectorLabelWidth))
        {
            Ui.Row("Shape outline", "The exact circle / ring / pie / rect the shader's SDF paints, as a 3D line on the decal plane. Follows Shape, ShapeParams and Surface live.");
            var hasShape = node.HasDecalShape;
            if (ImGui.Checkbox("##hasdecalshape", ref hasShape))
            {
                if (hasShape)
                    node.ShowDecalShape(decalShapeColor);
                else
                    node.HideDecalShape();
            }

            using (Ui.Disabled(!node.HasDecalShape))
            {
                Ui.Color4("Shape outline color", () => decalShapeColor, v =>
                {
                    decalShapeColor = v;
                    if (node.HasDecalShape)
                        node.ShowDecalShape(decalShapeColor);
                });
            }

            Ui.Row("Volume box", "The projection box the SDF is evaluated in - the limit of what this decal can paint at all. Shows how far the projection reaches above and below the surface, so a decal stopping short of a wall or a step explains itself.\n\nComposes with the shape outline: turn both on to see the shape inside its volume.");
            var hasVolume = node.HasDecalVolume;
            if (ImGui.Checkbox("##hasdecalvolume", ref hasVolume))
            {
                if (hasVolume)
                    node.ShowDecalVolume(decalVolumeColor);
                else
                    node.HideDecalVolume();
            }

            using (Ui.Disabled(!node.HasDecalVolume))
            {
                Ui.Color4("Volume box color", () => decalVolumeColor, v =>
                {
                    decalVolumeColor = v;
                    if (node.HasDecalVolume)
                        node.ShowDecalVolume(decalVolumeColor);
                });
            }
        }
    }

    private static void DrawInteraction(SceneNode node)
    {
        Ui.Section("Pointer");
        using (Ui.Form("insp.interaction", InspectorLabelWidth))
        {
            Ui.Toggle("Interactable", () => node.Interactable, v => node.Interactable = v,
                "Off, it is invisible to picking.");
            Ui.Toggle("Draggable", () => node.Draggable, v => node.Draggable = v,
                "A left press begins a drag, which takes the mouse so the camera can't pan underneath. Implies Interactable.");
            Ui.Toggle("Selectable", () => node.Selectable, v => node.Selectable = v,
                "A left-click routes into the scene selection, attaching the gizmo. Only while 'Select on click' is on.");
            Ui.Value("Hovered", node.IsHovered ? "yes" : "no",
                node.IsHovered ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3,
                "Live: whether the cursor is over this object right now.");
        }

        Ui.Section("Opt-in shortcuts");
        using (Ui.Form("insp.optin", InspectorLabelWidth))
        {
            Ui.Row("Apply", "MakeSelectable: interactable + selectable + the built-in hover tint.\n\nMakeInteractable: hover and click only - a click never touches the selection.\n\nClearHoverHighlight: drops the built-in tint feedback while clicks still select, for when you drive the tint yourself.");
            if (ImGui.SmallButton("MakeSelectable()"))
                node.MakeSelectable();
            ImGui.SameLine();
            if (ImGui.SmallButton("MakeInteractable()"))
                node.MakeInteractable();
            ImGui.SameLine();
            if (ImGui.SmallButton("ClearHoverHighlight()"))
                node.ClearHoverHighlight();
        }
    }

    private static void DrawExclusions(SceneNode node)
    {
        Ui.Section("Exclusions");
        Ui.Note("The decal paints the ground normally and the actor simply isn't painted on, cut along their exact stencil "
                + "silhouette. Refreshed each frame on the framework thread - no per-frame plumbing.");
        Ui.Gap();

        if (ImGui.Button("Exclude characters, monsters and NPCs", new Vector2(280f * Ui.Scale, 0f)))
            node.ExcludeObjects(static (IGameObject o) => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc);

        ImGui.SameLine();
        if (ImGui.Button("Clear exclusions", new Vector2(160f * Ui.Scale, 0f)))
            node.ClearExclusions();
        if (ImGui.IsItemHovered())
            Ui.Tooltip("The decal paints over everything again, actors included.");

        Ui.Gap();
        Ui.Callout("Needs the character stencil value set correctly (Decals page) - that is what identifies an actor's pixels. Default 0x08.");
    }
}
