using FluentAssertions;
using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Scene;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the flattened gizmo config (scalar <see cref="NoireGizmo.Snap"/> and object-initializer knobs delegating to
/// <see cref="GizmoOptions"/>) and the <see cref="SceneEditor"/> facade (owned by the scene, scoped multi-select,
/// follow-selection). No GPU: a gizmo's interactor registration early-outs when the library isn't initialized.
/// </summary>
public class Draw3DEditorTests
{
    // ---------------------------------------------------------------- gizmo flattening

    [Fact]
    public void Gizmo_ScalarSnap_SetsAllAxesAndReadsBack()
    {
        using var gizmo = new NoireGizmo(GizmoOp.Universal) { Snap = 0.5f };
        gizmo.Options.Snap.Should().Be(new Vector3(0.5f, 0.5f, 0.5f));
        gizmo.Snap.Should().Be(0.5f);
    }

    [Fact]
    public void Gizmo_FlattenedKnobs_DelegateToOptions()
    {
        using var gizmo = new NoireGizmo(GizmoOp.Universal)
        {
            Space = GizmoSpace.Local,
            Backend = GizmoBackend.Native,
            SnapPerAxis = new Vector3(1f, 2f, 3f),
            RotateSnapDeg = 15f,
            ScaleSnap = 0.25f,
            Depth = GizmoDepth.AlwaysOnTop,
        };

        gizmo.Options.Space.Should().Be(GizmoSpace.Local);
        gizmo.Options.Backend.Should().Be(GizmoBackend.Native);
        gizmo.Options.Snap.Should().Be(new Vector3(1f, 2f, 3f));
        gizmo.Options.RotateSnapDeg.Should().Be(15f);
        gizmo.Options.ScaleSnap.Should().Be(0.25f);
        gizmo.Options.Depth.Should().Be(GizmoDepth.AlwaysOnTop);
    }

    // ---------------------------------------------------------------- editor facade

    [Fact]
    public void CreateEditor_IsOwnedByScene_DisposedWithIt()
    {
        var scene = new Scene3D("t");
        var editor = scene.CreateEditor(GizmoOp.Universal);

        scene.Dispose();

        editor.IsDisposed.Should().BeTrue("scene.Dispose() must dispose editors it created");
    }

    [Fact]
    public void Editor_FollowsSelection_SingleThenGroupThenNone()
    {
        var scene = new Scene3D("t");
        using var editor = scene.CreateEditor(GizmoOp.Universal);
        editor.MultiSelect = true;

        var a = scene.CreateNode("a");
        var b = scene.CreateNode("b");

        scene.Selection.Pick(a);
        editor.Gizmo.Target.Should().BeSameAs(a);
        editor.Gizmo.TargetGroup.Should().BeNull();

        scene.Selection.Pick(b, SelectionModifiers.Add);
        editor.Gizmo.Target.Should().BeNull();
        editor.Gizmo.TargetGroup.Should().HaveCount(2);

        scene.Selection.Clear();
        editor.Gizmo.Target.Should().BeNull();
        editor.Gizmo.TargetGroup.Should().BeNull();
    }

    [Fact]
    public void Editor_MultiSelect_IsScoped_RestoredOnDispose()
    {
        var scene = new Scene3D("t");
        scene.Selection.Mode.Should().Be(SelectionMode.Single);

        var editor = scene.CreateEditor();
        editor.MultiSelect = true;
        scene.Selection.Mode.Should().Be(SelectionMode.Multi);

        editor.Dispose();
        scene.Selection.Mode.Should().Be(SelectionMode.Single, "the editor restores the selection mode it changed");
    }

    [Fact]
    public void Editor_EarlyDispose_IsIdempotentAndDisownsFromScene()
    {
        var scene = new Scene3D("t");
        var editor = scene.CreateEditor();

        editor.Dispose();
        editor.Dispose(); // idempotent
        editor.IsDisposed.Should().BeTrue();

        // Disowned: a later scene.Dispose() must not throw trying to dispose it again.
        var act = scene.Dispose;
        act.Should().NotThrow();
    }
}
