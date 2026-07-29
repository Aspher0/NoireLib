using FluentAssertions;
using NoireLib.Helpers;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the rotation helpers, including the two cases that silently corrupt a transform when they are missed: an up
/// hint parallel to the forward direction, and a matrix <see cref="Matrix4x4.Decompose"/> refuses.
/// </summary>
public class TransformHelperTests
{
    [Fact]
    public void LookRotation_AimsPositiveZAlongForward()
    {
        var rot = TransformHelper.LookRotation(new Vector3(0, 0, 5), Vector3.UnitY);
        var aimed = Vector3.Transform(Vector3.UnitZ, rot);

        Vector3.Distance(aimed, Vector3.UnitZ).Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void LookRotation_UpParallelToForward_StillProducesAUsableRotation()
    {
        var rot = TransformHelper.LookRotation(Vector3.UnitY, Vector3.UnitY);
        var aimed = Vector3.Transform(Vector3.UnitZ, rot);

        Vector3.Distance(aimed, Vector3.UnitY).Should().BeLessThan(1e-4f, "the substitute up keeps the aim correct when the hint is degenerate");
        aimed.Length().Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void FromToRotation_SameDirection_IsIdentity()
        => TransformHelper.FromToRotation(Vector3.UnitY, new Vector3(0, 3, 0)).Should().Be(Quaternion.Identity);

    [Fact]
    public void FromToRotation_OpposedDirections_StillTurnsOneIntoTheOther()
    {
        var rot = TransformHelper.FromToRotation(Vector3.UnitY, -Vector3.UnitY);
        var aimed = Vector3.Transform(Vector3.UnitY, rot);

        Vector3.Distance(aimed, -Vector3.UnitY).Should().BeLessThan(1e-4f, "the axis is undetermined here, but the result must still be the half turn");
    }

    [Fact]
    public void FromToRotation_GeneralCase_TurnsOneDirectionIntoTheOther()
    {
        var to = Vector3.Normalize(new Vector3(1, 1, 0));

        var aimed = Vector3.Transform(Vector3.UnitY, TransformHelper.FromToRotation(Vector3.UnitY, to));

        Vector3.Distance(aimed, to).Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void FromToRotation_ZeroLengthInput_FallsBackWithoutProducingNaN()
    {
        var rot = TransformHelper.FromToRotation(Vector3.Zero, Vector3.UnitZ);
        var aimed = Vector3.Transform(Vector3.UnitY, rot);

        float.IsNaN(aimed.X).Should().BeFalse();
        aimed.Length().Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void DecomposeSafe_MatrixDecomposeRefuses_ReturnsIdentityRatherThanWhateverWasLeftBehind()
    {
        // Two identical basis rows: the matrix has no orthonormal basis to recover, which is what Decompose refuses.
        var rankDeficient = new Matrix4x4(1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 2, 3, 1);

        Matrix4x4.Decompose(rankDeficient, out _, out _, out _)
            .Should().BeFalse("this test is only meaningful while the framework still refuses this matrix");

        TransformHelper.DecomposeSafe(in rankDeficient, out var scale, out var rotation, out var translation);

        scale.Should().Be(Vector3.One);
        rotation.Should().Be(Quaternion.Identity);
        translation.Should().Be(new Vector3(1, 2, 3), "the translation is readable straight off the matrix even when the rest is not");
    }

    [Fact]
    public void DecomposeSafe_OrdinaryTransform_MatchesTheFrameworkResult()
    {
        var m = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateRotationY(MathHelper.HalfPi) * Matrix4x4.CreateTranslation(4, 5, 6);

        TransformHelper.DecomposeSafe(in m, out var scale, out var rotation, out var translation);

        scale.X.Should().BeApproximately(2f, 1e-4f);
        translation.Should().Be(new Vector3(4, 5, 6));
        Vector3.Distance(Vector3.Transform(Vector3.UnitZ, rotation), Vector3.UnitX).Should().BeLessThan(1e-4f);
    }
}
