using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Checks the surfaces under the widgets allocate nothing per frame.<br/>
/// Every delegate handed to a scope is <see langword="static"/>. A capturing lambda allocates on entry to the enclosing method.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireSurfaceAllocationTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly Vector4 Color = new(0.8f, 0.4f, 0.2f, 1f);

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

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void CollapsibleWithAStyledHeader_AllocatesNothing()
    {
        var options = new CollapsibleOptions { HeaderBackground = Color, HeaderRounding = 0f, HeaderPadding = new Vector2(10f, 6f) };

        var result = harness.Draw(
            () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireLayout.Collapsible("alloc_styled", "A section", static () => NoireText.Draw("Body."), options);
            },
            warmUpFrames: 3);

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

    private static readonly SplitterOptions SplitOptions = new()
    {
        MinSize = 50f,
        MaxSize = 400f,
        Thickness = 8f,
        Length = 120f,
        CustomDraw = static _ => { },
    };

    [Fact]
    public void SplitterWithOptions_AllocatesNothing_WhenTheOptionsAreReused()
    {
        var result = harness.Draw(
            static () =>
            {
                var size = 200f;

                for (var i = 0; i < Repeats; i++)
                    NoireLayout.Splitter("alloc_splitter_options", ref size, SplitOptions);
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SplitterOptions_BuiltPerFrame_CostAnObjectPerCall()
    {
        var result = harness.Draw(
            static () =>
            {
                var size = 200f;

                for (var i = 0; i < Repeats; i++)
                {
                    NoireLayout.Splitter("alloc_splitter_inline", ref size, new SplitterOptions
                    {
                        MinSize = 50f,
                        MaxSize = 400f,
                        Thickness = 8f,
                        Length = 120f,
                        CustomDraw = static _ => { },
                    });
                }
            },
            warmUpFrames: 3);

        // Measured at 112 bytes. Only non-zero matters.
        var perCall = result.AllocatedBytes / (double)Repeats;

        perCall.Should().BeGreaterThan(64d);
    }

    [Fact]
    public void Content_AllocatesNothing()
    {
        var result = harness.Draw(static () => Content.Draw(), warmUpFrames: 3);

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

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void AttentionStillMovesWhatItWasAskedToMove()
    {
        // A bounce read on the frame it started is at progress zero and still at rest.
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
