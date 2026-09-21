using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the pure interaction/gizmo math: ray solvers, screen-constant sizing, the drag solvers each handle relies on,
/// snapping, and the selection model. All headless, with no renderer and no ImGui.
/// </summary>
public class Draw3DInteractionMathTests
{
    /// <summary>A reversed-Z infinite-far projection: FoV 90 degrees, aspect 1, near 0.1.</summary>
    private static readonly Matrix4x4 Proj = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0, 1,
        0, 0, 0.1f, 0);

    private static FrameContext Frame()
    {
        Matrix4x4.Invert(Proj, out var inv).Should().BeTrue();
        return new FrameContext(
            Proj, inv, Matrix4x4.Identity, Proj,
            Vector3.Zero, 0f, new Vector2(1000f, 1000f), Vector2.One,
            reversedZ: true, nearPlane: 0.1f, hasDepth: true, usedFallbackCamera: false, frameId: 1);
    }

    [Fact]
    public void RayPlane_HitsExpectedPoint()
    {
        Geometry3DHelper.RayPlane(new Vector3(0, 5, 0), new Vector3(0, -1, 0), Vector3.Zero, Vector3.UnitY, out var t, out var hit)
            .Should().BeTrue();
        t.Should().BeApproximately(5f, 1e-4f);
        hit.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void RayPlane_ParallelIsRejected()
    {
        Geometry3DHelper.RayPlane(new Vector3(0, 5, 0), new Vector3(1, 0, 0), Vector3.Zero, Vector3.UnitY, out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ClosestAxisParam_ProjectsRayOntoAxis()
    {
        Geometry3DHelper.ClosestAxisParam(new Vector3(3, 4, 0), new Vector3(0, -1, 0), Vector3.Zero, Vector3.UnitX, out var s)
            .Should().BeTrue();
        s.Should().BeApproximately(3f, 1e-4f);
    }

    [Fact]
    public void RaySegmentDistance_MeasuresPerpendicularGap()
    {
        var d = Geometry3DHelper.RaySegmentDistance(new Vector3(0, 2, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 0), new Vector3(5, 0, 0), out _);
        d.Should().BeApproximately(2f, 1e-4f);
    }

    [Fact]
    public void RayRing_HitsBandNotCenter()
    {
        // Radius 2 in the XZ plane. The rim hits, the center misses.
        Geometry3DHelper.RayRing(new Vector3(2, 5, 0), new Vector3(0, -1, 0), Vector3.Zero, Vector3.UnitY, 2f, 0.2f, out _).Should().BeTrue();
        Geometry3DHelper.RayRing(new Vector3(0, 5, 0), new Vector3(0, -1, 0), Vector3.Zero, Vector3.UnitY, 2f, 0.2f, out _).Should().BeFalse();
    }

    [Fact]
    public void SignedAngleOnPlane_MatchesHandTurn()
    {
        // -90 degrees: the right-handed cross points to -Y.
        var angle = Geometry3DHelper.SignedAngleOnPlane(Vector3.Zero, Vector3.UnitY, new Vector3(1, 0, 0), new Vector3(0, 0, 1));
        angle.Should().BeApproximately(-MathF.PI / 2f, 1e-4f);
    }

    [Fact]
    public void TryWorldPerPixel_MatchesProjectionScale()
    {
        // At z = 10 the 90 degree frustum is 20 units across 1000 px.
        Frame().TryWorldPerPixel(new Vector3(0, 0, 10), out var wpp, out var right, out var up).Should().BeTrue();
        wpp.Should().BeApproximately(0.02f, 1e-3f);
        right.X.Should().BeGreaterThan(0.99f);
        up.Y.Should().BeGreaterThan(0.99f);
    }

    [Fact]
    public void Snap_RoundsToGrid()
    {
        MathHelper.Snap(1.2f, 0.5f).Should().BeApproximately(1.0f, 1e-5f);
        Geometry3DHelper.Snap(new Vector3(0.24f, 0.26f, 9f), new Vector3(0.5f, 0.5f, 0f))
            .Should().Be(new Vector3(0f, 0.5f, 9f));
    }

    [Fact]
    public void ScreenConstantLength_IsPixelsTimesWorldPerPixel()
    {
        GizmoMath.ScreenConstantLength(Frame(), new Vector3(0, 0, 10), 90f).Should().BeApproximately(1.8f, 0.1f);
    }

    [Fact]
    public void AxisTranslationDelta_MeasuresMovementAlongAxis()
    {
        var d = GizmoMath.AxisTranslationDelta(
            Vector3.UnitX, Vector3.Zero,
            new Vector3(0, 5, 0), new Vector3(0, -1, 0),   // meets X axis at 0
            new Vector3(2, 5, 0), new Vector3(0, -1, 0));  // meets X axis at 2
        d.Should().BeApproximately(2f, 1e-4f);
    }

    [Fact]
    public void RotationAngle_MeasuresSweepAboutAxis()
    {
        var angle = GizmoMath.RotationAngle(
            Vector3.Zero, Vector3.UnitY,
            new Vector3(1, 10, 0), new Vector3(0, -1, 0),
            new Vector3(0, 10, 1), new Vector3(0, -1, 0));
        angle.Should().BeApproximately(-MathF.PI / 2f, 1e-3f);
    }

    [Fact]
    public void AxisScaleFactor_DoublesAtOneHandleLength()
    {
        var f = GizmoMath.AxisScaleFactor(
            Vector3.UnitX, Vector3.Zero, referenceLength: 2f,
            new Vector3(0, 5, 0), new Vector3(0, -1, 0),
            new Vector3(2, 5, 0), new Vector3(0, -1, 0));
        f.Should().BeApproximately(2f, 1e-4f);
    }

    [Fact]
    public void UniformScaleFactor_IsScreenDistanceRatio()
    {
        GizmoMath.UniformScaleFactor(new Vector2(500, 500), new Vector2(600, 500), new Vector2(700, 500))
            .Should().BeApproximately(2f, 1e-4f);
    }

    [Fact]
    public void SnapAngle_And_SnapScale_RoundToIncrement()
    {
        GizmoMath.SnapAngle(0.9f, 45f).Should().BeApproximately(MathF.PI / 4f, 1e-4f);
        GizmoMath.SnapAngle(0.1f, 45f).Should().Be(0f);
        GizmoMath.SnapScale(1.03f, 0.25f).Should().BeApproximately(1.0f, 1e-5f);
        GizmoMath.SnapScale(0.05f, 0.25f).Should().BeApproximately(0.25f, 1e-5f, "scale never snaps below one increment");
    }

    [Fact]
    public void Selection_Single_ReplacesOnPick()
    {
        var sel = new InteractSelection { Mode = SelectionMode.Single };
        var a = new SceneNode(null, "a");
        var b = new SceneNode(null, "b");

        sel.Pick(a);
        sel.Primary.Should().BeSameAs(a);
        sel.Pick(b);
        sel.Nodes.Should().ContainSingle().Which.Should().BeSameAs(b);
    }

    [Fact]
    public void Selection_Multi_AddAndToggle()
    {
        var sel = new InteractSelection { Mode = SelectionMode.Multi };
        var a = new SceneNode(null, "a");
        var b = new SceneNode(null, "b");

        sel.Pick(a);
        sel.Pick(b, SelectionModifiers.Add);
        sel.Nodes.Should().HaveCount(2);

        sel.Pick(a, SelectionModifiers.Toggle);
        sel.Nodes.Should().ContainSingle().Which.Should().BeSameAs(b);
    }

    [Fact]
    public void Selection_PickEmpty_ClearsUnlessModified()
    {
        var sel = new InteractSelection { Mode = SelectionMode.Multi };
        var a = new SceneNode(null, "a");

        sel.Pick(a);
        sel.Pick(null, SelectionModifiers.Add);
        sel.Count.Should().Be(1, "a modified pick on empty space keeps the selection");

        sel.Pick(null);
        sel.Count.Should().Be(0, "an unmodified pick on empty space clears");
    }

    [Fact]
    public void Selection_Raises_ChangedOnce()
    {
        var sel = new InteractSelection { Mode = SelectionMode.Single };
        var a = new SceneNode(null, "a");
        var changes = 0;
        sel.Changed += () => changes++;

        sel.Pick(a);
        sel.Pick(a);
        changes.Should().Be(1);
    }
}
