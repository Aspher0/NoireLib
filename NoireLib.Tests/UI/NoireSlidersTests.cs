using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the arithmetic behind <see cref="NoireSliders"/>: what a pointer position on the track means, and
/// where a value sits along it.
/// </summary>
/// <remarks>
/// The drawing needs a context and the eye; this is the part that decides what number the user actually gets, and it
/// is where an off-by-one hides. The two halves have to agree, so the round trip is checked rather than each alone.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireSlidersTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(50f, 5f)]
    [InlineData(100f, 10f)]
    public void ResolveValue_ReadsTheRangeOffTheTrack(float pointer, float expected)
        => NoireSliders.ResolveValue(pointer, 0f, 100f, 0f, 10f, whole: false).Should().Be(expected);

    [Fact]
    public void ResolveValue_ClampsPastEitherEnd()
    {
        // Dragging off the end of a track means "as far as it goes", not "no answer".
        NoireSliders.ResolveValue(-500f, 0f, 100f, 0f, 10f, whole: false).Should().Be(0f);
        NoireSliders.ResolveValue(9000f, 0f, 100f, 0f, 10f, whole: false).Should().Be(10f);
    }

    [Fact]
    public void ResolveValue_HonoursATrackThatDoesNotStartAtZero()
    {
        // The track starts half a handle in from the control's left edge, so an origin of zero is the one case that
        // would pass while every real slider was off by that half handle.
        NoireSliders.ResolveValue(250f, 200f, 100f, 0f, 10f, whole: false).Should().Be(5f);
    }

    [Fact]
    public void ResolveValue_WholeNumbers_LandOnTheNearestStep()
    {
        NoireSliders.ResolveValue(54f, 0f, 100f, 0f, 10f, whole: true).Should().Be(5f);
        NoireSliders.ResolveValue(56f, 0f, 100f, 0f, 10f, whole: true).Should().Be(6f);
    }

    [Fact]
    public void ResolveValue_WholeNumbers_StayInsideTheRangeAfterRounding()
    {
        // Rounding at the ends is the case that escapes a range: the clamp has to come after it, not before.
        for (var pointer = -10f; pointer <= 110f; pointer += 0.5f)
        {
            NoireSliders.ResolveValue(pointer, 0f, 100f, 1f, 20f, whole: true)
                .Should().BeInRange(1f, 20f);
        }
    }

    [Fact]
    public void ResolveValue_OnACollapsedTrack_AnswersTheLowEnd()
        => NoireSliders.ResolveValue(50f, 0f, 0f, 3f, 9f, whole: false).Should().Be(3f);

    [Fact]
    public void ResolveFraction_PlacesTheHandleAlongTheRange()
    {
        NoireSliders.ResolveFraction(0f, 0f, 10f).Should().Be(0f);
        NoireSliders.ResolveFraction(5f, 0f, 10f).Should().Be(0.5f);
        NoireSliders.ResolveFraction(10f, 0f, 10f).Should().Be(1f);
    }

    [Fact]
    public void ResolveFraction_OnARangeOfNoWidth_IsZeroRatherThanInfinite()
        => NoireSliders.ResolveFraction(5f, 5f, 5f).Should().Be(0f);

    [Fact]
    public void ResolveFraction_ClampsAValueFromOutsideTheRange()
    {
        // A caller's value need not already be inside the range, and a handle painted outside the track is a visible
        // bug rather than an exception.
        NoireSliders.ResolveFraction(-4f, 0f, 10f).Should().Be(0f);
        NoireSliders.ResolveFraction(40f, 0f, 10f).Should().Be(1f);
    }

    [Fact]
    public void PointerAndHandle_AgreeAcrossTheWholeTrack()
    {
        // The property that matters: the handle has to end up under the pointer that put it there. If these two ever
        // disagree the handle drifts away from the cursor mid-drag, which is the one thing a slider must never do.
        const float trackX = 200f;
        const float span = 320f;

        for (var pointer = trackX; pointer <= trackX + span; pointer += 1f)
        {
            var value = NoireSliders.ResolveValue(pointer, trackX, span, -50f, 150f, whole: false);
            var handle = trackX + (span * NoireSliders.ResolveFraction(value, -50f, 150f));

            handle.Should().BeApproximately(pointer, 0.01f);
        }
    }
}
