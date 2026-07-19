using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Geometry;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.World;

/// <summary>
/// Reads the game's real collision world (streamed terrain, placed background models, housing furniture and any
/// dynamic object that registers a collider) and turns it into Draw3D geometry. This is the same source a navmesh
/// tool walks; here it powers <b>surface-projected decals</b> that clip to the actual ground/walls/furniture instead
/// of the screen-space depth-buffer decal.<br/>
/// <b>Threading:</b> the collision scene is owned by the game's framework-thread update, so every method here MUST be
/// called on the framework thread (e.g. from a command handler, <c>IFramework.Update</c>, or
/// <c>NoireService.Framework.RunOnFrameworkThread</c>). All methods fail soft: no surface means null, never a throw.
/// </summary>
public static class WorldGeometry
{
    /// <summary>Collected world collision as owned geometry (flat per-triangle normals, positions relative to the query centre).</summary>
    /// <param name="Vertices">Vertex array (positions are world-space minus the query centre; pass the centre as the node position).</param>
    /// <param name="Indices">Triangle-list indices (32-bit; the collision world can exceed 65 535 vertices).</param>
    /// <param name="Center">The world-space centre the positions are relative to.</param>
    public readonly record struct CollectedGeometry(Vertex3D[] Vertices, uint[] Indices, Vector3 Center);

    /// <summary>
    /// Collects the collision triangles within <paramref name="radius"/> of <paramref name="center"/> as a renderable
    /// mesh (flat-shaded). Vertex positions are relative to <paramref name="center"/>, so spawn the node at
    /// <paramref name="center"/>. Returns null when no collision is found (or off the framework thread / on fault).
    /// </summary>
    /// <param name="center">World-space query centre.</param>
    /// <param name="radius">Half-size of the cubic query volume, in world units.</param>
    /// <param name="maxTriangles">Upper bound on collected triangles.</param>
    /// <param name="includeAnalytic">Also include box/cylinder/sphere/plane colliders (invisible walls, trigger volumes), not just mesh models.</param>
    public static CollectedGeometry? Collect(Vector3 center, float radius, int maxTriangles = 40000, bool includeAnalytic = true)
    {
        if (radius <= 0f || maxTriangles <= 0)
            return null;

        var tris = new List<Vector3>(Math.Min(maxTriangles, 4096) * 3);
        var half = new Vector3(radius);
        var count = WorldCollisionSource.CollectTriangles(center - half, center + half, tris, maxTriangles, includeAnalytic);
        if (count <= 0)
            return null;

        var vertices = new Vertex3D[count * 3];
        var indices = new uint[count * 3];
        var white = new Vector4(1f, 1f, 1f, 1f);
        for (var t = 0; t < count; t++)
        {
            var v0 = tris[t * 3];
            var v1 = tris[t * 3 + 1];
            var v2 = tris[t * 3 + 2];
            var n = Vector3.Cross(v1 - v0, v2 - v0);
            n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;

            var b = t * 3;
            vertices[b] = new Vertex3D(v0 - center, n, Vector2.Zero, white);
            vertices[b + 1] = new Vertex3D(v1 - center, n, new Vector2(1f, 0f), white);
            vertices[b + 2] = new Vertex3D(v2 - center, n, new Vector2(0f, 1f), white);
            indices[b] = (uint)b;
            indices[b + 1] = (uint)(b + 1);
            indices[b + 2] = (uint)(b + 2);
        }

        return new CollectedGeometry(vertices, indices, center);
    }

    /// <summary>
    /// Projects a rectangular decal footprint onto the world collision near <paramref name="center"/>, facing
    /// <paramref name="normal"/> (the surface-outward / projection direction, e.g. <c>+Y</c> for a ground decal).
    /// The returned mesh is the receiving surface clipped to the decal's oriented box, UV-mapped so the footprint
    /// fills [0,1]², lifted a hair off the surface to beat z-fighting - ready to spawn with any translucent
    /// textured or coloured material (<c>Cull = None</c>). Positions are relative to <paramref name="center"/>.
    /// Returns null when nothing is under the footprint (framework thread only; fail-soft).
    /// </summary>
    /// <param name="center">World-space centre of the footprint (roughly on the surface).</param>
    /// <param name="normal">Surface-outward direction the decal faces; also the axis the box projects along.</param>
    /// <param name="width">Footprint size along the decal U axis, world units.</param>
    /// <param name="height">Footprint size along the decal V axis, world units.</param>
    /// <param name="depth">Thickness of the projection volume along <paramref name="normal"/> (surfaces within ±depth/2 are captured).</param>
    /// <param name="maxTriangles">Upper bound on receiving triangles considered.</param>
    /// <param name="includeAnalytic">Also project onto box/cylinder/sphere/plane colliders (default false: real surfaces only).</param>
    public static MeshData? ProjectDecal(Vector3 center, Vector3 normal, float width, float height, float depth = 2f, int maxTriangles = 20000, bool includeAnalytic = false)
    {
        if (width <= 0f || height <= 0f || depth <= 0f || maxTriangles <= 0)
            return null;

        var n = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
        // Orthonormal decal basis: T = U axis, B = V axis, n = projection axis.
        var upHint = MathF.Abs(Vector3.Dot(n, Vector3.UnitY)) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        var t = Vector3.Normalize(Vector3.Cross(upHint, n));
        var b = Vector3.Cross(n, t);

        float hw = width * 0.5f, hh = height * 0.5f, hd = depth * 0.5f;

        // Query AABB: the world box enclosing the oriented decal volume.
        var ext = new Vector3(hw) * Vabs(t) + new Vector3(hh) * Vabs(b) + new Vector3(hd) * Vabs(n);
        var tris = new List<Vector3>(2048 * 3);
        var count = WorldCollisionSource.CollectTriangles(center - ext, center + ext, tris, maxTriangles, includeAnalytic);
        if (count <= 0)
            return null;

        var outVerts = new List<Vertex3D>(count * 3);
        var outIdx = new List<ushort>(count * 3);
        var poly = new List<Vector3>(12);
        var scratch = new List<Vector3>(12);
        const float bias = 0.02f; // lift off the surface (world units) so the decal wins the depth test against it
        var white = new Vector4(1f, 1f, 1f, 1f);

        for (var i = 0; i < count; i++)
        {
            // Triangle into decal-local space: x along T, y along B, z along n. The box is now axis-aligned.
            var l0 = ToLocal(tris[i * 3] - center, t, b, n);
            var l1 = ToLocal(tris[i * 3 + 1] - center, t, b, n);
            var l2 = ToLocal(tris[i * 3 + 2] - center, t, b, n);

            poly.Clear();
            poly.Add(l0); poly.Add(l1); poly.Add(l2);

            ClipComponent(poly, scratch, 0, hw, keepGreater: false); Swap(ref poly, ref scratch);
            ClipComponent(poly, scratch, 0, -hw, keepGreater: true); Swap(ref poly, ref scratch);
            ClipComponent(poly, scratch, 1, hh, keepGreater: false); Swap(ref poly, ref scratch);
            ClipComponent(poly, scratch, 1, -hh, keepGreater: true); Swap(ref poly, ref scratch);
            ClipComponent(poly, scratch, 2, hd, keepGreater: false); Swap(ref poly, ref scratch);
            ClipComponent(poly, scratch, 2, -hd, keepGreater: true); Swap(ref poly, ref scratch);

            if (poly.Count < 3 || outVerts.Count + poly.Count > 65000)
                continue;

            var baseIndex = outVerts.Count;
            for (var v = 0; v < poly.Count; v++)
            {
                var lp = poly[v];
                var world = t * lp.X + b * lp.Y + n * (lp.Z + bias); // back to center-relative world, lifted along n
                var uv = new Vector2(lp.X / width + 0.5f, 0.5f - lp.Y / height);
                outVerts.Add(new Vertex3D(world, n, uv, white));
            }

            for (var v = 1; v + 1 < poly.Count; v++)
            {
                outIdx.Add((ushort)baseIndex);
                outIdx.Add((ushort)(baseIndex + v));
                outIdx.Add((ushort)(baseIndex + v + 1));
            }
        }

        if (outIdx.Count == 0)
            return null;

        return new MeshData(outVerts.ToArray(), outIdx.ToArray());
    }

    private static Vector3 ToLocal(Vector3 rel, Vector3 t, Vector3 b, Vector3 n)
        => new(Vector3.Dot(rel, t), Vector3.Dot(rel, b), Vector3.Dot(rel, n));

    private static Vector3 Vabs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));

    private static void Swap(ref List<Vector3> a, ref List<Vector3> b)
    {
        (a, b) = (b, a);
    }

    /// <summary>Sutherland-Hodgman clip of a convex polygon against one axis half-space (component <= limit, or >= limit).</summary>
    private static void ClipComponent(List<Vector3> input, List<Vector3> output, int axis, float limit, bool keepGreater)
    {
        output.Clear();
        if (input.Count == 0)
            return;

        var prev = input[^1];
        var prevVal = Comp(prev, axis);
        var prevIn = keepGreater ? prevVal >= limit : prevVal <= limit;

        for (var i = 0; i < input.Count; i++)
        {
            var cur = input[i];
            var curVal = Comp(cur, axis);
            var curIn = keepGreater ? curVal >= limit : curVal <= limit;

            if (curIn != prevIn)
            {
                var denom = curVal - prevVal;
                var f = MathF.Abs(denom) > 1e-9f ? (limit - prevVal) / denom : 0f;
                output.Add(Vector3.Lerp(prev, cur, f));
            }
            if (curIn)
                output.Add(cur);

            prev = cur;
            prevVal = curVal;
            prevIn = curIn;
        }
    }

    private static float Comp(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
