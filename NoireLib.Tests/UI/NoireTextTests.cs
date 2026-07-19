using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the type scale <see cref="NoireText"/> resolves against: an unset step derives from the body size rather than
/// carrying its own default, so moving the body moves the whole scale, and setting one step opts only that one out.
/// </summary>
/// <remarks>
/// The font building itself needs an ImGui context and a Dalamud font atlas, so what is testable here is the arithmetic
/// that decides which size is asked for. That is also where the mistakes live: a scale that stops being proportional is
/// invisible until someone reskins it.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireTextTests : IDisposable
{
    public void Dispose()
    {
        NoireTheme.Current = new NoireTheme();
        NoireUI.ScaleOverride = null;
    }

    #region Resolution

    [Fact]
    public void ResolveTextSize_UsesTheHostDefault_WhenNothingIsSet()
    {
        var theme = new NoireTheme();

        theme.BodySize.Should().BeNull("because an unset size has to stay unset so it can fall through");
        theme.ResolveTextSize(TextSize.Body).Should().Be(NoireTheme.DefaultBodySize);
    }

    [Fact]
    public void ResolveTextSize_DerivesEveryStepFromTheBody()
    {
        var theme = new NoireTheme { BodySize = 20f };

        theme.ResolveTextSize(TextSize.Body).Should().Be(20f);
        theme.ResolveTextSize(TextSize.Heading).Should().Be(30f, "because heading ships at 1.5x the body");
        theme.ResolveTextSize(TextSize.Display).Should().Be(44f, "because display ships at 2.2x the body");
        theme.ResolveTextSize(TextSize.Caption).Should().Be(17f, "because caption ships at 0.85x the body");
    }

    [Fact]
    public void ResolveTextSize_MovingTheBody_MovesTheWholeScale()
    {
        var small = new NoireTheme { BodySize = 10f };
        var large = new NoireTheme { BodySize = 20f };

        (large.ResolveTextSize(TextSize.Heading) / small.ResolveTextSize(TextSize.Heading))
            .Should().Be(2f, "because the scale is proportional, which is the point of it being a scale");
    }

    [Fact]
    public void ResolveTextSize_AnExplicitStep_OptsOutOfTheProportion()
    {
        var theme = new NoireTheme { BodySize = 20f, HeadingSize = 24f };

        theme.ResolveTextSize(TextSize.Heading).Should().Be(24f);
        theme.ResolveTextSize(TextSize.Display).Should().Be(44f, "because the other steps keep following the body");
    }

    [Fact]
    public void ResolveTextSize_NeverReturnsZero()
    {
        var theme = new NoireTheme { BodySize = 0f };

        theme.ResolveTextSize(TextSize.Body).Should().BeGreaterThan(0f, "because a font built at zero pixels has no glyphs to draw");
        theme.ResolveTextSize(TextSize.Caption).Should().BeGreaterThan(0f);
    }

    [Fact]
    public void SettingASizeToNull_RemovesTheOverride()
    {
        var theme = new NoireTheme { HeadingSize = 40f };
        theme.ResolveTextSize(TextSize.Heading).Should().Be(40f);

        theme.HeadingSize = null;

        theme.ResolveTextSize(TextSize.Heading)
            .Should().Be(NoireTheme.DefaultBodySize * 1.5f, "because clearing a step puts it back on the proportion");
    }

    #endregion

    #region Scale

    [Fact]
    public void ResolveTextSize_IsALogicalSize_AndDoesNotFollowTheUiScale()
    {
        NoireUI.ScaleOverride = () => 2f;
        var theme = new NoireTheme { BodySize = 16f };

        // The atlas NoireText builds into is global-scaled, so Dalamud multiplies by the user's scale when it
        // rasterizes. Scaling here as well would render every heading at twice the size it was asked for.
        theme.ResolveTextSize(TextSize.Body).Should().Be(16f);
    }

    #endregion

    #region Cloning

    [Fact]
    public void Clone_CarriesTheTypeScale()
    {
        var theme = new NoireTheme { BodySize = 15f, DisplaySize = 48f };

        var clone = theme.Clone();

        clone.ResolveTextSize(TextSize.Body).Should().Be(15f);
        clone.ResolveTextSize(TextSize.Display).Should().Be(48f);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var theme = new NoireTheme { BodySize = 15f };
        var clone = theme.Clone();

        clone.BodySize = 30f;

        theme.ResolveTextSize(TextSize.Body).Should().Be(15f, "because a clone exists to be adjusted without touching the original");
    }

    #endregion

    #region Without an ImGui context

    [Fact]
    public void CalcSize_IsZero_WhenThereIsNoImGuiContext()
    {
        NoireText.CalcSize("anything", TextSize.Heading).Should().Be(Vector2.Zero);
    }

    [Fact]
    public void At_RunsTheBody_WhenThereIsNoImGuiContext()
    {
        var ran = false;

        NoireText.At(TextSize.Display, () => ran = true);

        ran.Should().BeTrue("because a body that silently does not run is the one failure a text helper must not have");
    }

    [Fact]
    public void At_RejectsANullBody()
    {
        var act = () => NoireText.At(TextSize.Body, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
