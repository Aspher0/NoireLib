using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

public static partial class MeshSimplifier
{
    /// <summary>Decimates a mesh by Garland-Heckbert quadric error edge collapse, pinning boundary edges and rejecting face flips.</summary>
    /// <param name="vertices">Source vertices.</param>
    /// <param name="indices">Source indices (triangle list).</param>
    /// <param name="targetRatio">Fraction of the original triangle count to keep (clamped to 0.02..0.95).</param>
    /// <returns>The reduced geometry, or null when the mesh is too small, already at target, or nothing collapsed.</returns>
    public static Result? Simplify(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<uint> indices, float targetRatio)
    {
        var vCount = vertices.Length;
        var triCount = indices.Length / 3;
        if (vCount < 4 || triCount < 4)
            return null;

        targetRatio = Math.Clamp(targetRatio, 0.02f, 0.95f);
        var targetTriangles = Math.Max(4, (int)(triCount * targetRatio));
        if (targetTriangles >= triCount)
            return null;

        var vert = new Vertex3D[vCount];
        var pos = new Vector3[vCount];
        for (var i = 0; i < vCount; i++)
        {
            vert[i] = vertices[i];
            pos[i] = vertices[i].Position;
        }

        var t0 = new int[triCount];
        var t1 = new int[triCount];
        var t2 = new int[triCount];
        var removed = new bool[triCount];
        for (var t = 0; t < triCount; t++)
        {
            t0[t] = (int)indices[t * 3];
            t1[t] = (int)indices[t * 3 + 1];
            t2[t] = (int)indices[t * 3 + 2];
        }

        var incident = new List<int>[vCount];
        for (var i = 0; i < vCount; i++)
            incident[i] = new List<int>(6);
        for (var t = 0; t < triCount; t++)
        {
            incident[t0[t]].Add(t);
            incident[t1[t]].Add(t);
            incident[t2[t]].Add(t);
        }

        // Plus a pinning plane per boundary edge. Open borders are not eroded.
        var quad = new Quadric[vCount];
        var edgeFaces = new Dictionary<long, int>(triCount * 3);
        for (var t = 0; t < triCount; t++)
        {
            var a = pos[t0[t]];
            var n = Vector3.Cross(pos[t1[t]] - a, pos[t2[t]] - a);
            var len = n.Length();
            if (len > 1e-12f)
            {
                n /= len;
                var q = Quadric.FromPlane(n.X, n.Y, n.Z, -Vector3.Dot(n, a));
                quad[t0[t]].Add(in q);
                quad[t1[t]].Add(in q);
                quad[t2[t]].Add(in q);
            }

            CountEdge(edgeFaces, t0[t], t1[t]);
            CountEdge(edgeFaces, t1[t], t2[t]);
            CountEdge(edgeFaces, t2[t], t0[t]);
        }

        AddBoundaryQuadrics(edgeFaces, t0, t1, t2, pos, quad, triCount);

        var alive = new bool[vCount];
        Array.Fill(alive, true);
        var version = new int[vCount];
        var current = triCount;

        // Keyed by endpoint versions for lazy invalidation.
        var pq = new PriorityQueue<(int U, int V, int VerU, int VerV), double>();
        foreach (var key in edgeFaces.Keys)
        {
            var u = (int)(key >> 32);
            var v = (int)(key & 0xFFFFFFFF);
            var (cost, _) = EvalCollapse(quad, pos, u, v);
            pq.Enqueue((u, v, version[u], version[v]), cost);
        }

        var guard = triCount * 6 + 16; // bound against a non-manifold loop
        while (current > targetTriangles && pq.Count > 0 && guard-- > 0)
        {
            var e = pq.Dequeue();
            if (!alive[e.U] || !alive[e.V] || version[e.U] != e.VerU || version[e.V] != e.VerV)
                continue; // stale

            TryCollapse(e.U, e.V, quad, pos, vert, t0, t1, t2, removed, incident, alive, version, ref current, pq);
        }

        var remap = new int[vCount];
        Array.Fill(remap, -1);
        var outVerts = new List<Vertex3D>(vCount);
        var outIndices = new List<uint>(current * 3);
        for (var t = 0; t < triCount; t++)
        {
            if (removed[t])
                continue;

            int a = t0[t], b = t1[t], c = t2[t];
            if (a == b || b == c || a == c || !alive[a] || !alive[b] || !alive[c])
                continue;

            outIndices.Add(Emit(a, remap, outVerts, vert));
            outIndices.Add(Emit(b, remap, outVerts, vert));
            outIndices.Add(Emit(c, remap, outVerts, vert));
        }

        if (outIndices.Count < 3 || outIndices.Count / 3 >= triCount)
            return null;

        return new Result(outVerts.ToArray(), outIndices.ToArray());
    }

    private static uint Emit(int index, int[] remap, List<Vertex3D> outVerts, Vertex3D[] vert)
    {
        if (remap[index] < 0)
        {
            remap[index] = outVerts.Count;
            outVerts.Add(vert[index]);
        }

        return (uint)remap[index];
    }

    // Commits only if no incident face would flip.
    private static void TryCollapse(
        int u, int v, Quadric[] quad, Vector3[] pos, Vertex3D[] vert,
        int[] t0, int[] t1, int[] t2, bool[] removed, List<int>[] incident,
        bool[] alive, int[] version, ref int current, PriorityQueue<(int, int, int, int), double> pq)
    {
        var (_, target) = EvalCollapse(quad, pos, u, v);

        foreach (var t in Faces(incident, removed, u, v))
        {
            int a = Fold(t0[t], v, u), b = Fold(t1[t], v, u), c = Fold(t2[t], v, u);
            if (a == b || b == c || a == c)
                continue; // a face using both endpoints dies

            var oldN = Vector3.Cross(pos[t1[t]] - pos[t0[t]], pos[t2[t]] - pos[t0[t]]);
            var pa = Place(a, u, v, target, pos);
            var pb = Place(b, u, v, target, pos);
            var pc = Place(c, u, v, target, pos);
            var newN = Vector3.Cross(pb - pa, pc - pa);
            if (newN.LengthSquared() < 1e-16f || Vector3.Dot(oldN, newN) < 0f)
                return;
        }

        pos[u] = target;
        vert[u].Position = target;
        quad[u].Add(in quad[v]);
        alive[v] = false;

        foreach (var t in incident[v])
        {
            if (removed[t])
                continue;

            if (t0[t] == u || t1[t] == u || t2[t] == u)
            {
                removed[t] = true;
                current--;
                continue;
            }

            if (t0[t] == v) t0[t] = u;
            if (t1[t] == v) t1[t] = u;
            if (t2[t] == v) t2[t] = u;
            incident[u].Add(t);
        }

        version[u]++;
        foreach (var t in incident[u])
        {
            if (removed[t])
                continue;

            PushNeighbour(u, t0[t], quad, pos, version, pq);
            PushNeighbour(u, t1[t], quad, pos, version, pq);
            PushNeighbour(u, t2[t], quad, pos, version, pq);
        }
    }

    private static void PushNeighbour(int u, int w, Quadric[] quad, Vector3[] pos, int[] version, PriorityQueue<(int, int, int, int), double> pq)
    {
        if (w == u)
            return;

        var (cost, _) = EvalCollapse(quad, pos, u, w);
        pq.Enqueue((u, w, version[u], version[w]), cost);
    }

    private static IEnumerable<int> Faces(List<int>[] incident, bool[] removed, int u, int v)
    {
        foreach (var t in incident[u])
            if (!removed[t])
                yield return t;
        foreach (var t in incident[v])
            if (!removed[t] && !incident[u].Contains(t))
                yield return t;
    }

    private static int Fold(int corner, int from, int to) => corner == from ? to : corner;

    private static Vector3 Place(int corner, int u, int v, Vector3 target, Vector3[] pos)
        => corner == u || corner == v ? target : pos[corner];

    // Whichever of u, v or their midpoint minimizes the summed error.
    private static (double Cost, Vector3 Target) EvalCollapse(Quadric[] quad, Vector3[] pos, int u, int v)
    {
        var q = quad[u];
        q.Add(in quad[v]);
        var pu = pos[u];
        var pv = pos[v];
        var mid = (pu + pv) * 0.5f;

        double eu = q.Error(pu), ev = q.Error(pv), em = q.Error(mid);
        if (eu <= ev && eu <= em)
            return (eu, pu);
        return ev <= em ? (ev, pv) : (em, mid);
    }

    private static void CountEdge(Dictionary<long, int> edges, int a, int b)
    {
        var key = EdgeKey(a, b);
        edges[key] = edges.GetValueOrDefault(key) + 1;
    }

    private static long EdgeKey(int a, int b)
    {
        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }

    private static void AddBoundaryQuadrics(Dictionary<long, int> edgeFaces, int[] t0, int[] t1, int[] t2, Vector3[] pos, Quadric[] quad, int triCount)
    {
        const float boundaryWeight = 3.16f; // sqrt(10), the plane weighs about 10x a face
        for (var t = 0; t < triCount; t++)
        {
            AddBoundaryEdge(edgeFaces, t0[t], t1[t], t2[t], pos, quad, boundaryWeight);
            AddBoundaryEdge(edgeFaces, t1[t], t2[t], t0[t], pos, quad, boundaryWeight);
            AddBoundaryEdge(edgeFaces, t2[t], t0[t], t1[t], pos, quad, boundaryWeight);
        }
    }

    private static void AddBoundaryEdge(Dictionary<long, int> edgeFaces, int a, int b, int opposite, Vector3[] pos, Quadric[] quad, float weight)
    {
        if (edgeFaces.GetValueOrDefault(EdgeKey(a, b)) != 1)
            return; // interior edge

        var pa = pos[a];
        var edge = pos[b] - pa;
        var faceN = Vector3.Cross(edge, pos[opposite] - pa);
        var perp = Vector3.Cross(edge, faceN);
        var len = perp.Length();
        if (len < 1e-12f)
            return;

        perp = perp / len * weight;
        var q = Quadric.FromPlane(perp.X, perp.Y, perp.Z, -Vector3.Dot(perp, pa));
        quad[a].Add(in q);
        quad[b].Add(in q);
    }

    // Symmetric 4x4, upper triangle.
    private struct Quadric
    {
        private double a, b, c, d, e, f, g, h, i, j; // xx xy xz xw yy yz yw zz zw ww

        public static Quadric FromPlane(double px, double py, double pz, double pw) => new()
        {
            a = px * px, b = px * py, c = px * pz, d = px * pw,
            e = py * py, f = py * pz, g = py * pw,
            h = pz * pz, i = pz * pw,
            j = pw * pw,
        };

        public void Add(in Quadric q)
        {
            a += q.a; b += q.b; c += q.c; d += q.d;
            e += q.e; f += q.f; g += q.g;
            h += q.h; i += q.i;
            j += q.j;
        }

        public readonly double Error(Vector3 v)
        {
            double x = v.X, y = v.Y, z = v.Z;
            return a * x * x + 2 * b * x * y + 2 * c * x * z + 2 * d * x
                   + e * y * y + 2 * f * y * z + 2 * g * y
                   + h * z * z + 2 * i * z
                   + j;
        }
    }
}
