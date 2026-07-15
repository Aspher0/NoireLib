using FluentAssertions;
using NoireLib.Draw3D.Geometry;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the MeshBuilder catalog: exact vertex/index counts for given segment counts, the
/// clockwise-front winding rule (a flipped winding under back-face culling renders nothing and costs
/// an hour of confusion), bounds containment, UV ranges and the ExtrudePath miter/bevel switch.
/// </summary>
public class Draw3DMeshBuilderTests
{
    /// <summary>
    /// Clockwise-front rule: for D3D default culling, the right-hand-rule cross product of a front
    /// face points away from the viewer, so dot(cross(e1, e2), outwardNormal) must be negative.
    /// Returns the first non-degenerate triangle's winding dot against its interpolated normal.
    /// </summary>
    private static float FirstWindingDot(in MeshData mesh)
    {
        for (var t = 0; t < mesh.Indices.Length / 3; t++)
        {
            var a = mesh.Vertices[mesh.Indices[t * 3]];
            var b = mesh.Vertices[mesh.Indices[t * 3 + 1]];
            var c = mesh.Vertices[mesh.Indices[t * 3 + 2]];
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.LengthSquared() < 1e-12f)
                continue;

            var normal = Vector3.Normalize(a.Normal + b.Normal + c.Normal);
            return Vector3.Dot(cross, normal);
        }

        throw new InvalidOperationException("Mesh has no non-degenerate triangle.");
    }

    private static void AssertBoundsContainAllVertices(in MeshData mesh)
    {
        var sphere = BoundingSphere.FromVertices(mesh.Vertices);
        foreach (var v in mesh.Vertices)
            Vector3.Distance(v.Position, sphere.Center).Should().BeLessThanOrEqualTo(sphere.Radius + 1e-5f);
    }

    [Fact]
    public void Quad_HasExactCountsAndWinding()
    {
        var mesh = MeshBuilder.Quad(2f, 3f);
        mesh.Vertices.Should().HaveCount(4);
        mesh.Indices.Should().HaveCount(6);
        FirstWindingDot(mesh).Should().BeNegative();
        AssertBoundsContainAllVertices(mesh);
    }

    [Fact]
    public void Box_HasPerFaceNormalsAndWinding()
    {
        var mesh = MeshBuilder.Box(new Vector3(2f, 1f, 3f));
        mesh.Vertices.Should().HaveCount(24);
        mesh.Indices.Should().HaveCount(36);

        // Every face must satisfy the winding rule.
        for (var face = 0; face < 6; face++)
        {
            var i = face * 6;
            var a = mesh.Vertices[mesh.Indices[i]];
            var b = mesh.Vertices[mesh.Indices[i + 1]];
            var c = mesh.Vertices[mesh.Indices[i + 2]];
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            Vector3.Dot(cross, a.Normal).Should().BeNegative($"face {face} must be clockwise-front");
        }
    }

    [Fact]
    public void Disc_HasExactCountsAndWinding()
    {
        var mesh = MeshBuilder.Disc(0.5f, 48);
        mesh.Vertices.Should().HaveCount(50);
        mesh.Indices.Should().HaveCount(144);
        FirstWindingDot(mesh).Should().BeNegative();
    }

    [Fact]
    public void Ring_HasExactCountsAndWinding()
    {
        var mesh = MeshBuilder.Ring(0.3f, 0.5f, 64);
        mesh.Vertices.Should().HaveCount(130);
        mesh.Indices.Should().HaveCount(384);
        FirstWindingDot(mesh).Should().BeNegative();
    }

    [Fact]
    public void Sector_HasExactCountsAndWinding()
    {
        var mesh = MeshBuilder.Sector(MathF.PI / 4f, 0.2f, 1f, 32);
        mesh.Vertices.Should().HaveCount(66);
        mesh.Indices.Should().HaveCount(192);
        FirstWindingDot(mesh).Should().BeNegative();
    }

    [Fact]
    public void Sector_IsCenteredOnPlusZ()
    {
        var mesh = MeshBuilder.Sector(MathF.PI / 6f, 0f, 1f, 16);
        foreach (var v in mesh.Vertices)
            v.Position.Z.Should().BeGreaterThanOrEqualTo(-1e-5f, "a 30° half-angle slice around +Z never reaches negative Z");
    }

    [Fact]
    public void Sphere_HasExactCountsWindingAndUnitNormals()
    {
        var mesh = MeshBuilder.Sphere(0.5f, 24, 16);
        mesh.Vertices.Should().HaveCount(425);
        mesh.Indices.Should().HaveCount(2304);
        FirstWindingDot(mesh).Should().BeNegative();

        foreach (var v in mesh.Vertices)
        {
            v.Position.Length().Should().BeApproximately(0.5f, 1e-4f);
            v.Normal.Length().Should().BeApproximately(1f, 1e-4f);
        }
    }

    [Fact]
    public void Cylinder_HasExactCountsAndWinding()
    {
        var mesh = MeshBuilder.Cylinder(0.5f, 1f, 24, caps: true);
        mesh.Vertices.Should().HaveCount(102);
        mesh.Indices.Should().HaveCount(288);
        FirstWindingDot(mesh).Should().BeNegative();
    }

    [Fact]
    public void Cone_HasExactCountsAndWinding()
    {
        var mesh = MeshBuilder.Cone(0.5f, 1f, 24, cap: true);
        mesh.Vertices.Should().HaveCount(76);
        mesh.Indices.Should().HaveCount(144);
        FirstWindingDot(mesh).Should().BeNegative();
    }

    [Fact]
    public void Torus_HasExactCountsWindingAndRadii()
    {
        var mesh = MeshBuilder.Torus(1.5f, 0.25f, 48, 16);
        mesh.Vertices.Should().HaveCount(833);
        mesh.Indices.Should().HaveCount(4608);
        FirstWindingDot(mesh).Should().BeNegative();

        // Every vertex sits exactly minorRadius away from the tube's center ring.
        foreach (var v in mesh.Vertices)
        {
            var onPlane = new Vector3(v.Position.X, 0, v.Position.Z);
            var ringCenter = onPlane.Length() > 1e-6f ? Vector3.Normalize(onPlane) * 1.5f : new Vector3(1.5f, 0, 0);
            Vector3.Distance(v.Position, ringCenter).Should().BeApproximately(0.25f, 1e-4f);
        }
    }

    [Fact]
    public void Arrow_PointsUpAndStaysInBounds()
    {
        var mesh = MeshBuilder.Arrow(2f, 0.05f, 0.15f, 0.4f, 16);
        mesh.Vertices.Should().NotBeEmpty();
        FirstWindingDot(mesh).Should().BeNegative();

        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in mesh.Vertices)
        {
            minY = MathF.Min(minY, v.Position.Y);
            maxY = MathF.Max(maxY, v.Position.Y);
        }

        minY.Should().BeApproximately(0f, 1e-4f, "the arrow base sits at the origin");
        maxY.Should().BeApproximately(2f, 1e-4f, "the tip reaches the requested length");
    }

    [Fact]
    public void ExtrudePath_StraightPath_UsesMiterStations()
    {
        var mesh = MeshBuilder.ExtrudePath(new List<Vector3> { new(0, 0, 0), new(0, 0, 5), new(0, 0, 10) }, 1f);
        mesh.Vertices.Should().HaveCount(6, "three stations, two vertices each");
        mesh.Indices.Should().HaveCount(12, "two strip quads");
        FirstWindingDot(mesh).Should().BeNegative();
    }

    [Fact]
    public void ExtrudePath_GentleTurn_StaysMitered()
    {
        // 90° turn - well below the ~150° bevel threshold.
        var mesh = MeshBuilder.ExtrudePath(new List<Vector3> { new(0, 0, 0), new(0, 0, 5), new(5, 0, 5) }, 0.5f);
        mesh.Vertices.Should().HaveCount(6);
        mesh.Indices.Should().HaveCount(12);
    }

    [Fact]
    public void ExtrudePath_SharpTurn_InsertsBevelStation()
    {
        // 170° turn - beyond the bevel threshold: the corner emits two station pairs.
        var turn = MathF.PI * 170f / 180f;
        var dir2 = new Vector3(MathF.Sin(turn), 0, MathF.Cos(turn));
        var p1 = new Vector3(0, 0, 5);
        var mesh = MeshBuilder.ExtrudePath(new List<Vector3> { new(0, 0, 0), p1, p1 + dir2 * 5f }, 0.5f);

        mesh.Vertices.Should().HaveCount(8, "the beveled corner adds one extra station pair");
        mesh.Indices.Should().HaveCount(18, "three strip quads");
    }

    [Fact]
    public void ExtrudePath_UvRunsAlongTheLength()
    {
        var mesh = MeshBuilder.ExtrudePath(new List<Vector3> { new(0, 0, 0), new(0, 0, 10) }, 1f);
        mesh.Vertices[0].Uv.X.Should().Be(0f);
        mesh.Vertices[^1].Uv.X.Should().Be(1f);
    }

    [Fact]
    public void Builders_KeepUvsInUnitRange()
    {
        foreach (var mesh in new[] { MeshBuilder.Quad(), MeshBuilder.Box(), MeshBuilder.Disc(), MeshBuilder.Ring(0.2f, 0.5f), MeshBuilder.Sphere(), MeshBuilder.Cylinder(), MeshBuilder.Cone(), MeshBuilder.Torus(1f, 0.2f) })
        {
            foreach (var v in mesh.Vertices)
            {
                v.Uv.X.Should().BeInRange(-1e-4f, 1f + 1e-4f);
                v.Uv.Y.Should().BeInRange(-1e-4f, 1f + 1e-4f);
            }
        }
    }
}
