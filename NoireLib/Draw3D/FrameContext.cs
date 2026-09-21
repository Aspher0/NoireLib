using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// The immutable per-frame camera snapshot every part of the renderer consumes, shaders reading only
/// <see cref="ViewProj"/> and <see cref="InvViewProj"/>.
/// </summary>
public readonly struct FrameContext
{
    /// <summary>The combined view-projection matrix, in row-vector convention (<c>clip = v * VP</c>).</summary>
    public readonly Matrix4x4 ViewProj;

    /// <summary>The inverse of <see cref="ViewProj"/>, mapping clip space to world space.</summary>
    public readonly Matrix4x4 InvViewProj;

    /// <summary>The view matrix, for diagnostics only and identity when no render camera was readable.</summary>
    public readonly Matrix4x4 View;

    /// <summary>The projection matrix, for diagnostics only and identity when no render camera was readable.</summary>
    public readonly Matrix4x4 Proj;

    /// <summary>The camera origin in world space.</summary>
    public readonly Vector3 EyePos;

    /// <summary>The seconds since the renderer initialized, used as shader animation time.</summary>
    public readonly float Time;

    /// <summary>The backbuffer size in pixels.</summary>
    public readonly Vector2 ViewportSize;

    /// <summary>The UV scale mapping display UVs into the in-use region of the depth texture, under dynamic resolution and upscalers.</summary>
    public readonly Vector2 DepthUvScale;

    /// <summary>Whether the game's depth runs reversed-Z, with near at 1 and far toward 0.</summary>
    public readonly bool ReversedZ;

    /// <summary>The camera near-plane distance.</summary>
    public readonly float NearPlane;

    /// <summary>Whether the game's depth buffer is readable this frame, false meaning depth-off mode.</summary>
    public readonly bool HasDepth;

    /// <summary>Whether no render camera was readable and the frame projected with <c>Control.ViewProjectionMatrix</c> alone.</summary>
    public readonly bool UsedFallbackCamera;

    /// <summary>The monotonic frame counter.</summary>
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

    /// <summary>Projects a world position to screen pixels with the same math the GPU performs.</summary>
    /// <param name="world">The world-space position.</param>
    /// <param name="screen">Receives the screen position in pixels.</param>
    /// <returns>False when the point is behind the camera.</returns>
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

    /// <summary>Unprojects a screen-pixel position into a world-space ray starting on the near plane.</summary>
    /// <param name="screenPx">The screen position in pixels.</param>
    /// <param name="origin">Receives the ray origin.</param>
    /// <param name="direction">Receives the normalized ray direction.</param>
    /// <returns>True when the position unprojects.</returns>
    public bool TryScreenToRay(Vector2 screenPx, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        if (ViewportSize.X <= 0 || ViewportSize.Y <= 0)
            return false;

        var ndc = new Vector2(screenPx.X / ViewportSize.X * 2f - 1f, 1f - screenPx.Y / ViewportSize.Y * 2f);

        // Reversed-Z puts the near plane at z = 1.
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

    /// <summary>Computes the world length spanning one screen pixel at a point, plus the screen-aligned world axes there. False when the point is behind the camera.</summary>
    /// <param name="worldPoint">The point to size around.</param>
    /// <param name="worldPerPixel">Receives the world length that spans one pixel at that depth.</param>
    /// <param name="rightWorld">Receives the world-space +screen-x axis at that depth (normalized).</param>
    /// <param name="upWorld">Receives the world-space +screen-y (visually up) axis at that depth (normalized).</param>
    public bool TryWorldPerPixel(Vector3 worldPoint, out float worldPerPixel, out Vector3 rightWorld, out Vector3 upWorld)
    {
        worldPerPixel = 0f;
        rightWorld = Vector3.UnitX;
        upWorld = Vector3.UnitY;

        var vp = ViewportSize;
        if (vp.X <= 0f || vp.Y <= 0f)
            return false;

        var toPoint = worldPoint - EyePos;
        var dist = toPoint.Length();
        if (dist < 1e-5f)
            return false;

        toPoint /= dist;
        var refUp = System.MathF.Abs(toPoint.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        rightWorld = Geometry3DHelper.SafeNormalize(Vector3.Cross(refUp, toPoint), Vector3.UnitX);
        upWorld = Geometry3DHelper.SafeNormalize(Vector3.Cross(toPoint, rightWorld), Vector3.UnitY);

        // Analytic derivative. Reconstructing depth from NDC collapses near the camera under reversed-Z.
        var vpMat = ViewProj;
        var clip = Vector4.Transform(new Vector4(worldPoint, 1f), vpMat);
        if (clip.W <= 1e-4f)
            return false;

        var colX = new Vector3(vpMat.M11, vpMat.M21, vpMat.M31);
        var colY = new Vector3(vpMat.M12, vpMat.M22, vpMat.M32);
        var colW = new Vector3(vpMat.M14, vpMat.M24, vpMat.M34);
        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        var invW = 1f / clip.W;
        var halfX = 0.5f * vp.X;
        var halfY = 0.5f * vp.Y;

        float PixelsPerWorld(Vector3 axis)
        {
            var dNdcX = (Vector3.Dot(axis, colX) - ndcX * Vector3.Dot(axis, colW)) * invW;
            var dNdcY = (Vector3.Dot(axis, colY) - ndcY * Vector3.Dot(axis, colW)) * invW;
            var dPxX = halfX * dNdcX;
            var dPxY = halfY * dNdcY;
            return System.MathF.Sqrt(dPxX * dPxX + dPxY * dPxY);
        }

        var pxRight = PixelsPerWorld(rightWorld);
        var pxUp = PixelsPerWorld(upWorld);
        if (pxRight < 1e-6f || pxUp < 1e-6f)
            return false;

        worldPerPixel = 0.5f * (1f / pxRight + 1f / pxUp);
        return worldPerPixel > 0f;
    }
}
