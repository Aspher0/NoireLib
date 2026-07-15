using FluentAssertions;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Scene;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the ease-of-use creation surface that needs no GPU device: the appendable <see cref="MeshBuilder"/>
/// instance form (index rebasing + offsets), the fluent node transforms (At / Rotate / Scale / LookAt), and the
/// <see cref="SceneNode.Tint"/> / <see cref="SceneNode.HasRenderer"/> proxies with no renderer attached.
/// (Owned-mesh / Spawn paths build a real <see cref="Mesh"/> and are covered in-game, not here.)
/// </summary>
public class Draw3DCreationTests
{
    // ---------------------------------------------------------------- appendable MeshBuilder

    [Fact]
    public void MeshBuilder_Instance_AppendsAndRebasesIndices()
    {
        var mb = new MeshBuilder();
        mb.AddBox();                 // 24 verts / 36 idx
        mb.AddSphere(0.5f);          // 425 verts / 2304 idx

        var data = mb.ToMeshData();
        data.Vertices.Should().HaveCount(24 + 425);
        data.Indices.Should().HaveCount(36 + 2304);

        // Every index must address a real vertex - a rebasing bug shows up here immediately.
        foreach (var idx in data.Indices)
            idx.Should().BeLessThan((ushort)data.Vertices.Length);
    }

    [Fact]
    public void MeshBuilder_Instance_AppliesOffset()
    {
        var mb = new MeshBuilder();
        mb.AddBox(new Vector3(1f, 1f, 1f), offset: new Vector3(0f, 10f, 0f));

        foreach (var v in mb.ToMeshData().Vertices)
            v.Position.Y.Should().BeInRange(9.4f, 10.6f, "the unit box is offset to y≈10");
    }

    [Fact]
    public void MeshBuilder_Instance_RawAppend_RebasesOntoCurrentBuffer()
    {
        var mb = new MeshBuilder();
        mb.AddBox(); // seed with 24 verts

        var raw = MeshBuilder.Quad();
        mb.Add(raw.Vertices, raw.Indices);

        var data = mb.ToMeshData();
        data.Vertices.Should().HaveCount(24 + 4);
        // The appended quad's indices must have been shifted by the 24 already present.
        data.Indices[^6].Should().BeGreaterThanOrEqualTo((ushort)24);
    }

    [Fact]
    public void MeshBuilder_Instance_ClearResetsBuffer()
    {
        var mb = new MeshBuilder();
        mb.AddBox();
        mb.Clear();
        mb.VertexCount.Should().Be(0);
        mb.IndexCount.Should().Be(0);
    }

    // ---------------------------------------------------------------- fluent transforms

    [Fact]
    public void At_SetsLocalPosition()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode().At(new Vector3(1f, 2f, 3f));
        node.LocalPosition.Should().Be(new Vector3(1f, 2f, 3f));
    }

    [Fact]
    public void RotateY_Composes()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode().RotateY(MathF.PI / 4f).RotateY(MathF.PI / 4f);

        // Two 45° turns == one 90° turn: local +X maps to -Z in this left-handed frame.
        var p = Vector3.Transform(new Vector3(1f, 0f, 0f), node.WorldMatrix);
        p.X.Should().BeApproximately(0f, 1e-4f);
        p.Z.Should().BeApproximately(-1f, 1e-4f);
    }

    [Fact]
    public void Scale_SetsUniformLocalScale()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode().Scale(2.5f);
        node.LocalScale.Should().Be(new Vector3(2.5f, 2.5f, 2.5f));
    }

    [Fact]
    public void LookAt_OrientsForwardTowardTarget()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode().LookAt(new Vector3(5f, 0f, 0f));

        // The node's local +Z (forward) should now point toward +X.
        var forward = Vector3.TransformNormal(new Vector3(0f, 0f, 1f), node.WorldMatrix);
        forward.X.Should().BeApproximately(1f, 1e-4f);
        forward.Y.Should().BeApproximately(0f, 1e-4f);
        forward.Z.Should().BeApproximately(0f, 1e-4f);
    }

    [Fact]
    public void FluentTransforms_ReturnTheSameNode()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();
        node.At(Vector3.One).RotateY(0.3f).Scale(1.2f).Should().BeSameAs(node);
    }

    // ---------------------------------------------------------------- renderer proxies with no renderer

    [Fact]
    public void Tint_WithNoRenderer_ReadsWhiteAndSetIsNoOp()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();

        node.HasRenderer.Should().BeFalse();
        node.Tint.Should().Be(new Vector4(1f, 1f, 1f, 1f));

        var act = () => node.Tint = new Vector4(2f, 2f, 2f, 1f);
        act.Should().NotThrow("setting Tint with no renderer is a logged no-op, never a crash");
        node.Tint.Should().Be(new Vector4(1f, 1f, 1f, 1f));
    }
}
