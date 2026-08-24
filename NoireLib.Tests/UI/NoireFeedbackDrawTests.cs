using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the feedback surfaces: the tooltip to being placed, drawn and free of per-frame allocation, and every id the
/// batch builds to the exact bytes the interpolation it replaced produced.
/// </summary>
/// <remarks>
/// A tooltip is measured by being drawn once and that size is remembered under its window id, so an id that changed per
/// frame would leave it parked off screen.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireFeedbackDrawTests : IClassFixture<UiHarness>
{
    private static readonly NoireContent Content = new NoireContent().AddText("A tooltip explaining something.");

    private readonly UiHarness harness;

    public NoireFeedbackDrawTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Tooltip_ReachesTheScreenOnceItHasBeenMeasured()
    {
        var settled = harness.Draw(static () => NoireTooltip.Show(Content, null, "settled_tip"), warmUpFrames: 3);

        settled.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Tooltip_WithoutAnId_ReachesTheScreenToo()
    {
        // The unnamed path numbers its tooltips by the order they are shown in, resetting each frame, so the same
        // tooltip keeps the same id from one frame to the next and is measured exactly like a named one.
        var settled = harness.Draw(static () => NoireTooltip.Show(Content), warmUpFrames: 3);

        settled.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Tooltip_AllocatesNothing()
    {
        var named = harness.Draw(static () => NoireTooltip.Show(Content, null, "alloc_tip"), warmUpFrames: 4);
        var unnamed = harness.Draw(static () => NoireTooltip.Show(Content), warmUpFrames: 4);

        // 80 bytes a named tooltip and 60 an unnamed one before this, both of them the window id being composed again
        // for a tooltip that had not changed.
        named.AllocatedBytes.Should().Be(0L);
        unnamed.AllocatedBytes.Should().Be(0L);
    }

    [Theory]
    [InlineData("myTip", "###NoireTooltip_myTip")]
    [InlineData("save-button", "###NoireTooltip_save-button")]
    public void NamedTooltipId_IsByteIdenticalToTheInterpolationItReplaced(string id, string expected)
        => UiIds.For("###NoireTooltip_", id).Should().Be(expected);

    [Theory]
    [InlineData(0, "###NoireTooltip_0")]
    [InlineData(7, "###NoireTooltip_7")]
    public void UnnamedTooltipId_IsByteIdenticalToTheInterpolationItReplaced(int index, string expected)
        => UiIds.For("###NoireTooltip", string.Empty, index).Should().Be(expected);

    [Theory]
    [InlineData("Cancel", "##NoireModalCancel", "Cancel##NoireModalCancel")]
    [InlineData("Delete everything", "##NoireModalConfirm", "Delete everything##NoireModalConfirm")]
    public void ModalButtonIds_AreByteIdenticalToTheInterpolationTheyReplaced(string label, string prefix, string expected)
        => UiIds.Labelled(label, prefix, string.Empty).Should().Be(expected);

    [Theory]
    [InlineData("Keep", 0, "Keep##NoireModalChoice0")]
    [InlineData("Discard", 2, "Discard##NoireModalChoice2")]
    public void ModalChoiceIds_AreByteIdenticalToTheInterpolationTheyReplaced(string label, int index, string expected)
        => UiIds.Labelled(label, "##NoireModalChoice", string.Empty, string.Empty, index).Should().Be(expected);

    [Theory]
    [InlineData("toast-1", "##toast-1Close")]
    public void ToastCloseId_IsByteIdenticalToTheInterpolationItReplaced(string toastId, string expected)
        => UiIds.Join("##", toastId, "Close").Should().Be(expected);

    [Theory]
    [InlineData("Undo", "toast-1", 0, "Undo##toast-1Action0")]
    [InlineData("Retry", "toast-9", 3, "Retry##toast-9Action3")]
    public void ToastActionId_IsByteIdenticalToTheInterpolationItReplaced(string label, string toastId, int index, string expected)
        => UiIds.Labelled(label, "##", toastId, "Action", index).Should().Be(expected);
}
