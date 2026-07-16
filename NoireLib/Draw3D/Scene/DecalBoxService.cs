using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Draws the opt-in decal-box wireframes on the render thread: nodes that turned one on
/// (<see cref="SceneNode.ShowDecalBox"/>) are traced here each frame off <see cref="NoireDraw3D.OnRenderOverlay"/> - the
/// same zero-latency point the native gizmo uses - so the box tracks the live camera and lands this frame. Consumers
/// never plumb a per-frame call. Fail-soft: a destroyed or turned-off node auto-unregisters; a node that throws while
/// emitting its edges is logged and skipped.
/// </summary>
internal static class DecalBoxService
{
    private const string DisposeKey = "NoireLib.Draw3D.DecalBoxService";

    private static readonly object Sync = new();
    private static readonly List<SceneNode> Nodes = new();
    private static bool hooked;

    /// <summary>Registers a node for per-frame decal-box drawing (idempotent). Hooks the render overlay on first use.</summary>
    public static void Register(SceneNode node)
    {
        lock (Sync)
        {
            if (!Nodes.Contains(node))
                Nodes.Add(node);
            EnsureHooked();
        }
    }

    /// <summary>Stops drawing a node's decal box.</summary>
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
        SceneNode[] snapshot;
        lock (Sync)
        {
            if (Nodes.Count == 0)
                return;
            snapshot = Nodes.ToArray();
        }

        var im = NoireDraw3D.Im;
        foreach (var node in snapshot)
        {
            if (node.Destroyed || !node.HasDecalBox)
            {
                Unregister(node);
                continue;
            }

            try
            {
                node.DrawDecalBoxEdges(im);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "A decal-box wireframe threw while drawing; skipped this frame.", "Draw3D");
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
        }
    }
}
