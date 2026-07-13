using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Draws a mesh with a material at its node's world transform. Attached via <see cref="SceneNode.SetMesh"/>.
/// </summary>
public sealed class MeshRenderer
{
    private readonly SceneNode node;

    /// <summary>The mesh to draw. Referenced, never owned — dispose it wherever it was created.</summary>
    public Mesh Mesh { get; set; }

    /// <summary>The material to draw with. Immutable record — swap the reference to change appearance.</summary>
    public Material Material { get; set; }

    /// <summary>
    /// Whether opaque draws write the private Draw3D depth buffer (so other Draw3D meshes occlude correctly).<br/>
    /// Defaults to true for opaque materials; ignored for blended ones.
    /// </summary>
    public bool CastsIntoPrivateDepth { get; set; } = true;

    /// <summary>Per-node color multiplier on top of the material color (cheap variation without a new material).</summary>
    public Vector4 Tint { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>The mesh bounds transformed by the node's current world matrix.</summary>
    public BoundingSphere WorldBounds => Mesh.LocalBounds.Transform(node.WorldMatrix);

    /// <summary>The node this renderer is attached to.</summary>
    public SceneNode Node => node;

    internal MeshRenderer(SceneNode node, Mesh mesh, Material material)
    {
        this.node = node;
        Mesh = mesh;
        Material = material;
    }
}
