using FluentAssertions;
using NoireLib.Hooking;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins <see cref="HookGuardMode.None"/> when the factory still wraps the detour for stats or fault counting: the
/// exception keeps flying, as it would from a raw detour. Counting a fault is allowed, swallowing it is not, since
/// <see cref="NoireHook{TDelegate}.IsGuarded"/> reports false.
/// </summary>
public sealed class DetourGuardModeTests
{
    private delegate int ProbeDelegate(int value);

    private static HookGuardContext<ProbeDelegate> ContextFor(ProbeDelegate detour, bool collectStats, int faultLimit)
        => new()
        {
            Detour = detour,
            Stats = new HookStats(),
            Name = "guard-mode-probe",
            FaultLimit = faultLimit,
            CollectStats = collectStats,
            OnFaultLimitReached = static () => { },
        };

    [Fact]
    public void ModeNone_WithNothingToRecord_InstallsTheRawDetour()
    {
        ProbeDelegate detour = static value => value + 1;
        var context = ContextFor(detour, collectStats: false, faultLimit: 0);

        var installed = DetourGuardFactory.Wrap(context, HookGuardMode.None, out var guarded);

        guarded.Should().BeFalse();
        installed.Should().BeSameAs(detour, "with no stats and no fault limit there is nothing to wrap");
    }

    [Fact]
    public void ModeNone_WithStats_StillLetsTheExceptionFly()
    {
        var context = ContextFor(static _ => throw new InvalidOperationException("detour fault"), collectStats: true, faultLimit: 0);

        var installed = DetourGuardFactory.Wrap(context, HookGuardMode.None, out var guarded);

        guarded.Should().BeFalse("mode None reports unguarded whatever else the wrapper records");

        var act = () => installed(42);
        act.Should().Throw<InvalidOperationException>(
            "an unguarded hook must not swallow the detour's exception just because stats wrap it");
        context.Stats.ConsecutiveFaults.Should().Be(1, "the fault is still counted on its way through");
    }

    [Fact]
    public void ModeNone_WithAFaultLimit_StillLetsTheExceptionFly()
    {
        var context = ContextFor(static _ => throw new InvalidOperationException("detour fault"), collectStats: false, faultLimit: 5);

        var installed = DetourGuardFactory.Wrap(context, HookGuardMode.None, out var guarded);

        guarded.Should().BeFalse();

        var act = () => installed(42);
        act.Should().Throw<InvalidOperationException>(
            "fault counting exists to disable a repeat offender, not to change what the caller observes");
    }
}
