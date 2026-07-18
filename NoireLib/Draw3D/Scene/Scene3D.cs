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
    private readonly List<ISceneFeature> featureScratch = new(); // reused per-frame snapshot of FeatureList (see FirePrepare)
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
    /// <br/>
    /// This is stricter than "not the framework thread": on the default under-UI path the callback fires <b>mid-frame,
    /// from inside one of the game's own D3D calls</b>, with the game part-way through composing the frame. Touch only
    /// the scene graph, <see cref="NoireDraw3D.Im"/>, and your own state. Do not read or write game state, print to
    /// chat, or call any Dalamud game service from here; hand that work to the framework thread instead.
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

    /// <summary>
    /// Adopts a detached node subtree (e.g. an imported model's root) as a scene root. O(1) reparent, any thread.
    /// Throws when the scene is disposed, matching <see cref="CreateNode"/>: a disposed scene has already run its
    /// teardown, so a root added afterwards would never be freed by it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The scene has been disposed.</exception>
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

    /// <summary>
    /// Traces every visible ground decal in this scene as its painted shape (wireframe mode). Render-thread only, called
    /// before the immediate layer is consumed so the outlines land this frame.
    /// <br/>
    /// Wireframe mode has nothing to rasterize for a decal - the box carries no shape, only the volume the SDF runs in -
    /// so the pass drops decals and this draws what they actually paint instead.
    /// </summary>
    /// <param name="im">The immediate layer to draw into.</param>
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

    /// <summary>Walks a subtree emitting decal outlines. Each node re-checks its own visibility and material.</summary>
    private static void TraceDecalShapesRecursive(SceneNode node, Im.ImDraw3D im)
    {
        if (!node.Visible)
            return; // the whole subtree is hidden

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

    /// <summary>
    /// Traces every visible ground decal in this scene as its projection box - the volume the SDF is evaluated in - for
    /// <see cref="NoireDraw3D.DecalVolumeOutlines"/>. Render-thread only, called before the immediate layer is consumed so
    /// the boxes land this frame. Independent of <see cref="TraceDecalShapes"/>: turn both on to see the painted shape
    /// sitting inside the volume that produced it.
    /// </summary>
    /// <param name="im">The immediate layer to draw into.</param>
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

    /// <summary>Walks a subtree emitting decal projection boxes. Each node re-checks its own visibility and material.</summary>
    private static void TraceDecalVolumesRecursive(SceneNode node, Im.ImDraw3D im)
    {
        if (!node.Visible)
            return; // the whole subtree is hidden

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

        // Snapshot the features under the lock, then run them outside it: a feature is free to add or remove features
        // (and a throwing one is detached below, mid-loop). The buffer is reused across frames because a fresh array
        // per frame is steady-state garbage (Law 9); it is per-scene and only ever touched from the render thread.
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
