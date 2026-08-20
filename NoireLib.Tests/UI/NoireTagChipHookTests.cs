using FluentAssertions;
using NoireLib.UI;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The tag input's per-chip custom-draw hook: called once per chip with its index and state, replacing the shipped
/// painting, with the parts reproducing it.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireTagChipHookTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public NoireTagChipHookTests(UiHarness harness) => this.harness = harness;

    private static NoireTagInput Input(string id, params string[] tags)
    {
        var input = new NoireTagInput(id);

        foreach (var tag in tags)
            input.Add(tag);

        return input;
    }

    [Fact]
    public void Hook_IsCalledPerChipWithItsState()
    {
        var input = Input("hook_chips", "alpha", "beta");
        var seen = new List<(int Index, string Tag, bool Hovered)>();
        input.ChipDraw = args => seen.Add((args.Index, args.Tag, args.Hovered));

        // Cleared per frame: the harness pumps several, and the assertion is about one settled frame's calls. Warmed
        // first because the wrapping row places chips against the previous frame's layout, and an unsettled first
        // frame can cull a chip the settled layout shows.
        harness.Draw(
            () =>
            {
                seen.Clear();
                input.Draw();
            },
            warmUpFrames: 2);

        seen.Should().Equal((0, "alpha", false), (1, "beta", false));
    }

    [Fact]
    public void Hook_ReplacesTheShippedChipPainting()
    {
        var shippedInput = Input("chips_shipped", "alpha", "beta");
        var hookedInput = Input("chips_hooked", "alpha", "beta");
        hookedInput.ChipDraw = static _ => { };

        var shipped = harness.Draw(() => shippedInput.Draw(), warmUpFrames: 2);
        var hooked = harness.Draw(() => hookedInput.Draw(), warmUpFrames: 2);

        // The field itself draws either way; an empty hook removes exactly the chips' pills, labels and crosses.
        hooked.TotalVtxCount.Should().BeLessThan(shipped.TotalVtxCount);
    }

    [Fact]
    public void Parts_DrawWhatNoireUiWould()
    {
        var shippedInput = Input("parity_shipped", "alpha", "beta");
        var hookedInput = Input("parity_hooked", "alpha", "beta");
        hookedInput.ChipDraw = static args =>
        {
            args.DrawPill();
            args.DrawLabel();
            args.DrawCross();
        };

        var shipped = harness.Draw(() => shippedInput.Draw(), warmUpFrames: 2);
        var hooked = harness.Draw(() => hookedInput.Draw(), warmUpFrames: 2);

        hooked.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }
}
