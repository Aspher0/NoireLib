using FluentAssertions;
using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Geometry;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks <see cref="Draw3DImportFlips"/>, the handedness options both model loaders run.<br/>
/// The mesh half and the transform half have to agree: a hierarchical model mirrored on its vertices alone
/// comes out with each part flipped in place and the arrangement untouched, which reads as a rigged model
/// falling apart while a flat prop looks settled. These check the two stay in step and that the default path
/// changes nothing at all.
/// </summary>
public class Draw3DImportFlipTests
{
    private static Vertex3D[] OneVertex(Vector3 position, Vector2 uv)
        => [new Vertex3D(position, Vector3.UnitY, uv, Vector4.One)];

    [Fact]
    public void Default_ChangesNothing()
    {
        var flips = new Draw3DImportFlips();
        var vertices = OneVertex(new Vector3(1f, 2f, 3f), new Vector2(0.25f, 0.75f));
        ushort[] indices = [0, 1, 2];

        flips.Any.Should().BeFalse();
        flips.Apply(vertices, indices);

        vertices[0].Position.Should().Be(new Vector3(1f, 2f, 3f));
        indices.Should().Equal((ushort)0, (ushort)1, (ushort)2);
        flips.Apply(Matrix4x4.Identity).Should().Be(Matrix4x4.Identity);
    }

    [Fact]
    public void MirrorZ_NegatesZOnPositionsAndNormals()
    {
        var flips = new Draw3DImportFlips { MirrorZ = true };
        var vertices = OneVertex(new Vector3(1f, 2f, 3f), Vector2.Zero);

        flips.Apply(vertices, System.Array.Empty<ushort>());

        vertices[0].Position.Should().Be(new Vector3(1f, 2f, -3f));
        vertices[0].Normal.Should().Be(new Vector3(0f, 1f, -0f));
    }

    [Fact]
    public void ReverseWinding_SwapsTheSecondAndThirdIndexOfEveryTriangle()
    {
        var flips = new Draw3DImportFlips { ReverseWinding = true };
        ushort[] indices = [0, 1, 2, 3, 4, 5];

        flips.Apply(System.Array.Empty<Vertex3D>(), indices);

        indices.Should().Equal((ushort)0, (ushort)2, (ushort)1, (ushort)3, (ushort)5, (ushort)4);
    }

    [Fact]
    public void MirrorZ_MovesATranslationTheSameWayItMovesAVertex()
    {
        // The whole point of applying this to transforms as well: a part's placement and the part's own
        // geometry have to move together, or a model comes apart into correctly-mirrored pieces in the wrong
        // places.
        var flips = new Draw3DImportFlips { MirrorZ = true };
        var vertices = OneVertex(new Vector3(4f, 5f, 6f), Vector2.Zero);
        flips.Apply(vertices, System.Array.Empty<ushort>());

        var moved = flips.Apply(Matrix4x4.CreateTranslation(4f, 5f, 6f));

        moved.Translation.Should().Be(vertices[0].Position);
    }

    [Fact]
    public void Conjugation_PreservesDeterminant()
    {
        // Points are transformed by the mirror and matrices are conjugated by it, which is a change of basis
        // rather than a second reflection - so the transform half never adds a flip of its own. If this ever
        // reads -1, the two halves have stopped describing the same operation and a hierarchical model will
        // come apart.
        var source = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(1f, 2f, 3f);

        new Draw3DImportFlips { MirrorZ = true }.Apply(source).GetDeterminant()
            .Should().BeApproximately(source.GetDeterminant(), 1e-5f);
    }

    [Fact]
    public void MirrorZ_ReversesARotationAboutY()
    {
        // The readable consequence of the conjugation: mirroring the world reverses the sense of a turn in it.
        var turned = new Draw3DImportFlips { MirrorZ = true }.Apply(Matrix4x4.CreateRotationY(0.7f));
        var expected = Matrix4x4.CreateRotationY(-0.7f);

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
                turned[row, column].Should().BeApproximately(expected[row, column], 1e-5f);
        }
    }

    [Fact]
    public void Reset_TurnsEverythingOff()
    {
        var flips = new Draw3DImportFlips
        {
            MirrorX = true,
            MirrorZ = true,
            ReverseWinding = true,
            FlipU = true,
            FlipV = true,
        };

        flips.Reset();
        flips.Any.Should().BeFalse();
    }
}
