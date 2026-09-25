using NoireDraw3DDemoPlugin.Helpers;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Services;

internal sealed class WorldGeometryPreviewService : IDisposable
{
    private Scene3D? scene;

    public bool IsOn => scene is { IsDisposed: false };

    public string Status { get; private set; } = string.Empty;

    // Framework thread only.
    public void Toggle()
    {
        if (scene is { IsDisposed: false } existing)
        {
            existing.Dispose();
            scene = null;
            Status = string.Empty;
            return;
        }

        var center = DemoPlayerHelper.Position();
        var s = scene = NoireDraw3D.CreateScene("worldgeo");
        var mat = Material.Lit(new Vector4(0.35f, 0.75f, 1f, 0.4f)) with { Cull = CullMode.None, Blend = BlendMode.Premultiplied };
        var node = s.SpawnWorldGeometry(center, 20f, mat, includeAnalytic: true, name: "WorldGeo");
        if (node == null)
        {
            s.Dispose();
            scene = null;
            Status = "No collision found near you. You may be in an open area or airborne, or the read faulted (see /xllog).";
            return;
        }

        Status = "Showing the real collision around you, in translucent blue.";
    }

    public void Dispose()
    {
        scene?.Dispose();
        scene = null;
    }
}
