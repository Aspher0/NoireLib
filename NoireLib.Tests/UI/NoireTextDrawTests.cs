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
        var result = harness.Draw(static () => NoireText.Tracked(Label), warmUpFrames: 2);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TrackedSize_AllocatesNothing()
    {
        var measured = Vector2.Zero;
        var result = harness.Draw(() => measured = NoireText.TrackedSize(Label), warmUpFrames: 2);

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

        harness.Draw(() =>
        {
            fontSize = ImGui.GetFontSize();
            tight = NoireText.Tracked(Label, 0f);
            loose = NoireText.Tracked(Label, tracking);
        }, warmUpFrames: 2);

        var gaps = Label.Length - 1;

        tight.X.Should().BeGreaterThan(0f);
        (loose.X - tight.X).Should().BeApproximately(tracking * fontSize * gaps, 0.01f);
    }
}
