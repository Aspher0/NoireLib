using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the surfaces underneath the widgets at zero allocation per frame: text, shapes, layout, panels, attention and
/// content.
/// </summary>
/// <remarks>
/// The widget tests hold what a consumer calls by name. These hold what those widgets are built out of, which is the
/// half a regression actually lands in: a shape or a layout scope is entered dozens of times in a frame by widgets that
/// never mention it, so a few bytes added here arrive multiplied and attributed to something else entirely.<br/>
/// Every delegate handed to a scope is <see langword="static"/> with its state passed alongside. A lambda that captures
/// anything is allocated on entry to the enclosing method rather than at the point of use, so a capturing test delegate
/// would read as the surface's own cost and pass or fail for the wrong reason.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireSurfaceAllocationTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly Vector4 Color = new(0.8f, 0.4f, 0.2f, 1f);

    /// <summary>
    /// A content built once, the way a consumer holds one, so the measurement is of drawing it rather than of building
    /// it.
    /// </summary>
    private static readonly NoireContent Content = new NoireContent()
        .AddText("A line of explanation.")
        .AddKeyCap("Ctrl")
        .AddText("and another line.");

    private readonly UiHarness harness;

    public NoireSurfaceAllocationTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Text_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireText.Draw("A line of body text.");
                    NoireText.Muted("Something quieter.");
                    NoireText.Colored(Color, "Something coloured.");
                    NoireText.Bullet("A bulleted point.");
                    NoireText.Centered("Centred.");
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TextMeasurement_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireText.CalcSize("A line of body text.");
                    NoireText.LineHeight();
                    NoireText.CenterOffset();
                }
            },
            warmUpFrames: 3);

        // Measurement is asked for far more often than painting: a widget sizes itself, then centres its label, then
        // paints, and every one of those is a measure of the same string.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void WrappedText_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireText.Wrapped(240f, "A paragraph long enough that it has to be broken across several lines.");
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Shapes_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var min = new Vector2(10f, 10f);
                var max = new Vector2(120f, 60f);
                var centre = new Vector2(200f, 200f);

                for (var i = 0; i < Repeats; i++)
                {
                    NoireShapes.Rect(min, max, Color, CornerShape.Rounded, 4f);
                    NoireShapes.RectOutline(min, max, Color, 1f, CornerShape.Rounded, 4f);
                    NoireShapes.GradientRect(min, max, Color, Color);
                    NoireShapes.Glow(min, max, Color, 6f);
                    NoireShapes.Plate(min, max);
                    NoireShapes.Frame(min, max);
                    NoireShapes.Brackets(min, max, Color, 8f);
                    NoireShapes.FadedLine(min, max, Color);
                    NoireShapes.Ring(centre, 40f, Color);
                    NoireShapes.Arc(centre, 40f, 0f, 0.75f, Color);
                    NoireShapes.Wedge(centre, 20f, 40f, 0f, 0.5f, Color);
                    NoireShapes.Diamond(centre, 12f, Color);
                    NoireShapes.DiamondOutline(centre, 12f, Color);
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void DecorativeShapes_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var centre = new Vector2(200f, 200f);

                for (var i = 0; i < Repeats; i++)
                {
                    NoireShapes.Sunburst(centre, 80f, Color);
                    NoireShapes.Guilloche(centre, 80f, Color);
                    NoireShapes.SweepLine(new Vector2(10f, 10f), new Vector2(200f, 10f), Color, Color, 0.5f);
                }
            },
            warmUpFrames: 3);

        // The expensive decorations, and the ones most likely to grow a scratch buffer: a sunburst tessellates per ray
        // per layer and a guilloche walks a lissajous, both of which are written into stack or pooled storage.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void LayoutScopes_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireLayout.Group(static () => NoireText.Draw("Grouped."));
                    NoireLayout.Indent(12f, static () => NoireText.Draw("Indented."));
                    NoireLayout.Id("scope", static () => NoireText.Draw("Scoped."));
                    NoireLayout.Disabled(true, static () => NoireText.Draw("Disabled."));
                    NoireLayout.ItemWidth(120f, static () => NoireText.Draw("Sized."));
                    NoireLayout.WrapText(200f, static () => NoireText.Draw("Wrapped."));
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Section_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireLayout.Section("A heading", static () => NoireText.Draw("Body."), "A description under it.");
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Collapsible_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireLayout.Collapsible("alloc_section", "A section", static () => NoireText.Draw("Body."));
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void CollapsibleThatRemembersItsState_AllocatesNothing()
    {
        var options = new CollapsibleOptions { Persist = true, DefaultOpen = true };

        var result = harness.Draw(
            () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireLayout.Collapsible("alloc_persisted", "A section", static () => NoireText.Draw("Body."), options);
            },
            warmUpFrames: 3);

        // A persisting section resolved its state key by interpolating the id on every frame it drew, which is once per
        // section per frame on a settings page built out of them. The key is a constant of the section.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Panels_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoirePanel.Frame(static () => NoireText.Draw("Framed."));
                    NoirePanel.Plate(static () => NoireText.Draw("Plated."));
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Splitter_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var size = 200f;

                for (var i = 0; i < Repeats; i++)
                    NoireLayout.Splitter("alloc_splitter", ref size, 50f, 400f);
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Content_AllocatesNothing()
    {
        var result = harness.Draw(static () => Content.Draw(), warmUpFrames: 3);

        // Content is what every tooltip is made of, so a per-frame cost here is paid by the surface most likely to be
        // on screen while the user is doing something else.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Badges_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireText.Draw("Inbox");
                    NoireBadge.OnLast(12);

                    NoireText.Draw("Settings");
                    NoireBadge.DotOnLast();
                }
            },
            warmUpFrames: 3);

        // The count was written out on every frame, which is a string per badge per frame to arrive at digits that
        // change when something arrives and at no other time.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void BadgeThatOverflowsItsCap_AllocatesNothing()
    {
        var style = new BadgeStyle { MaxCount = 99 };

        var result = harness.Draw(
            () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireText.Draw("Inbox");
                    NoireBadge.OnLast(500, style);
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Attention_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireText.Draw("A field");
                    NoireAttention.Glow(true);
                    NoireAttention.Pulse();
                    NoireAttention.Offset("alloc_attention", out _);
                    NoireAttention.FlashStrength("alloc_attention");
                }
            },
            warmUpFrames: 3);

        // Reading a shake or a flash back is the per-frame half of this API, and it composed the caller's id into a
        // sub key on each of those reads. The id is the entry's id; which motion it is, is a constant.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void AttentionStillMovesWhatItWasAskedToMove()
    {
        // The keys behind these moved, so the fact worth holding is that a fired motion is still the one read back:
        // a mismatch would be silent, and would look exactly like a motion that had already finished.
        // Fired on the first frame only and read on the ones after it, because a bounce read on the frame it started is
        // at a progress of zero and is legitimately still at rest.
        var frame = 0;
        var moved = false;

        harness.Draw(
            () =>
            {
                if (frame++ == 0)
                    NoireAttention.Bounce("attention_roundtrip");

                moved |= NoireAttention.Offset("attention_roundtrip", out var offset) && offset != Vector2.Zero;
            },
            warmUpFrames: 3);

        moved.Should().BeTrue();
    }
}
