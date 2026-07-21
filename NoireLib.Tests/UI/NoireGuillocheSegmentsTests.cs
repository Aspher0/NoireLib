using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks how many points a guilloche ring is drawn with.
/// </summary>
/// <remarks>
/// The count used to come from the lobe count alone, so a rosette an inch across was drawn with the same hundreds of
/// points as one filling the window. It now follows the radius, which is both cheaper and the reason the rings share
/// one buffer: every ring inside the outermost has to fit in the outermost ring's allocation.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireGuillocheSegmentsTests
{
    [Fact]
    public void Segments_GrowWithTheRadius()
    {
        var small = NoireShapes.GuillocheSegments(40f, 7);
        var large = NoireShapes.GuillocheSegments(400f, 7);

        large.Should().BeGreaterThan(small, "a longer curve needs more points to stay smooth");
    }

    [Fact]
    public void Segments_NeverDropBelowTheLobeFloor()
    {
        // A tiny rosette still has to read as having its petals rather than as a polygon, however short the curve is.
        NoireShapes.GuillocheSegments(1f, 12).Should().BeGreaterThanOrEqualTo(12 * 12);
    }

    [Fact]
    public void Segments_AreBounded()
    {
        // Past the bound the segments are already shorter than a pixel, and the buffer they are written into is sized
        // for it.
        NoireShapes.GuillocheSegments(100_000f, 40).Should().BeLessThanOrEqualTo(512);
    }

    [Fact]
    public void Segments_OfAnInnerRing_NeverExceedTheOutermost()
    {
        // The invariant the shared buffer rests on: the outermost ring sizes the allocation and every ring inside it is
        // written into a slice of that. A count that grew as the radius shrank would slice past the end of it.
        const float outer = 260f;
        const int lobes = 7;

        var allocation = NoireShapes.GuillocheSegments(outer, lobes);

        for (var ring = 1; ring < 8; ring++)
        {
            var inner = outer * (1f - (ring * 0.12f));

            if (inner <= 0f)
                break;

            NoireShapes.GuillocheSegments(inner, lobes).Should()
                .BeLessThanOrEqualTo(allocation, "ring {0} is drawn into a slice of the outermost ring's buffer", ring);
        }
    }
}
