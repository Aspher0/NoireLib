using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>A retained 3D scene of long-lived nodes, hierarchies and imported models. Mutation is thread-safe.</summary>
public sealed partial class Scene3D
{
    // Shared by all scenes, held briefly.
    internal static readonly object GraphLock = new();

    internal readonly List<SceneNode> Roots = new();
    internal readonly List<ISceneFeature> FeatureList = new();
    private readonly List<ISceneFeature> featureScratch = new();
    private int nodeCount;

    /// <summary>Optional scene name for diagnostics.</summary>
    public string? Name { get; set; }

    /// <summary>Whole-scene kill switch.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Number of live nodes in the scene.</summary>
    public int NodeCount => nodeCount;

    /// <summary>
    /// Fires once per frame on the render thread before culling. Mutations made here render this frame.<br/>
    /// It can fire inside a game D3D call. Touch only the scene graph, <see cref="NoireDraw3D.Im"/> and your own state.
    /// </summary>
    public event Action<FrameContext>? OnPrepareFrame;

    internal Scene3D(string? name) => Name = name;

    /// <summary>Creates a node parented to the scene root. Thread-safe.</summary>
    /// <param name="name">Optional debug/lookup name.</param>
    /// <returns>The new node.</returns>
    public SceneNode CreateNode(string? name = null)
    {
        lock (GraphLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var node = new SceneNode(this, name);
            Roots.Add(node);
            OnNodeAdded();
            return node;
        }
    }

    /// <summary>Removes a node and its subtree from the scene.</summary>
    /// <param name="node">The node to remove.</param>
    /// <returns>False when the node is not part of this scene.</returns>
    public bool Remove(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (GraphLock)
        {
            if (!ReferenceEquals(node.SceneRef, this))
                return false;

            node.DetachFromParentNoLock();
            node.DestroyRecursiveNoLock();
            return true;
        }
    }

    /// <summary>Removes every node from the scene.</summary>
    public void Clear()
    {
        lock (GraphLock)
        {
            foreach (var root in Roots)
                root.DestroyRecursiveNoLock();
            Roots.Clear();
        }
    }

    /// <summary>Registers a per-frame feature (see <see cref="ISceneFeature"/>).</summary>
    /// <param name="feature">The feature to add.</param>
    public void AddFeature(ISceneFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        lock (GraphLock)
        {
            if (!FeatureList.Contains(feature))
                FeatureList.Add(feature);
        }
    }

    /// <summary>Unregisters a per-frame feature.</summary>
    /// <param name="feature">The feature to remove.</param>
    /// <returns>Whether the feature was registered.</returns>
    public bool RemoveFeature(ISceneFeature feature)
    {
        lock (GraphLock)
            return FeatureList.Remove(feature);
    }

    internal void OnNodeAdded() => nodeCount++;

    internal void OnNodeRemoved() => nodeCount--;

    // A disposed scene would never free the adopted root.
    internal void AdoptRoot(SceneNode node)
    {
        lock (GraphLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            node.DetachFromParentNoLock();
            node.SetSceneRecursive(this);
            Roots.Add(node);
            node.MarkDirty();
        }
    }

    // Render thread. Wireframe drops decals, whose box carries no shape.
    internal void TraceDecalShapes(Im.ImDraw3D im)
    {
        if (IsDisposed || !Visible)
            return;

        lock (GraphLock)
        {
            foreach (var root in Roots)
                TraceDecalShapesRecursive(root, im);
        }
    }

    private static void TraceDecalShapesRecursive(SceneNode node, Im.ImDraw3D im)
    {
        if (!node.Visible)
            return;

        try
        {
            node.DrawDecalShapeEdges(im, force: true);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<Scene3D>(ex, "A decal-shape outline threw while tracing for wireframe; skipped this frame.", "Draw3D");
        }

        foreach (var child in node.Children)
            TraceDecalShapesRecursive(child, im);
    }

    // Render thread.
    internal void TraceDecalVolumes(Im.ImDraw3D im)
    {
        if (IsDisposed || !Visible)
            return;

        lock (GraphLock)
        {
            foreach (var root in Roots)
                TraceDecalVolumesRecursive(root, im);
        }
    }

    private static void TraceDecalVolumesRecursive(SceneNode node, Im.ImDraw3D im)
    {
        if (!node.Visible)
            return;

        try
        {
            node.DrawDecalVolumeEdges(im, force: true);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<Scene3D>(ex, "A decal volume box threw while tracing; skipped this frame.", "Draw3D");
        }

        foreach (var child in node.Children)
            TraceDecalVolumesRecursive(child, im);
    }

    // Render thread. A throwing feature is detached and logged once.
    internal void FirePrepare(in FrameContext frame)
    {
        try
        {
            OnPrepareFrame?.Invoke(frame);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<Scene3D>(ex, $"Scene '{Name}': OnPrepareFrame handler threw. Handlers must not throw; continuing.", "Draw3D");
        }

        // Features may add or remove features.
        lock (GraphLock)
        {
            if (FeatureList.Count == 0)
                return;

            featureScratch.Clear();
            featureScratch.AddRange(FeatureList);
        }

        foreach (var feature in featureScratch)
        {
            try
            {
                feature.OnPrepareFrame(this, in frame);
            }
            catch (Exception ex)
            {
                lock (GraphLock)
                    FeatureList.Remove(feature);
                NoireLogger.LogError<Scene3D>(ex, $"Scene '{Name}': feature {feature.GetType().Name} threw and was detached.", "Draw3D");
                NoireDraw3D.RaiseFault(Enums.Draw3DFaultKind.Feature, ex, $"Feature {feature.GetType().Name} detached.");
            }
        }
    }
}
