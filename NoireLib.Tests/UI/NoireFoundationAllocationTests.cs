using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the foundations every widget sits on at zero allocation per frame: the animation clocks, the style scope,
/// the two widget memories and the tooltip.
/// </summary>
/// <remarks>
/// These had no allocation coverage at all, which is its own kind of blind spot: the audit that put every widget
/// under a zero measured the widgets, and a cost in what they all call would have been counted against whichever of
/// them happened to be measured first. The animation clocks in particular are read by every animated widget on every
/// frame, so a single allocation here is multiplied by the whole interface.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireFoundationAllocationTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly UiStyle Style = new() { TextColor = new(1f, 1f, 1f, 1f) };

    private static readonly NoireContent Tip = new NoireContent().AddText("What this control does.");

    private readonly UiHarness harness;

    public NoireFoundationAllocationTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void AnimationClocks_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireAnim.Ease("alloc.anim", "hover", 1f);
                    NoireAnim.Presence("alloc.anim", "shown", true);
                    NoireAnim.Pulse(1.5f);
                    NoireAnim.Spin(4f);
                    NoireAnim.Shake("alloc.anim", "shake");
                }
            },
            warmUpFrames: 3);

        // The two-part id exists for this: composing "{id}.hover" instead would be a string per animated property per
        // frame, on the one thread a plugin cannot afford a collection on.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void StyleScope_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                    NoireStyle.With(Style, 0, static _ => { });
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SingleValueStyleScopes_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireStyle.WithColor(Dalamud.Bindings.ImGui.ImGuiCol.Text, new(1f, 1f, 1f, 1f), 0, static _ => { });
                    NoireStyle.WithAlpha(0.5f, 0, static _ => { });
                }
            },
            warmUpFrames: 3);

        // These two had no state overload at all, so every caller wanting one colour held for a block either captured
        // into a lambda, which costs a display class at method entry on every frame the call is reached, or reached
        // past the library for Dalamud's ImRaii, which is measured at 24 bytes per scope entered.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void ReadingWidgetMemory_AllocatesNothing()
    {
        NoireUiSession.Set("alloc.session", 12);

        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireUiSession.TryGet<int>("alloc.session", out _);
                    NoireUiState.TryGet<int>("alloc.state.missing", out _);
                }
            },
            warmUpFrames: 3);

        // Read on every frame by any widget that remembers anything, so a boxed value or a missing-key allocation here
        // is paid by the whole interface rather than by the one widget that stored something.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Tooltip_AllocatesNothing()
    {
        var result = harness.Draw(
            static () => NoireTooltip.Show(Tip, null, "alloc_tooltip"),
            warmUpFrames: 4);

        // A tooltip is on screen for as long as the pointer rests, which is exactly when the user is looking at the
        // frame rate.
        result.AllocatedBytes.Should().Be(0L);
    }
}
