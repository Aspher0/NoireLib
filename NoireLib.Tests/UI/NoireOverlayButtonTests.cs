using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the non-drawing logic of <see cref="NoireOverlayButton"/>: the game-state hide decision driven by <see cref="OverlayDrawConditions"/>.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireOverlayButtonTests
{
    #region Default conditions (None)

    [Theory]
    [InlineData(false, false, false, false)] // Nothing active means visible
    [InlineData(true, false, false, true)]   // Cutscene means hidden
    [InlineData(false, true, false, true)]   // Gpose means hidden
    [InlineData(false, false, true, true)]   // Game UI hidden means hidden
    public void ShouldHideForGameState_None_HidesInEveryHiddenState(bool cutscene, bool gpose, bool uiHidden, bool expectedHidden)
    {
        NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions.None, cutscene, gpose, uiHidden)
            .Should().Be(expectedHidden);
    }

    #endregion

    #region Individual flags keep the button visible in their own state

    [Fact]
    public void ShouldHideForGameState_DrawInCutscenes_StaysVisibleDuringCutscene()
    {
        NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions.DrawInCutscenes, true, false, false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldHideForGameState_DrawInGpose_StaysVisibleDuringGpose()
    {
        NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions.DrawInGpose, false, true, false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldHideForGameState_DrawWhenGameUiHidden_StaysVisibleWhenUiHidden()
    {
        NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions.DrawWhenGameUiHidden, false, false, true)
            .Should().BeFalse();
    }

    #endregion

    #region A flag only covers its own state

    [Fact]
    public void ShouldHideForGameState_DrawInCutscenes_StillHidesDuringGpose()
    {
        NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions.DrawInCutscenes, false, true, false)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldHideForGameState_DrawInGpose_StillHidesDuringCutscene()
    {
        NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions.DrawInGpose, true, false, false)
            .Should().BeTrue();
    }

    #endregion

    #region Combined flags

    [Fact]
    public void ShouldHideForGameState_CombinedFlags_CoverBothStates()
    {
        var conditions = OverlayDrawConditions.DrawInCutscenes | OverlayDrawConditions.DrawInGpose;
        NoireOverlayButton.ShouldHideForGameState(conditions, true, true, false).Should().BeFalse();
        NoireOverlayButton.ShouldHideForGameState(conditions, false, false, true).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void ShouldHideForGameState_AlwaysDraw_NeverHides(bool cutscene, bool gpose, bool uiHidden)
    {
        NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions.AlwaysDraw, cutscene, gpose, uiHidden)
            .Should().BeFalse();
    }

    #endregion
}
