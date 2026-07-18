using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

/// <summary>
/// CPU mesh decimation for the level-of-detail chain. The primary path is a topology-preserving <b>quadric error
/// edge-collapse</b> (<see cref="Simplify"/>): it collapses the lowest-error edges first and keeps every vertex on an
/// original edge, so an organic model degrades smoothly instead of shattering. A robust <b>vertex-clustering</b>
/// fallback (<see cref="Cluster"/>) covers meshes the quadric pass cannot reduce (heavily non-manifold soup); it never
/// produces NaN/degenerate geometry, but can bridge gaps, so it is only a backstop.<br/>
/// <see cref="BuildLods"/> assembles the chain, quadric-first with a clustering fallback per level.
/// </summary>
public static partial class MeshSimplifier
{
    /// <summary>A decimated vertex/index set (32-bit indices; the caller narrows to 16-bit when it fits).</summary>
    /// <param name="Vertices">The reduced vertex set.</param>
    /// <param name="Indices">The reduced index set (triangle list, winding preserved).</param>
    public readonly record struct Result(Vertex3D[] Vertices, uint[] Indices);

    /// <summary>Running attribute sum for one cluster cell; averaged into a single representative vertex.</summary>
    private struct Accum
    {
        public Vector3 PositionSum;
        public Vector3 NormalSum;
        public Vector2 UvSum;
        public Vector4 ColorSum;
        public int Count;

        public void Add(in Vertex3D v)
        {
            PositionSum += v.Position;
            NormalSum += v.Normal;
            UvSum += v.Uv;
            ColorSum += v.Color;
            Count++;
        }

        public readonly Vertex3D ToVertex()
        {
            var inv = Count > 0 ? 1f / Count : 0f;
            var n = NormalSum * inv;
            var len = n.Length();
            n = len > 1e-6f ? n / len : Vector3.UnitY;
            return new Vertex3D(PositionSum * inv, n, UvSum * inv, ColorSum * inv);
        }
    }

    /// <summary>
    /// Clusters the mesh onto a lattice of about <paramref name="cellsPerLongestAxis"/> cells across its longest bounds
    /// axis and returns the reduced geometry, or null when the input is empty/flat or the reduction is not worth keeping
    /// (at least <paramref name="minReduction"/> of the triangles must be removed, else there is no LOD to gain).
    /// </summary>
    /// <param name="vertices">Source vertices.</param>
    /// <param name="indices">Source indices (triangle list).</param>
    /// <param name="cellsPerLongestAxis">Lattice resolution along the longest bounds axis - lower = coarser LOD.</param>
    /// <param name="minReduction">Minimum fraction of triangles that must be removed for the result to be returned (0..1).</param>
    public static Result? Cluster(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<uint> indices, int cellsPerLongestAxis, float minReduction = 0.2f)
    {
        if (vertices.Length < 3 || indices.Length < 3 || cellsPerLongestAxis < 1)
            return null;

        Vector3 min = vertices[0].Position, max = vertices[0].Position;
        for (var i = 1; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i].Position);
            max = Vector3.Max(max, vertices[i].Position);
        }

        var size = max - min;
        var longest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (longest <= 1e-6f)
            return null; // a flat/point set has no volume to cluster

        var invCell = cellsPerLongestAxis / longest;
        var gy = Math.Max(1, (int)(size.Y * invCell) + 1);
        var gz = Math.Max(1, (int)(size.Z * invCell) + 1);

        // Map each vertex to its cell's representative (accumulated), so a cell shared by many vertices merges them.
        var cellToRep = new Dictionary<long, int>(vertices.Length);
        var reps = new List<Accum>();
        var remap = new int[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i].Position - min;
            var cx = Math.Max(0, (int)(p.X * invCell));
            var cy = Math.Max(0, (int)(p.Y * invCell));
            var cz = Math.Max(0, (int)(p.Z * invCell));
            var key = ((long)cx * gy + cy) * gz + cz;

            if (!cellToRep.TryGetValue(key, out var rep))
            {
                rep = reps.Count;
                cellToRep[key] = rep;
                reps.Add(default);
            }

            remap[i] = rep;
            var acc = reps[rep];
            acc.Add(in vertices[i]);
            reps[rep] = acc;
        }

        // Rebuild triangles, dropping any whose corners collapsed into a shared cell (winding of the survivors kept).
        var newIndices = new List<uint>(indices.Length);
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = remap[indices[t]], b = remap[indices[t + 1]], c = remap[indices[t + 2]];
            if (a == b || b == c || a == c)
                continue;

            newIndices.Add((uint)a);
            newIndices.Add((uint)b);
            newIndices.Add((uint)c);
        }

        if (newIndices.Count < 3)
            return null;

        var reduction = 1f - (float)newIndices.Count / indices.Length;
        if (reduction < minReduction)
            return null;

        // Compact to only the representatives a surviving triangle references (an emptied cell leaves no vertex behind).
        var compact = new int[reps.Count];
        Array.Fill(compact, -1);
        var outVertices = new List<Vertex3D>(reps.Count);
        for (var i = 0; i < newIndices.Count; i++)
        {
            var rep = (int)newIndices[i];
            if (compact[rep] < 0)
            {
                compact[rep] = outVertices.Count;
                outVertices.Add(reps[rep].ToVertex());
            }

            newIndices[i] = (uint)compact[rep];
        }

        return new Result(outVertices.ToArray(), newIndices.ToArray());
    }

    /// <summary>Lattice resolutions for the clustering backstop, used only when the quadric pass cannot reduce a mesh.</summary>
    private static readonly int[] FallbackClusterCells = { 48, 24, 12 };

    /// <summary>
    /// Builds a chain of progressively coarser LOD meshes from the source geometry, ordered finest-first. Each level is
    /// a quadric error edge-collapse to the matching fraction of the original triangle count (<paramref name="targetRatios"/>,
    /// e.g. <c>[0.5, 0.25, 0.12]</c>); if the quadric pass cannot reduce the mesh at all, it falls back to a clustering
    /// chain. A level no smaller than the previous one is skipped, so the chain is monotonic and may be shorter than
    /// requested (empty when nothing reduces usefully). Meshes are created on the calling thread (devices are
    /// free-threaded), so this runs on the import thread. The caller owns and disposes the returned meshes.
    /// </summary>
    /// <param name="vertices">Full-resolution vertices.</param>
    /// <param name="indices">Full-resolution indices.</param>
    /// <param name="targetRatios">Descending target triangle fractions (0..1), one per desired LOD level.</param>
    /// <param name="name">Optional debug name carried onto each LOD mesh.</param>
    public static Mesh[] BuildLods(Vertex3D[] vertices, uint[] indices, ReadOnlySpan<float> targetRatios, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        var lods = new List<Mesh>();
        var prevTriangles = indices.Length / 3;
        foreach (var ratio in targetRatios)
        {
            if (Simplify(vertices, indices, ratio) is not { } result)
                continue;

            var triangles = result.Indices.Length / 3;
            if (triangles >= prevTriangles || triangles < 1)
                continue; // no improvement on the previous level - drop this rung

            lods.Add(MakeMesh(result.Vertices, result.Indices, name));
            prevTriangles = triangles;
        }

        // Backstop for a mesh the quadric pass could not reduce (heavily non-manifold): the robust clustering chain.
        if (lods.Count == 0)
        {
            prevTriangles = indices.Length / 3;
            foreach (var cells in FallbackClusterCells)
            {
                if (Cluster(vertices, indices, cells) is not { } result)
                    continue;

                var triangles = result.Indices.Length / 3;
                if (triangles >= prevTriangles || triangles < 1)
                    continue;

                lods.Add(MakeMesh(result.Vertices, result.Indices, name));
                prevTriangles = triangles;
            }
        }

        return lods.ToArray();
    }

    /// <summary>Creates a GPU mesh from decimated data, narrowing to a 16-bit index buffer when the vertex count allows.</summary>
    private static Mesh MakeMesh(Vertex3D[] vertices, uint[] indices, string? name)
    {
        if (vertices.Length <= ushort.MaxValue)
        {
            var indices16 = new ushort[indices.Length];
            for (var i = 0; i < indices.Length; i++)
                indices16[i] = (ushort)indices[i];
            return new Mesh(vertices, indices16, keepCpuData: false, name);
        }

        return new Mesh(vertices, indices, keepCpuData: false, name);
    }
}
