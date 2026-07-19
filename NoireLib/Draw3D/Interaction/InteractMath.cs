using System;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// Pure geometric primitives shared by the interaction layer and the gizmo: ray/plane/axis solvers,
/// analytic handle hit-tests, screen-constant sizing and angle-on-plane. Every method is a plain function
/// of its inputs (no renderer/ImGui state) so the drag math can be unit-tested headlessly.<br/>
/// Conventions match the rest of Draw3D: row-vector matrices, world units, rays with a normalized direction.
/// </summary>
public static class InteractMath
{
    /// <summary>Intersects a ray with an infinite plane. Returns false when the ray is parallel to the plane.</summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction (need not be normalized; <paramref name="t"/> is in its units).</param>
    /// <param name="planePoint">Any point on the plane.</param>
    /// <param name="planeNormal">Plane normal (need not be normalized).</param>
    /// <param name="t">Receives the ray parameter of the hit (may be negative; the plane can be behind the origin).</param>
    /// <param name="hit">Receives the world-space hit point.</param>
    public static bool RayPlane(Vector3 origin, Vector3 direction, Vector3 planePoint, Vector3 planeNormal, out float t, out Vector3 hit)
    {
        var denom = Vector3.Dot(direction, planeNormal);
        if (MathF.Abs(denom) < 1e-9f)
        {
            t = 0f;
            hit = origin;
            return false;
        }

        t = Vector3.Dot(planePoint - origin, planeNormal) / denom;
        hit = origin + direction * t;
        return true;
    }

    /// <summary>
    /// Finds the parameter along an axis line closest to a ray: the core of axis-constrained dragging
    /// (project the cursor ray onto the handle's axis). Returns false when the ray is (near) parallel to the axis,
    /// in which case <paramref name="axisParam"/> falls back to the projection of the ray origin onto the axis.
    /// </summary>
    /// <param name="rayOrigin">Ray origin.</param>
    /// <param name="rayDir">Ray direction, normalized.</param>
    /// <param name="axisPoint">A point on the axis line.</param>
    /// <param name="axisDir">Axis direction, normalized.</param>
    /// <param name="axisParam">Receives the signed distance along <paramref name="axisDir"/> from <paramref name="axisPoint"/>.</param>
    public static bool ClosestAxisParam(Vector3 rayOrigin, Vector3 rayDir, Vector3 axisPoint, Vector3 axisDir, out float axisParam)
    {
        var w0 = rayOrigin - axisPoint;
        var b = Vector3.Dot(rayDir, axisDir);
        var denom = 1f - b * b;
        var e = Vector3.Dot(axisDir, w0);
        if (denom < 1e-6f)
        {
            // Ray parallel to the axis: no unique closest point; project the origin onto the axis.
            axisParam = e;
            return false;
        }

        var d = Vector3.Dot(rayDir, w0);
        axisParam = (e - b * d) / denom;
        return true;
    }

    /// <summary>
    /// Shortest distance between a ray and a finite segment, plus the ray parameter of the closest approach,
    /// used to hit-test axis/arrow handles (grab when the distance is under the handle's pick radius).
    /// </summary>
    /// <param name="rayOrigin">Ray origin.</param>
    /// <param name="rayDir">Ray direction, normalized.</param>
    /// <param name="a">Segment start.</param>
    /// <param name="b">Segment end.</param>
    /// <param name="rayT">Receives the (clamped >= 0) ray parameter at the closest approach.</param>
    public static float RaySegmentDistance(Vector3 rayOrigin, Vector3 rayDir, Vector3 a, Vector3 b, out float rayT)
    {
        var u = b - a;
        var len = u.Length();
        if (len < 1e-9f)
        {
            rayT = MathF.Max(0f, Vector3.Dot(a - rayOrigin, rayDir));
            return Vector3.Distance(a, rayOrigin + rayDir * rayT);
        }

        var ud = u / len;
        ClosestAxisParam(rayOrigin, rayDir, a, ud, out var s);
        s = Math.Clamp(s, 0f, len);
        var pOnSeg = a + ud * s;

        rayT = MathF.Max(0f, Vector3.Dot(pOnSeg - rayOrigin, rayDir));
        var pOnRay = rayOrigin + rayDir * rayT;
        return Vector3.Distance(pOnSeg, pOnRay);
    }

    /// <summary>Ray/sphere intersection (nearest non-negative root). Used for center/cube handles and bounds picks.</summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction, normalized.</param>
    /// <param name="center">Sphere center.</param>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="t">Receives the ray parameter of the nearest hit.</param>
    public static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float t)
    {
        t = 0f;
        var oc = origin - center;
        var b = Vector3.Dot(oc, direction);
        var c = oc.LengthSquared() - radius * radius;
        var disc = b * b - c;
        if (disc < 0f)
            return false;

        var sq = MathF.Sqrt(disc);
        t = -b - sq;
        if (t < 0f)
            t = -b + sq;
        return t >= 0f;
    }

    /// <summary>
    /// Ray hit-test against a flat ring lying on a plane (the rotation gizmo's rings): true when the ray meets the
    /// plane at a radius within <paramref name="tolerance"/> of <paramref name="ringRadius"/>, in front of the origin.
    /// </summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction, normalized.</param>
    /// <param name="center">Ring center.</param>
    /// <param name="axis">Ring plane normal (the rotation axis), normalized.</param>
    /// <param name="ringRadius">Ring radius.</param>
    /// <param name="tolerance">Half-width of the grabbable band around the ring, in world units.</param>
    /// <param name="t">Receives the ray parameter of the plane hit.</param>
    public static bool RayRing(Vector3 origin, Vector3 direction, Vector3 center, Vector3 axis, float ringRadius, float tolerance, out float t)
    {
        if (!RayPlane(origin, direction, center, axis, out t, out var hit) || t < 0f)
            return false;

        var r = Vector3.Distance(hit, center);
        return MathF.Abs(r - ringRadius) <= tolerance;
    }

    /// <summary>
    /// Signed angle (radians) swept from <paramref name="from"/> to <paramref name="to"/> about <paramref name="axis"/>,
    /// measuring both points relative to <paramref name="center"/> after projecting them into the axis plane. The core
    /// of rotate-gizmo dragging. Returns 0 when either projected vector collapses to the axis.
    /// </summary>
    public static float SignedAngleOnPlane(Vector3 center, Vector3 axis, Vector3 from, Vector3 to)
    {
        axis = SafeNormalize(axis, Vector3.UnitY);
        var f = from - center;
        var g = to - center;
        f -= axis * Vector3.Dot(f, axis);
        g -= axis * Vector3.Dot(g, axis);
        if (f.LengthSquared() < 1e-12f || g.LengthSquared() < 1e-12f)
            return 0f;

        f = Vector3.Normalize(f);
        g = Vector3.Normalize(g);
        var cross = Vector3.Dot(Vector3.Cross(f, g), axis);
        var dot = Math.Clamp(Vector3.Dot(f, g), -1f, 1f);
        return MathF.Atan2(cross, dot);
    }

    /// <summary>
    /// Screen-constant sizing: the world distance at <paramref name="worldPoint"/> that projects to one screen pixel,
    /// plus the camera-aligned right/up world axes at that point: everything a gizmo needs to keep a fixed pixel size
    /// and to build a screen-space handle basis, derived purely from the view-projection pair (no camera struct).
    /// Returns false when the point is at/behind the camera.
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
        // front of the camera). The pixel scale is then the exact analytic screen-space derivative of the projection
        // along each axis. This never reconstructs depth from NDC, so it is immune to the reversed-Z precision collapse
        // near the camera that made the round-trip estimate (and the handle size resting on it) jitter up close.
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

    /// <summary>Normalizes <paramref name="v"/>, falling back to <paramref name="fallback"/> for a (near) zero vector.</summary>
    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var len = v.Length();
        return len > 1e-9f ? v / len : fallback;
    }

    /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="step"/> (no-op when step <= 0).</summary>
    public static float Snap(float value, float step)
        => step > 1e-9f ? MathF.Round(value / step) * step : value;

    /// <summary>Per-component snap of a translation to a grid (components with step <= 0 pass through).</summary>
    public static Vector3 Snap(Vector3 value, Vector3 step)
        => new(Snap(value.X, step.X), Snap(value.Y, step.Y), Snap(value.Z, step.Z));
}
