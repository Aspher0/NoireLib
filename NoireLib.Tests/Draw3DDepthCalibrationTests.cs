using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Materials;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the depth self-calibration math (the fit that replaces every "which projection/near plane
/// is real" assumption) and the depth-aware nameplate policy semantics (factors are UI visibility:
/// 1 = letters on top, behindFactor = covered by nearer 3D content).
/// </summary>
public class Draw3DDepthCalibrationTests
{
    // ---------------------------------------------------------------- fit: sample = a + b/w

    [Fact]
    public void TrySolve_ReversedZInfiniteFar_RecoversNearPlane()
    {
        // z = near/w with near = 0.1 — the expected FFXIV convention.
        var (xs, ys) = Synthesize(w => 0.1f / w);

        DepthCalibration.TrySolve(xs, ys, out var a, out var b, out var medianResidual, out var inliers).Should().BeTrue();
        a.Should().BeApproximately(0f, 1e-5f);
        b.Should().BeApproximately(0.1f, 1e-5f);
        medianResidual.Should().BeLessThan(1e-6f);
        inliers.Should().Be(xs.Length);
    }

    [Fact]
    public void TrySolve_StandardZFiniteFar_RecoversBothConstants()
    {
        // z = far/(far-near) - far*near/((far-near)·w), near 0.1, far 100.
        const float a0 = 100f / (100f - 0.1f);
        const float b0 = -100f * 0.1f / (100f - 0.1f);
        var (xs, ys) = Synthesize(w => a0 + b0 / w);

        DepthCalibration.TrySolve(xs, ys, out var a, out var b, out _, out _).Should().BeTrue();
        a.Should().BeApproximately(a0, 1e-4f);
        b.Should().BeApproximately(b0, 1e-4f);
        b.Should().BeNegative("standard-Z slope is negative — the fit must carry the direction");
    }

    [Fact]
    public void TrySolve_OutliersFromInvisibleWalls_AreRejected()
    {
        // Collision raycasts can hit invisible barriers the depth buffer never saw — the robust
        // refit must shrug those off.
        var (xs, ys) = Synthesize(w => 0.1f / w);
        ys[3] = 0.9f;  // wildly wrong
        ys[11] = 0.5f;

        DepthCalibration.TrySolve(xs, ys, out var a, out var b, out var medianResidual, out var inliers).Should().BeTrue();
        a.Should().BeApproximately(0f, 1e-4f);
        b.Should().BeApproximately(0.1f, 1e-4f);
        inliers.Should().Be(xs.Length - 2);
        medianResidual.Should().BeLessThan(1e-5f);
    }

    [Fact]
    public void TrySolve_DegenerateFlatWall_IsRejected()
    {
        // All samples at the same distance — the 2x2 system is singular.
        var xs = new float[10];
        var ys = new float[10];
        for (var i = 0; i < 10; i++)
        {
            xs[i] = 1f / 25f;
            ys[i] = 0.1f / 25f;
        }

        DepthCalibration.TrySolve(xs, ys, out _, out _, out _, out _).Should().BeFalse();
    }

    // ---------------------------------------------------------------- attempt throttle (overflow-safety)

    [Fact]
    public void AttemptDue_NeverAttempted_IsDueRegardlessOfFrameId()
    {
        // The long.MinValue "never attempted" sentinel must always be due — the original bug subtracted
        // it (frameId - long.MinValue overflows negative), which wedged calibration off forever and left
        // every 3D shape drawing on top of all world geometry.
        DepthCalibration.AttemptDue(0, long.MinValue, 30).Should().BeTrue();
        DepthCalibration.AttemptDue(5000, long.MinValue, 30).Should().BeTrue();
        DepthCalibration.AttemptDue(long.MaxValue, long.MinValue, 30).Should().BeTrue();
    }

    [Fact]
    public void AttemptDue_ThrottlesWithinIntervalThenAllows()
    {
        DepthCalibration.AttemptDue(100, 90, 30).Should().BeFalse();  // 10 frames since last — throttled
        DepthCalibration.AttemptDue(119, 90, 30).Should().BeFalse();  // 29 frames — still throttled
        DepthCalibration.AttemptDue(120, 90, 30).Should().BeTrue();   // 30 frames — due
        DepthCalibration.AttemptDue(500, 90, 30).Should().BeTrue();
    }

    private static (float[] xs, float[] ys) Synthesize(Func<float, float> mapping)
    {
        var xs = new float[16];
        var ys = new float[16];
        for (var i = 0; i < 16; i++)
        {
            var w = 2f + i * 4.5f; // 2 .. 69.5 world units
            xs[i] = 1f / w;
            ys[i] = mapping(w);
        }

        return (xs, ys);
    }

    // ---------------------------------------------------------------- nameplate policy factors

    [Fact]
    public void ComputeRectOcclusion_PlateInFront_KeepsLettersOnTop_PlateBehind_GetsCovered()
    {
        // Reversed-Z infinite-far row-vector projection, FoV 90°, aspect 1, camera at origin +Z.
        var proj = new Matrix4x4(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 0, 1,
            0, 0, 0.1f, 0);
        Matrix4x4.Invert(proj, out var inv).Should().BeTrue();
        var frame = new FrameContext(
            proj, inv, Matrix4x4.Identity, proj,
            Vector3.Zero, 0f, new Vector2(1000f, 1000f), Vector2.One,
            reversedZ: true, nearPlane: 0.1f, hasDepth: true, usedFallbackCamera: false, frameId: 1);

        var pass = new ScenePass();
        pass.BeginCollect(in frame, mainPass: true);

        MaterialData.TryFrom(Material.Unlit(Vector4.One), out var mat).Should().BeTrue();
        var stats = new RenderStats();

        // One item dead-center at 10 world units, radius 1 (occupies [9, 11]).
        pass.AddDynamicItem(0, 3, in mat, Vector4.One, Matrix4x4.Identity, layer: 0,
            center: new Vector3(0f, 0f, 10f), radius: 1f, stats, depthAvailable: true);

        var rects = new[]
        {
            new Vector4(0.4f, 0.4f, 0.6f, 0.6f), // covers screen center — overlaps the item
            new Vector4(0.4f, 0.4f, 0.6f, 0.6f), // same rect, plate farther away
            new Vector4(0.9f, 0.9f, 0.95f, 0.95f), // corner — no overlap
        };
        var distances = new[] { 5f, 20f, 20f };
        var factors = new float[3];

        pass.ComputeRectOcclusion(in frame, rects, distances, factors, 3, behindFactor: 0.25f);

        factors[0].Should().Be(1f, "the plate is in front of the item — its letters keep reading on top");
        factors[1].Should().Be(0.25f, "the plate is behind the item's farthest surface — the shape covers it");
        factors[2].Should().Be(1f, "nothing overlaps this rect — the plate reads on top by default");
    }
}
