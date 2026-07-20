using FluentAssertions;
using NoireLib.UI;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="NoireToastArea"/>'s stack geometry.
/// </summary>
/// <remarks>
/// One property, and it is the one that has to hold: a toast arriving or leaving must not move the toasts that are
/// staying. It is arithmetic rather than drawing, so it is checked here rather than by looking at the screen, which is
/// what let it be got wrong repeatedly.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireToastAreaTests
{
    /// <summary>
    /// Stands in for the pixel grid a window position is snapped to before it is drawn.
    /// </summary>
    private static float Snap(float value) => MathF.Floor(value);

    /// <summary>
    /// Where the stack's anchored edge lands, the way the drawing works it out: a bottom-anchored window is placed at
    /// the fixed screen edge less its own height, and the edge is recovered by adding that height back on.
    /// </summary>
    private static float AnchoredEdge(float fixedEdge, float total)
    {
        var height = NoireToastArea.ResolveStackHeight(total);

        return Snap(fixedEdge - height) + height;
    }

    [Fact]
    public void ResolveStackHeight_IsAlwaysAWholePixel()
    {
        for (var total = 1f; total < 200f; total += 0.37f)
            NoireToastArea.ResolveStackHeight(total).Should().Be(MathF.Truncate(NoireToastArea.ResolveStackHeight(total)));
    }

    [Fact]
    public void ResolveStackHeight_NeverCollapsesToNothing()
    {
        NoireToastArea.ResolveStackHeight(0f).Should().Be(1f);
        NoireToastArea.ResolveStackHeight(-5f).Should().Be(1f);
    }

    [Fact]
    public void ResolveStackHeight_IsNeverShorterThanWhatItHolds()
    {
        for (var total = 1f; total < 200f; total += 0.37f)
            NoireToastArea.ResolveStackHeight(total).Should().BeGreaterThanOrEqualTo(total);
    }

    [Fact]
    public void ResolveSlotHeight_IsAlwaysAWholePixel()
    {
        for (var content = 0.1f; content < 200f; content += 0.29f)
        {
            var slot = NoireToastArea.ResolveSlotHeight(content);
            slot.Should().Be(MathF.Truncate(slot));
        }
    }

    [Fact]
    public void ResolveSlotHeight_NeverCropsTheContent()
    {
        for (var content = 0.1f; content < 200f; content += 0.29f)
            NoireToastArea.ResolveSlotHeight(content).Should().BeGreaterThanOrEqualTo(content);
    }

    [Fact]
    public void ResolveSlotHeight_NeverCollapsesToNothing()
    {
        NoireToastArea.ResolveSlotHeight(0f).Should().Be(1f);
        NoireToastArea.ResolveSlotHeight(-3f).Should().Be(1f);
    }

    [Fact]
    public void StackHeight_OfWholePixelSlots_AddsNoSlack()
    {
        // The two roundings have to compose: once every slot and gap is on the grid, the total is already on it, so
        // the stack height is exactly the sum. Any slack here would sit between the toasts and the window's edge and
        // put the whole stack back off the grid, which is what the anchored edge depends on.
        const float gap = 8f;

        var slots = new[] { 41f, 63f, 27f, 55f };
        var total = 0f;

        for (var index = 0; index < slots.Length; index++)
            total += NoireToastArea.ResolveSlotHeight(slots[index]) + (index > 0 ? gap : 0f);

        NoireToastArea.ResolveStackHeight(total).Should().Be(total);
    }

    [Fact]
    public void SlotHeights_AreStableAcrossAContinuousMeasurement()
    {
        // ImGui floors the cursor onto the pixel grid after each item, so the height a toast measures drifts by up to a
        // pixel with where it started. A slot rounded off that measurement must not drift with it, or every toast
        // further from the anchor is laid out against a number that keeps changing.
        var measurements = new[] { 40.05f, 40.2f, 40.5f, 40.9f, 40.99f };
        var slots = new float[measurements.Length];

        for (var index = 0; index < measurements.Length; index++)
            slots[index] = NoireToastArea.ResolveSlotHeight(measurements[index]);

        slots.Should().AllBeEquivalentTo(41f, "a sub-pixel difference in the measurement is not a different slot");
    }

    [Fact]
    public void AnchoredEdge_HoldsStillWhileTheStackHeightAnimates()
    {
        // A toast leaving sweeps the measured total continuously. The edge the remaining toasts hang from is derived
        // from a snapped window position plus that height, and it must not move while the sweep runs, or every toast
        // in the stack slides back and forth across a pixel for the duration of the animation.
        const float fixedEdge = 1080.4f;

        var expected = AnchoredEdge(fixedEdge, 240f);

        for (var total = 240f; total > 180f; total -= 0.13f)
        {
            AnchoredEdge(fixedEdge, total)
                .Should().Be(expected, "the anchored edge may not move because the stack above it is shrinking");
        }
    }

    [Fact]
    public void AnchoredEdge_HoldsStillForAnyFixedEdge()
    {
        // The fixed edge is a viewport bottom plus a scaled offset, so it is not itself a whole pixel in general.
        foreach (var fixedEdge in new[] { 1080f, 1080.5f, 1439.25f, 2159.99f })
        {
            var expected = AnchoredEdge(fixedEdge, 300f);

            for (var total = 300f; total > 250f; total -= 0.17f)
                AnchoredEdge(fixedEdge, total).Should().Be(expected);
        }
    }

    [Fact]
    public void AnchoredEdge_WithAFractionalHeight_IsExactlyTheDefectThisPrevents()
    {
        // The same arithmetic without the rounding, to pin why it is there: the leftover fraction moves the edge.
        static float Unrounded(float fixedEdge, float total) => Snap(fixedEdge - total) + total;

        const float fixedEdge = 1080.4f;
        var seen = new System.Collections.Generic.HashSet<float>();

        for (var total = 240f; total > 235f; total -= 0.13f)
            seen.Add(Unrounded(fixedEdge, total));

        seen.Count.Should().BeGreaterThan(
            1, "an unrounded height leaves the snap's remainder in the result, which is what made the stack wander");
    }
}
