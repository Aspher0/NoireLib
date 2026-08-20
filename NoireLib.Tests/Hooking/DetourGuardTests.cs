using FluentAssertions;
using NoireLib.Hooking;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the generated detour guard, which is the piece that decides what the game sees when a detour throws.
/// The wrapper is emitted as IL, so nothing here can be checked by reading the source: every mode, the pointer
/// signature, the fault counters and the fault limit are exercised against real generated methods.
/// </summary>
public sealed class DetourGuardTests
{
    private delegate int AddDelegate(int left, int right);

    private delegate void RecordDelegate(int value);

    private unsafe delegate int ReadDelegate(int* value);

    private delegate string? NameDelegate();

    [Fact]
    public void Wrap_WithNothingToDo_InstallsTheDetourUnchanged()
    {
        var context = CreateContext<AddDelegate>((left, right) => left + right);

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.None, out var guarded);

        wrapped.Should().BeSameAs(context.Detour, "no guard, no stats and no fault limit means there is nothing for a wrapper to do");
        guarded.Should().BeFalse();
    }

    [Fact]
    public void Wrap_CallOriginal_SwallowsTheExceptionAndReturnsWhatTheOriginalReturns()
    {
        var originalCalls = 0;
        var context = CreateContext<AddDelegate>((_, _) => throw new InvalidOperationException("detour failed"));
        context.Original = (left, right) =>
        {
            originalCalls++;
            return left + right;
        };

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out var guarded);

        wrapped(2, 3).Should().Be(5);
        originalCalls.Should().Be(1);
        guarded.Should().BeTrue();
        context.Stats.FaultCount.Should().Be(1);
    }

    [Fact]
    public void Wrap_CallOriginal_ReturnsTheDefaultWhenNoOriginalIsAssignedYet()
    {
        var context = CreateContext<AddDelegate>((_, _) => throw new InvalidOperationException("detour failed"));

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);

        wrapped(2, 3).Should().Be(0, "a detour that faults before the hook finished creating has no original to fall through to");
    }

    [Fact]
    public void Wrap_ReturnDefault_DoesNotCallTheOriginal()
    {
        var originalCalls = 0;
        var context = CreateContext<AddDelegate>((_, _) => throw new InvalidOperationException("detour failed"));
        context.Original = (_, _) =>
        {
            originalCalls++;
            return 99;
        };

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.ReturnDefault, out _);

        wrapped(2, 3).Should().Be(0);
        originalCalls.Should().Be(0);
    }

    [Fact]
    public void Wrap_Rethrow_LetsTheExceptionOutAfterRecordingIt()
    {
        var context = CreateContext<AddDelegate>((_, _) => throw new InvalidOperationException("detour failed"));

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.Rethrow, out _);

        var act = () => wrapped(2, 3);

        act.Should().Throw<InvalidOperationException>().WithMessage("detour failed");
        context.Stats.FaultCount.Should().Be(1);
    }

    [Fact]
    public void Wrap_VoidDetour_RunsAndRecordsWithoutAReturnValue()
    {
        var seen = 0;
        var context = CreateContext<RecordDelegate>(value => seen = value);
        context.CollectStats = true;

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);
        wrapped(7);

        seen.Should().Be(7);
        context.Stats.CallCount.Should().Be(1);
    }

    [Fact]
    public void Wrap_ReferenceReturn_ReturnsNullOnFault()
    {
        var context = CreateContext<NameDelegate>(() => throw new InvalidOperationException("detour failed"));

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.ReturnDefault, out _);

        wrapped().Should().BeNull();
    }

    [Fact]
    public unsafe void Wrap_PointerParameter_PassesThePointerThrough()
    {
        var context = CreateContext<ReadDelegate>(static value => *value * 2);
        context.Original = static value => *value;

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);

        var source = 21;
        wrapped(&source).Should().Be(42, "the emitted wrapper must carry a pointer argument through unchanged");
    }

    [Fact]
    public unsafe void Wrap_PointerParameter_FallsThroughToTheOriginalOnFault()
    {
        var context = CreateContext<ReadDelegate>(static _ => throw new InvalidOperationException("detour failed"));
        context.Original = static value => *value;

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);

        var source = 13;
        wrapped(&source).Should().Be(13);
    }

    [Fact]
    public void Wrap_CollectStats_CountsEveryCallThatDidNotThrow()
    {
        var context = CreateContext<AddDelegate>((left, right) => left + right);
        context.CollectStats = true;

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);
        wrapped(1, 1);
        wrapped(2, 2);

        context.Stats.CallCount.Should().Be(2);
        context.Stats.FaultCount.Should().Be(0);
    }

    [Fact]
    public void Wrap_AfterAFault_ResetsTheConsecutiveCountOnTheNextGoodCall()
    {
        var shouldThrow = true;
        var context = CreateContext<AddDelegate>((left, right) => shouldThrow ? throw new InvalidOperationException("detour failed") : left + right);
        context.Original = (left, right) => left + right;

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);

        wrapped(1, 1);
        context.Stats.ConsecutiveFaults.Should().Be(1);

        shouldThrow = false;
        wrapped(1, 1);
        context.Stats.ConsecutiveFaults.Should().Be(0);
    }

    [Fact]
    public void Wrap_FaultLimit_ReportsOnceTheDetourHasThrownThatManyTimesInARow()
    {
        var disabled = 0;
        var context = CreateContext<AddDelegate>((_, _) => throw new InvalidOperationException("detour failed"));
        context.FaultLimit = 2;
        context.OnFaultLimitReached = () => disabled++;

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);

        wrapped(1, 1);
        disabled.Should().Be(0, "one fault is below the limit");

        wrapped(1, 1);
        disabled.Should().Be(1);
    }

    /// <summary>
    /// The window switches counting on for a hook that is already installed, so the wrapper cannot depend on the
    /// timing local it only emits when stats were on at construction.
    /// </summary>
    [Fact]
    public void Wrap_CountingSwitchedOnAfterTheFact_StartsCountingWithoutTimings()
    {
        var context = CreateContext<AddDelegate>((left, right) => left + right);
        context.CollectStats = false;

        var wrapped = DetourGuardFactory.Wrap(context, HookGuardMode.CallOriginal, out _);

        wrapped(1, 1);
        context.Stats.CallCount.Should().Be(0);

        context.CollectStats = true;
        wrapped(1, 1);

        context.Stats.CallCount.Should().Be(1);
        context.Stats.TotalDetourTime.Should().Be(TimeSpan.Zero, "the wrapper was built without a timestamp to read");
    }

    [Fact]
    public void CreatePassthrough_CallsTheOriginalAndCountsTheCall()
    {
        var context = CreateContext<AddDelegate>((_, _) => throw new InvalidOperationException("an observer never calls its detour"));
        context.CollectStats = true;
        context.Original = (left, right) => left + right;

        var passthrough = DetourGuardFactory.CreatePassthrough(context);

        passthrough(20, 22).Should().Be(42);
        context.Stats.CallCount.Should().Be(1);
    }

    [Fact]
    public void CreatePassthrough_WithoutAnOriginal_ReturnsTheDefaultAndCountsNothing()
    {
        var context = CreateContext<AddDelegate>((_, _) => 0);
        context.CollectStats = true;

        var passthrough = DetourGuardFactory.CreatePassthrough(context);

        passthrough(1, 1).Should().Be(0);
        context.Stats.CallCount.Should().Be(0, "a call that never reached the original is not a call to the function");
    }

    private static HookGuardContext<TDelegate> CreateContext<TDelegate>(TDelegate detour)
        where TDelegate : Delegate
        => new()
        {
            Detour = detour,
            Stats = new HookStats(),
            Name = typeof(TDelegate).Name,
            FaultLogInterval = TimeSpan.Zero,
        };
}
