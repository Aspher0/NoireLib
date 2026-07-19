using FluentAssertions;
using NoireLib.HotkeyManager;
using System;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Characterization tests for the hotkey activation state machine (<see cref="NoireHotkeyManager.EvaluateActivation"/>).<br/>
/// This decision logic reads live game input in production, so these tests pin its behavior by driving it with
/// synthetic "combination active" / "key physically down" inputs and an explicit clock, one tick at a time, for
/// every activation mode. Any change to the trigger stream a mode produces must be a deliberate, visible change to
/// these tests, not a silent one.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireHotkeyManagerActivationTests : IDisposable
{
    private readonly NoireHotkeyManager manager =
        new(moduleId: null, active: false, enableLogging: false, shouldSaveKeybinds: false);

    public void Dispose() => manager.Dispose();

    private static HotkeyEntry Entry(HotkeyActivationMode mode, int holdMs = 400, int repeatMs = 80)
        => new()
        {
            Id = "t",
            ActivationMode = mode,
            HoldDelay = TimeSpan.FromMilliseconds(holdMs),
            FixedRepeatDelay = TimeSpan.FromMilliseconds(repeatMs),
            RepeatDelayMin = TimeSpan.FromMilliseconds(repeatMs),
            RepeatDelayMax = TimeSpan.FromMilliseconds(repeatMs),
        };

    // Drives one tick: the combination is active, the main key is down, at time t.
    private bool Tick(HotkeyEntry entry, bool comboActive, bool keyDown, long t)
        => manager.EvaluateActivation(entry, comboActive, keyDown, t);

    // ---------------------------------------------------------------- Pressed

    [Fact]
    public void Pressed_Triggers_Once_On_Key_Down_Then_Not_While_Held()
    {
        var e = Entry(HotkeyActivationMode.Pressed);

        Tick(e, true, true, 0).Should().BeTrue("a Pressed hotkey fires on the down edge");
        Tick(e, true, true, 16).Should().BeFalse("it does not fire again while held");
        Tick(e, true, true, 32).Should().BeFalse();
    }

    [Fact]
    public void Pressed_Triggers_Again_After_Release_And_Repress()
    {
        var e = Entry(HotkeyActivationMode.Pressed);

        Tick(e, true, true, 0).Should().BeTrue();
        Tick(e, false, false, 16).Should().BeFalse("release does not fire a Pressed hotkey");
        Tick(e, true, true, 32).Should().BeTrue("a fresh down edge fires again");
    }

    [Fact]
    public void Pressed_Does_Not_Trigger_When_Combination_Inactive_Even_If_Key_Physically_Down()
    {
        var e = Entry(HotkeyActivationMode.Pressed);

        Tick(e, false, true, 0).Should().BeFalse("the main key is down but the full combination is not satisfied");
    }

    // ---------------------------------------------------------------- Released

    [Fact]
    public void Released_Triggers_On_Release_After_An_Arming_Press()
    {
        var e = Entry(HotkeyActivationMode.Released);

        Tick(e, true, true, 0).Should().BeFalse("Released does not fire on the down edge");
        Tick(e, true, true, 16).Should().BeFalse();
        Tick(e, false, false, 32).Should().BeTrue("it fires on the up edge of an armed press");
    }

    [Fact]
    public void Released_Fires_Only_Once_Per_Press_Release_Cycle()
    {
        var e = Entry(HotkeyActivationMode.Released);

        Tick(e, true, true, 0).Should().BeFalse();
        Tick(e, false, false, 16).Should().BeTrue();
        Tick(e, false, false, 32).Should().BeFalse("a second release without a new press does not fire");
    }

    [Fact]
    public void Released_Does_Not_Fire_When_Released_Without_Ever_Being_Down()
    {
        var e = Entry(HotkeyActivationMode.Released);

        Tick(e, false, false, 0).Should().BeFalse("nothing was armed, so there is nothing to release");
    }

    // ---------------------------------------------------------------- Held

    [Fact]
    public void Held_Triggers_Once_After_The_Hold_Delay()
    {
        var e = Entry(HotkeyActivationMode.Held, holdMs: 400);

        Tick(e, true, true, 0).Should().BeFalse("the hold delay has not elapsed");
        Tick(e, true, true, 200).Should().BeFalse();
        Tick(e, true, true, 400).Should().BeTrue("the hold delay elapsed");
        Tick(e, true, true, 500).Should().BeFalse("Held fires once, not repeatedly");
    }

    [Fact]
    public void Held_Does_Not_Trigger_If_Released_Before_The_Delay()
    {
        var e = Entry(HotkeyActivationMode.Held, holdMs: 400);

        Tick(e, true, true, 0).Should().BeFalse();
        Tick(e, true, true, 200).Should().BeFalse();
        Tick(e, false, false, 300).Should().BeFalse("released before the delay elapsed");
    }

    [Fact]
    public void Held_Rearms_And_Triggers_Again_After_Release_And_Re_Hold()
    {
        var e = Entry(HotkeyActivationMode.Held, holdMs: 400);

        Tick(e, true, true, 0);
        Tick(e, true, true, 400).Should().BeTrue();
        Tick(e, false, false, 450).Should().BeFalse("release");
        Tick(e, true, true, 500).Should().BeFalse("fresh hold started, delay not yet elapsed");
        Tick(e, true, true, 900).Should().BeTrue("the new hold delay elapsed");
    }

    // ---------------------------------------------------------------- Repeat

    [Fact]
    public void Repeat_Triggers_Immediately_Then_On_The_Interval()
    {
        var e = Entry(HotkeyActivationMode.Repeat, repeatMs: 80);

        Tick(e, true, true, 0).Should().BeTrue("Repeat fires immediately on press");
        Tick(e, true, true, 16).Should().BeFalse("before the interval elapses");
        Tick(e, true, true, 80).Should().BeTrue("one interval later");
        Tick(e, true, true, 96).Should().BeFalse();
        Tick(e, true, true, 160).Should().BeTrue("the next interval");
    }

    [Fact]
    public void Repeat_Stops_On_Release()
    {
        var e = Entry(HotkeyActivationMode.Repeat, repeatMs: 80);

        Tick(e, true, true, 0).Should().BeTrue();
        Tick(e, false, false, 40).Should().BeFalse("released");
        Tick(e, false, false, 200).Should().BeFalse("still up, so no repeat");
    }

    // ---------------------------------------------------------------- HoldAndRepeat

    [Fact]
    public void HoldAndRepeat_Waits_The_Initial_Hold_Then_Repeats_On_The_Interval()
    {
        var e = Entry(HotkeyActivationMode.HoldAndRepeat, holdMs: 400, repeatMs: 80);

        Tick(e, true, true, 0).Should().BeFalse("nothing fires during the initial hold");
        Tick(e, true, true, 200).Should().BeFalse();
        Tick(e, true, true, 399).Should().BeFalse("still inside the hold delay");
        Tick(e, true, true, 400).Should().BeTrue("the first fire is at the end of the hold delay");
        Tick(e, true, true, 440).Should().BeFalse("before the repeat interval");
        Tick(e, true, true, 480).Should().BeTrue("one interval after the first fire");
        Tick(e, true, true, 560).Should().BeTrue("and again one interval later");
    }

    [Fact]
    public void HoldAndRepeat_Does_Not_Fire_If_Released_During_The_Initial_Hold()
    {
        var e = Entry(HotkeyActivationMode.HoldAndRepeat, holdMs: 400, repeatMs: 80);

        Tick(e, true, true, 0).Should().BeFalse();
        Tick(e, true, true, 300).Should().BeFalse();
        Tick(e, false, false, 350).Should().BeFalse("released before the hold delay elapsed");
    }

    [Fact]
    public void HoldAndRepeat_Restarts_The_Initial_Hold_After_Release_And_Re_Hold()
    {
        var e = Entry(HotkeyActivationMode.HoldAndRepeat, holdMs: 400, repeatMs: 80);

        Tick(e, true, true, 0);
        Tick(e, true, true, 400).Should().BeTrue("first hold elapsed");
        Tick(e, false, false, 450).Should().BeFalse("release resets the machine");
        Tick(e, true, true, 500).Should().BeFalse("a new hold delay is timed from the re-press");
        Tick(e, true, true, 900).Should().BeTrue("the new hold delay elapsed");
    }

    // ---------------------------------------------------------------- combination edges

    [Fact]
    public void Combination_Going_Inactive_While_Key_Stays_Down_Does_Not_Trigger()
    {
        var e = Entry(HotkeyActivationMode.Pressed);

        Tick(e, true, true, 0).Should().BeTrue("initial press");
        // The main key is still physically down, but the full combination lapsed (for instance an extra key joined
        // or a required modifier was released), so no re-trigger and the entry is not treated as freshly down.
        Tick(e, false, true, 16).Should().BeFalse();
        Tick(e, true, true, 32).Should().BeFalse("the physical key never lifted, so this is not a new down edge");
    }
}
