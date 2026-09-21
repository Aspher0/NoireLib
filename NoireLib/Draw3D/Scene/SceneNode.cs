using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>A node in the retained scene graph: a local transform, children, visibility and an optional renderer. Mutation is thread-safe.</summary>
public sealed partial class SceneNode
{
    private Vector3 localPosition = Vector3.Zero;
    private Quaternion localRotation = Quaternion.Identity;
    private Vector3 localScale = Vector3.One;
    private Matrix4x4 worldMatrix = Matrix4x4.Identity;
    private bool worldDirty = true;
    private SceneNode? parent;
    internal readonly List<SceneNode> Children = new();
    internal Scene3D? SceneRef;
    internal bool Destroyed;

    // Null for a shared mesh.
    private Mesh? ownedMesh;

    /// <summary>Optional debug/lookup name.</summary>
    public string? Name { get; set; }

    /// <summary>The parent node, null for scene roots and detached subtrees.</summary>
    public SceneNode? Parent => parent;

    /// <summary>The scene this node currently belongs to, null while detached.</summary>
    public Scene3D? Scene => SceneRef;

    /// <summary>True once this node has been destroyed. A destroyed node must not be reused.</summary>
    public bool IsDestroyed => Destroyed;

    /// <summary>Draw layer ordering ground decals and draws within a bucket, higher layers drawing later.</summary>
    public int Layer { get; set; }

    /// <summary>Whether this node and its whole subtree render.</summary>
    public bool Visible { get; set; } = true;

    // The scene pass skips the node that frame. Visible = false would also remove it from picking.
    internal long GameLitFrameId;

    /// <summary>The node's renderer, when one was attached via <see cref="SetMesh"/>.</summary>
    public MeshRenderer? Renderer { get; private set; }

    /// <summary>Local position relative to the parent.</summary>
    public Vector3 LocalPosition
    {
        get => localPosition;
        set
        {
            lock (Scene3D.GraphLock)
            {
                localPosition = value;
                MarkDirty();
            }
        }
    }

    /// <summary>Local rotation relative to the parent.</summary>
    public Quaternion LocalRotation
    {
        get => localRotation;
        set
        {
            lock (Scene3D.GraphLock)
            {
                localRotation = value;
                MarkDirty();
            }
        }
    }

    /// <summary>Local scale relative to the parent.</summary>
    public Vector3 LocalScale
    {
        get => localScale;
        set
        {
            lock (Scene3D.GraphLock)
            {
                localScale = value;
                MarkDirty();
            }
        }
    }

    /// <summary>The resolved local-to-world matrix.</summary>
    public Matrix4x4 WorldMatrix
    {
        get
        {
            lock (Scene3D.GraphLock)
                return ResolveWorld();
        }
    }

    internal SceneNode(Scene3D? scene, string? name)
    {
        SceneRef = scene;
        Name = name;
    }

    /// <summary>Creates a child node.</summary>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <returns>The new child.</returns>
    public SceneNode CreateChild(string? name = null)
    {
        lock (Scene3D.GraphLock)
        {
            ThrowIfDestroyed();
            var child = new SceneNode(SceneRef, name) { parent = this };
            Children.Add(child);
            SceneRef?.OnNodeAdded();
            return child;
        }
    }

    /// <summary>Attaches or replaces a renderer drawing a shared mesh, disposing any mesh the node previously owned.</summary>
    /// <param name="mesh">The mesh to draw. Referenced, never owned.</param>
    /// <param name="material">The material to draw with.</param>
    /// <returns>The attached renderer.</returns>
    public MeshRenderer SetMesh(Mesh mesh, Material material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);

        lock (Scene3D.GraphLock)
        {
            ThrowIfDestroyed();
            DisposeOwnedMeshNoLock();
            Renderer = new MeshRenderer(this, mesh, material);
            return Renderer;
        }
    }

    /// <summary>Attaches or replaces a renderer drawing a mesh the node builds from the data and disposes when replaced, cleared or destroyed.</summary>
    /// <param name="data">CPU mesh data (see <see cref="MeshBuilder"/>).</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    /// <returns>The attached renderer.</returns>
    public MeshRenderer SetMesh(MeshData data, Material material, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        var mesh = new Mesh(data, keepCpuData, Name);
        return SetMeshOwnedInternal(mesh, material);
    }

    // The caller must not dispose or share the mesh.
    internal MeshRenderer SetMeshOwnedInternal(Mesh mesh, Material material)
    {
        lock (Scene3D.GraphLock)
        {
            ThrowIfDestroyed();
            DisposeOwnedMeshNoLock();
            ownedMesh = mesh;
            Renderer = new MeshRenderer(this, mesh, material);
            return Renderer;
        }
    }

    /// <summary>Removes the node's renderer, disposing an owned mesh but not a shared one.</summary>
    public void ClearMesh()
    {
        lock (Scene3D.GraphLock)
        {
            DisposeOwnedMeshNoLock();
            Renderer = null;
        }
    }

    // Caller holds GraphLock.
    private void DisposeOwnedMeshNoLock()
    {
        var owned = ownedMesh;
        ownedMesh = null;
        owned?.Dispose();
    }

    /// <summary>Reparents the node, or makes it a root of its scene when <paramref name="newParent"/> is null.</summary>
    /// <param name="newParent">The new parent, or null.</param>
    /// <exception cref="InvalidOperationException">The reparent would create a cycle.</exception>
    public void SetParent(SceneNode? newParent)
    {
        lock (Scene3D.GraphLock)
        {
            ThrowIfDestroyed();
            for (var walk = newParent; walk != null; walk = walk.parent)
            {
                if (ReferenceEquals(walk, this))
                    throw new InvalidOperationException("Draw3D: reparenting would create a cycle.");
            }

            DetachFromParentNoLock();
            parent = newParent;
            if (newParent != null)
            {
                newParent.Children.Add(this);
                SetSceneRecursive(newParent.SceneRef);
            }
            else
            {
                SceneRef?.Roots.Add(this);
            }

            MarkDirty();
        }
    }

    /// <summary>Removes this node and its whole subtree from the scene, leaving shared meshes untouched.</summary>
    public void Destroy()
    {
        lock (Scene3D.GraphLock)
        {
            if (Destroyed)
                return;

            DetachFromParentNoLock();
            DestroyRecursiveNoLock();
        }
    }

    internal void DestroyRecursiveNoLock()
    {
        Destroyed = true;
        DisposeOwnedMeshNoLock();
        Renderer = null;
        ReleaseInteraction();
        ReleaseExclusions();
        ReleaseDecalShape();
        ReleaseDecalVolume();
        SceneRef?.OnNodeRemoved();
        SceneRef = null;
        foreach (var child in Children)
            child.DestroyRecursiveNoLock();
        Children.Clear();
    }

    internal void SetSceneRecursive(Scene3D? scene)
    {
        if (!ReferenceEquals(SceneRef, scene))
        {
            SceneRef?.OnNodeRemoved();
            scene?.OnNodeAdded();
            SceneRef = scene;
        }

        foreach (var child in Children)
            child.SetSceneRecursive(scene);
    }

    internal void DetachFromParentNoLock()
    {
        if (parent != null)
        {
            parent.Children.Remove(this);
            parent = null;
        }
        else
        {
            SceneRef?.Roots.Remove(this);
        }
    }

    internal Matrix4x4 ResolveWorld()
    {
        if (!worldDirty)
            return worldMatrix;

        var local = Matrix4x4.CreateScale(localScale)
                    * Matrix4x4.CreateFromQuaternion(localRotation)
                    * Matrix4x4.CreateTranslation(localPosition);
        var world = parent != null ? local * parent.ResolveWorld() : local;

        // Re-oriented onto their plane. The shader needs no surface-mode branching.
        if (Renderer?.Material is { Domain: MaterialDomain.GroundDecal } decalMat && decalMat.Surface != DecalSurface.Both)
            world = ConstrainDecalWorld(in world, decalMat.Surface);

        worldMatrix = world;
        worldDirty = false;
        return worldMatrix;
    }

    // Local Y stays the projection sweep in both modes.
    private static Matrix4x4 ConstrainDecalWorld(in Matrix4x4 world, DecalSurface surface)
    {
        // Rows are local X/Y/Z in world. Length is per-axis scale.
        var xAxis = new Vector3(world.M11, world.M12, world.M13);
        var yAxis = new Vector3(world.M21, world.M22, world.M23);
        var zAxis = new Vector3(world.M31, world.M32, world.M33);
        float sx = xAxis.Length(), sy = yAxis.Length(), sz = zAxis.Length();

        // Heading from local Z, falling back to local X when Z is vertical.
        float yaw;
        var hz = new Vector2(zAxis.X, zAxis.Z);
        if (hz.LengthSquared() > 1e-8f)
            yaw = MathF.Atan2(hz.X, hz.Y);
        else
        {
            var hx = new Vector2(xAxis.X, xAxis.Z);
            yaw = hx.LengthSquared() > 1e-8f ? MathF.Atan2(hx.X, hx.Y) - MathF.PI / 2f : 0f;
        }

        var (sin, cos) = MathF.SinCos(yaw);
        var tangent = new Vector3(cos, 0f, -sin);
        var facing = new Vector3(sin, 0f, cos);
        var up = new Vector3(0f, 1f, 0f);

        Vector3 nx, ny, nz;
        if (surface == DecalSurface.Wall)
        {
            // -facing keeps det +1. A reflection breaks Matrix4x4.Decompose, which the gizmo uses.
            nx = tangent * sx;  // footprint width
            ny = -facing * sy;  // sweep through the wall
            nz = up * sz;       // footprint height
        }
        else // Ground
        {
            nx = tangent * sx; // footprint width
            ny = up * sy;      // sweep straight down
            nz = facing * sz;  // footprint depth
        }

        return new Matrix4x4(
            nx.X, nx.Y, nx.Z, 0f,
            ny.X, ny.Y, ny.Z, 0f,
            nz.X, nz.Y, nz.Z, 0f,
            world.M41, world.M42, world.M43, 1f);
    }

    internal void MarkDirty()
    {
        if (worldDirty)
            return;

        worldDirty = true;
        foreach (var child in Children)
            child.MarkDirty();
    }

    private void ThrowIfDestroyed()
    {
        if (Destroyed)
            throw new InvalidOperationException("Draw3D: this SceneNode has been destroyed.");
    }
}
