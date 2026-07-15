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

    /// <summary>The resolved local→world matrix (lazily recomputed via dirty flags).</summary>
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
        worldMatrix = parent != null ? local * parent.ResolveWorld() : local;
        worldDirty = false;
        return worldMatrix;
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
