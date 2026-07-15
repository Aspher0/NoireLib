using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Core;
using System;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Renders a scene through a virtual camera into a texture, once per frame before the main pass:
/// minimap portals, mirrors, model-viewer thumbnails, picture-in-picture.<br/>
/// World-depth occlusion is force-disabled per view (a virtual camera has no matching game Z-buffer).
/// Feeding <see cref="Texture"/> back into materials is legal (one-frame latency; views render in
/// registration order).<br/>
/// Dispose the view to release its GPU target; the scene it renders is not owned.
/// </summary>
public sealed class RenderView : IDisposable
{
    internal readonly RenderTarget Target = new();
    internal readonly DepthTarget Depth = new();
    private GpuTexture? texture;

    /// <summary>The scene this view renders. Referenced, never owned.</summary>
    public Scene3D Scene { get; set; }

    /// <summary>The virtual camera.</summary>
    public Camera3D Camera { get; set; }

    /// <summary>Output width in pixels.</summary>
    public int Width { get; }

    /// <summary>Output height in pixels.</summary>
    public int Height { get; }

    /// <summary>Whether the view renders this frame.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True once disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// The rendered output as a material-ready texture. Null until the first frame rendered.
    /// Owned by the view - do not dispose it separately.
    /// </summary>
    public GpuTexture? Texture => texture;

    internal RenderView(Scene3D scene, Camera3D camera, int width, int height)
    {
        Scene = scene;
        Camera = camera;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    /// <summary>Ensures the GPU target exists (render thread) and returns whether the view can render.</summary>
    internal unsafe bool EnsureTarget(RenderDevice device)
    {
        if (IsDisposed)
            return false;

        if (!Target.EnsureSize(device, (uint)Width, (uint)Height))
            return false;

        texture ??= GpuTexture.FromSrv(Target.Srv, Width, Height, addRef: true);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Enabled = false;
        texture?.Dispose();
        texture = null;
        NoireDraw3D.EnqueueRelease(() =>
        {
            Target.Dispose();
            Depth.Dispose();
        });
        NoireDraw3D.UnregisterView(this);
    }
}
