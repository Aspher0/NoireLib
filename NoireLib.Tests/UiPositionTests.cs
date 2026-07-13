using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="UiPosition"/>: anchor, absolute and ratio resolution, pivots, offsets and viewport clamping.
/// </summary>
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
