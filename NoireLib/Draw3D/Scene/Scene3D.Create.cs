using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

public sealed partial class Scene3D
{
    /// <summary>Spawns a node with a mesh it <b>owns</b>, built from geometry data. The scene frees it on <see cref="Dispose"/>.</summary>
    /// <param name="data">CPU mesh data (see <see cref="MeshBuilder"/>).</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    /// <returns>The new node.</returns>
    public SceneNode Spawn(MeshData data, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        var node = CreateNode(name);
        node.LocalPosition = position;
        node.SetMesh(data, material, keepCpuData);
        return node;
    }

    /// <summary>Spawns a node drawing a shared mesh that the caller (or <see cref="Own"/>) disposes.</summary>
    /// <param name="sharedMesh">The mesh to reference. Never owned by the node.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <returns>The new node.</returns>
    public SceneNode Spawn(Mesh sharedMesh, Material material, Vector3 position = default, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(sharedMesh);
        ArgumentNullException.ThrowIfNull(material);
        var node = CreateNode(name);
        node.LocalPosition = position;
        node.SetMesh(sharedMesh, material);
        return node;
    }

    /// <summary>Spawns a node with a mesh it owns, built from raw 16-bit-indexed geometry.</summary>
    /// <param name="vertices">Vertex array (up to 65 535 vertices).</param>
    /// <param name="indices">Index array, triangle list, clockwise-front winding.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    /// <returns>The new node.</returns>
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
    /// <returns>The new node.</returns>
    public SceneNode Spawn(Vertex3D[] vertices, uint[] indices, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        var node = CreateNode(name);
        node.LocalPosition = position;
        node.SetMeshOwnedInternal(new Mesh(vertices, indices, keepCpuData, name), material);
        return node;
    }

    /// <summary>Spawns a node with a mesh it owns, built from a <see cref="MeshBuilder"/>'s accumulated geometry.</summary>
    /// <param name="builder">The builder whose geometry to spawn.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="position">Local position (scene root space).</param>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    /// <returns>The new node.</returns>
    public SceneNode Spawn(MeshBuilder builder, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return Spawn(builder.ToMeshData(), material, position, name, keepCpuData);
    }
}
