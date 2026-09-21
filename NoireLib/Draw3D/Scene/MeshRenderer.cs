using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>Draws a mesh with a material at its node's world transform, attached via <see cref="SceneNode.SetMesh"/>.</summary>
public sealed class MeshRenderer
{
    private readonly SceneNode node;

    /// <summary>The mesh to draw. Referenced, never owned.</summary>
    public Mesh Mesh { get; set; }

    /// <summary>The material to draw with.</summary>
    public Material Material { get; set; }

    /// <summary>Whether opaque draws write the Draw3D depth buffer so other Draw3D meshes occlude against them. Ignored for blended materials.</summary>
    public bool CastsIntoPrivateDepth { get; set; } = true;

    /// <summary>Per-node color multiplier on top of the material color.</summary>
    public Vector4 Tint { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>Screen-space silhouette outline color in straight alpha, where alpha 0 (the default) draws no outline.</summary>
    public Vector4 OutlineColor { get; set; }

    /// <summary>Outline thickness in screen pixels (default 4).</summary>
    public float OutlineWidthPixels { get; set; } = 4f;

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: world cylinders the decal skips, up to 64. <see cref="SceneNode.ExcludeObjects(System.Func{Dalamud.Game.ClientState.Objects.Types.IGameObject, bool}, float)"/> fills this each tick.</summary>
    public IReadOnlyList<ExcludeVolume>? ExcludeVolumes { get; set; }

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
