using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives the text surfaces through a real ImGui frame, for the two properties a draw path is held to: it produces no
/// garbage, and caching a measurement did not change the answer.
/// </summary>
/// <remarks>
/// The arithmetic of the type scale is covered by <see cref="NoireTextTests"/>, which needs no frame. What needs one is
/// everything below: allocation is only observable across a drawn frame, and a tracked run's width is only real once a
/// font is pushed.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireTextDrawTests : IClassFixture<UiHarness>
{
    private const string Label = "Acceptance Settings";

    private readonly UiHarness harness;

    public NoireTextDrawTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Draw_AllocatesNothing()
    {
        var result = harness.Draw(static () => NoireText.Draw(Label), warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Tracked_AllocatesNothing()
    {
        // Two warm-up frames rather than one: the first settles the window, and the glyph advances are measured on the
        // first frame this label is drawn and only looked up afterwards. The steady state is what a plugin pays.
        var result = harness.Draw(static () => NoireText.Tracked(Label), warmUpFrames: 2);

        // This read 88 bytes a call before ticket 17, from a lambda that captured a local to get the size back out.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TrackedSize_AllocatesNothing()
    {
        var measured = Vector2.Zero;
        var result = harness.Draw(() => measured = NoireText.TrackedSize(Label), warmUpFrames: 2);

        // Asserted non-zero first, because this guarded on the plugin rather than on the gate until ticket 19 and so
        // returned early here. A zero would mean the byte count below is timing an early return again.
        measured.X.Should().BeGreaterThan(0f);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Centered_AllocatesNothing()
    {
        var result = harness.Draw(static () => NoireText.Centered(Label), warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void CalcSize_IsReachableFromTheHarnessAndAllocatesNothingOnceWarm()
    {
        var measured = Vector2.Zero;

        var result = harness.Draw(
            () =>
            {
                for (var i = 0; i < 20; i++)
                    measured = NoireText.CalcSize(Label);
            },
            warmUpFrames: 2);

        // The reachability half is the point. This returned Vector2.Zero under the harness until its guard moved from
        // the plugin check to the gate, which meant every byte count taken over a measurement was timing an early
        // return. Every audit that measures text depends on this staying non-zero.
        measured.X.Should().BeGreaterThan(0f);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Tracked_ReportsTheSameSizeAsTrackedSize()
    {
        var drawn = Vector2.Zero;
        var measured = Vector2.Zero;

        harness.Draw(() =>
        {
            drawn = NoireText.Tracked(Label);
            measured = NoireText.TrackedSize(Label);
        }, warmUpFrames: 2);

        // The painting path and the measuring path walk the same glyphs through the same cache. If they ever disagree,
        // a caller placing something beside a tracked label puts it in the wrong place.
        drawn.X.Should().BeGreaterThan(0f);
        drawn.Should().Be(measured);
    }

    [Fact]
    public void Tracked_WidensByTheTrackingBetweenEveryPairOfCharacters()
    {
        const float tracking = 0.2f;

        var tight = Vector2.Zero;
        var loose = Vector2.Zero;
        var fontSize = 0f;

        // Driven through Tracked rather than TrackedSize, which returns at its service guard outside a plugin.
        // Tracked has no such guard and returns the size it drew, which is the same walk over the same cached
        // advances, so the arithmetic under test is identical.
        harness.Draw(() =>
        {
            fontSize = ImGui.GetFontSize();
            tight = NoireText.Tracked(Label, 0f);
            loose = NoireText.Tracked(Label, tracking);
        }, warmUpFrames: 2);

        // The gap goes between characters and not after the last one, so a label of n characters gains n-1 gaps. This
        // is the check that the cached per-glyph advances are still being summed the way the uncached ones were: a
        // cache returning a wrong or stale advance moves this width and nothing else would notice.
        var gaps = Label.Length - 1;

        tight.X.Should().BeGreaterThan(0f);
        (loose.X - tight.X).Should().BeApproximately(tracking * fontSize * gaps, 0.01f);
    }
}
