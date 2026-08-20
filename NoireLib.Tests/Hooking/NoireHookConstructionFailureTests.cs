using FluentAssertions;
using NoireLib.Hooking;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks what happens when a constructor throws. A hook registers itself before it resolves anything, so a
/// failure has to take that registration back out: the caller never receives the instance, and a hook that does
/// not exist must not keep appearing in the registry, the diagnostics, the duplicate-address check or the
/// shutdown callbacks.
/// </summary>
public sealed class NoireHookConstructionFailureTests
{
    private delegate int NotAClientStructsDelegate(int value);

    [Fact]
    public void AFailedResolve_LeavesNothingBehindInTheRegistry()
    {
        var before = NoireHook.Count;

        var act = () => new NoireHook<NotAClientStructsDelegate>(static value => value);

        act.Should().Throw<InvalidOperationException>(
            "a delegate that is not declared by XIVClientStructs has no address to resolve");

        NoireHook.Count.Should().Be(before);
    }

    [Fact]
    public void AFailedResolve_ReportsWhichDelegateCouldNotResolve()
    {
        var act = () => new NoireHook<NotAClientStructsDelegate>(static value => value);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NotAClientStructsDelegate*", "the message has to name the delegate the caller wrote");
    }
}
