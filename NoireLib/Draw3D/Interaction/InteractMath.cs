using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// Pure geometric primitives shared by the interaction layer and the gizmo: ray/plane/axis solvers,
/// analytic handle hit-tests, screen-constant sizing and angle-on-plane. Every method is a plain function
/// of its inputs (no renderer/ImGui state) so the drag math can be unit-tested headlessly.<br/>
/// Conventions match the rest of Draw3D: row-vector matrices, world units, rays with a normalized direction.<br/>
/// The solvers themselves live in <see cref="Geometry3DHelper"/>, which any plugin can reach; this stays as the
/// name the interaction layer is documented under.
/// </summary>
public static class InteractMath
{
    /// <inheritdoc cref="Geometry3DHelper.RayPlane"/>
    public static bool RayPlane(Vector3 origin, Vector3 direction, Vector3 planePoint, Vector3 planeNormal, out float t, out Vector3 hit)
        => Geometry3DHelper.RayPlane(origin, direction, planePoint, planeNormal, out t, out hit);

    /// <inheritdoc cref="Geometry3DHelper.ClosestAxisParam"/>
    public static bool ClosestAxisParam(Vector3 rayOrigin, Vector3 rayDir, Vector3 axisPoint, Vector3 axisDir, out float axisParam)
        => Geometry3DHelper.ClosestAxisParam(rayOrigin, rayDir, axisPoint, axisDir, out axisParam);

    /// <inheritdoc cref="Geometry3DHelper.RaySegmentDistance"/>
    public static float RaySegmentDistance(Vector3 rayOrigin, Vector3 rayDir, Vector3 a, Vector3 b, out float rayT)
        => Geometry3DHelper.RaySegmentDistance(rayOrigin, rayDir, a, b, out rayT);

    /// <inheritdoc cref="Geometry3DHelper.RaySphere"/>
    public static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float t)
        => Geometry3DHelper.RaySphere(origin, direction, center, radius, out t);

    /// <inheritdoc cref="Geometry3DHelper.RayRing"/>
    public static bool RayRing(Vector3 origin, Vector3 direction, Vector3 center, Vector3 axis, float ringRadius, float tolerance, out float t)
        => Geometry3DHelper.RayRing(origin, direction, center, axis, ringRadius, tolerance, out t);

    /// <inheritdoc cref="Geometry3DHelper.SignedAngleOnPlane"/>
    public static float SignedAngleOnPlane(Vector3 center, Vector3 axis, Vector3 from, Vector3 to)
        => Geometry3DHelper.SignedAngleOnPlane(center, axis, from, to);

    /// <summary>
    /// Screen-constant sizing: the world distance at <paramref name="worldPoint"/> that projects to one screen pixel,
    /// plus the camera-aligned right/up world axes at that point - everything a gizmo needs to keep a fixed pixel size
    /// and to build a screen-space handle basis, derived purely from the view-projection pair (no camera struct);
    /// returns false when the point is at/behind the camera.
    /// </summary>
    /// <param name="frame">The frame whose projection to sample.</param>
    /// <param name="worldPoint">The point to size around.</param>
    /// <param name="worldPerPixel">Receives the world length that spans one pixel at that depth.</param>
    /// <param name="rightWorld">Receives the world-space +screen-x axis at that depth (normalized).</param>
    /// <param name="upWorld">Receives the world-space +screen-y (visually up) axis at that depth (normalized).</param>
    public static bool WorldPerPixel(in FrameContext frame, Vector3 worldPoint, out float worldPerPixel, out Vector3 rightWorld, out Vector3 upWorld)
    {
        worldPerPixel = 0f;
        rightWorld = Vector3.UnitX;
        upWorld = Vector3.UnitY;

        var vp = frame.ViewportSize;
        if (vp.X <= 0f || vp.Y <= 0f)
            return false;

        // Screen-parallel world axes at this point: perpendicular to the camera-to-point line. They are the sampling
        // directions for the pixel scale below and the returned right/up.
        var toPoint = worldPoint - frame.EyePos;
        var dist = toPoint.Length();
        if (dist < 1e-5f)
            return false;

        toPoint /= dist;
        var refUp = MathF.Abs(toPoint.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        rightWorld = SafeNormalize(Vector3.Cross(refUp, toPoint), Vector3.UnitX);
        upWorld = SafeNormalize(Vector3.Cross(toPoint, rightWorld), Vector3.UnitY);

        // Perspective denominator at the point, straight from the forward transform (well-conditioned everywhere in
        // front of the camera): the pixel scale is the exact analytic screen-space derivative of the projection along
        // each axis, never reconstructing depth from NDC, so it stays immune to the reversed-Z precision collapse near
        // the camera.
        var vpMat = frame.ViewProj;
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
            // d(screen)/d(world along axis): the derivative of screen = (clip.xy / clip.w) mapped to pixels.
            var dNdcX = (Vector3.Dot(axis, colX) - ndcX * Vector3.Dot(axis, colW)) * invW;
            var dNdcY = (Vector3.Dot(axis, colY) - ndcY * Vector3.Dot(axis, colW)) * invW;
            var dPxX = halfX * dNdcX;
            var dPxY = halfY * dNdcY;
            return MathF.Sqrt(dPxX * dPxX + dPxY * dPxY);
        }

        var pxRight = PixelsPerWorld(rightWorld);
        var pxUp = PixelsPerWorld(upWorld);
        if (pxRight < 1e-6f || pxUp < 1e-6f)
            return false;

        worldPerPixel = 0.5f * (1f / pxRight + 1f / pxUp);
        return worldPerPixel > 0f;
    }

    /// <inheritdoc cref="Geometry3DHelper.SafeNormalize"/>
    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        => Geometry3DHelper.SafeNormalize(v, fallback);

    /// <inheritdoc cref="MathHelper.Snap"/>
    public static float Snap(float value, float step)
        => MathHelper.Snap(value, step);

    /// <inheritdoc cref="Geometry3DHelper.Snap"/>
    public static Vector3 Snap(Vector3 value, Vector3 step)
        => Geometry3DHelper.Snap(value, step);
}
