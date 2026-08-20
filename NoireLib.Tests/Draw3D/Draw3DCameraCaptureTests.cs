using FluentAssertions;
using NoireLib.Draw3D.Core;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the pure logic of the camera-constant capture: the window matcher (including the rule that the Z column is
/// excluded, because the game's uploaded Z column legitimately differs from the struct-composed one), the layout
/// extraction, the commit validation gate, and the commit-freshness rules the two composite paths rely on.
/// </summary>
public class Draw3DCameraCaptureTests
{
    /// <summary>A realistic perspective view-projection in the row-vector convention the renderer uses.</summary>
    private static Matrix4x4 MakeViewProj(Vector3 eye, Vector3 target)
    {
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        return view * proj;
    }

    /// <summary>The same matrix with a replaced Z column, representing what the game's uploaded VP looks like next to the struct's.</summary>
    private static Matrix4x4 WithForeignZColumn(Matrix4x4 m)
    {
        m.M13 = 0f;
        m.M23 = 0f;
        m.M33 = 0f;
        m.M43 = 0.1f;
        return m;
    }

    private static float[] ToFloats(in Matrix4x4 m, bool transposed)
    {
        var source = transposed ? Matrix4x4.Transpose(m) : m;
        return new[]
        {
            source.M11, source.M12, source.M13, source.M14,
            source.M21, source.M22, source.M23, source.M24,
            source.M31, source.M32, source.M33, source.M34,
            source.M41, source.M42, source.M43, source.M44,
        };
    }

    [Fact]
    public void WindowError_MatchesPlantedVp_DespiteForeignZColumn()
    {
        var refVp = MakeViewProj(new Vector3(120f, 35f, -240f), new Vector3(118f, 33f, -238f));
        var uploaded = WithForeignZColumn(refVp);

        var direct = ToFloats(in uploaded, transposed: false);
        CameraConstantCapture.WindowError(direct, in refVp, transposed: false, skipZColumn: true)
            .Should().BeLessThan(1e-6f, "the X/Y/W columns are identical and the divergent Z column is excluded");

        CameraConstantCapture.WindowError(direct, in refVp, transposed: false, skipZColumn: false)
            .Should().BeGreaterThan(1e-3f, "with the Z column included the divergence must be visible");
    }

    [Fact]
    public void WindowError_ResolvesLayout()
    {
        var refVp = MakeViewProj(new Vector3(5f, 2f, 9f), new Vector3(0f, 1f, 0f));
        var transposedWindow = ToFloats(in refVp, transposed: true);

        CameraConstantCapture.WindowError(transposedWindow, in refVp, transposed: true, skipZColumn: true)
            .Should().BeLessThan(1e-6f);
        CameraConstantCapture.WindowError(transposedWindow, in refVp, transposed: false, skipZColumn: true)
            .Should().BeGreaterThan(1e-2f, "a perspective VP is far from symmetric, so the wrong layout must not match");
    }

    [Fact]
    public void WindowError_NonFiniteWindow_IsNaN()
    {
        var refVp = MakeViewProj(Vector3.UnitZ * 10f, Vector3.Zero);
        var window = ToFloats(in refVp, transposed: false);
        window[5] = float.NaN;

        CameraConstantCapture.WindowError(window, in refVp, transposed: false, skipZColumn: true)
            .Should().Be(float.NaN);
    }

    [Fact]
    public void ExtractMatrix_RoundTripsBothLayouts()
    {
        var vp = MakeViewProj(new Vector3(-40f, 12f, 77f), new Vector3(-39f, 12f, 76f));

        CameraConstantCapture.ExtractMatrix(ToFloats(in vp, transposed: false), transposed: false)
            .Should().Be(vp);
        CameraConstantCapture.ExtractMatrix(ToFloats(in vp, transposed: true), transposed: true)
            .Should().Be(vp);
    }

    [Fact]
    public void MatrixError_AcceptsTemporalSkew_RejectsForeignCamera()
    {
        var eye = new Vector3(200f, 40f, -100f);
        var refVp = MakeViewProj(eye, eye + new Vector3(0.6f, -0.2f, 0.9f));

        // A fast camera one sim-tick ahead: a few centimeters of travel and a fraction of a degree of turn.
        var skewed = MakeViewProj(eye + new Vector3(0.05f, 0f, 0.07f), eye + new Vector3(0.605f, -0.199f, 0.975f));
        CameraConstantCapture.MatrixError(in skewed, in refVp, skipZColumn: true)
            .Should().BeLessThan(0.05f, "the validation gate must tolerate the sim-vs-render skew being fixed");

        // A different view entirely (a portrait/offscreen camera).
        var foreign = MakeViewProj(new Vector3(0f, 1.6f, 2.2f), new Vector3(0f, 1.4f, 0f));
        CameraConstantCapture.MatrixError(in foreign, in refVp, skipZColumn: true)
            .Should().BeGreaterThan(0.05f, "a foreign view's constants must fail validation and never be committed");
    }

    [Fact]
    public void IsCommitFresh_MatchesThePathTiming()
    {
        // The inject path runs before its frame's present boundary: the commit carries the current index.
        CameraConstantCapture.IsCommitFresh(commitIndex: 7, presentIndex: 7, presentTimePath: false).Should().BeTrue();
        CameraConstantCapture.IsCommitFresh(commitIndex: 6, presentIndex: 7, presentTimePath: false).Should().BeFalse();

        // The present-time path runs after the boundary advanced: the commit carries the previous index.
        CameraConstantCapture.IsCommitFresh(commitIndex: 7, presentIndex: 8, presentTimePath: true).Should().BeTrue();
        CameraConstantCapture.IsCommitFresh(commitIndex: 7, presentIndex: 9, presentTimePath: true).Should().BeFalse();

        // No commit yet.
        CameraConstantCapture.IsCommitFresh(commitIndex: -1, presentIndex: 0, presentTimePath: false).Should().BeFalse();
    }

    // ---------------------------------------------------------------- extrapolated commit (miss frames)

    [Fact]
    public void ExtrapolateCamera_ConstantTranslation_PredictsTheNextStepExactly()
    {
        // A camera panning at constant velocity: the prediction one present ahead is the true next matrix,
        // because view-projection is linear in the eye translation for a fixed orientation.
        var prev = MakeViewProj(new Vector3(0f, 5f, 0f), new Vector3(0f, 5f, 10f));
        var last = MakeViewProj(new Vector3(1f, 5f, 0f), new Vector3(1f, 5f, 10f));
        var truth = MakeViewProj(new Vector3(2f, 5f, 0f), new Vector3(2f, 5f, 10f));

        var pred = CameraConstantCapture.ExtrapolateCamera(in prev, in last, lastGap: 1, gap: 1);

        CameraConstantCapture.MatrixError(in pred, in truth, skipZColumn: false)
            .Should().BeLessThan(1e-5f);
    }

    [Fact]
    public void ExtrapolateCamera_ScalesTheStepToTheGaps()
    {
        // Two presents to bridge from a one-present velocity sample: twice the step.
        var prev = MakeViewProj(new Vector3(0f, 5f, 0f), new Vector3(0f, 5f, 10f));
        var last = MakeViewProj(new Vector3(1f, 5f, 0f), new Vector3(1f, 5f, 10f));
        var truth = MakeViewProj(new Vector3(3f, 5f, 0f), new Vector3(3f, 5f, 10f));

        var pred = CameraConstantCapture.ExtrapolateCamera(in prev, in last, lastGap: 1, gap: 2);

        CameraConstantCapture.MatrixError(in pred, in truth, skipZColumn: false)
            .Should().BeLessThan(1e-5f);

        // And a two-present velocity sample bridging one present: half the step.
        var half = CameraConstantCapture.ExtrapolateCamera(in prev, in last, lastGap: 2, gap: 1);
        var truthHalf = MakeViewProj(new Vector3(1.5f, 5f, 0f), new Vector3(1.5f, 5f, 10f));
        CameraConstantCapture.MatrixError(in half, in truthHalf, skipZColumn: false)
            .Should().BeLessThan(1e-5f);
    }

    [Fact]
    public void ExtrapolateCamera_FrameSizedRotation_StaysInsideTheValidationGate()
    {
        // Rotation extrapolates only to first order, so the prediction is approximate; what matters is that a
        // frame-sized turn predicts well inside the commit validation gate while a camera cut lands far outside,
        // so the gate keeps cuts on the struct fallback.
        var eye = new Vector3(100f, 20f, -50f);
        var prev = MakeViewProj(eye, eye + Direction(0.00f));
        var last = MakeViewProj(eye, eye + Direction(0.02f));
        var truth = MakeViewProj(eye, eye + Direction(0.04f));

        var pred = CameraConstantCapture.ExtrapolateCamera(in prev, in last, lastGap: 1, gap: 1);
        CameraConstantCapture.MatrixError(in pred, in truth, skipZColumn: true)
            .Should().BeLessThan(0.05f, "a frame of turn must validate, or the prediction never engages");

        var cut = MakeViewProj(new Vector3(-300f, 8f, 400f), new Vector3(-300f, 8f, 410f));
        CameraConstantCapture.MatrixError(in pred, in cut, skipZColumn: true)
            .Should().BeGreaterThan(0.05f, "a camera cut must fail validation so the prediction is never used across one");
    }

    private static Vector3 Direction(float yaw) => new(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
}
