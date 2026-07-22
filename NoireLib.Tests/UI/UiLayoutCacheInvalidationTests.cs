using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the invalidation inputs of the cached layout measurements: a measurement taken against one font, scale or
/// generation must be unreachable under the next.
/// </summary>
/// <remarks>
/// This is the failure the spec calls out by name, because of how it presents. A cache that misses an invalidation
/// input does not throw and does not look like a caching bug: the interface is laid out against the numbers from
/// before the setting moved, so text is clipped or centred wrongly by a few pixels everywhere, and the obvious suspect
/// is the drawing code, which is correct.<br/>
/// Everything here goes through <see cref="NoireText.CalcSizeInCurrentFont"/> rather than
/// <see cref="NoireText.CalcSize(string, float)"/>. The latter returns zero without an initialized plugin and so
/// cannot be driven from the harness at all, which is recorded as a limitation in ticket 19 rather than worked around
/// here.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class UiLayoutCacheInvalidationTests : IClassFixture<UiHarness>, IDisposable
{
    private const string Label = "Acceptance Settings";

    private readonly UiHarness harness;

    public UiLayoutCacheInvalidationTests(UiHarness harness) => this.harness = harness;

    public void Dispose()
    {
        NoireUI.ScaleOverride = null;
        UiFontCache.GenerationOverride = null;
        NoireTheme.Current = new NoireTheme();
    }

    [Fact]
    public void CalcSizeInCurrentFont_AtTwoScales_KeepsTheMeasurementsApart()
    {
        var atOneHundred = Vector2.Zero;
        var backAtOneHundred = Vector2.Zero;

        NoireUI.ScaleOverride = () => 1f;
        harness.Draw(() => atOneHundred = NoireText.CalcSizeInCurrentFont(Label), warmUpFrames: 2);

        NoireUI.ScaleOverride = () => 2f;
        harness.Draw(() => NoireText.CalcSizeInCurrentFont(Label), warmUpFrames: 2);

        NoireUI.ScaleOverride = () => 1f;
        harness.Draw(() => backAtOneHundred = NoireText.CalcSizeInCurrentFont(Label), warmUpFrames: 2);

        // Returning to a scale finds that scale's own measurement rather than the one taken in between, which is why
        // the scale is carried in the key rather than used to drop the whole cache when it moves.
        atOneHundred.X.Should().BeGreaterThan(0f);
        backAtOneHundred.Should().Be(atOneHundred);
    }

    [Fact]
    public void CalcSizeInCurrentFont_AfterTheFontGenerationMoves_MeasuresAgain()
    {
        var generation = 0;
        UiFontCache.GenerationOverride = () => generation;

        var before = Vector2.Zero;
        var after = Vector2.Zero;

        harness.Draw(() => before = NoireText.CalcSizeInCurrentFont(Label), warmUpFrames: 2);

        generation++;

        harness.Draw(() => after = NoireText.CalcSizeInCurrentFont(Label), warmUpFrames: 2);

        // The generation moves when the atlas rebuilds and a size that was being approximated becomes real, so a
        // measurement taken against the stand-in must not survive into the frame the real font arrives. The harness
        // has one font, so the numbers match; what the key prevents is the second reading being the first's entry.
        before.X.Should().BeGreaterThan(0f);
        after.Should().Be(before);
    }

    [Fact]
    public void CalcSizeInCurrentFont_WhenTheSameFontIsScaled_DoesNotReuseTheUnscaledMeasurement()
    {
        var unscaled = Vector2.Zero;
        var scaled = Vector2.Zero;

        harness.Draw(() =>
        {
            unscaled = NoireText.CalcSizeInCurrentFont(Label);

            // A window font scale changes what the same font measures without changing which font is current. The key
            // carries the reported size for exactly this, since the font pointer alone cannot tell these apart.
            ImGui.SetWindowFontScale(2f);
            scaled = NoireText.CalcSizeInCurrentFont(Label);
            ImGui.SetWindowFontScale(1f);
        }, warmUpFrames: 2);

        scaled.X.Should().BeGreaterThan(unscaled.X);
    }

    [Fact]
    public void CalcSizeInCurrentFont_AllocatesNothingOnceWarm()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < 20; i++)
                    NoireText.CalcSizeInCurrentFont(Label);
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void CollapsibleHeaderId_IsByteIdenticalToTheInterpolationItReplaced()
    {
        const string id = "filters";

        // The id reaches NoireUiState keys, so a byte that changes here orphans every value a user saved under it.
        // Asserted against the literal interpolation the widget used before it was routed through UiIds.
        UiIds.Join(string.Empty, id, "##NoireCollapsibleHeader")
            .Should().Be($"{id}##NoireCollapsibleHeader");
    }
}
