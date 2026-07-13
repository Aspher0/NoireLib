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
public sealed class SceneNode
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

    /// <summary>Attaches (or replaces) a renderer drawing the given mesh with the given material. Fluent.</summary>
    /// <param name="mesh">The mesh to draw. Referenced, never owned.</param>
    /// <param name="material">The material to draw with.</param>
    public MeshRenderer SetMesh(Mesh mesh, Material material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);

        lock (Scene3D.GraphLock)
        {
            ThrowIfDestroyed();
            Renderer = new MeshRenderer(this, mesh, material);
            return Renderer;
        }
    }

    /// <summary>Removes the node's renderer, if any.</summary>
    public void ClearMesh()
    {
        lock (Scene3D.GraphLock)
            Renderer = null;
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
        Renderer = null;
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
