using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// A node in the retained scene graph: local TRS transform, hierarchy, visibility, optional renderer.<br/>
/// Thread-safe: all mutation goes through the shared scene-graph lock; the render thread snapshots
/// resolved world matrices once per frame.
/// </summary>
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

    /// <summary>The mesh this node created and owns (via a <see cref="MeshData"/> <see cref="SetMesh(MeshData, Material, bool)"/> / Spawn), freed on replace or destroy. Null when the node references a shared mesh instead.</summary>
    private Mesh? ownedMesh;

    /// <summary>Optional debug/lookup name.</summary>
    public string? Name { get; set; }

    /// <summary>The parent node (null for scene roots and detached subtrees). Reparent via <see cref="SetParent"/>.</summary>
    public SceneNode? Parent => parent;

    /// <summary>The scene this node currently belongs to (null while detached, e.g. an unattached imported model).</summary>
    public Scene3D? Scene => SceneRef;

    /// <summary>True once this node has been destroyed (via <see cref="Destroy"/>, <see cref="Scene3D.Remove"/>, or the scene's disposal). A destroyed node must not be reused.</summary>
    public bool IsDestroyed => Destroyed;

    /// <summary>Draw layer: orders ground decals and feeds the sort key (higher layers draw later within a bucket).</summary>
    public int Layer { get; set; }

    /// <summary>Whether this node (and its whole subtree) renders. ANDs down the hierarchy.</summary>
    public bool Visible { get; set; } = true;

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

    /// <summary>The resolved local-to-world matrix (lazily recomputed via dirty flags).</summary>
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

    /// <summary>Attaches (or replaces) a renderer drawing the given <b>shared</b> mesh with the given material. Fluent.</summary>
    /// <param name="mesh">The mesh to draw. Referenced, never owned - you (or a <see cref="Scene3D.Own"/> scope) dispose it.</param>
    /// <param name="material">The material to draw with.</param>
    /// <remarks>If this node previously owned a mesh (attached via the <see cref="MeshData"/> overload), that owned mesh is disposed; the shared <paramref name="mesh"/> is left untouched.</remarks>
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

    /// <summary>
    /// Attaches (or replaces) a renderer drawing a mesh the node builds and <b>owns</b> from the given geometry data.
    /// The node disposes the mesh when it is replaced, cleared or destroyed - no mesh bookkeeping. Fluent.
    /// </summary>
    /// <param name="data">CPU mesh data (see <see cref="MeshBuilder"/>). A fresh <see cref="Mesh"/> is built from it and owned exclusively by this node.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="keepCpuData">Retain the CPU arrays on the mesh for exact triangle picking.</param>
    public MeshRenderer SetMesh(MeshData data, Material material, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        var mesh = new Mesh(data, keepCpuData, Name);
        return SetMeshOwnedInternal(mesh, material);
    }

    /// <summary>Attaches a renderer for a mesh this node should own (dispose on replace/destroy). The caller must not dispose or share <paramref name="mesh"/>.</summary>
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

    /// <summary>Removes the node's renderer, if any (disposing an owned mesh; a shared mesh is left untouched).</summary>
    public void ClearMesh()
    {
        lock (Scene3D.GraphLock)
        {
            DisposeOwnedMeshNoLock();
            Renderer = null;
        }
    }

    /// <summary>Disposes the node's owned mesh (if any) and clears the reference. Caller holds <see cref="Scene3D.GraphLock"/>. Idempotent - <see cref="Mesh.Dispose"/> is render-thread-deferred and safe to call twice.</summary>
    private void DisposeOwnedMeshNoLock()
    {
        var owned = ownedMesh;
        ownedMesh = null;
        owned?.Dispose();
    }

    /// <summary>
    /// Reparents the node (null = make it a root of its scene). Rejects cycles with <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="newParent">The new parent, or null.</param>
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

    /// <summary>Removes this node and its whole subtree from the scene. Referenced meshes/materials are untouched (the creator owns them).</summary>
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
        DisposeOwnedMeshNoLock(); // free the mesh the node built for itself; a shared mesh stays with its owner
        Renderer = null;
        ReleaseInteraction(); // drop this node from the interaction bookkeeping if it opted in
        ReleaseExclusions();  // stop any per-frame decal-exclusion refresh for this node
        ReleaseDecalShape();  // stop any per-frame decal-shape outline drawing for this node
        ReleaseDecalVolume(); // and any per-frame decal projection-box drawing
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

        // A Ground/Wall decal is locked to its plane: the mode re-orients the box (keeping heading + scale) so it can never
        // be rotated out of horizontal (Ground) / vertical (Wall). The decal shader then always projects it onto the
        // intended surface with no surface-mode branching. Both leaves the box free.
        if (Renderer?.Material is { Domain: MaterialDomain.GroundDecal } decalMat && decalMat.Surface != DecalSurface.Both)
            world = ConstrainDecalWorld(in world, decalMat.Surface);

        worldMatrix = world;
        worldDirty = false;
        return worldMatrix;
    }

    /// <summary>
    /// Re-orients a ground-decal's world matrix to its <see cref="DecalSurface"/> plane, keeping the box's horizontal
    /// heading (yaw), scale and position but dropping any pitch/roll. <see cref="DecalSurface.Ground"/> forces the
    /// footprint (local XZ) horizontal with the sweep (local Y) pointing down; <see cref="DecalSurface.Wall"/> stands the
    /// footprint upright with the sweep pointing horizontally into the wall. The thin (local Y) axis is the projection
    /// depth in both, so one box works for either mode.
    /// </summary>
    private static Matrix4x4 ConstrainDecalWorld(in Matrix4x4 world, DecalSurface surface)
    {
        // Row-vector basis: rows = local X/Y/Z in world, length = per-axis scale.
        var xAxis = new Vector3(world.M11, world.M12, world.M13);
        var yAxis = new Vector3(world.M21, world.M22, world.M23);
        var zAxis = new Vector3(world.M31, world.M32, world.M33);
        float sx = xAxis.Length(), sy = yAxis.Length(), sz = zAxis.Length();

        // Horizontal heading from local Z (forward); if it is vertical, fall back to local X (which leads Z by 90 deg).
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
        var tangent = new Vector3(cos, 0f, -sin); // horizontal, perpendicular to the heading
        var facing = new Vector3(sin, 0f, cos);   // horizontal heading
        var up = new Vector3(0f, 1f, 0f);

        Vector3 nx, ny, nz;
        if (surface == DecalSurface.Wall)
        {
            // -facing (not +facing) keeps this a PROPER rotation (det +1) instead of a reflection - the box is symmetric
            // along the sweep axis, so the projection is identical, but a reflection breaks Matrix4x4.Decompose (the gizmo
            // decomposes the world matrix on drag, so a reflected wall decal collapses the moment it is moved/rotated/scaled).
            nx = tangent * sx;  // footprint width  (horizontal along the wall)
            ny = -facing * sy;  // sweep: horizontally through the wall (thin axis = depth); sign keeps det +1
            nz = up * sz;       // footprint height (world up), so the shape stands upright
        }
        else // Ground
        {
            nx = tangent * sx; // footprint width  (horizontal)
            ny = up * sy;      // sweep: straight down (thin axis = depth)
            nz = facing * sz;  // footprint depth  (horizontal, along the heading)
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
