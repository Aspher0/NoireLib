using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Draws the opt-in decal-shape outlines on the render thread: nodes that turned one on
/// (<see cref="SceneNode.ShowDecalShape"/>) are traced here each frame off <see cref="NoireDraw3D.OnRenderOverlay"/> - the
/// same zero-latency point the native gizmo uses - so the outline tracks the live camera and lands this frame. Consumers
/// never plumb a per-frame call. Fail-soft: a destroyed or turned-off node auto-unregisters; a node that throws while
/// emitting its outline is logged and skipped.
/// </summary>
internal static class DecalShapeService
{
    private const string DisposeKey = "NoireLib.Draw3D.DecalShapeService";

    private static readonly object Sync = new();
    private static readonly List<SceneNode> Nodes = new();

    // Reused snapshot for the per-frame pass: the loop unregisters stale nodes as it goes, so it cannot run over the
    // live list, and a fresh array every frame is steady-state garbage (Law 9). Render thread only (see OnOverlay).
    private static readonly List<SceneNode> Scratch = new();
    private static bool hooked;

    /// <summary>Registers a node for per-frame decal-shape drawing (idempotent). Hooks the render overlay on first use.</summary>
    public static void Register(SceneNode node)
    {
        lock (Sync)
        {
            if (!Nodes.Contains(node))
                Nodes.Add(node);
            EnsureHooked();
        }
    }

    /// <summary>Stops drawing a node's decal-shape outline.</summary>
    public static void Unregister(SceneNode node)
    {
        lock (Sync)
            Nodes.Remove(node);
    }

    private static void EnsureHooked()
    {
        if (hooked)
            return;

        NoireDraw3D.OnRenderOverlay += OnOverlay;
        hooked = true;

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeKey))
            NoireLibMain.RegisterOnDispose(DisposeKey, Cleanup);
    }

    private static void OnOverlay(FrameContext frame)
    {
        if (NoireDraw3D.Wireframe || NoireDraw3D.DecalShapeOutlines)
            return; // those trace every decal, opted in or not - tracing again would double-draw this node's outline

        lock (Sync)
        {
            if (Nodes.Count == 0)
                return;

            Scratch.Clear();
            Scratch.AddRange(Nodes);
        }

        var im = NoireDraw3D.Im;
        foreach (var node in Scratch)
        {
            if (node.Destroyed || !node.HasDecalShape)
            {
                Unregister(node);
                continue;
            }

            try
            {
                node.DrawDecalShapeEdges(im);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "A decal-shape outline threw while drawing; skipped this frame.", "Draw3D");
            }
        }
    }

    private static void Cleanup()
    {
        lock (Sync)
        {
            if (hooked)
                NoireDraw3D.OnRenderOverlay -= OnOverlay;
            hooked = false;
            Nodes.Clear();
            Scratch.Clear(); // the reused snapshot must not keep node references alive past teardown
        }
    }
}
