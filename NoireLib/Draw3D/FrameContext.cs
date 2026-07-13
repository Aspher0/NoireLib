using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// The immutable per-frame snapshot every part of the renderer consumes (Law 2: one camera snapshot
/// per presented frame, taken at a stable point, passed by <c>in</c> reference — nothing reads camera
/// state after it is built).<br/>
/// Shaders consume only <see cref="ViewProj"/>/<see cref="InvViewProj"/> (the VP-only contract);
/// <see cref="View"/>/<see cref="Proj"/> exist for diagnostics only.
/// </summary>
public readonly struct FrameContext
{
    /// <summary>Combined view-projection matrix (row-vector convention: <c>clip = v · VP</c>).</summary>
    public readonly Matrix4x4 ViewProj;

    /// <summary>Inverse of <see cref="ViewProj"/> (clip → world; decal reconstruction, picking, unprojection).</summary>
    public readonly Matrix4x4 InvViewProj;

    /// <summary>View matrix — diagnostics only (identity when the wholesale VP fallback is active).</summary>
    public readonly Matrix4x4 View;

    /// <summary>Projection matrix — diagnostics only (identity when the wholesale VP fallback is active).</summary>
    public readonly Matrix4x4 Proj;

    /// <summary>Camera origin in world space (sort keys, LOD, billboards).</summary>
    public readonly Vector3 EyePos;

    /// <summary>Seconds since the renderer initialized (shader animation time).</summary>
    public readonly float Time;

    /// <summary>Backbuffer size in pixels.</summary>
    public readonly Vector2 ViewportSize;

    /// <summary>UV scale mapping display UVs into the depth texture's actual (in-use) region — handles dynamic resolution and upscalers.</summary>
    public readonly Vector2 DepthUvScale;

    /// <summary>True when the game runs reversed-Z (near = 1, far → 0). Expected true; carried so an engine flip degrades to one constant.</summary>
    public readonly bool ReversedZ;

    /// <summary>Camera near-plane distance (reversed-Z linearization).</summary>
    public readonly float NearPlane;

    /// <summary>True when the game's depth buffer is readable this frame; false = depth-off mode.</summary>
    public readonly bool HasDepth;

    /// <summary>True when the camera came from the wholesale Control.ViewProjectionMatrix fallback instead of the RenderCamera pair.</summary>
    public readonly bool UsedFallbackCamera;

    /// <summary>Monotonic frame counter (Im-layer timing contract, diagnostics).</summary>
    public readonly long FrameId;

    internal FrameContext(
        in Matrix4x4 viewProj, in Matrix4x4 invViewProj, in Matrix4x4 view, in Matrix4x4 proj,
        Vector3 eyePos, float time, Vector2 viewportSize, Vector2 depthUvScale,
        bool reversedZ, float nearPlane, bool hasDepth, bool usedFallbackCamera, long frameId)
    {
        ViewProj = viewProj;
        InvViewProj = invViewProj;
        View = view;
        Proj = proj;
        EyePos = eyePos;
        Time = time;
        ViewportSize = viewportSize;
        DepthUvScale = depthUvScale;
        ReversedZ = reversedZ;
        NearPlane = nearPlane;
        HasDepth = hasDepth;
        UsedFallbackCamera = usedFallbackCamera;
        FrameId = frameId;
    }

    /// <summary>
    /// Projects a world position to screen pixels. Returns false when the point is behind the camera (w ≤ 0).<br/>
    /// This is the same math the GPU performs — used by the parity validator and available to consumers for labels/anchors.
    /// </summary>
    /// <param name="world">World-space position.</param>
    /// <param name="screen">Receives the screen position in pixels.</param>
    public bool TryWorldToScreen(Vector3 world, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), ViewProj);
        if (clip.W <= 1e-6f)
        {
            screen = default;
            return false;
        }

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        screen = new Vector2((ndcX * 0.5f + 0.5f) * ViewportSize.X, (1f - (ndcY * 0.5f + 0.5f)) * ViewportSize.Y);
        return true;
    }

    /// <summary>
    /// Unprojects a screen-pixel position into a world-space ray (origin on the near plane, direction normalized).
    /// </summary>
    /// <param name="screenPx">Screen position in pixels.</param>
    /// <param name="origin">Receives the ray origin.</param>
    /// <param name="direction">Receives the normalized ray direction.</param>
    public bool TryScreenToRay(Vector2 screenPx, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        if (ViewportSize.X <= 0 || ViewportSize.Y <= 0)
            return false;

        var ndc = new Vector2(screenPx.X / ViewportSize.X * 2f - 1f, 1f - screenPx.Y / ViewportSize.Y * 2f);

        // Reversed-Z: near plane is z = 1. Pick two depths and unproject.
        var nearClip = Vector4.Transform(new Vector4(ndc.X, ndc.Y, 1f, 1f), InvViewProj);
        var farClip = Vector4.Transform(new Vector4(ndc.X, ndc.Y, 0.05f, 1f), InvViewProj);
        if (System.MathF.Abs(nearClip.W) <= 1e-9f || System.MathF.Abs(farClip.W) <= 1e-9f)
            return false;

        var nearWorld = new Vector3(nearClip.X, nearClip.Y, nearClip.Z) / nearClip.W;
        var farWorld = new Vector3(farClip.X, farClip.Y, farClip.Z) / farClip.W;
        var dir = farWorld - nearWorld;
        var len = dir.Length();
        if (len <= 1e-9f)
            return false;

        origin = nearWorld;
        direction = dir / len;
        return true;
    }
}
