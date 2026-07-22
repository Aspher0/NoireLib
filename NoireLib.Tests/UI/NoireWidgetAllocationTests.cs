using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
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

    /// <summary>
    /// The series the sparkline is drawn from, held in a field rather than written inside the measured delegate.
    /// </summary>
    /// <remarks>
    /// The harness measures everything the delegate does, so a test's own fixture data is charged to the surface under
    /// test. A collection expression assigned to a <see cref="System.ReadOnlySpan{T}"/> of a multi-byte element type
    /// allocates, and written inline it read as 72 bytes a frame that the sparkline never spent.
    /// </remarks>
    private static readonly float[] Series = [1f, 4f, 2f, 8f, 3f, 9f, 5f, 7f];

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
    public void Number_WithAStableId_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var value = 1.5f;

                for (var i = 0; i < Repeats; i++)
                    NoireInputs.Number("Interval###interval", ref value, "ms");
            },
            warmUpFrames: 2);

        // 80 bytes a field before this: a label carrying a stable id was split into two substrings on every frame, and
        // a stable id is what a settings page uses precisely so its state survives the label being reworded.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Duration_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var value = TimeSpan.FromSeconds(90);

                for (var i = 0; i < Repeats; i++)
                    NoireInputs.Duration("Cooldown", ref value);
            },
            warmUpFrames: 2);

        // 136 bytes a field before this, all of it writing the duration the field is already showing.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void HexColor_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var value = new Vector4(1f, 0.5f, 0.25f, 1f);

                for (var i = 0; i < Repeats; i++)
                    NoireInputs.HexColor("Accent", ref value);
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Timer_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireGauges.Timer(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
            },
            warmUpFrames: 2);

        // 256 bytes a timer before this: the style was cloned to carry a label, and the label was the remaining time
        // written out again on every frame it had not changed. A countdown ticks once a second and draws sixty times.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Timer_WithAValueThatMovesEveryFrame_StillCostsLessThanCloningItsStyle()
    {
        var ticks = 0L;

        var result = harness.Draw(
            () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireGauges.Timer(TimeSpan.FromTicks(ticks++ * 12345L), TimeSpan.FromSeconds(60));
            },
            warmUpFrames: 200);

        // The worst case the text cache has: a countdown reading a real clock is a different value on every frame, so
        // the cache never hits and fills to its bound over and over. What is left is the one string the countdown
        // genuinely has to write, and the dictionary behind it stops growing once it has been round once, which is why
        // 200 frames are warmed rather than two. Measured at 144 bytes a timer against 256 before this audit, so even
        // the case the cache cannot help is cheaper than it was.
        var perTimer = result.AllocatedBytes / (double)Repeats;

        perTimer.Should().BeLessThan(200d);
    }

    [Fact]
    public void Gauges_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireGauges.Ring(0.5f);
                    NoireGauges.Bar(0.5f);
                    NoireGauges.Pips(3, 5);
                    NoireGauges.Sparkline(Series);
                }
            },
            warmUpFrames: 2);

        // These four were already clean when the audit reached them, which is worth holding rather than assuming: the
        // sparkline in particular builds its points on the stack, and a later change to a heap array would not fail
        // anything else.
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
