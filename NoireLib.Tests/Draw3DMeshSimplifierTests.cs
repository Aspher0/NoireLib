using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Geometry;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the LOD math that runs without a GPU: the vertex-clustering decimator (<see cref="MeshSimplifier.Cluster"/>)
/// and the screen-size LOD selection (<see cref="Draw3DPerformance.SelectLevel"/>). The decimator backs the automatic
/// LOD chain, so the invariants that matter are the safety ones - it never emits NaN, out-of-range or degenerate
/// geometry, and it declines rather than returns a useless result - because a distant LOD must never be the thing that
/// garbles a render.
/// </summary>
public class Draw3DMeshSimplifierTests
{
    /// <summary>A flat, subdivided grid: (n+1)² unique vertices spanning 10×10 in XZ, two triangles per cell.</summary>
    private static (Vertex3D[] Vertices, uint[] Indices) Grid(int n)
    {
        var verts = new Vertex3D[(n + 1) * (n + 1)];
        for (var y = 0; y <= n; y++)
        {
            for (var x = 0; x <= n; x++)
            {
                var p = new Vector3(x / (float)n * 10f, 0f, y / (float)n * 10f);
                verts[y * (n + 1) + x] = new Vertex3D(p, Vector3.UnitY, new Vector2(x / (float)n, y / (float)n), new Vector4(1f, 1f, 1f, 1f));
            }
        }

        var indices = new uint[n * n * 6];
        var k = 0;
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var i0 = (uint)(y * (n + 1) + x);
                var i1 = i0 + 1;
                var i2 = (uint)((y + 1) * (n + 1) + x);
                var i3 = i2 + 1;
                indices[k++] = i0; indices[k++] = i2; indices[k++] = i1;
                indices[k++] = i1; indices[k++] = i2; indices[k++] = i3;
            }
        }

        return (verts, indices);
    }

    private static void AssertValidGeometry(in MeshSimplifier.Result r)
    {
        r.Vertices.Should().NotBeEmpty();
        r.Indices.Length.Should().BeGreaterThan(0);
        (r.Indices.Length % 3).Should().Be(0);

        foreach (var v in r.Vertices)
        {
            float.IsFinite(v.Position.X).Should().BeTrue();
            float.IsFinite(v.Position.Y).Should().BeTrue();
            float.IsFinite(v.Position.Z).Should().BeTrue();
            v.Normal.Length().Should().BeApproximately(1f, 1e-4f, "cluster normals are averaged then renormalized");
        }

        for (var t = 0; t < r.Indices.Length; t += 3)
        {
            uint a = r.Indices[t], b = r.Indices[t + 1], c = r.Indices[t + 2];
            a.Should().BeLessThan((uint)r.Vertices.Length);
            b.Should().BeLessThan((uint)r.Vertices.Length);
            c.Should().BeLessThan((uint)r.Vertices.Length);
            (a != b && b != c && a != c).Should().BeTrue("a collapsed triangle must be dropped, never emitted");
        }
    }

    [Fact]
    public void Cluster_ReducesTriangleCountAndStaysValid()
    {
        var (v, i) = Grid(32);
        var result = MeshSimplifier.Cluster(v, i, cellsPerLongestAxis: 8);

        result.Should().NotBeNull();
        var r = result!.Value;
        AssertValidGeometry(in r);
        r.Indices.Length.Should().BeLessThan(i.Length, "clustering a fine grid onto a coarse lattice merges vertices");
        r.Vertices.Length.Should().BeLessThan(v.Length);
    }

    [Fact]
    public void Cluster_KeepsResultInsideTheSourceBounds()
    {
        var (v, i) = Grid(32);
        var source = BoundingSphere.FromVertices(v);
        var r = MeshSimplifier.Cluster(v, i, cellsPerLongestAxis: 6)!.Value;

        // Cell representatives are averages of source vertices, so every one lies within the source bounds (plus slack).
        foreach (var vert in r.Vertices)
            Vector3.Distance(vert.Position, source.Center).Should().BeLessThanOrEqualTo(source.Radius + 1e-3f);
    }

    [Fact]
    public void Cluster_CoarserLatticeYieldsFewerTriangles()
    {
        var (v, i) = Grid(48);
        var fine = MeshSimplifier.Cluster(v, i, cellsPerLongestAxis: 16)!.Value;
        var coarse = MeshSimplifier.Cluster(v, i, cellsPerLongestAxis: 6)!.Value;
        coarse.Indices.Length.Should().BeLessThan(fine.Indices.Length);
    }

    [Fact]
    public void Cluster_ReturnsNullForDegenerateOrTinyInput()
    {
        // Too few vertices.
        MeshSimplifier.Cluster(new Vertex3D[2], new uint[] { 0, 1, 0 }, 8).Should().BeNull();

        // A zero-volume point cloud (all coincident) has no lattice to cluster onto.
        var coincident = new Vertex3D[8];
        var idx = new uint[] { 0, 1, 2, 3, 4, 5 };
        MeshSimplifier.Cluster(coincident, idx, 8).Should().BeNull();
    }

    [Fact]
    public void Cluster_ReturnsNullWhenReductionIsInsufficient()
    {
        // A lattice finer than the grid puts every vertex in its own cell: nothing merges, so there is no LOD to keep.
        var (v, i) = Grid(16);
        MeshSimplifier.Cluster(v, i, cellsPerLongestAxis: 1000, minReduction: 0.2f).Should().BeNull();
    }

    // ---------------------------------------------------------------- quadric-error simplification

    [Fact]
    public void Simplify_ReducesToNearTargetAndStaysValid()
    {
        var (v, i) = Grid(40); // 3200 triangles
        var result = MeshSimplifier.Simplify(v, i, 0.25f);

        result.Should().NotBeNull();
        var r = result!.Value;
        AssertValidGeometry(in r);

        var sourceTris = i.Length / 3;
        var outTris = r.Indices.Length / 3;
        outTris.Should().BeLessThan(sourceTris);
        // Target is a guide (a collapse that would flip a face is skipped), so allow generous slack around 25%.
        outTris.Should().BeLessThan((int)(sourceTris * 0.55f), "the quadric pass should approach the requested reduction");
    }

    [Fact]
    public void Simplify_KeepsVerticesWithinTheSourceBounds()
    {
        // Every collapse places the merged vertex at an endpoint or edge midpoint, so no vertex leaves the source hull.
        var (v, i) = Grid(32);
        var source = BoundingSphere.FromVertices(v);
        var r = MeshSimplifier.Simplify(v, i, 0.2f)!.Value;
        foreach (var vert in r.Vertices)
            Vector3.Distance(vert.Position, source.Center).Should().BeLessThanOrEqualTo(source.Radius + 1e-3f);
    }

    [Fact]
    public void Simplify_PreservesAFlatSurface()
    {
        // A flat grid decimated must stay flat (y = 0): a quadric collapse on a plane adds zero error and never bulges.
        var (v, i) = Grid(24);
        var r = MeshSimplifier.Simplify(v, i, 0.15f)!.Value;
        foreach (var vert in r.Vertices)
            vert.Position.Y.Should().BeApproximately(0f, 1e-4f);
    }

    [Fact]
    public void Simplify_ReturnsNullForTinyInput()
    {
        MeshSimplifier.Simplify(new Vertex3D[3], new uint[] { 0, 1, 2 }, 0.5f).Should().BeNull();
        MeshSimplifier.Simplify(System.Array.Empty<Vertex3D>(), System.Array.Empty<uint>(), 0.5f).Should().BeNull();
    }

    // ---------------------------------------------------------------- LOD level selection

    private static Draw3DPerformance.Snapshot Snapshot(bool lod = true, float bias = 1f, float[]? radii = null)
        => new(lod, bias, 0f, 0f, radii ?? new[] { 160f, 60f, 22f });

    [Theory]
    [InlineData(300f, 0)] // above the first boundary: full detail
    [InlineData(100f, 1)] // below 160, above 60
    [InlineData(40f, 2)]  // below 60, above 22
    [InlineData(5f, 3)]   // below every boundary
    public void SelectLevel_MapsScreenRadiusToLevel(float radius, int expected)
        => Draw3DPerformance.SelectLevel(radius, lodCount: 3, Snapshot()).Should().Be(expected);

    [Fact]
    public void SelectLevel_ClampsToAvailableLodCount()
        => Draw3DPerformance.SelectLevel(1f, lodCount: 2, Snapshot()).Should().Be(2, "a tiny object cannot pick a level the mesh does not have");

    [Fact]
    public void SelectLevel_IsZeroWhenDisabledOrNoChain()
    {
        Draw3DPerformance.SelectLevel(1f, lodCount: 3, Snapshot(lod: false)).Should().Be(0);
        Draw3DPerformance.SelectLevel(1f, lodCount: 0, Snapshot()).Should().Be(0);
    }

    [Fact]
    public void SelectLevel_EmptyRadiiKeepsFullDetail()
        => Draw3DPerformance.SelectLevel(1f, lodCount: 3, Snapshot(radii: System.Array.Empty<float>())).Should().Be(0);

    [Fact]
    public void SelectLevel_BiasDropsDetailSooner()
    {
        // At radius 100 the unbiased selection is level 1; a 2× bias scales the 160/60/22 boundaries to 320/120/44,
        // pushing 100 below the second boundary -> level 2.
        Draw3DPerformance.SelectLevel(100f, lodCount: 3, Snapshot(bias: 2f)).Should().Be(2);
    }
}
