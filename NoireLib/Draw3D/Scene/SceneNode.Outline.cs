using NoireLib.Draw3D.Materials;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// An opt-in selection/highlight outline for a node, built entirely from existing primitives (no renderer-core change):
/// a child "hull" that re-draws the node's mesh slightly enlarged, front-face-culled and solid, so the object covers
/// its own center while the hull's far shell reads as a rim around the silhouette. Sugar over a child node + an unlit
/// material - fully inspectable, fully removable. The default <see cref="MakeSelectable"/> highlight stays a tint;
/// outline is something you turn on (directly here, or via <c>editor.SelectionOutline</c>).
/// </summary>
public sealed partial class SceneNode
{
    private SceneNode? outlineChild;

    /// <summary>Whether an outline hull is currently attached.</summary>
    public bool HasOutline => outlineChild is { Destroyed: false };

    /// <summary>
    /// Shows a solid outline around this node by attaching an enlarged front-culled hull of its mesh. No-op (logged)
    /// when the node has no renderer; skipped for ground decals (they carry their own <c>OutlineWidth</c>). Calling it
    /// again updates the color/scale. Fluent.
    /// </summary>
    /// <param name="color">Outline color (straight alpha; drawn solid).</param>
    /// <param name="scale">Hull scale relative to the node (default 1.06 - a thin rim; larger = thicker).</param>
    public SceneNode ShowOutline(Vector4 color, float scale = 1.06f)
    {
        var renderer = Renderer;
        if (renderer == null)
        {
            NoireLogger.LogWarning($"Draw3D: SceneNode '{Name ?? "(unnamed)"}'.ShowOutline with no renderer - ignored. Attach a mesh first.", "Draw3D");
            return this;
        }

        if (renderer.Material.Domain == MaterialDomain.GroundDecal)
            return this; // a decal already has a real outline band; an opaque hull would look wrong

        if (outlineChild is null or { Destroyed: true })
            outlineChild = CreateChild($"{Name ?? "node"}.Outline");

        outlineChild.Selectable = false;
        outlineChild.LocalScale = new Vector3(scale);
        outlineChild.SetMesh(renderer.Mesh, Material.Unlit(color) with { Blend = BlendMode.Opaque, Cull = CullMode.Front });
        return this;
    }

    /// <summary>Removes the outline hull, if any. Fluent.</summary>
    public SceneNode HideOutline()
    {
        var child = outlineChild;
        outlineChild = null;
        if (child is { Destroyed: false })
            child.Destroy();
        return this;
    }
}
