using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the key the text measurement cache stores under: every input that can change a measurement is part of it, and
/// a measurement taken under one of them is unreachable once it moves.
/// </summary>
/// <remarks>
/// The font generation is the field this exists for. A size that has not finished building is measured with a
/// stretched stand-in, and the frame the real font arrives the same text measures differently. A cache that answered
/// with the stand-in's numbers after that would leave the interface wrong everywhere by a few pixels, in a way that
/// reads as a layout bug rather than a caching one.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class UiTextMeasureCacheTests : IDisposable
{
    private readonly Func<int>? previousGeneration;
    private readonly Func<float>? previousScale;

    private int generation;
    private float scale = 1f;

    public UiTextMeasureCacheTests()
    {
        previousGeneration = UiFontCache.GenerationOverride;
        previousScale = NoireUI.ScaleOverride;

        UiFontCache.GenerationOverride = () => generation;
        NoireUI.ScaleOverride = () => scale;

        UiTextMeasureCache.Clear();
    }

    public void Dispose()
    {
        UiTextMeasureCache.Clear();

        UiFontCache.GenerationOverride = previousGeneration;
        NoireUI.ScaleOverride = previousScale;

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AMeasurementThatWasStored_IsFoundAgain()
    {
        UiTextMeasureCache.StoreSize("Save", 14f, 14f, new Vector2(40f, 16f));

        UiTextMeasureCache.TryGetSize("Save", 14f, 14f, out var size).Should().BeTrue();
        size.Should().Be(new Vector2(40f, 16f));
    }

    [Fact]
    public void AMeasurementTakenDuringAFontBuild_IsNotReusedAfterTheRealFontArrives()
    {
        // Taken while the size was still building, so measured against the stretched stand-in.
        UiTextMeasureCache.StoreSize("Heading", 24f, 14f, new Vector2(80f, 28f));
        UiTextMeasureCache.TryGetSize("Heading", 24f, 14f, out _).Should().BeTrue();

        // The atlas rebuilds and the real font at 24px arrives, which is what moves the generation.
        generation++;

        UiTextMeasureCache.TryGetSize("Heading", 24f, 14f, out _).Should()
            .BeFalse("a measurement taken against a stand-in font must not survive into the frame the real font arrives");
    }

    [Fact]
    public void AMeasurementTakenAtOneScale_IsNotReusedAtAnother()
    {
        UiTextMeasureCache.StoreSize("Save", 14f, 14f, new Vector2(40f, 16f));

        scale = 1.25f;

        UiTextMeasureCache.TryGetSize("Save", 14f, 14f, out _).Should().BeFalse();
    }

    [Fact]
    public void AScaleThatMovesAndComesBack_FindsItsMeasurementsStillThere()
    {
        // Carried in the key rather than dropping the whole cache, so returning to a scale does not re-measure every
        // label that was already measured at it.
        UiTextMeasureCache.StoreSize("Save", 14f, 14f, new Vector2(40f, 16f));

        scale = 1.25f;
        UiTextMeasureCache.StoreSize("Save", 14f, 14f, new Vector2(50f, 20f));

        scale = 1f;

        UiTextMeasureCache.TryGetSize("Save", 14f, 14f, out var size).Should().BeTrue();
        size.Should().Be(new Vector2(40f, 16f));
    }

    [Fact]
    public void MeasurementsDifferingInAnyOtherInput_AreDifferentEntries()
    {
        UiTextMeasureCache.StoreSize("Save", 14f, 14f, Vector2.One);

        UiTextMeasureCache.TryGetSize("Cancel", 14f, 14f, out _).Should().BeFalse();
        UiTextMeasureCache.TryGetSize("Save", 18f, 14f, out _).Should().BeFalse();
        UiTextMeasureCache.TryGetSize("Save", 14f, 18f, out _).Should().BeFalse();
    }

    [Fact]
    public void CentreOffsetsFollowTheSameInvalidationRule()
    {
        UiTextMeasureCache.StoreCenterOffset(14f, 14f, 0.32f);
        UiTextMeasureCache.TryGetCenterOffset(14f, 14f, out var offset).Should().BeTrue();
        offset.Should().Be(0.32f);

        generation++;

        UiTextMeasureCache.TryGetCenterOffset(14f, 14f, out _).Should().BeFalse();
    }

    [Fact]
    public void ClearingForgetsBothCaches()
    {
        UiTextMeasureCache.StoreSize("Save", 14f, 14f, Vector2.One);
        UiTextMeasureCache.StoreCenterOffset(14f, 14f, 0.32f);

        UiTextMeasureCache.Clear();

        UiTextMeasureCache.TryGetSize("Save", 14f, 14f, out _).Should().BeFalse();
        UiTextMeasureCache.TryGetCenterOffset(14f, 14f, out _).Should().BeFalse();
    }

    [Fact]
    public void AHitAllocatesNothing()
    {
        UiTextMeasureCache.StoreSize("Save", 14f, 14f, new Vector2(40f, 16f));

        for (var index = 0; index < 128; index++)
            UiTextMeasureCache.TryGetSize("Save", 14f, 14f, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1024; index++)
            UiTextMeasureCache.TryGetSize("Save", 14f, 14f, out _);

        var after = GC.GetAllocatedBytesForCurrentThread();

        // The key is composed per lookup and must stay a struct on the stack. A key that allocated would put those
        // bytes on the draw thread once per label per frame, which is the cost this cache exists to remove.
        (after - before).Should().Be(0L);
    }
}
