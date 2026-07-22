using Dalamud.Interface;
using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the chrome, overlay and animation batch: the ids it builds, the icon glyphs it draws, and the fact that the
/// animation clock still moves.
/// </summary>
/// <remarks>
/// Most of this batch cannot be driven from the harness. Window chrome, overlay buttons and world labels are all
/// drawables that need an initialized plugin service to exist, and anything that pushes the icon font is worse than
/// unmeasurable: reading <c>UiBuilder.IconFont</c> without Dalamud behind it <b>hangs the test process</b> rather than
/// throwing, taking the whole run with it. So the icon work is held by testing the conversion the drawing calls, which
/// touches no font at all, and the id work by asserting the builder against the literal it replaced.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireChromeAndAnimationTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public NoireChromeAndAnimationTests(UiHarness harness) => this.harness = harness;

    [Theory]
    [InlineData(FontAwesomeIcon.Save)]
    [InlineData(FontAwesomeIcon.Trash)]
    [InlineData(FontAwesomeIcon.Check)]
    public void IconGlyph_IsWhatTheConversionProduces(FontAwesomeIcon icon)
        => UiValueText.Icon(icon).Should().Be(icon.ToIconString());

    [Fact]
    public void IconGlyph_IsTheSameStringEveryTime()
    {
        // 24 bytes a call before this, and every icon in the library went through it on every frame. An icon button
        // paid it twice, once to size itself and once to paint.
        UiValueText.Icon(FontAwesomeIcon.Save).Should().BeSameAs(UiValueText.Icon(FontAwesomeIcon.Save));
        UiValueText.Icon(FontAwesomeIcon.Save).Should().NotBe(UiValueText.Icon(FontAwesomeIcon.Trash));
    }

    [Fact]
    public void IconGlyphs_AllocateNothingOnceSeen()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    _ = UiValueText.Icon(FontAwesomeIcon.Save);
                    _ = UiValueText.Icon(FontAwesomeIcon.Trash);
                }
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Theory]
    [InlineData("Window", "myWindow", "###NoireWindow_myWindow")]
    [InlineData("OverlayButton", "toggle", "###NoireOverlayButton_toggle")]
    public void DrawableId_IsByteIdenticalToTheInterpolationItReplaced(string kind, string id, string expected)
        => UiIds.For("###Noire", kind, id).Should().Be(expected);

    [Theory]
    [InlineData("close", "###NoireWindowChrome_close")]
    public void ChromeButtonId_IsByteIdenticalToTheInterpolationItReplaced(string id, string expected)
        => UiIds.For("###NoireWindowChrome_", id).Should().Be(expected);

    [Fact]
    public void AnimatedValues_StillAdvanceAcrossFrames()
    {
        var first = 0f;
        var later = 0f;

        // Settled at rest first. An eased value first seen at its target is already there, so a value read straight
        // after a target change is the only place movement is observable.
        harness.Draw(static () => NoireAnim.Ease("anim_probe", "hover", 0f), warmUpFrames: 10);

        // The batch's standing hazard: a value cached without the clock in its key stops moving, and a frozen animation
        // reads as a broken animation rather than as a caching bug. Nothing here caches the clock, and this is what
        // says so out loud.
        harness.Draw(() => first = NoireAnim.Ease("anim_probe", "hover", 1f), warmUpFrames: 1);
        harness.Draw(() => later = NoireAnim.Ease("anim_probe", "hover", 1f), warmUpFrames: 8);

        later.Should().BeGreaterThan(first);
        later.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void Animation_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    _ = NoireAnim.Ease("anim_alloc", "hover", 1f);
                    _ = NoireAnim.Presence("anim_alloc", "shown", true);
                }
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }
}
