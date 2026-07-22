using FluentAssertions;
using NoireLib.UI;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives <see cref="NoireContent"/> through a real ImGui frame, which is where its per-frame cost is observable.
/// <see cref="NoireContentTests"/> covers the building contract, which needs no context.
/// </summary>
/// <remarks>
/// Content is the body of every custom tooltip, so it draws on any frame a tooltip is open and its line working set is
/// sized by how many segments the caller built. That makes it the shape a borrowed buffer is for: bounded by data
/// rather than by a constant, and therefore not something <see langword="stackalloc"/> can carry.
/// </remarks>
[SupportedOSPlatform("windows")]
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireContentDrawTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public NoireContentDrawTests(UiHarness harness) => this.harness = harness;

    /// <summary>
    /// Content with several lines, which is what makes the line working set non-trivial: every new line flushes one
    /// batch of segments and starts another.
    /// </summary>
    private static NoireContent MultiLine()
        => new NoireContent()
            .AddText("first line")
            .AddText(" continued")
            .AddNewLine()
            .AddText("second line")
            .AddKeyCap("Ctrl")
            .AddText(" third piece")
            .AddNewLine()
            .AddText("third line");

    /// <summary>
    /// The same line structure built from the one segment kind that draws without pushing anything, so what a frame
    /// costs is the working set and nothing else.
    /// </summary>
    private static NoireContent MultiLineWithoutPushes()
    {
        var content = new NoireContent();

        for (var line = 0; line < 4; line++)
        {
            for (var segment = 0; segment < 6; segment++)
                content.AddSpacing(3f);

            content.AddNewLine();
        }

        return content;
    }

    [Fact]
    public void Draw_MultipleLines_ReachesTheDrawData()
    {
        var result = harness.Draw(static () => MultiLine().Draw());

        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Draw_SegmentsThatPushNothing_AllocatesNothing()
    {
        // Twenty-four segments across four lines, none of which pushes a colour, a style or a font. Every byte a frame
        // of this allocated would be the working set the lines are gathered into, so zero is the whole claim.
        var content = MultiLineWithoutPushes();

        var result = harness.Draw(() => content.Draw());

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Draw_MixedSegments_AllocatesNothingInSteadyState()
    {
        // The content is built once outside the measured frame, because building it is the caller's cost and happens
        // when the tooltip is defined rather than on every frame it is shown.
        var content = MultiLine();

        // Drawn once and thrown away first. The first draw of a path in a process pays for jitting it and for the
        // array pool's first array in a size class, neither of which any later frame pays again. Steady state is what
        // a plugin actually lives in, and it is what this asserts.
        harness.Draw(() => content.Draw());

        var result = harness.Draw(() => content.Draw());

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Draw_TwoContentsInOneFrame_BothReachTheDrawData()
    {
        var content = MultiLine();

        var one = harness.Draw(() => content.Draw());
        var two = harness.Draw(() =>
        {
            content.Draw();
            content.Draw();
        });

        // Both borrow a line buffer inside the same frame, and the second still draws. Not asserted as exactly twice
        // the geometry: content flows down the window from wherever the cursor is, so the second copy runs past the
        // host window's bottom edge and ImGui culls the glyphs that fall outside it. Independence of two borrowed
        // buffers in one frame is asserted on absolutely positioned geometry instead, in PooledBufferInAFrameTests.
        two.TotalVtxCount.Should().BeGreaterThan(one.TotalVtxCount);
        two.TotalIdxCount.Should().BeGreaterThan(one.TotalIdxCount);
    }
}
