using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

// Render thread, from OnRenderOverlay.
internal static class DecalOverlayService
{
    private const string DisposeKey = "NoireLib.Draw3D.DecalOverlayService";

    private static readonly object Sync = new();
    private static readonly List<SceneNode> Nodes = new();

    // Render thread only. The loop unregisters as it goes.
    private static readonly List<SceneNode> Scratch = new();
    private static bool hooked;

    public static void Register(SceneNode node)
    {
        lock (Sync)
        {
            if (!Nodes.Contains(node))
                Nodes.Add(node);
            EnsureHooked();
        }
    }

    // A node keeps its slot while either overlay is on.
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
        // The master toggles already trace every decal.
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
            Scratch.Clear(); // releases node references past teardown
        }
    }
}
