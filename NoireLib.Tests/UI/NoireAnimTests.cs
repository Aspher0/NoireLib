using FluentAssertions;
using NoireLib.UI;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the animation layer: the easing curves are pure and bounded, an eased value actually arrives at its target,
/// a spring settles instead of exploding, one-shots run once, and every one of them degrades correctly under
/// <see cref="NoireUI.ReducedMotion"/>.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireAnimTests : IDisposable
{
    /// <remarks>
    /// Nullable because an unset reduced motion is not the same as a false one: it follows the host. Assigning the
    /// value read back would leave an override behind where there had been none.
    /// </remarks>
    private readonly bool? originalReducedMotion = NoireUI.HasReducedMotionOverride ? NoireUI.ReducedMotion : null;

    private float time;
    private int frame;

    public NoireAnimTests()
    {
        NoireUI.TimeOverride = () => time;
        NoireUI.FrameOverride = () => frame;
        NoireUI.ReducedMotion = false;
        UiFrameState.Clear();
    }

    public void Dispose()
    {
        NoireUI.TimeOverride = null;
        NoireUI.FrameOverride = null;

        if (originalReducedMotion is { } motion)
            NoireUI.ReducedMotion = motion;
        else
            NoireUI.ClearReducedMotion();

        UiFrameState.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>Advances the shared clock and the frame counter together, as a real frame would.</summary>
    private void Advance(float seconds)
    {
        time += seconds;
        frame++;
    }

    #region Easing curves

    [Theory]
    [InlineData(UiEasing.Linear)]
    [InlineData(UiEasing.InSine)]
    [InlineData(UiEasing.OutSine)]
    [InlineData(UiEasing.InOutSine)]
    [InlineData(UiEasing.InQuad)]
    [InlineData(UiEasing.OutQuad)]
    [InlineData(UiEasing.InOutQuad)]
    [InlineData(UiEasing.InCubic)]
    [InlineData(UiEasing.OutCubic)]
    [InlineData(UiEasing.InOutCubic)]
    [InlineData(UiEasing.InQuart)]
    [InlineData(UiEasing.OutQuart)]
    [InlineData(UiEasing.InOutQuart)]
    [InlineData(UiEasing.InExpo)]
    [InlineData(UiEasing.OutExpo)]
    [InlineData(UiEasing.InOutExpo)]
    [InlineData(UiEasing.InBack)]
    [InlineData(UiEasing.OutBack)]
    [InlineData(UiEasing.InOutBack)]
    [InlineData(UiEasing.OutElastic)]
    [InlineData(UiEasing.OutBounce)]
    public void Apply_StartsAtZeroAndEndsAtOne(UiEasing easing)
    {
        easing.Apply(0f).Should().BeApproximately(0f, 1e-4f);
        easing.Apply(1f).Should().BeApproximately(1f, 1e-4f);
    }

    [Theory]
    [InlineData(UiEasing.Linear)]
    [InlineData(UiEasing.OutCubic)]
    [InlineData(UiEasing.InOutExpo)]
    [InlineData(UiEasing.OutBounce)]
    public void Apply_ClampsProgressOutsideZeroToOne(UiEasing easing)
    {
        easing.Apply(-5f).Should().BeApproximately(0f, 1e-4f);
        easing.Apply(5f).Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void Apply_OutCubic_MovesMostOfTheWayInTheFirstHalf()
    {
        UiEasing.OutCubic.Apply(0.5f).Should().BeGreaterThan(0.8f, "a decelerating curve is what makes interface motion feel responsive");
    }

    [Fact]
    public void Apply_TheBackAndElasticCurves_OvershootInTheMiddle()
    {
        UiEasing.OutBack.Apply(0.6f).Should().BeGreaterThan(1f);
        UiEasing.InBack.Apply(0.2f).Should().BeLessThan(0f);
    }

    #endregion

    #region Ease

    [Fact]
    public void Ease_FirstCall_StartsAtTheTargetInsteadOfAnimatingFromZero()
    {
        NoireAnim.Ease("widget", "hover", 1f, 0.2f)
            .Should().Be(1f, "a widget appearing already in its final state must not slide in from nowhere");
    }

    [Fact]
    public void Ease_MovesTowardANewTargetAndArrives()
    {
        NoireAnim.Ease("widget", "hover", 0f, 0.2f);

        // The frame a target changes on is the frame the curve starts, so no time has elapsed along it yet.
        Advance(0.1f);
        NoireAnim.Ease("widget", "hover", 1f, 0.2f).Should().Be(0f);

        Advance(0.1f);
        NoireAnim.Ease("widget", "hover", 1f, 0.2f).Should().BeGreaterThan(0f).And.BeLessThan(1f);

        Advance(0.2f);
        NoireAnim.Ease("widget", "hover", 1f, 0.2f).Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void Ease_TargetReversedMidway_ContinuesFromWhereItIs()
    {
        NoireAnim.Ease("widget", "hover", 0f, 0.2f);

        Advance(0.1f);
        NoireAnim.Ease("widget", "hover", 1f, 0.2f);

        Advance(0.1f);
        var midway = NoireAnim.Ease("widget", "hover", 1f, 0.2f);
        midway.Should().BeGreaterThan(0f).And.BeLessThan(1f);

        Advance(0.01f);
        NoireAnim.Ease("widget", "hover", 0f, 0.2f).Should().BeApproximately(midway, 1e-4f, "reversing starts the new curve from where the value already is");

        Advance(0.02f);
        NoireAnim.Ease("widget", "hover", 0f, 0.2f)
            .Should().BeLessThan(midway).And.BeGreaterThan(0f, "reversing must not snap back to the start");
    }

    [Fact]
    public void Ease_UnderReducedMotion_SnapsToTheTarget()
    {
        NoireAnim.Ease("widget", "hover", 0f, 0.2f);
        NoireUI.ReducedMotion = true;

        Advance(0.01f);

        NoireAnim.Ease("widget", "hover", 1f, 0.2f).Should().Be(1f);
    }

    [Fact]
    public void Ease_WithACustomCurve_UsesIt()
    {
        NoireAnim.Ease("widget", "custom", 0f, 0.2f, static t => t);

        Advance(0.1f);
        NoireAnim.Ease("widget", "custom", 1f, 0.2f, static t => t);

        Advance(0.1f);
        NoireAnim.Ease("widget", "custom", 1f, 0.2f, static t => t)
            .Should().BeApproximately(0.5f, 1e-3f, "a linear curve of my own must be honoured exactly");
    }

    [Fact]
    public void Ease_WithANullCurve_Throws()
    {
        var act = () => NoireAnim.Ease("widget", "custom", 1f, 0.2f, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Presence_TracksVisibilityBetweenZeroAndOne()
    {
        NoireAnim.Presence("panel", "open", false, 0.2f).Should().Be(0f);

        Advance(0.05f);
        NoireAnim.Presence("panel", "open", true, 0.2f).Should().BeInRange(0f, 1f);

        Advance(0.3f);
        NoireAnim.Presence("panel", "open", true, 0.2f).Should().BeApproximately(1f, 1e-4f);
    }

    #endregion

    #region Spring

    [Fact]
    public void Spring_SettlesOnItsTarget()
    {
        NoireAnim.Spring("panel", "offset", 0f);

        for (var i = 0; i < 200; i++)
        {
            Advance(1f / 60f);
            NoireAnim.Spring("panel", "offset", 100f);
        }

        NoireAnim.Spring("panel", "offset", 100f).Should().BeApproximately(100f, 0.5f);
    }

    [Fact]
    public void Spring_StaysFiniteOverALongFrame()
    {
        NoireAnim.Spring("panel", "offset", 0f);

        // DeltaTime is clamped, but the sub-stepping is what keeps a stiff spring stable across a stalled frame.
        for (var i = 0; i < 20; i++)
        {
            Advance(0.1f);
            NoireAnim.Spring("panel", "offset", 100f, stiffness: 900f, damping: 10f);
        }

        var value = NoireAnim.Spring("panel", "offset", 100f, stiffness: 900f, damping: 10f);
        float.IsFinite(value).Should().BeTrue();
        value.Should().BeInRange(-500f, 700f);
    }

    [Fact]
    public void Spring_UnderReducedMotion_SnapsToTheTarget()
    {
        NoireUI.ReducedMotion = true;

        NoireAnim.Spring("panel", "offset", 250f).Should().Be(250f);
    }

    #endregion

    #region Periodic

    [Fact]
    public void Pulse_StaysWithinItsBounds()
    {
        for (var i = 0; i < 40; i++)
        {
            Advance(0.05f);
            NoireAnim.Pulse(1f, 0.2f, 0.8f).Should().BeInRange(0.2f, 0.8f);
        }
    }

    [Fact]
    public void Pulse_UnderReducedMotion_HoldsAtTheHighEnd()
    {
        NoireUI.ReducedMotion = true;

        NoireAnim.Pulse(1f, 0.2f, 0.8f).Should().Be(0.8f, "the highlight stays visible, it just stops moving");
    }

    [Fact]
    public void Sweep_WrapsAroundWithoutGoingNegative()
    {
        for (var i = 0; i < 40; i++)
        {
            Advance(0.13f);
            NoireAnim.Sweep(0.5f).Should().BeInRange(0f, 1f);
        }
    }

    [Fact]
    public void Spin_StaysWithinOneTurn()
    {
        for (var i = 0; i < 40; i++)
        {
            Advance(0.37f);
            NoireAnim.Spin(2f).Should().BeInRange(0f, 1f);
        }
    }

    [Fact]
    public void Spin_CompletesOneTurnPerPeriod()
    {
        Advance(0.5f);
        var quarter = NoireAnim.Spin(2f);

        Advance(0.5f);
        var half = NoireAnim.Spin(2f);

        quarter.Should().BeApproximately(0.25f, 0.0001f);
        half.Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void Spin_UnderReducedMotion_StandsStill()
    {
        NoireUI.ReducedMotion = true;
        Advance(5f);

        NoireAnim.Spin(2f).Should().Be(0f, "a rotation has no finished position to park at, so it rests at none");
    }

    [Fact]
    public void Spin_WithoutAPeriod_StandsStill()
    {
        Advance(5f);

        NoireAnim.Spin(0f).Should().Be(0f);
        NoireAnim.Spin(-1f).Should().Be(0f);
    }

    #endregion

    #region One-shots

    [Fact]
    public void Progress_WithoutATrigger_ReadsAsFinished()
    {
        NoireAnim.Progress("button", "saved", 0.5f).Should().Be(1f);
        NoireAnim.IsRunning("button", "saved", 0.5f).Should().BeFalse();
    }

    [Fact]
    public void Progress_AfterATrigger_RunsOnceThenFinishes()
    {
        NoireAnim.Trigger("button", "saved");

        NoireAnim.Progress("button", "saved", 0.5f).Should().Be(0f);
        NoireAnim.IsRunning("button", "saved", 0.5f).Should().BeTrue();

        Advance(0.25f);
        NoireAnim.Progress("button", "saved", 0.5f).Should().BeApproximately(0.5f, 1e-4f);

        Advance(0.5f);
        NoireAnim.Progress("button", "saved", 0.5f).Should().Be(1f);
        NoireAnim.IsRunning("button", "saved", 0.5f).Should().BeFalse();
    }

    [Fact]
    public void Flash_FallsFromOneToZero()
    {
        NoireAnim.Trigger("button", "saved");
        NoireAnim.Flash("button", "saved", 0.5f).Should().BeApproximately(1f, 1e-4f);

        Advance(0.6f);
        NoireAnim.Flash("button", "saved", 0.5f).Should().Be(0f);
    }

    [Fact]
    public void Shake_DiesOutAndReturnsToZero()
    {
        NoireAnim.Trigger("field", "rejected");

        var moved = false;
        for (var i = 0; i < 10; i++)
        {
            Advance(0.02f);
            if (MathF.Abs(NoireAnim.Shake("field", "rejected", 0.4f)) > 0.01f)
                moved = true;
        }

        moved.Should().BeTrue();

        Advance(1f);
        NoireAnim.Shake("field", "rejected", 0.4f).Should().Be(0f);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FlashAndShake_UnderReducedMotion_DoNothing(bool triggered)
    {
        NoireUI.ReducedMotion = true;

        if (triggered)
            NoireAnim.Trigger("field", "rejected");

        NoireAnim.Flash("field", "rejected", 0.5f).Should().Be(0f);
        NoireAnim.Shake("field", "rejected", 0.4f).Should().Be(0f);
    }

    [Fact]
    public void Reset_ClearsTheStoredState()
    {
        NoireAnim.Ease("widget", "hover", 0f, 0.2f);
        Advance(0.05f);
        NoireAnim.Ease("widget", "hover", 1f, 0.2f);

        NoireAnim.Reset("widget", "hover");

        NoireAnim.Ease("widget", "hover", 1f, 0.2f).Should().Be(1f, "a reset animation starts from scratch at its target");
    }

    #endregion
}
