using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

/// <summary>
/// A bounding-volume hierarchy over a mesh's triangles for fast ray picking. Built once per mesh (lazily, on the first
/// pick) and reused, it turns a hover hit-test from a per-triangle scan - O(triangles), which alone makes hovering a
/// dense imported model stall - into an O(log triangles) traversal. Model-space: the caller brings the ray into mesh
/// space so a moving node never invalidates the tree.<br/>
/// Median-split build, stack traversal, no per-query allocation. Two-sided triangle test, matching the picker's own.
/// </summary>
internal sealed class MeshBvh
{
    private const int LeafTriangles = 4;   // stop splitting at this many triangles per leaf
    private const int MaxDepth = 48;       // traversal stack bound (a well-balanced tree is far shallower)

    /// <summary>A flat node: a leaf holds a triangle range; an internal node holds two child indices (Left &gt;= 0).</summary>
    private struct Node
    {
        public Vector3 Min;
        public Vector3 Max;
        public int Left;   // child node index, or -1 for a leaf
        public int Start;  // leaf: first triangle in the permuted order
        public int Count;  // leaf: triangle count
    }

    private readonly Node[] nodes;
    private readonly int[] tri;        // permuted triangle order (leaves index into this)
    private readonly Vector3[] v0, v1, v2; // per-triangle corner positions (model space), indexed by original triangle id

    private MeshBvh(Node[] nodes, int[] tri, Vector3[] v0, Vector3[] v1, Vector3[] v2)
    {
        this.nodes = nodes;
        this.tri = tri;
        this.v0 = v0;
        this.v1 = v1;
        this.v2 = v2;
    }

    /// <summary>Builds a BVH from a mesh's CPU geometry. Returns null when the mesh has no triangles.</summary>
    public static MeshBvh? Build(Vertex3D[] vertices, ushort[]? indices16, uint[]? indices32)
    {
        var indexCount = indices16?.Length ?? indices32?.Length ?? 0;
        var triCount = indexCount / 3;
        if (vertices == null || triCount == 0)
            return null;

        var v0 = new Vector3[triCount];
        var v1 = new Vector3[triCount];
        var v2 = new Vector3[triCount];
        var centroid = new Vector3[triCount];
        var tri = new int[triCount];

        for (var t = 0; t < triCount; t++)
        {
            int i0, i1, i2;
            if (indices16 != null)
            {
                i0 = indices16[t * 3];
                i1 = indices16[t * 3 + 1];
                i2 = indices16[t * 3 + 2];
            }
            else
            {
                i0 = (int)indices32![t * 3];
                i1 = (int)indices32[t * 3 + 1];
                i2 = (int)indices32[t * 3 + 2];
            }

            var a = vertices[i0].Position;
            var b = vertices[i1].Position;
            var c = vertices[i2].Position;
            v0[t] = a;
            v1[t] = b;
            v2[t] = c;
            centroid[t] = (a + b + c) * (1f / 3f);
            tri[t] = t;
        }

        // A growable list keeps the build correct under recursion (a fixed pool would have to be grown by ref, which a
        // parent frame cannot observe after a child grows it). One flattening copy at the end; the tree is built once.
        var list = new List<Node>(2 * (triCount / LeafTriangles + 1));
        BuildRange(list, tri, centroid, v0, v1, v2, 0, triCount);

        return new MeshBvh(list.ToArray(), tri, v0, v1, v2);
    }

    /// <summary>Recursively builds a node covering triangles <c>[lo, hi)</c> of the permuted order and returns its index.</summary>
    private static int BuildRange(List<Node> nodes, int[] tri, Vector3[] centroid, Vector3[] v0, Vector3[] v1, Vector3[] v2, int lo, int hi)
    {
        var self = nodes.Count;
        nodes.Add(default); // reserve this node's slot; filled in below (after children, for an internal node)

        // Node bounds over the triangle corners in this range.
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = lo; i < hi; i++)
        {
            var t = tri[i];
            min = Vector3.Min(min, Vector3.Min(v0[t], Vector3.Min(v1[t], v2[t])));
            max = Vector3.Max(max, Vector3.Max(v0[t], Vector3.Max(v1[t], v2[t])));
        }

        var count = hi - lo;
        if (count <= LeafTriangles)
        {
            nodes[self] = new Node { Min = min, Max = max, Left = -1, Start = lo, Count = count };
            return self;
        }

        // Split on the axis with the widest spread of centroids, at the spatial midpoint; fall back to the object
        // median if the midpoint leaves one side empty (all centroids on one side of the plane).
        Vector3 cmin = new(float.MaxValue), cmax = new(float.MinValue);
        for (var i = lo; i < hi; i++)
        {
            cmin = Vector3.Min(cmin, centroid[tri[i]]);
            cmax = Vector3.Max(cmax, centroid[tri[i]]);
        }

        var extent = cmax - cmin;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        var split = 0.5f * (Axis(cmin, axis) + Axis(cmax, axis));

        var mid = Partition(tri, centroid, lo, hi, axis, split);
        if (mid == lo || mid == hi)
            mid = lo + count / 2; // midpoint degenerate - split by count so recursion always shrinks

        var left = BuildRange(nodes, tri, centroid, v0, v1, v2, lo, mid);
        var right = BuildRange(nodes, tri, centroid, v0, v1, v2, mid, hi);
        nodes[self] = new Node { Min = min, Max = max, Left = left, Start = right, Count = -1 };
        return self;
    }

    private static float Axis(in Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>Hoare-style partition of <c>tri[lo,hi)</c> by centroid on <paramref name="axis"/> around <paramref name="split"/>.</summary>
    private static int Partition(int[] tri, Vector3[] centroid, int lo, int hi, int axis, float split)
    {
        var i = lo;
        var j = hi - 1;
        while (i <= j)
        {
            while (i <= j && Axis(centroid[tri[i]], axis) < split)
                i++;
            while (i <= j && Axis(centroid[tri[j]], axis) >= split)
                j--;
            if (i < j)
                (tri[i], tri[j]) = (tri[j], tri[i]);
        }

        return i;
    }

    /// <summary>
    /// Casts a ray (model space) and returns the nearest two-sided triangle hit: <paramref name="t"/> is the distance
    /// along <paramref name="direction"/>, <paramref name="triangle"/> the original triangle index. No allocation.
    /// </summary>
    public bool RayCast(Vector3 origin, Vector3 direction, out float t, out int triangle)
    {
        t = float.MaxValue;
        triangle = -1;
        if (nodes.Length == 0)
            return false;

        var inv = new Vector3(
            MathF.Abs(direction.X) > 1e-12f ? 1f / direction.X : float.PositiveInfinity,
            MathF.Abs(direction.Y) > 1e-12f ? 1f / direction.Y : float.PositiveInfinity,
            MathF.Abs(direction.Z) > 1e-12f ? 1f / direction.Z : float.PositiveInfinity);

        Span<int> stack = stackalloc int[MaxDepth];
        var sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            var node = nodes[stack[--sp]];
            if (!RayBox(origin, inv, node.Min, node.Max, t))
                continue;

            if (node.Left < 0)
            {
                for (var k = 0; k < node.Count; k++)
                {
                    var id = tri[node.Start + k];
                    if (RayTriangle(origin, direction, v0[id], v1[id], v2[id], out var th) && th < t)
                    {
                        t = th;
                        triangle = id;
                    }
                }

                continue;
            }

            // Push both children (Left = child, Start = right child for an internal node); bounded by MaxDepth.
            if (sp + 2 <= MaxDepth)
            {
                stack[sp++] = node.Left;
                stack[sp++] = node.Start;
            }
        }

        return triangle >= 0;
    }

    /// <summary>Slab ray-vs-AABB test; true when the box is hit before <paramref name="tMax"/>.</summary>
    private static bool RayBox(in Vector3 origin, in Vector3 invDir, in Vector3 min, in Vector3 max, float tMax)
    {
        var t1 = (min.X - origin.X) * invDir.X;
        var t2 = (max.X - origin.X) * invDir.X;
        var tmin = MathF.Min(t1, t2);
        var tmax = MathF.Max(t1, t2);

        t1 = (min.Y - origin.Y) * invDir.Y;
        t2 = (max.Y - origin.Y) * invDir.Y;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));

        t1 = (min.Z - origin.Z) * invDir.Z;
        t2 = (max.Z - origin.Z) * invDir.Z;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));

        return tmax >= MathF.Max(tmin, 0f) && tmin <= tMax;
    }

    /// <summary>Möller-Trumbore, two-sided (matches the picker). Returns the forward hit distance along <paramref name="dir"/>.</summary>
    private static bool RayTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(dir, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f)
            return false;

        var invDet = 1f / det;
        var s = origin - a;
        var u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f)
            return false;

        var q = Vector3.Cross(s, e1);
        var vv = Vector3.Dot(dir, q) * invDet;
        if (vv < 0f || u + vv > 1f)
            return false;

        t = Vector3.Dot(e2, q) * invDet;
        return t >= 0f;
    }
}
