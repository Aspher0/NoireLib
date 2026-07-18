using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Draws the opt-in decal overlays on the render thread: nodes that turned on a painted-shape outline
/// (<see cref="SceneNode.ShowDecalShape"/>) or a projection-box wireframe (<see cref="SceneNode.ShowDecalVolume"/>) are
/// traced here each frame off <see cref="NoireDraw3D.OnRenderOverlay"/> - the same zero-latency point the native gizmo
/// uses - so an overlay tracks the live camera and lands this frame. Consumers never plumb a per-frame call. A node
/// registers once and keeps the slot while either overlay is on. Fail-soft: a destroyed or fully turned-off node
/// auto-unregisters; a node that throws while emitting is logged and skipped.
/// </summary>
internal static class DecalOverlayService
{
    private const string DisposeKey = "NoireLib.Draw3D.DecalOverlayService";

    private static readonly object Sync = new();
    private static readonly List<SceneNode> Nodes = new();

    // Reused snapshot for the per-frame pass: the loop unregisters stale nodes as it goes, so it cannot run over the
    // live list, and a fresh array every frame is steady-state garbage (Law 9). Render thread only (see OnOverlay).
    private static readonly List<SceneNode> Scratch = new();
    private static bool hooked;

    /// <summary>Registers a node for per-frame decal-overlay drawing (idempotent). Hooks the render overlay on first use.</summary>
    public static void Register(SceneNode node)
    {
        lock (Sync)
        {
            if (!Nodes.Contains(node))
                Nodes.Add(node);
            EnsureHooked();
        }
    }

    /// <summary>Stops drawing a node's decal overlays. Callers check the other overlay first - a node needs the slot while either is on.</summary>
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
        // The master toggles trace every decal, opted in or not, so tracing here as well would double-draw this node's
        // overlay. Each is skipped independently: the shape master does not suppress an opted-in volume box.
        var skipShapes = NoireDraw3D.Wireframe || NoireDraw3D.DecalShapeOutlines;
        var skipVolumes = NoireDraw3D.DecalVolumeOutlines;
        if (skipShapes && skipVolumes)
            return;

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
            if (node.Destroyed || (!node.HasDecalShape && !node.HasDecalVolume))
            {
                Unregister(node);
                continue;
            }

            try
            {
                if (!skipShapes)
                    node.DrawDecalShapeEdges(im);
                if (!skipVolumes)
                    node.DrawDecalVolumeEdges(im);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "A decal overlay threw while drawing; skipped this frame.", "Draw3D");
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
