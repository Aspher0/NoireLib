using FluentAssertions;
using NoireLib.Hooking;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the defaults a hook created with no configuration inherits. Two of them are deliberate and load-bearing:
/// a mismatched delegate never becomes a live hook, and a detour that throws does not reach the game.
/// </summary>
public sealed class HookOptionsTests
{
    [Fact]
    public void Defaults_RefuseAMismatchedDelegateOutright()
        => new HookOptions().Verification.Should().Be(HookVerificationPolicy.Throw);

    [Fact]
    public void Defaults_KeepAFaultingDetourAwayFromTheGame()
        => new HookOptions().Guard.Should().Be(HookGuardMode.CallOriginal);

    [Fact]
    public void Defaults_DoNotEnableTheHookOrChargeForInstrumentation()
    {
        var options = new HookOptions();

        options.AutoEnable.Should().BeFalse();
        options.CollectStats.Should().BeFalse();
        options.FaultLimit.Should().Be(0);
        options.StrictVerification.Should().BeFalse();
    }

    [Fact]
    public void Defaults_CleanUpWithTheLibrary()
        => new HookOptions().AutoDispose.Should().BeTrue();

    [Fact]
    public void Defaults_GiveADeferredTargetABoundedWaitRatherThanForever()
        => new HookOptions().ResolveTimeout.Should().Be(TimeSpan.FromSeconds(30));

    [Fact]
    public void Clone_ProducesACopyThatDoesNotShareState()
    {
        var options = new HookOptions { Name = "original", Group = "first", FaultLimit = 3 };

        var copy = options.Clone();
        copy.Name = "copy";
        copy.Group = "second";

        options.Name.Should().Be("original");
        options.Group.Should().Be("first");
        copy.FaultLimit.Should().Be(3);
    }
}
