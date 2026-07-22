using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the everyday interactive widgets at zero allocation per frame.
/// </summary>
/// <remarks>
/// Every figure here was non-zero before wave 2's audit, and none of the causes were visible by reading the widget:
/// two were lambdas capturing a parameter, which Roslyn allocates on entry to the method rather than at the point of
/// use, so a custom-draw hook nobody had set still cost every button and every slider in the frame. One was a style
/// cloned per segment per frame, and one a value formatted to a string that had not changed.<br/>
/// Bytes rather than milliseconds, because bytes are the same number on every machine. Each surface is warmed first:
/// the first draw of a path in a process pays jitting and the first entry into each cache, and what a plugin actually
/// pays is the steady state.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireWidgetAllocationTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private readonly UiHarness harness;

    public NoireWidgetAllocationTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Button_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireButtons.Button("Save changes");
            },
            warmUpFrames: 2);

        // 24 bytes a button before this, from the lambda in Paint that closes over the style to run a custom draw.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Toggle_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var on = true;

                for (var i = 0; i < Repeats; i++)
                    NoireButtons.Toggle("##alloc_toggle", ref on);
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SliderFloat_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var value = 0.5f;

                for (var i = 0; i < Repeats; i++)
                    NoireSliders.Float("##alloc_slider_f", ref value, 0f, 1f);
            },
            warmUpFrames: 2);

        // Two causes: the same custom-draw lambda, and the value formatted to a string on every frame it had not
        // moved. A slider's value only changes while it is being dragged.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SliderInt_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var value = 5;

                for (var i = 0; i < Repeats; i++)
                    NoireSliders.Int("##alloc_slider_i", ref value, 0, 10);
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Segmented_DoesNotAllocateAStylePerSegment()
    {
        string[] options = ["Everything", "Unread", "Flagged", "Archived"];

        var oneSegment = harness.Draw(
            () =>
            {
                var selected = 0;
                NoireButtons.Segmented("##alloc_seg_one", ref selected, options[..1]);
            },
            warmUpFrames: 2);

        var fourSegments = harness.Draw(
            () =>
            {
                var selected = 0;
                NoireButtons.Segmented("##alloc_seg_four", ref selected, options);
            },
            warmUpFrames: 2);

        // Asserted as a bound on what each extra segment costs rather than as zero, because a residual this audit did
        // not isolate remains. What it locks is the shape of the fix: the per-segment style is copied into a reused
        // scratch instead of cloned, so an extra segment cannot cost another ButtonStyle. A clone measured about 315
        // bytes, so a regression would put this far above the bound.
        var perSegment = (fourSegments.AllocatedBytes - oneSegment.AllocatedBytes) / 3d;

        perSegment.Should().BeLessThan(150d);
    }
}
