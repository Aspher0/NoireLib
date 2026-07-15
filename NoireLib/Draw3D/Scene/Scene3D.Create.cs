using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// The one-liner creation surface of a scene: <see cref="Spawn(MeshData, Material, Vector3, string, bool)"/> collapses
/// "create node → build mesh → attach → track for disposal" into a single call that returns the node and owns its mesh.
/// Everything here is sugar over <see cref="CreateNode"/> + <see cref="SceneNode.SetMesh(MeshData, Material, bool)"/>;
/// drop to those any time you want the manual, shared-mesh reference model.
/// </summary>
public sealed partial class Scene3D
{
    /// <summary>Spawns a node with a mesh it <b>owns</b>, built from geometry data. The scene frees it on <see cref="Dispose"/>.</summary>
    /// <param name="data">CPU mesh data (see <see cref="MeshBuilder"/>).</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    public SceneNode Spawn(MeshData data, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        var node = CreateNode(name);
        node.LocalPosition = position;
        node.SetMesh(data, material, keepCpuData);
        return node;
    }

    /// <summary>Spawns a node drawing a <b>shared</b> mesh you own (one mesh, many nodes - the instancing path). You (or <see cref="Own"/>) dispose the mesh.</summary>
    /// <param name="sharedMesh">The mesh to reference. Never owned by the node.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    public SceneNode Spawn(Mesh sharedMesh, Material material, Vector3 position = default, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(sharedMesh);
        ArgumentNullException.ThrowIfNull(material);
        var node = CreateNode(name);
        node.LocalPosition = position;
        node.SetMesh(sharedMesh, material);
        return node;
    }

    /// <summary>Spawns a node with a mesh it owns, built from raw 16-bit-indexed geometry (no closed primitive catalog).</summary>
    /// <param name="vertices">Vertex array (up to 65 535 vertices).</param>
    /// <param name="indices">Index array, triangle list, clockwise-front winding.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    public SceneNode Spawn(Vertex3D[] vertices, ushort[] indices, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        var node = CreateNode(name);
        node.LocalPosition = position;
        node.SetMeshOwnedInternal(new Mesh(vertices, indices, keepCpuData, name), material);
        return node;
    }

    /// <summary>Spawns a node with a mesh it owns, built from raw 32-bit-indexed geometry (large meshes).</summary>
    /// <param name="vertices">Vertex array.</param>
    /// <param name="indices">Index array, triangle list, clockwise-front winding.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    public SceneNode Spawn(Vertex3D[] vertices, uint[] indices, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        var node = CreateNode(name);
        node.LocalPosition = position;
        node.SetMeshOwnedInternal(new Mesh(vertices, indices, keepCpuData, name), material);
        return node;
    }

    /// <summary>Spawns a node from an appendable <see cref="MeshBuilder"/> (reads its accumulated geometry), owned by the node.</summary>
    /// <param name="builder">The builder whose geometry to spawn.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    public SceneNode Spawn(MeshBuilder builder, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return Spawn(builder.ToMeshData(), material, position, name, keepCpuData);
    }
}
