using FluentAssertions;
using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Scene;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the per-scene selection model and the <see cref="SceneNode.MakeSelectable"/> /
/// <see cref="SceneNode.MakeInteractable"/> opt-ins (no GPU needed): selections are per-scene and independent, the
/// default hover highlight is ×1.2 RGB, and the helpers compose over pre-existing handlers rather than clobbering.
/// </summary>
public class Draw3DSelectionTests
{
    [Fact]
    public void Selection_IsPerSceneAndIndependent()
    {
        var a = new Scene3D("a");
        var b = new Scene3D("b");
        var na = a.CreateNode("na");

        a.Selection.Should().NotBeSameAs(b.Selection);

        a.Selection.Pick(na);
        a.Selection.Count.Should().Be(1);
        b.Selection.Count.Should().Be(0, "selecting in one scene must not touch another scene's selection");
    }

    [Fact]
    public void DefaultHoverHighlight_Brightens1Point2RgbKeepsAlpha()
    {
        var highlighted = SceneNode.DefaultHoverHighlight(new Vector4(0.5f, 0.4f, 0.2f, 0.8f));
        highlighted.X.Should().BeApproximately(0.6f, 1e-5f);
        highlighted.Y.Should().BeApproximately(0.48f, 1e-5f);
        highlighted.Z.Should().BeApproximately(0.24f, 1e-5f);
        highlighted.W.Should().Be(0.8f, "alpha is preserved by the default highlight");
    }

    [Fact]
    public void MakeSelectable_OptsIntoInteractionAndSelection()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode().MakeSelectable();
        node.Interactable.Should().BeTrue();
        node.Selectable.Should().BeTrue();
    }

    [Fact]
    public void MakeInteractable_IsHoverClickOnly_NotSelectable()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode().MakeInteractable();
        node.Interactable.Should().BeTrue();
        node.Selectable.Should().BeFalse("MakeInteractable is hover/click only - no selection");
    }

    [Fact]
    public void MakeSelectable_ComposesOverExistingHoverHandler()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();

        var priorRan = false;
        node.OnHoverEnter = _ => priorRan = true;

        node.MakeSelectable();
        node.OnHoverEnter!.Invoke(default);

        priorRan.Should().BeTrue("MakeSelectable must add to, not replace, an existing OnHoverEnter");
    }

    [Fact]
    public void MakeSelectable_LeavesLaterHandlerAssignmentWorking()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode().MakeSelectable();

        var clicked = false;
        node.OnClick = _ => clicked = true; // set AFTER MakeSelectable
        node.OnClick!.Invoke(default);

        clicked.Should().BeTrue();
    }

    [Fact]
    public void GlobalSelectionShim_IsGone()
    {
        // The process-global NoireInteract.Selection was removed outright (unused). Selection is per-scene only.
        typeof(NoireInteract).GetProperty("Selection").Should().BeNull();
    }
}
