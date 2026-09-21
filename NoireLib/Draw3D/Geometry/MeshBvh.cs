using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

// Model-space. A moving node never invalidates the tree.
internal sealed class MeshBvh
{
    private const int LeafTriangles = 4;
    private const int MaxDepth = 48;

    // An internal node (Left >= 0) holds its right child in Start.
    private struct Node
    {
        public Vector3 Min;
        public Vector3 Max;
        public int Left;   // -1 for a leaf
        public int Start;
        public int Count;
    }

    private readonly Node[] nodes;
    private readonly int[] tri;
    private readonly Vector3[] v0, v1, v2;

    private MeshBvh(Node[] nodes, int[] tri, Vector3[] v0, Vector3[] v1, Vector3[] v2)
    {
        this.nodes = nodes;
        this.tri = tri;
        this.v0 = v0;
        this.v1 = v1;
        this.v2 = v2;
    }

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

        var list = new List<Node>(2 * (triCount / LeafTriangles + 1));
        BuildRange(list, tri, centroid, v0, v1, v2, 0, triCount);

        return new MeshBvh(list.ToArray(), tri, v0, v1, v2);
    }

    private static int BuildRange(List<Node> nodes, int[] tri, Vector3[] centroid, Vector3[] v0, Vector3[] v1, Vector3[] v2, int lo, int hi)
    {
        var self = nodes.Count;
        nodes.Add(default); // filled after the children

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

        // Midpoint of the widest centroid axis, falling back to a count split when one side is empty.
        Vector3 cmin = new(float.MaxValue), cmax = new(float.MinValue);
        for (var i = lo; i < hi; i++)
        {
            cmin = Vector3.Min(cmin, centroid[tri[i]]);
            cmax = Vector3.Max(cmax, centroid[tri[i]]);
        }

        var extent = cmax - cmin;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        var split = 0.5f * (cmin[axis] + cmax[axis]);

        var mid = Partition(tri, centroid, lo, hi, axis, split);
        if (mid == lo || mid == hi)
            mid = lo + count / 2;

        var left = BuildRange(nodes, tri, centroid, v0, v1, v2, lo, mid);
        var right = BuildRange(nodes, tri, centroid, v0, v1, v2, mid, hi);
        nodes[self] = new Node { Min = min, Max = max, Left = left, Start = right, Count = -1 };
        return self;
    }

    private static int Partition(int[] tri, Vector3[] centroid, int lo, int hi, int axis, float split)
    {
        var i = lo;
        var j = hi - 1;
        while (i <= j)
        {
            while (i <= j && centroid[tri[i]][axis] < split)
                i++;
            while (i <= j && centroid[tri[j]][axis] >= split)
                j--;
            if (i < j)
                (tri[i], tri[j]) = (tri[j], tri[i]);
        }

        return i;
    }

    // Two-sided. triangle is the original index.
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
            if (!Geometry3DHelper.RayBox(origin, inv, node.Min, node.Max, t))
                continue;

            if (node.Left < 0)
            {
                for (var k = 0; k < node.Count; k++)
                {
                    var id = tri[node.Start + k];
                    if (Geometry3DHelper.RayTriangle(origin, direction, v0[id], v1[id], v2[id], out var th) && th < t)
                    {
                        t = th;
                        triangle = id;
                    }
                }

                continue;
            }

            if (sp + 2 <= MaxDepth)
            {
                stack[sp++] = node.Left;
                stack[sp++] = node.Start;
            }
        }

        return triangle >= 0;
    }

}
