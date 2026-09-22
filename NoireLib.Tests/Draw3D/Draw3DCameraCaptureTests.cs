using FluentAssertions;
using NoireLib.Draw3D.Core;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the pure logic of the camera-constant capture: the window matcher, the layout extraction, view-shape
/// identity, jitter removal, the half-pixel offset and the commit-freshness rules the composite paths rely on.
/// </summary>
public class Draw3DCameraCaptureTests
{
    /// <summary>A perspective view-projection in the row-vector convention.</summary>
    private static Matrix4x4 MakeViewProj(Vector3 eye, Vector3 target)
    {
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        return view * proj;
    }

    /// <summary>The same matrix with a replaced Z column, like the game's uploaded VP.</summary>
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
            .Should().BeGreaterThan(1e-2f, "the wrong layout must not match a far-from-symmetric perspective VP");
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
    public void MatrixError_IgnoresTemporalJitter_RejectsForeignCamera()
    {
        var eye = new Vector3(200f, 40f, -100f);
        var view = Matrix4x4.CreateLookAt(eye, eye + new Vector3(0.6f, -0.2f, 0.9f), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        var jittered = proj;
        jittered.M31 = 0.5f / 1920f;
        jittered.M32 = -0.5f / 1080f;

        CameraConstantCapture.MatrixError(view * jittered, view * proj, skipZColumn: true)
            .Should().BeLessThan(1e-5f, "the drawn camera carries jitter the CPU camera does not, and must still rank first");

        var foreign = MakeViewProj(new Vector3(0f, 1.6f, 2.2f), new Vector3(0f, 1.4f, 0f));
        CameraConstantCapture.MatrixError(foreign, view * proj, skipZColumn: true)
            .Should().BeGreaterThan(0.05f);
    }

    [Fact]
    public void ApplyPixelOffset_ShiftsEveryPointByTheSamePixels()
    {
        var vp = MakeViewProj(new Vector3(10f, 5f, 10f), Vector3.Zero);
        var shifted = CameraConstantCapture.ApplyPixelOffset(in vp, new Vector2(0.5f, 0.5f), new Vector2(1920f, 1080f));

        foreach (var p in new[] { Vector3.Zero, new Vector3(3f, 1f, -2f), new Vector3(-6f, 0f, 4f) })
        {
            var a = Vector4.Transform(new Vector4(p, 1f), vp);
            var b = Vector4.Transform(new Vector4(p, 1f), shifted);
            var dxPixels = ((b.X / b.W) - (a.X / a.W)) * 0.5f * 1920f;
            var dyPixels = -((b.Y / b.W) - (a.Y / a.W)) * 0.5f * 1080f;
            dxPixels.Should().BeApproximately(0.5f, 1e-3f, "right by half a pixel");
            dyPixels.Should().BeApproximately(0.5f, 1e-3f, "down by half a pixel");
            b.W.Should().BeApproximately(a.W, 1e-4f, "depth is untouched");
        }
    }

    [Fact]
    public void IsCommitFresh_MatchesThePathTiming()
    {
        // The inject path runs before its present boundary.
        CameraConstantCapture.IsCommitFresh(commitIndex: 7, presentIndex: 7, presentTimePath: false).Should().BeTrue();
        CameraConstantCapture.IsCommitFresh(commitIndex: 6, presentIndex: 7, presentTimePath: false).Should().BeFalse();

        // The present-time path runs after the boundary advanced.
        CameraConstantCapture.IsCommitFresh(commitIndex: 7, presentIndex: 8, presentTimePath: true).Should().BeTrue();
        CameraConstantCapture.IsCommitFresh(commitIndex: 7, presentIndex: 9, presentTimePath: true).Should().BeFalse();

        CameraConstantCapture.IsCommitFresh(commitIndex: -1, presentIndex: 0, presentTimePath: false).Should().BeFalse();
    }

    private static CameraConstantCapture.ViewShape Shape(in Matrix4x4 m)
    {
        CameraConstantCapture.TryReadViewShape(in m, out var shape).Should().BeTrue();
        return shape;
    }

    [Fact]
    public void ViewShape_IsTheSameForTheSameCameraAnywhere_WhateverTheMotion()
    {
        // Two poses farther apart than any frame of motion.
        var reference = MakeViewProj(new Vector3(120f, 35f, -240f), new Vector3(118f, 33f, -238f));
        var elsewhere = WithForeignZColumn(MakeViewProj(new Vector3(-400f, 2f, 90f), new Vector3(-400f, 50f, 91f)));

        CameraConstantCapture.CompareViewShape(Shape(in elsewhere), Shape(in reference))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.None, "shape says which view this is, never where it is");
    }

    [Fact]
    public void ViewShape_RecoversTheEye()
    {
        var eye = new Vector3(120f, 35f, -240f);
        var shape = Shape(MakeViewProj(eye, new Vector3(118f, 33f, -238f)));

        Vector3.Distance(shape.Eye, eye).Should().BeLessThan(1e-2f);
    }

    [Fact]
    public void ViewShape_ToleratesTemporalJitter()
    {
        var eye = new Vector3(10f, 5f, 10f);
        var target = new Vector3(0f, 0f, 0f);
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        var jittered = proj;
        jittered.M31 = 2f / 2560f; // half a pixel at 1440p width
        jittered.M32 = 2f / 1440f;

        CameraConstantCapture.CompareViewShape(Shape(view * jittered), Shape(view * proj))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.None);
    }

    [Fact]
    public void RemoveTemporalJitter_RecoversTheCentredCamera()
    {
        var view = Matrix4x4.CreateLookAt(new Vector3(120f, 35f, -240f), new Vector3(118f, 33f, -238f), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        var jittered = proj;
        jittered.M31 = 0.37f / 1920f;
        jittered.M32 = -0.21f / 1080f;

        var centred = CameraConstantCapture.RemoveTemporalJitter(view * jittered, out var jitter);
        var expected = view * proj;

        foreach (var (a, b) in new[] { (centred.M11, expected.M11), (centred.M21, expected.M21), (centred.M31, expected.M31), (centred.M41, expected.M41),
                                       (centred.M12, expected.M12), (centred.M22, expected.M22), (centred.M32, expected.M32), (centred.M42, expected.M42) })
            a.Should().BeApproximately(b, 1e-4f);

        MathF.Abs(jitter.X).Should().BeApproximately(0.37f / 1920f, 1e-6f);
        MathF.Abs(jitter.Y).Should().BeApproximately(0.21f / 1080f, 1e-6f);
    }

    [Fact]
    public void RemoveTemporalJitter_LeavesACentredCameraAlone()
    {
        var vp = MakeViewProj(new Vector3(10f, 5f, 10f), Vector3.Zero);
        var result = CameraConstantCapture.RemoveTemporalJitter(in vp, out var jitter);

        jitter.Length().Should().BeLessThan(1e-6f);
        result.M41.Should().BeApproximately(vp.M41, 1e-5f);
        result.M11.Should().BeApproximately(vp.M11, 1e-6f);
    }

    [Fact]
    public void IsCameraScaled_AcceptsRealCameras()
    {
        var view = Matrix4x4.CreateLookAt(new Vector3(4800f, 120f, -5100f), new Vector3(4790f, 118f, -5095f), Vector3.UnitY);
        var jittered = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        jittered.M31 = 0.5f * 2f / 1920f;
        jittered.M32 = -0.5f * 2f / 1080f;
        var telephoto = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 90f, 16f / 9f, 0.1f, 1000f);
        var shadow = Matrix4x4.CreateLookAt(new Vector3(50f, 100f, 20f), Vector3.Zero, Vector3.UnitY) * Matrix4x4.CreateOrthographic(80f, 80f, 1f, 500f);

        CameraConstantCapture.IsCameraScaled(MakeViewProj(new Vector3(10f, 5f, 10f), Vector3.Zero)).Should().BeTrue();
        CameraConstantCapture.IsCameraScaled(view * jittered).Should().BeTrue("half a pixel of jitter far from the origin");
        CameraConstantCapture.IsCameraScaled(view * telephoto).Should().BeTrue("a two degree field of view");
        CameraConstantCapture.IsCameraScaled(in shadow).Should().BeTrue("orthographic views are left to the shape check");
    }

    [Fact]
    public void IsCameraScaled_RefusesTheGarbageUploadsSeenInGame()
    {
        // The in-game uploads read back as a jitter of 1e20 to 1e23 and focal terms of 1e24.
        var reference = MakeViewProj(new Vector3(10f, 5f, 10f), Vector3.Zero);
        var garbage = reference;
        garbage.M11 += 3e22f * reference.M14;
        garbage.M21 += 3e22f * reference.M24;
        garbage.M31 += 3e22f * reference.M34;
        CameraConstantCapture.IsCameraScaled(in garbage).Should().BeFalse();

        var largeJitter = reference;
        largeJitter.M11 += 0.2f * reference.M14;
        largeJitter.M21 += 0.2f * reference.M24;
        largeJitter.M31 += 0.2f * reference.M34;
        CameraConstantCapture.IsCameraScaled(in largeJitter).Should().BeFalse("a fifth of the screen is no jitter");

        var huge = reference;
        huge.M11 = -6e24f;
        CameraConstantCapture.IsCameraScaled(in huge).Should().BeFalse();

        var notFinite = reference;
        notFinite.M42 = float.NaN;
        CameraConstantCapture.IsCameraScaled(in notFinite).Should().BeFalse();
    }

    [Fact]
    public void ViewShape_RejectsAnOrthographicShadowView()
    {
        var reference = MakeViewProj(new Vector3(10f, 5f, 10f), Vector3.Zero);
        var light = Matrix4x4.CreateLookAt(new Vector3(50f, 100f, 20f), Vector3.Zero, Vector3.UnitY)
                    * Matrix4x4.CreateOrthographic(80f, 80f, 1f, 500f);

        CameraConstantCapture.CompareViewShape(Shape(in light), Shape(in reference))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.Projection);
    }

    [Fact]
    public void ViewShape_RejectsAnotherAspect_AcceptsAnotherFieldOfView()
    {
        var eye = new Vector3(10f, 5f, 10f);
        var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        var reference = view * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        var portrait = view * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 3f / 4f, 0.1f, 1000f);
        var probe = view * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 1000f);
        var groupPoseWide = view * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI * 0.8f, 16f / 9f, 0.1f, 1000f);
        var groupPoseNarrow = view * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 12f, 16f / 9f, 0.1f, 1000f);

        CameraConstantCapture.CompareViewShape(Shape(in portrait), Shape(in reference))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.AspectRatio);
        CameraConstantCapture.CompareViewShape(Shape(in probe), Shape(in reference))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.AspectRatio);
        CameraConstantCapture.CompareViewShape(Shape(in groupPoseWide), Shape(in reference))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.None, "the field of view is the player's to change");
        CameraConstantCapture.CompareViewShape(Shape(in groupPoseNarrow), Shape(in reference))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.None, "the field of view is the player's to change");
    }

    [Fact]
    public void ViewShape_RejectsAReflectedView()
    {
        var view = Matrix4x4.CreateLookAt(new Vector3(10f, 5f, 10f), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 1000f);
        var mirror = Matrix4x4.CreateReflection(new Plane(Vector3.UnitY, 0f));

        CameraConstantCapture.CompareViewShape(Shape(mirror * view * proj), Shape(view * proj))
            .Should().Be(CameraConstantCapture.ViewShapeMismatch.Handedness);
    }
}
