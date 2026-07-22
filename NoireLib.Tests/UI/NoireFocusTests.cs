using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the focus mark: drawn only for the control that has focus, visibly different per shape, and free per frame.
/// </summary>
/// <remarks>
/// The mark exists to be told apart from hover, selection and emphasis, which are drawn with soft and glowing marks. A
/// shape that stopped reaching the screen, or that collapsed into the same geometry as another, would look like a theme
/// change rather than like a bug, so both are asserted rather than left to the eye.<br/>
/// Arrival is switched off in most of these. The mark fades in as it settles, so on the frame focus lands it is
/// legitimately at zero alpha and draws nothing; a test that did not account for that would be measuring the early
/// return rather than the drawing.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireFocusTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly UiRect Target = new(20f, 20f, 160f, 24f);

    private static readonly FocusStyle Ring = new() { Shape = FocusShape.Ring, ArrivalSeconds = 0f };
    private static readonly FocusStyle Corners = new() { Shape = FocusShape.Corners, ArrivalSeconds = 0f };
    private static readonly FocusStyle Brackets = new() { Shape = FocusShape.Brackets, ArrivalSeconds = 0f };
    private static readonly FocusStyle Underline = new() { Shape = FocusShape.Underline, ArrivalSeconds = 0f };

    private readonly UiHarness harness;

    public NoireFocusTests(UiHarness harness) => this.harness = harness;

    private UiHarnessResult DrawShape(FocusStyle style)
        => harness.Draw(() => NoireFocus.On(Target, focused: true, 1u, style), warmUpFrames: 2);

    [Fact]
    public void EveryShape_ReachesTheScreen()
    {
        DrawShape(Ring).TotalVtxCount.Should().BeGreaterThan(0);
        DrawShape(Corners).TotalVtxCount.Should().BeGreaterThan(0);
        DrawShape(Brackets).TotalVtxCount.Should().BeGreaterThan(0);
        DrawShape(Underline).TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EveryShape_DrawsSomethingDifferent()
    {
        var ring = DrawShape(Ring).TotalVtxCount;
        var corners = DrawShape(Corners).TotalVtxCount;
        var brackets = DrawShape(Brackets).TotalVtxCount;
        var underline = DrawShape(Underline).TotalVtxCount;

        // The point of offering four is that they read differently. A closed outline is the heaviest, four elbows and
        // two brackets are open paths, and an underline is one bar, so no two of them can produce the same geometry.
        ring.Should().NotBe(corners);
        corners.Should().NotBe(brackets);
        underline.Should().BeLessThan(ring);
    }

    [Fact]
    public void AControlWithoutFocus_IsNotMarked()
    {
        var result = harness.Draw(static () => NoireFocus.On(Target, focused: false, 1u, Ring), warmUpFrames: 2);

        result.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void TurningItOff_DrawsNothing()
    {
        var wasEnabled = NoireFocus.Enabled;

        try
        {
            NoireFocus.Enabled = false;

            var result = harness.Draw(static () => NoireFocus.On(Target, focused: true, 1u, Ring), warmUpFrames: 2);

            result.TotalVtxCount.Should().Be(0);
        }
        finally
        {
            NoireFocus.Enabled = wasEnabled;
        }
    }

    [Fact]
    public void TheMarkFadesOntoAControlRatherThanPoppingOntoIt()
    {
        var arriving = new FocusStyle { ArrivalSeconds = 0.5f };

        // Focus is singular, so its arrival lives in one slot keyed on which control holds it, and an id the slot has
        // not seen starts a fresh arrival. With no warm-up the measured frame is that first frame, where the mark is
        // at zero alpha and has not begun to appear.
        var landing = harness.Draw(() => NoireFocus.On(Target, focused: true, 101u, arriving), warmUpFrames: 0);
        var settled = harness.Draw(() => NoireFocus.On(Target, focused: true, 102u, arriving), warmUpFrames: 60);

        landing.TotalVtxCount.Should().Be(0);
        settled.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ReducedMotion_StillMarksTheFocusedControl()
    {
        var hadOverride = NoireUI.HasReducedMotionOverride;
        var wasReduced = NoireUI.ReducedMotion;

        try
        {
            NoireUI.ReducedMotion = true;

            var arriving = new FocusStyle { ArrivalSeconds = 0.5f };

            // The one signal that must survive reduced motion: the people who navigate by keyboard are exactly the
            // people who need to see where focus is. It is placed immediately instead of animating, never skipped.
            var result = harness.Draw(() => NoireFocus.On(Target, focused: true, 7u, arriving), warmUpFrames: 1);

            result.TotalVtxCount.Should().BeGreaterThan(0);
        }
        finally
        {
            if (hadOverride)
                NoireUI.ReducedMotion = wasReduced;
            else
                NoireUI.ClearReducedMotion();
        }
    }

    [Fact]
    public void LeavingAControlAndComingBackArrivesAgain()
    {
        var arriving = new FocusStyle { ArrivalSeconds = 0.5f };

        // The bug this pins: nothing tells the class that focus was lost, because a control without focus simply stops
        // calling. Without noticing the gap in frames, coming back to a control found the timestamp from the first
        // visit still sitting there, read the arrival as long finished, and placed the mark instantly ever after.
        harness.Draw(() => NoireFocus.On(Target, focused: true, 200u, arriving), warmUpFrames: 60);

        // Frames where nothing is marked at all, which is what a control losing focus looks like from in here.
        harness.Draw(static () => NoireFocus.On(Target, focused: false, 200u), warmUpFrames: 4);

        var returning = harness.Draw(() => NoireFocus.On(Target, focused: true, 200u, arriving), warmUpFrames: 0);

        // At the instant of a real arrival the mark is at zero alpha and draws nothing. Before the fix this was the
        // settled mark, at full strength, on the very first frame back.
        returning.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void ACustomHookReplacesTheShippedMark()
    {
        var calls = 0;
        var hooked = new FocusStyle { ArrivalSeconds = 0f, CustomDraw = _ => calls++ };

        var result = harness.Draw(() => NoireFocus.On(Target, focused: true, 300u, hooked), warmUpFrames: 2);

        // Drawing nothing is a valid hook, and is how one widget goes unmarked while the rest keep their mark.
        calls.Should().BeGreaterThan(0);
        result.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void ACustomHookCanStillDrawWhatNoireUiWouldHave()
    {
        var addOn = new FocusStyle { ArrivalSeconds = 0f, CustomDraw = static args => args.DrawShape() };

        var hooked = harness.Draw(() => NoireFocus.On(Target, focused: true, 301u, addOn), warmUpFrames: 2);
        var shipped = DrawShape(Ring);

        hooked.TotalVtxCount.Should().Be(shipped.TotalVtxCount);
    }

    [Fact]
    public void TheNoneShape_LeavesOneWidgetUnmarked()
    {
        var off = new FocusStyle { Shape = FocusShape.None, ArrivalSeconds = 0f };

        var result = harness.Draw(() => NoireFocus.On(Target, focused: true, 302u, off), warmUpFrames: 2);

        result.TotalVtxCount.Should().Be(0);
    }

    [Fact]
    public void Focus_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    NoireFocus.On(Target, focused: true, 1u, Ring);
                    NoireFocus.On(Target, focused: true, 1u, Corners);
                    NoireFocus.On(Target, focused: true, 1u, Brackets);
                    NoireFocus.On(Target, focused: true, 1u, Underline);
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void ArmsNeverMeetInTheMiddleOfAnEdge()
    {
        // A reach long enough for two arms to join turns corner ticks into a closed frame drawn the expensive way, and
        // the shape stops reading as corners at all. Asked for far more than the control can hold.
        var greedy = new FocusStyle { ArmLength = 500f };
        var small = new Vector2(40f, 20f);

        greedy.ResolveArmLength(small).Should().BeLessThan(small.Y * 0.5f);
    }

    [Fact]
    public void AFixedArmLengthOverridesTheRatio()
    {
        var byRatio = new FocusStyle { ArmRatio = 0.5f };
        var byLength = new FocusStyle { ArmRatio = 0.5f, ArmLength = 4f };
        var size = new Vector2(200f, 40f);

        byLength.ResolveArmLength(size).Should().NotBe(byRatio.ResolveArmLength(size));
        byLength.ResolveArmLength(size).Should().Be(NoireUI.Scaled(4f));
    }
}
