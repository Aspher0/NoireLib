using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The input fields' reset-dot hook: a set hook replaces the dot's painting, the part reproduces it, and every field
/// style's clone carries it.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireInputHookTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public NoireInputHookTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Clones_CarryTheHook()
    {
        static void Hook(UiResetDotDraw _)
        {
        }

        new NumberStyle { ResetDotDraw = Hook }.Clone().ResetDotDraw.Should().NotBeNull();
        new DurationStyle { ResetDotDraw = Hook }.Clone().ResetDotDraw.Should().NotBeNull();
        new HexColorStyle { ResetDotDraw = Hook }.Clone().ResetDotDraw.Should().NotBeNull();
    }

    [Fact]
    public void Hook_ReplacesTheShippedDot()
    {
        var calls = 0;

        var empty = harness.Draw(
            () =>
            {
                NoireText.Draw("Field");
                NoireInputs.ResetDot("hook_dot", modified: true, customDraw: _ => calls++);
            },
            warmUpFrames: 2);

        var shipped = harness.Draw(
            static () =>
            {
                NoireText.Draw("Field");
                NoireInputs.ResetDot("plain_dot", modified: true);
            },
            warmUpFrames: 2);

        calls.Should().BeGreaterThan(0);
        empty.TotalVtxCount.Should().BeLessThan(shipped.TotalVtxCount, "an empty hook removes the dot's vertices");
    }

    [Fact]
    public void Part_DrawsWhatNoireUiWould()
    {
        var shipped = harness.Draw(
            static () =>
            {
                NoireText.Draw("Field");
                NoireInputs.ResetDot("parity_plain", modified: true);
            },
            warmUpFrames: 2);

        var hooked = harness.Draw(
            static () =>
            {
                NoireText.Draw("Field");
                NoireInputs.ResetDot("parity_hooked", modified: true, customDraw: static args => args.DrawDot());
            },
            warmUpFrames: 2);

        hooked.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void Hook_IsNotCalledWhileUnmodified()
    {
        var calls = 0;

        harness.Draw(
            () =>
            {
                NoireText.Draw("Field");
                NoireInputs.ResetDot("unmodified_dot", modified: false, customDraw: _ => calls++);
            },
            warmUpFrames: 0);

        calls.Should().Be(0, "an unmodified field reserves the column and draws nothing, hook or not");
    }
}
