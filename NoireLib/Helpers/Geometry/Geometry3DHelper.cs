using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Ray, volume and polygon primitives in three dimensions: intersection tests, closest-approach solvers, and a
/// convex-polygon clip.<br/>
/// Every method is a pure function of its arguments, so it runs on any thread and unit-tests without a game.
/// Rays take a normalized direction unless a parameter says otherwise.
/// </summary>
public static class Geometry3DHelper
{
    /// <summary>
    /// Intersects a ray with an infinite plane.
    /// </summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction; need not be normalized, and <paramref name="t"/> is in its units.</param>
    /// <param name="planePoint">Any point on the plane.</param>
    /// <param name="planeNormal">Plane normal; need not be normalized.</param>
    /// <param name="t">Ray parameter of the hit, which may be negative when the plane is behind the origin.</param>
    /// <param name="hit">World-space hit point.</param>
    /// <returns>False when the ray is parallel to the plane.</returns>
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
    /// Intersects a ray with a sphere, taking the nearest non-negative root.
    /// </summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction, normalized.</param>
    /// <param name="center">Sphere center.</param>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="t">Ray parameter of the nearest hit.</param>
    /// <returns>True when the sphere is hit at or ahead of the origin.</returns>
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
    /// Moller-Trumbore ray/triangle intersection. Two-sided on purpose, matching what a picker must accept: a
    /// back-facing triangle is still under the cursor.
    /// </summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction.</param>
    /// <param name="a">First triangle vertex.</param>
    /// <param name="b">Second triangle vertex.</param>
    /// <param name="c">Third triangle vertex.</param>
    /// <param name="t">Forward hit distance along <paramref name="direction"/>.</param>
    /// <returns>False for a degenerate triangle, an edge-parallel ray, or a hit behind the origin.</returns>
    public static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(direction, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f)
            return false;

        var invDet = 1f / det;
        var s = origin - a;
        var u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f)
            return false;

        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(direction, q) * invDet;
        if (v < 0f || u + v > 1f)
            return false;

        t = Vector3.Dot(e2, q) * invDet;
        return t >= 0f;
    }

    /// <summary>
    /// Slab ray/AABB test.
    /// </summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="invDirection">Componentwise reciprocal of the ray direction, hoisted out of the caller's loop.</param>
    /// <param name="min">Box minimum corner.</param>
    /// <param name="max">Box maximum corner.</param>
    /// <param name="tMax">Furthest ray parameter still of interest.</param>
    /// <returns>True when the box is hit at or before <paramref name="tMax"/>.</returns>
    public static bool RayBox(in Vector3 origin, in Vector3 invDirection, in Vector3 min, in Vector3 max, float tMax)
    {
        var t1 = (min.X - origin.X) * invDirection.X;
        var t2 = (max.X - origin.X) * invDirection.X;
        var tmin = MathF.Min(t1, t2);
        var tmax = MathF.Max(t1, t2);

        t1 = (min.Y - origin.Y) * invDirection.Y;
        t2 = (max.Y - origin.Y) * invDirection.Y;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));

        t1 = (min.Z - origin.Z) * invDirection.Z;
        t2 = (max.Z - origin.Z) * invDirection.Z;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));

        return tmax >= MathF.Max(tmin, 0f) && tmin <= tMax;
    }

    /// <summary>
    /// Hit-tests a ray against a flat ring lying on a plane, the shape a rotation handle presents.
    /// </summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction, normalized.</param>
    /// <param name="center">Ring center.</param>
    /// <param name="axis">Ring plane normal, normalized.</param>
    /// <param name="ringRadius">Ring radius.</param>
    /// <param name="tolerance">Half-width of the grabbable band around the ring, in world units.</param>
    /// <param name="t">Ray parameter of the plane hit.</param>
    /// <returns>True when the ray meets the plane in front of the origin, within <paramref name="tolerance"/> of the ring.</returns>
    public static bool RayRing(Vector3 origin, Vector3 direction, Vector3 center, Vector3 axis, float ringRadius, float tolerance, out float t)
    {
        if (!RayPlane(origin, direction, center, axis, out t, out var hit) || t < 0f)
            return false;

        var r = Vector3.Distance(hit, center);
        return MathF.Abs(r - ringRadius) <= tolerance;
    }

    /// <summary>
    /// Shortest distance between a ray and a finite segment, plus the ray parameter of the closest approach.
    /// </summary>
    /// <param name="rayOrigin">Ray origin.</param>
    /// <param name="rayDirection">Ray direction, normalized.</param>
    /// <param name="a">Segment start.</param>
    /// <param name="b">Segment end.</param>
    /// <param name="rayT">Ray parameter at the closest approach, clamped to zero or more.</param>
    /// <returns>The distance in world units.</returns>
    public static float RaySegmentDistance(Vector3 rayOrigin, Vector3 rayDirection, Vector3 a, Vector3 b, out float rayT)
    {
        var u = b - a;
        var len = u.Length();
        if (len < 1e-9f)
        {
            rayT = MathF.Max(0f, Vector3.Dot(a - rayOrigin, rayDirection));
            return Vector3.Distance(a, rayOrigin + rayDirection * rayT);
        }

        var ud = u / len;
        ClosestAxisParam(rayOrigin, rayDirection, a, ud, out var s);
        s = Math.Clamp(s, 0f, len);
        var pOnSeg = a + ud * s;

        rayT = MathF.Max(0f, Vector3.Dot(pOnSeg - rayOrigin, rayDirection));
        var pOnRay = rayOrigin + rayDirection * rayT;
        return Vector3.Distance(pOnSeg, pOnRay);
    }

    /// <summary>
    /// Finds the parameter along an axis line closest to a ray, the solve behind axis-constrained dragging.
    /// </summary>
    /// <param name="rayOrigin">Ray origin.</param>
    /// <param name="rayDirection">Ray direction, normalized.</param>
    /// <param name="axisPoint">A point on the axis line.</param>
    /// <param name="axisDirection">Axis direction, normalized.</param>
    /// <param name="axisParam">Signed distance along <paramref name="axisDirection"/> from <paramref name="axisPoint"/>.</param>
    /// <returns>False when the ray is near parallel to the axis, where <paramref name="axisParam"/> falls back to the projection of the ray origin.</returns>
    public static bool ClosestAxisParam(Vector3 rayOrigin, Vector3 rayDirection, Vector3 axisPoint, Vector3 axisDirection, out float axisParam)
    {
        var w0 = rayOrigin - axisPoint;
        var b = Vector3.Dot(rayDirection, axisDirection);
        var denom = 1f - b * b;
        var e = Vector3.Dot(axisDirection, w0);
        if (denom < 1e-6f)
        {
            // Parallel: there is no unique closest point, so project the origin onto the axis instead.
            axisParam = e;
            return false;
        }

        var d = Vector3.Dot(rayDirection, w0);
        axisParam = (e - b * d) / denom;
        return true;
    }

    /// <summary>
    /// Signed angle in radians swept from one point to another about an axis, both measured relative to a center
    /// after projection into the axis plane.
    /// </summary>
    /// <param name="center">The point both vectors are measured from.</param>
    /// <param name="axis">Rotation axis.</param>
    /// <param name="from">Start point.</param>
    /// <param name="to">End point.</param>
    /// <returns>The swept angle, or zero when either projected vector collapses onto the axis.</returns>
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
    /// Normalizes a vector, substituting a fallback for a near-zero one.
    /// </summary>
    /// <param name="v">The vector to normalize.</param>
    /// <param name="fallback">The direction to return when <paramref name="v"/> has no usable length.</param>
    /// <returns>The unit vector.</returns>
    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var len = v.Length();
        return len > 1e-9f ? v / len : fallback;
    }

    /// <summary>
    /// Snaps each component to its own grid step. A component whose step is zero or less passes through.
    /// </summary>
    /// <param name="value">The value to snap.</param>
    /// <param name="step">Per-component grid step.</param>
    /// <returns>The snapped value.</returns>
    public static Vector3 Snap(Vector3 value, Vector3 step)
        => new(MathHelper.Snap(value.X, step.X), MathHelper.Snap(value.Y, step.Y), MathHelper.Snap(value.Z, step.Z));

    /// <summary>
    /// Whether two axis-aligned boxes overlap. Touching faces count as an overlap.
    /// </summary>
    /// <param name="aMin">First box minimum corner.</param>
    /// <param name="aMax">First box maximum corner.</param>
    /// <param name="bMin">Second box minimum corner.</param>
    /// <param name="bMax">Second box maximum corner.</param>
    /// <returns>True when the boxes intersect.</returns>
    public static bool AabbOverlap(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        => aMin.X <= bMax.X && aMax.X >= bMin.X
        && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y
        && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;

    /// <summary>
    /// Sutherland-Hodgman clip of a convex polygon against one axis-aligned half-space. Clip against each of a box's
    /// six half-spaces in turn to trim a polygon to that box.
    /// </summary>
    /// <param name="polygon">The polygon to clip, as ordered vertices.</param>
    /// <param name="result">Receives the clipped polygon; cleared first, and never the same list as <paramref name="polygon"/>.</param>
    /// <param name="axis">Component index to compare: 0 for X, 1 for Y, 2 for Z.</param>
    /// <param name="limit">The half-space boundary on that component.</param>
    /// <param name="keepGreater">True keeps vertices at or above <paramref name="limit"/>, false keeps those at or below.</param>
    public static void ClipConvexPolygon(IReadOnlyList<Vector3> polygon, List<Vector3> result, int axis, float limit, bool keepGreater)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(result);

        result.Clear();
        if (polygon.Count == 0)
            return;

        var prev = polygon[polygon.Count - 1];
        var prevVal = prev[axis];
        var prevIn = keepGreater ? prevVal >= limit : prevVal <= limit;

        for (var i = 0; i < polygon.Count; i++)
        {
            var cur = polygon[i];
            var curVal = cur[axis];
            var curIn = keepGreater ? curVal >= limit : curVal <= limit;

            if (curIn != prevIn)
            {
                var denom = curVal - prevVal;
                var f = MathF.Abs(denom) > 1e-9f ? (limit - prevVal) / denom : 0f;
                result.Add(Vector3.Lerp(prev, cur, f));
            }

            if (curIn)
                result.Add(cur);

            prev = cur;
            prevVal = curVal;
            prevIn = curIn;
        }
    }
}
