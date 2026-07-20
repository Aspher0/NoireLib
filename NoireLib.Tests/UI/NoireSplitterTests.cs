using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the splitter's drag arithmetic, which is the part a drag cannot demonstrate: that the divider is resolved from
/// where the pointer is rather than from how far it moved, so overshooting a bound costs nothing.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireSplitterTests
{
    private const float Min = 140f;
    private const float Max = 190f;

    /// <summary>The offset a drag started at a pointer of 500 against a pane of 190 would have recorded.</summary>
    private const float Grab = 500f - Max;

    [Fact]
    public void ResolveSize_FollowsThePointer()
    {
        NoireLayout.ResolveSize(500f, Grab, Min, Max).Should().Be(190f);
        NoireLayout.ResolveSize(480f, Grab, Min, Max).Should().Be(170f);
        NoireLayout.ResolveSize(460f, Grab, Min, Max).Should().Be(150f);
    }

    [Fact]
    public void ResolveSize_ClampsToTheBounds()
    {
        NoireLayout.ResolveSize(300f, Grab, Min, Max).Should().Be(Min);
        NoireLayout.ResolveSize(900f, Grab, Min, Max).Should().Be(Max);
    }

    [Fact]
    public void ResolveSize_AfterOvershooting_WaitsForThePointerToComeBack()
    {
        // Pushed far past the minimum, then brought back most of the way. Everything still short of the bound has to
        // read as the bound: a delta-driven splitter would have started moving the moment the direction changed, and
        // the divider would be ahead of the pointer by the whole overshoot for the rest of the drag.
        NoireLayout.ResolveSize(200f, Grab, Min, Max).Should().Be(Min, "the pointer is still left of the minimum");
        NoireLayout.ResolveSize(440f, Grab, Min, Max).Should().Be(Min, "one pixel short is still short");
        NoireLayout.ResolveSize(450f, Grab, Min, Max).Should().Be(Min, "exactly at the minimum");
        NoireLayout.ResolveSize(455f, Grab, Min, Max).Should().Be(145f, "and past it, it moves with the pointer again");
    }

    [Fact]
    public void ResolveSize_HoldsTheGrabOffset()
    {
        // Grabbing the divider anywhere along its thickness must not snap it to the pointer: the distance between the
        // two is whatever it was when the drag started, for as long as the drag lasts.
        const float offCentre = 500f - Max + 4f;

        NoireLayout.ResolveSize(500f, offCentre, Min, Max).Should().Be(186f);
        NoireLayout.ResolveSize(504f, offCentre, Min, Max).Should().Be(Max);
    }

    [Fact]
    public void ResolveSize_WithCrossedBounds_PrefersTheMinimum()
    {
        NoireLayout.ResolveSize(500f, Grab, 200f, 100f).Should().Be(200f);
    }
}
