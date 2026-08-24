using Dalamud.Interface;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds every icon-bearing surface at zero allocation per frame.
/// </summary>
/// <remarks>
/// None of these could be measured before. Asking Dalamud for the icon font with no plugin behind the library does not
/// return an empty font, it blocks, so any widget drawing an icon hung the headless frame and was quietly left out of
/// the audit that put everything else under a zero. <see cref="UiIconFont"/> now degrades instead, which is what makes
/// this file possible: the glyph is drawn in whatever font is current, and everything around it, which is the part
/// that allocates, still runs.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireIconAllocationTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly ButtonStyle Iconed = new() { Icon = FontAwesomeIcon.Save };

    private static readonly ButtonStyle IconedGhost = new() { Tone = ButtonTone.Ghost, Icon = FontAwesomeIcon.Times };

    private static readonly NoireContent IconContent = new NoireContent()
        .AddIcon(FontAwesomeIcon.Mouse)
        .AddText(" scroll to cycle")
        .AddKeyCap("Ctrl");

    private readonly UiHarness harness;

    public NoireIconAllocationTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void IconButton_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireButtons.Button("Save##alloc_icon", Iconed);
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void IconOnlyButton_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireButtons.Button("##alloc_icon_only", IconedGhost, new Vector2(24f, 24f));
            },
            warmUpFrames: 3);

        // The shape a toast's dismiss cross and a window's chrome buttons are drawn in.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SplitButton_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireButtons.Split("Save##alloc_split", static () => { });
            },
            warmUpFrames: 3);

        // Three per split button per frame before this: the caret's style was cloned from the caller's, and both the
        // popup id and the caret's own id were concatenated fresh. The caret is why this one drew an icon at all, and
        // the icon is why it could never be measured.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Split_KeepsTheCallersStyleUntouched()
    {
        var style = new ButtonStyle { Tone = ButtonTone.Accent };

        harness.Draw(() => NoireButtons.Split("Save##alloc_split_style", static () => { }, style), warmUpFrames: 2);

        // The caret is drawn from a scratch copy, so setting its icon must not reach the style the caller holds.
        style.Icon.Should().BeNull();
        style.Tone.Should().Be(ButtonTone.Accent);
    }

    [Fact]
    public void ContentWithIcons_AllocatesNothing()
    {
        var result = harness.Draw(static () => IconContent.Draw(), warmUpFrames: 3);

        // Content is what every tooltip is built from, and an icon segment is the common case in one.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void ChromeButtons_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireWindowChrome.ChromeButton("alloc_chrome_close", new Vector2(40f, 40f), 16f, ChromeGlyph.Close);
                    NoireWindowChrome.ChromeButton("alloc_chrome_menu", new Vector2(80f, 40f), 16f, ChromeGlyph.Menu);
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }
}
