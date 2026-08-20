using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The badge's custom-draw hook: a set hook replaces the shipped painting for the count and the dot both, the
/// record's parts reproduce it, and the record carries resolved values.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireBadgeHookTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly UiRect Target = new(new Vector2(100f, 100f), new Vector2(48f, 20f));

    private static readonly BadgeStyle Hooked = new()
    {
        CustomDraw = static args =>
        {
            args.DrawPlate();
            args.DrawLabel();
        },
    };

    private readonly UiHarness harness;

    public NoireBadgeHookTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Hook_ReplacesTheShippedPainting()
    {
        var calls = 0;
        var style = new BadgeStyle { CustomDraw = _ => calls++ };

        var result = harness.Draw(() => NoireBadge.Count(Target, 12, style), warmUpFrames: 2);

        calls.Should().BeGreaterThan(0);
        result.TotalVtxCount.Should().Be(0, "the hook drew nothing, so nothing may paint underneath it");
    }

    [Fact]
    public void Hook_PartsDrawWhatNoireUiWould_ForACount()
    {
        var shipped = harness.Draw(static () => NoireBadge.Count(Target, 12), warmUpFrames: 2);
        var hooked = harness.Draw(static () => NoireBadge.Count(Target, 12, Hooked), warmUpFrames: 2);

        hooked.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void Hook_PartsDrawWhatNoireUiWould_ForADot()
    {
        var shipped = harness.Draw(static () => NoireBadge.Dot(Target), warmUpFrames: 2);
        var hooked = harness.Draw(static () => NoireBadge.Dot(Target, Hooked), warmUpFrames: 2);

        hooked.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void Hook_ReceivesTheFormattedCount()
    {
        var captured = default(UiBadgeDraw);
        var style = new BadgeStyle { MaxCount = 99 };
        style.CustomDraw = args => captured = args;

        harness.Draw(() => NoireBadge.Count(Target, 500, style), warmUpFrames: 0);

        captured.Text.Should().Be(style.FormatCount(500), "the cap is applied before the hook sees the text");
        captured.Alpha.Should().Be(1f, "nothing is pulsing");
        captured.Radius.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void Hook_ForADot_ReceivesNoText()
    {
        var captured = default(UiBadgeDraw);
        var color = new Vector4(0.2f, 0.6f, 1f, 1f);
        var style = new BadgeStyle { Color = color, OutlineThickness = 0f };
        style.CustomDraw = args => captured = args;

        harness.Draw(() => NoireBadge.Dot(Target, style), warmUpFrames: 0);

        captured.Text.Should().BeNull("a dot badge has no count");
        captured.Color.Should().Be(color);
        captured.OutlineThickness.Should().Be(0f);
    }

    [Fact]
    public void Hook_IsNotCalledForACountThatDrawsNothing()
    {
        var calls = 0;
        var style = new BadgeStyle { CustomDraw = _ => calls++ };

        harness.Draw(() => NoireBadge.Count(Target, 0, style), warmUpFrames: 0);

        calls.Should().Be(0, "nothing is drawn for a count of zero, hook or not");
    }

    [Fact]
    public void CountSize_IsTheSameWhicheverPathPaints()
    {
        var plain = new BadgeStyle();
        var hooked = plain.Clone();
        hooked.CustomDraw = static _ => { };

        Vector2 withHook = default;
        Vector2 without = default;

        harness.Draw(
            () =>
            {
                withHook = NoireBadge.CountSize(12, hooked);
                without = NoireBadge.CountSize(12, plain);
            },
            warmUpFrames: 0);

        withHook.Should().Be(without, "the hook paints, it does not size");
    }

    [Fact]
    public void HookedBadges_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireBadge.Count(Target, 12, Hooked);
                    NoireBadge.Dot(Target, Hooked);
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }
}
