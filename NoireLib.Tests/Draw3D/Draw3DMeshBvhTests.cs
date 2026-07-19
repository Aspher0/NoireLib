using FluentAssertions;
using NoireLib.Draw3D.Geometry;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Verifies the picking BVH (<see cref="MeshBvh"/>) returns exactly what a brute-force per-triangle scan returns: it is
/// the acceleration that replaces the per-triangle hover scan, so a wrong nearest hit would silently break picking. The
/// oracle is the same two-sided Möller-Trumbore the BVH uses internally, run linearly over every triangle.
/// </summary>
public class Draw3DMeshBvhTests
{
    /// <summary>A bumpy grid so triangles sit at varied depths and the tree has real structure (not a single flat plane).</summary>
    private static (Vertex3D[] Vertices, uint[] Indices) BumpyGrid(int n)
    {
        var verts = new Vertex3D[(n + 1) * (n + 1)];
        for (var z = 0; z <= n; z++)
        {
            for (var x = 0; x <= n; x++)
            {
                var fx = x / (float)n * 6f - 3f;
                var fz = z / (float)n * 6f - 3f;
                var fy = 0.6f * MathF.Sin(fx * 1.3f) * MathF.Cos(fz * 1.1f);
                verts[z * (n + 1) + x] = new Vertex3D(new Vector3(fx, fy, fz), Vector3.UnitY, Vector2.Zero, new Vector4(1f, 1f, 1f, 1f));
            }
        }

        var indices = new uint[n * n * 6];
        var k = 0;
        for (var z = 0; z < n; z++)
        {
            for (var x = 0; x < n; x++)
            {
                var i0 = (uint)(z * (n + 1) + x);
                var i1 = i0 + 1;
                var i2 = (uint)((z + 1) * (n + 1) + x);
                var i3 = i2 + 1;
                indices[k++] = i0; indices[k++] = i2; indices[k++] = i1;
                indices[k++] = i1; indices[k++] = i2; indices[k++] = i3;
            }
        }

        return (verts, indices);
    }

    private static bool BruteForce(Vertex3D[] v, uint[] idx, Vector3 origin, Vector3 dir, out float bestT)
    {
        bestT = float.MaxValue;
        var hit = false;
        for (var t = 0; t < idx.Length; t += 3)
        {
            if (RayTriangle(origin, dir, v[idx[t]].Position, v[idx[t + 1]].Position, v[idx[t + 2]].Position, out var th) && th < bestT)
            {
                bestT = th;
                hit = true;
            }
        }

        return hit;
    }

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

    [Fact]
    public void Bvh_MatchesBruteForceOverRandomRays()
    {
        var (v, idx) = BumpyGrid(24);
        var bvh = MeshBvh.Build(v, null, idx);
        bvh.Should().NotBeNull();

        var rng = new Random(20260718);
        var compared = 0;
        var hits = 0;
        for (var r = 0; r < 2000; r++)
        {
            // Origins around/above the grid; directions biased downward so many rays actually cross it.
            var origin = new Vector3((float)(rng.NextDouble() * 8 - 4), (float)(rng.NextDouble() * 4 + 1), (float)(rng.NextDouble() * 8 - 4));
            var dir = Vector3.Normalize(new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * -1.5 - 0.2), (float)(rng.NextDouble() * 2 - 1)));

            var bruteHit = BruteForce(v, idx, origin, dir, out var bruteT);
            var bvhHit = bvh!.RayCast(origin, dir, out var bvhT, out _);

            bvhHit.Should().Be(bruteHit, "the BVH must agree with the linear scan on hit/miss");
            if (bruteHit)
            {
                bvhT.Should().BeApproximately(bruteT, 1e-3f, "the BVH must find the same nearest surface");
                hits++;
            }

            compared++;
        }

        compared.Should().Be(2000);
        hits.Should().BeGreaterThan(100, "the random-ray setup should actually cross the grid a fair number of times");
    }

    [Fact]
    public void Bvh_HitsStraightDownAndMissesAway()
    {
        var (v, idx) = BumpyGrid(16);
        var bvh = MeshBvh.Build(v, null, idx)!;

        // Straight down through a triangle interior (offset off the grid's vertex/edge lines) must hit.
        var origin = new Vector3(0.13f, 10f, 0.07f);
        bvh.RayCast(origin, new Vector3(0f, -1f, 0f), out var t, out var tri).Should().BeTrue();
        t.Should().BeGreaterThan(0f);
        tri.Should().BeGreaterThanOrEqualTo(0);
        BruteForce(v, idx, origin, new Vector3(0f, -1f, 0f), out var bruteT).Should().BeTrue();
        t.Should().BeApproximately(bruteT, 1e-3f);

        // Pointing away from the grid must miss.
        bvh.RayCast(new Vector3(0f, 10f, 0f), new Vector3(0f, 1f, 0f), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Bvh_BuildReturnsNullForNoTriangles()
    {
        MeshBvh.Build(Array.Empty<Vertex3D>(), Array.Empty<ushort>(), null).Should().BeNull();
        MeshBvh.Build(new Vertex3D[3], null, Array.Empty<uint>()).Should().BeNull();
    }
}
