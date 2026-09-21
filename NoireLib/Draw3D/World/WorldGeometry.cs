using NoireLib.Draw3D.Geometry;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.World;

/// <summary>Turns the game's live collision into Draw3D geometry. Framework thread only. Returns null when no surface is found.</summary>
public static class WorldGeometry
{
    /// <summary>Collected world collision with flat per-triangle normals and positions relative to the query centre.</summary>
    /// <param name="Vertices">Vertex array, positioned relative to the query centre.</param>
    /// <param name="Indices">32-bit triangle-list indices.</param>
    /// <param name="Center">The world-space centre the positions are relative to.</param>
    public readonly record struct CollectedGeometry(Vertex3D[] Vertices, uint[] Indices, Vector3 Center);

    /// <summary>Collects the collision triangles within <paramref name="radius"/> of <paramref name="center"/>, relative to the centre.</summary>
    /// <param name="center">World-space query centre.</param>
    /// <param name="radius">Half-size of the cubic query volume, in world units.</param>
    /// <param name="maxTriangles">Upper bound on collected triangles.</param>
    /// <param name="includeAnalytic">Whether to include box, cylinder, sphere and plane colliders.</param>
    /// <returns>The geometry, or null when no collision is found, off the framework thread, or on fault.</returns>
    public static CollectedGeometry? Collect(Vector3 center, float radius, int maxTriangles = 40000, bool includeAnalytic = true)
    {
        if (radius <= 0f || maxTriangles <= 0)
            return null;

        var tris = new List<Vector3>(Math.Min(maxTriangles, 4096) * 3);
        var half = new Vector3(radius);
        // Every collision layer.
        var count = GameCollisionHelper.CollectTrianglesInBox(center - half, center + half, tris, maxTriangles, includeAnalytic, layerMask: 0);
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

    /// <summary>Clips the world collision near <paramref name="center"/> to a decal's oriented box, UV-mapped over the footprint and positioned relative to the centre.</summary>
    /// <param name="center">World-space centre of the footprint (roughly on the surface).</param>
    /// <param name="normal">Surface-outward direction the decal faces. Also the axis the box projects along.</param>
    /// <param name="width">Footprint size along the decal U axis, world units.</param>
    /// <param name="height">Footprint size along the decal V axis, world units.</param>
    /// <param name="depth">Thickness of the projection volume along <paramref name="normal"/>.</param>
    /// <param name="maxTriangles">Upper bound on receiving triangles considered.</param>
    /// <param name="includeAnalytic">Whether to also project onto box, cylinder, sphere and plane colliders.</param>
    /// <returns>The clipped mesh, or null when nothing is under the footprint.</returns>
    public static MeshData? ProjectDecal(Vector3 center, Vector3 normal, float width, float height, float depth = 2f, int maxTriangles = 20000, bool includeAnalytic = false)
    {
        if (width <= 0f || height <= 0f || depth <= 0f || maxTriangles <= 0)
            return null;

        var n = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
        // T = U axis, B = V axis, n = projection axis.
        var upHint = MathF.Abs(Vector3.Dot(n, Vector3.UnitY)) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        var t = Vector3.Normalize(Vector3.Cross(upHint, n));
        var b = Vector3.Cross(n, t);

        float hw = width * 0.5f, hh = height * 0.5f, hd = depth * 0.5f;

        var ext = new Vector3(hw) * Vabs(t) + new Vector3(hh) * Vabs(b) + new Vector3(hd) * Vabs(n);
        var tris = new List<Vector3>(2048 * 3);
        var count = GameCollisionHelper.CollectTrianglesInBox(center - ext, center + ext, tris, maxTriangles, includeAnalytic, layerMask: 0);
        if (count <= 0)
            return null;

        var outVerts = new List<Vertex3D>(count * 3);
        var outIdx = new List<ushort>(count * 3);
        var poly = new List<Vector3>(12);
        var scratch = new List<Vector3>(12);
        const float bias = 0.02f; // lifts the decal so it wins the depth test
        var white = new Vector4(1f, 1f, 1f, 1f);

        for (var i = 0; i < count; i++)
        {
            // Decal-local: x along T, y along B, z along n.
            var l0 = ToLocal(tris[i * 3] - center, t, b, n);
            var l1 = ToLocal(tris[i * 3 + 1] - center, t, b, n);
            var l2 = ToLocal(tris[i * 3 + 2] - center, t, b, n);

            poly.Clear();
            poly.Add(l0); poly.Add(l1); poly.Add(l2);

            Geometry3DHelper.ClipConvexPolygon(poly, scratch, 0, hw, keepGreater: false); Swap(ref poly, ref scratch);
            Geometry3DHelper.ClipConvexPolygon(poly, scratch, 0, -hw, keepGreater: true); Swap(ref poly, ref scratch);
            Geometry3DHelper.ClipConvexPolygon(poly, scratch, 1, hh, keepGreater: false); Swap(ref poly, ref scratch);
            Geometry3DHelper.ClipConvexPolygon(poly, scratch, 1, -hh, keepGreater: true); Swap(ref poly, ref scratch);
            Geometry3DHelper.ClipConvexPolygon(poly, scratch, 2, hd, keepGreater: false); Swap(ref poly, ref scratch);
            Geometry3DHelper.ClipConvexPolygon(poly, scratch, 2, -hd, keepGreater: true); Swap(ref poly, ref scratch);

            if (poly.Count < 3 || outVerts.Count + poly.Count > 65000)
                continue;

            var baseIndex = outVerts.Count;
            for (var v = 0; v < poly.Count; v++)
            {
                var lp = poly[v];
                var world = t * lp.X + b * lp.Y + n * (lp.Z + bias);
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

}
