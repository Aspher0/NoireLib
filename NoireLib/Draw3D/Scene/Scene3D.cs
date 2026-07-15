using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// A retained 3D scene: long-lived nodes, hierarchies, imported models.<br/>
/// Mutation is thread-safe (one shared graph lock, uncontended in practice); the render thread applies
/// a snapshot once per frame. Get the main scene via <see cref="NoireDraw3D.MainScene"/> or create extra
/// ones with <see cref="NoireDraw3D.CreateScene"/>.
/// </summary>
public sealed partial class Scene3D
{
    /// <summary>The single scene-graph mutation lock shared by all scenes (kept coarse on purpose - held only briefly).</summary>
    internal static readonly object GraphLock = new();

    internal readonly List<SceneNode> Roots = new();
    internal readonly List<ISceneFeature> FeatureList = new();
    private int nodeCount;

    /// <summary>Optional scene name (diagnostics).</summary>
    public string? Name { get; set; }

    /// <summary>Whole-scene kill switch.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Number of live nodes in the scene.</summary>
    public int NodeCount => nodeCount;

    /// <summary>
    /// Fires once per frame on the render thread before culling - the place for per-frame procedural
    /// updates (billboards, pulses) without touching Framework events. Mutations made here render this frame.
    /// </summary>
    public event Action<FrameContext>? OnPrepareFrame;

    internal Scene3D(string? name) => Name = name;

    /// <summary>Creates a node parented to the scene root.<br/>Thread-safe.</summary>
    /// <param name="name">Optional debug/lookup name.</param>
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

    /// <summary>Removes a node (and its subtree) from the scene. Returns false when the node is not part of it.</summary>
    /// <param name="node">The node to remove.</param>
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
    public bool RemoveFeature(ISceneFeature feature)
    {
        lock (GraphLock)
            return FeatureList.Remove(feature);
    }

    internal void OnNodeAdded() => nodeCount++;

    internal void OnNodeRemoved() => nodeCount--;

    /// <summary>Adopts a detached node subtree (e.g. an imported model's root) as a scene root. O(1) reparent, any thread.</summary>
    internal void AdoptRoot(SceneNode node)
    {
        lock (GraphLock)
        {
            node.DetachFromParentNoLock();
            node.SetSceneRecursive(this);
            Roots.Add(node);
            node.MarkDirty();
        }
    }

    /// <summary>
    /// Runs OnPrepareFrame + features on the render thread. A feature that throws is detached
    /// (self-disable rung 2) - logged once, everything else keeps running.
    /// </summary>
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

        ISceneFeature[]? features = null;
        lock (GraphLock)
        {
            if (FeatureList.Count > 0)
                features = FeatureList.ToArray();
        }

        if (features == null)
            return;

        foreach (var feature in features)
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
