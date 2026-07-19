using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="UiPosition"/>: anchor, absolute and ratio resolution, pivots, offsets and viewport clamping.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class UiPositionTests
{
    private static readonly Vector2 ViewportPos = new(0f, 0f);
    private static readonly Vector2 ViewportSize = new(1000f, 500f);
    private static readonly Vector2 ElementSize = new(100f, 50f);

    #region Anchor mode

    [Fact]
    public void Anchor_TopLeft_ResolvesToViewportOrigin()
    {
        var position = UiPosition.AtAnchor(UiAnchor.TopLeft);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(0f, 0f));
    }

    [Fact]
    public void Anchor_BottomRight_PinsElementBottomRightCorner()
    {
        var position = UiPosition.AtAnchor(UiAnchor.BottomRight);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(900f, 450f));
    }

    [Fact]
    public void Anchor_MiddleCenter_CentersElement()
    {
        var position = UiPosition.AtAnchor(UiAnchor.MiddleCenter);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(450f, 225f));
    }

    [Fact]
    public void Anchor_TopCenter_CentersHorizontallyOnly()
    {
        var position = UiPosition.AtAnchor(UiAnchor.TopCenter);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(450f, 0f));
    }

    [Fact]
    public void Anchor_WithOffset_AppliesOffset()
    {
        var position = UiPosition.AtAnchor(UiAnchor.TopLeft, new Vector2(20f, 30f));
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(20f, 30f));
    }

    [Fact]
    public void Anchor_BottomRight_WithNegativeOffset_MovesInward()
    {
        var position = UiPosition.AtAnchor(UiAnchor.BottomRight, new Vector2(-10f, -10f));
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(890f, 440f));
    }

    [Fact]
    public void Anchor_RespectsViewportOrigin()
    {
        var viewportPos = new Vector2(100f, 200f);
        var position = UiPosition.AtAnchor(UiAnchor.TopLeft);
        position.Resolve(ElementSize, viewportPos, ViewportSize).Should().Be(viewportPos);
    }

    #endregion

    #region Ratio mode

    [Fact]
    public void Ratio_PlacesElementTopLeftAtRatioPointByDefault()
    {
        var position = UiPosition.AtRatio(0.1f, 0.1f);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(100f, 50f));
    }

    [Fact]
    public void Ratio_WithCenterPivot_CentersElementOnRatioPoint()
    {
        var position = UiPosition.AtRatio(0.5f, 0.5f).WithPivot(new Vector2(0.5f, 0.5f));
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(450f, 225f));
    }

    [Fact]
    public void Ratio_FullRight_IsClampedInsideViewport()
    {
        var position = UiPosition.AtRatio(1f, 1f);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(900f, 450f));
    }

    [Fact]
    public void Ratio_WithOffset_AppliesOffset()
    {
        var position = UiPosition.AtRatio(0.1f, 0.1f, new Vector2(5f, -5f));
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(105f, 45f));
    }

    #endregion

    #region Absolute mode

    [Fact]
    public void Absolute_IsRelativeToViewportOrigin()
    {
        var viewportPos = new Vector2(50f, 60f);
        var position = UiPosition.AtAbsolute(10f, 20f);
        position.Resolve(ElementSize, viewportPos, ViewportSize).Should().Be(new Vector2(60f, 80f));
    }

    [Fact]
    public void Absolute_WithPivot_ShiftsByElementSize()
    {
        var position = UiPosition.AtAbsolute(500f, 250f).WithPivot(new Vector2(1f, 1f));
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(400f, 200f));
    }

    #endregion

    #region Clamping

    [Fact]
    public void Clamp_KeepsElementInsideViewport()
    {
        var position = UiPosition.AtAbsolute(2000f, -100f);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(900f, 0f));
    }

    [Fact]
    public void Clamp_Disabled_AllowsOverflow()
    {
        var position = UiPosition.AtAbsolute(2000f, -100f).WithClampToViewport(false);
        position.Resolve(ElementSize, ViewportPos, ViewportSize).Should().Be(new Vector2(2000f, -100f));
    }

    [Fact]
    public void Clamp_ElementLargerThanViewport_PinsTopLeft()
    {
        var position = UiPosition.AtAnchor(UiAnchor.MiddleCenter);
        var huge = new Vector2(2000f, 1000f);
        position.Resolve(huge, ViewportPos, ViewportSize).Should().Be(ViewportPos);
    }

    #endregion

    #region Addon mode

    private static readonly UiRect PartyList = new(200f, 100f, 300f, 200f);

    private static UiRect? Rects(string name) => name == "_PartyList" ? PartyList : null;

    [Fact]
    public void Addon_TopLeft_PinsElementToAddonCorner()
    {
        var position = UiPosition.AtAddon("_PartyList");

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(200f, 100f));
    }

    [Fact]
    public void Addon_BottomRight_PinsElementBottomRightToAddonBottomRight()
    {
        var position = UiPosition.AtAddon("_PartyList", UiAnchor.BottomRight);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(400f, 250f), "the element sits inside the corner, as a screen anchor does");
    }

    [Fact]
    public void Addon_IsRelativeToViewportOrigin()
    {
        var viewportPos = new Vector2(50f, 60f);
        var position = UiPosition.AtAddon("_PartyList");

        position.TryResolve(ElementSize, viewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(250f, 160f), "addon rects are relative to the game window, not the desktop");
    }

    [Fact]
    public void Addon_WithOffset_AppliesOffset()
    {
        var position = UiPosition.AtAddon("_PartyList", UiAnchor.TopLeft, new Vector2(10f, -5f));

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(210f, 95f));
    }

    [Fact]
    public void Addon_WithRatio_OverridesTheAnchor()
    {
        var position = UiPosition.AtAddon("_PartyList", UiAnchor.BottomRight);
        position.AddonRatio = new Vector2(0f, 0.5f);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(200f, 175f));
    }

    [Fact]
    public void Addon_MissingAddon_DoesNotResolve()
    {
        var position = UiPosition.AtAddon("_ItemDetail");

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeFalse();
        resolved.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void Addon_EmptyRect_DoesNotResolve()
    {
        var position = UiPosition.AtAddon("_Collapsed");

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, _ => UiRect.Empty, out _)
            .Should().BeFalse("an addon collapsed to nothing is not somewhere to put an element");
    }

    [Fact]
    public void Addon_Resolve_FallsBackToTheEquivalentScreenAnchor()
    {
        var position = UiPosition.AtAddon("_ItemDetail", UiAnchor.BottomRight);

        position.Resolve(ElementSize, ViewportPos, ViewportSize)
            .Should().Be(new Vector2(900f, 450f), "Resolve always answers, and the screen corner is the honest fallback");
    }

    [Theory]
    [InlineData(UiPositionMode.Anchor)]
    [InlineData(UiPositionMode.Absolute)]
    [InlineData(UiPositionMode.Ratio)]
    public void TryResolve_NonAddonModes_AlwaysResolve(UiPositionMode mode)
    {
        var position = mode switch
        {
            UiPositionMode.Absolute => UiPosition.AtAbsolute(10f, 10f),
            UiPositionMode.Ratio => UiPosition.AtRatio(0.5f, 0.5f),
            _ => UiPosition.AtAnchor(UiAnchor.TopLeft),
        };

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, _ => null, out _).Should().BeTrue();
    }

    #endregion

    #region Side placement

    [Fact]
    public void NextToAddon_Right_PutsElementOutsideTheRightEdge()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Right);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(500f, 100f), "the element's left edge meets the addon's right edge");
    }

    [Fact]
    public void NextToAddon_Left_PutsElementOutsideTheLeftEdge()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Left);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(100f, 100f));
    }

    [Fact]
    public void NextToAddon_Below_PutsElementUnderTheBottomEdge()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Below);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(200f, 300f));
    }

    [Fact]
    public void NextToAddon_Above_PutsElementOverTheTopEdge()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Above);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(200f, 50f));
    }

    [Fact]
    public void NextToAddon_RightCenter_CentersOnTheEdge()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Right, UiAlign.Center);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(500f, 175f));
    }

    [Fact]
    public void NextToAddon_RightEnd_AlignsBottomEdges()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Right, UiAlign.End);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(500f, 250f));
    }

    [Fact]
    public void NextToAddon_WithGap_SeparatesTheTwo()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Right, UiAlign.Start, new Vector2(8f, 0f));

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(508f, 100f));
    }

    [Fact]
    public void NextToAddon_Over_SharesTheAddonArea()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Over, UiAlign.Center);

        position.TryResolve(ElementSize, ViewportPos, ViewportSize, Rects, out var resolved).Should().BeTrue();
        resolved.Should().Be(new Vector2(300f, 175f), "centred over the addon in both axes");
    }

    [Theory]
    [InlineData(UiSide.Left, 0f, 1f)]
    [InlineData(UiSide.Right, 1f, 0f)]
    [InlineData(UiSide.Above, 0f, 1f)]
    [InlineData(UiSide.Below, 1f, 0f)]
    public void GetSidePlacement_MirrorsAlongThePlacementAxis(UiSide side, float target, float pivot)
    {
        var (targetRatio, pivotRatio) = UiPosition.GetSidePlacement(side);
        var axis = side is UiSide.Left or UiSide.Right;

        (axis ? targetRatio.X : targetRatio.Y).Should().Be(target);
        (axis ? pivotRatio.X : pivotRatio.Y).Should().Be(pivot);
    }

    [Fact]
    public void NextToAddon_DoesNotClampByDefault()
    {
        var position = UiPosition.NextToAddon("_PartyList", UiSide.Right);

        position.ClampToViewport.Should().BeFalse(
            "an element that follows a window has to be allowed to follow it off the edge, not snap back");
    }

    #endregion

    #region Anchor ratios

    [Theory]
    [InlineData(UiAnchor.TopLeft, 0f, 0f)]
    [InlineData(UiAnchor.TopCenter, 0.5f, 0f)]
    [InlineData(UiAnchor.TopRight, 1f, 0f)]
    [InlineData(UiAnchor.MiddleLeft, 0f, 0.5f)]
    [InlineData(UiAnchor.MiddleCenter, 0.5f, 0.5f)]
    [InlineData(UiAnchor.MiddleRight, 1f, 0.5f)]
    [InlineData(UiAnchor.BottomLeft, 0f, 1f)]
    [InlineData(UiAnchor.BottomCenter, 0.5f, 1f)]
    [InlineData(UiAnchor.BottomRight, 1f, 1f)]
    public void GetAnchorRatio_MapsAllAnchors(UiAnchor anchor, float expectedX, float expectedY)
    {
        UiPosition.GetAnchorRatio(anchor).Should().Be(new Vector2(expectedX, expectedY));
    }

    #endregion
}
