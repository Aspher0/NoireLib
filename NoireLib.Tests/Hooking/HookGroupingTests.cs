using FluentAssertions;
using NoireLib.Hooking;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks how a hook joins and leaves a group. A group handle is a live view over the registry rather than a
/// collection that was captured when it was taken, so a hook that changes group afterwards has to move with it.
/// </summary>
public sealed class HookGroupingTests : IDisposable
{
    private readonly string first = $"first-{Guid.NewGuid():N}";
    private readonly string second = $"second-{Guid.NewGuid():N}";
    private readonly GroupedSpy spy = new();

    public HookGroupingTests() => HookRegistry.Register(spy);

    public void Dispose() => HookRegistry.Unregister(spy);

    [Fact]
    public void AGroupHandleTakenBeforeTheHookJoined_StillSeesIt()
    {
        var handle = NoireHook.Group(first);

        handle.Hooks.Should().BeEmpty();

        spy.Group = first;

        handle.Hooks.Should().ContainSingle().Which.Should().BeSameAs(spy);
    }

    [Fact]
    public void MovingBetweenGroups_LeavesTheOldOneBehind()
    {
        spy.Group = first;
        spy.Group = second;

        NoireHook.Group(first).Hooks.Should().BeEmpty();
        NoireHook.Group(second).Hooks.Should().ContainSingle();
    }

    [Fact]
    public void DisablingAGroup_ReachesTheHooksInIt()
    {
        spy.Group = first;
        spy.Enable();

        NoireHook.Group(first).Disable();

        spy.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void DisablingAGroup_LeavesHooksOutsideItAlone()
    {
        spy.Group = second;
        spy.Enable();

        NoireHook.Group(first).Disable();

        spy.IsEnabled.Should().BeTrue();
    }

    private sealed class GroupedSpy : INoireHook
    {
        public string Name => "grouped spy";

        public string? Group { get; set; }

        public HookState State => HookState.Installed;

        public nint Address => 0;

        public HookTarget Target { get; } = HookTarget.Address(0x4000);

        public HookIdentity? Identity => null;

        public HookVerificationResult Verification { get; } = HookVerificationResult.Skipped(typeof(Action));

        public HookStats Stats { get; } = new();

        public bool CollectsStats { get; set; }

        public Type DelegateType => typeof(Action);

        public bool IsEnabled { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool IsGuarded => false;

        public string BackendName => "spy";

        public System.Collections.Generic.IReadOnlyCollection<string> StateCallbackKeys => [];

        public event Action<INoireHook, HookEvent>? OnHookEvent;

        public void AddStateCallback(string key, Action<INoireHook, HookEvent> callback) => OnHookEvent?.Invoke(this, HookEvent.Installed);

        public bool ContainsStateCallback(string key) => false;

        public bool RemoveStateCallback(string key) => false;

        public void ClearStateCallbacks()
        {
        }

        public void Enable() => IsEnabled = true;

        public void Disable() => IsEnabled = false;

        public bool SetEnabled(bool enabled)
        {
            if (enabled == IsEnabled)
                return false;

            IsEnabled = enabled;
            return true;
        }

        public bool Toggle()
        {
            IsEnabled = !IsEnabled;
            return IsEnabled;
        }

        public IDisposable EnabledScope() => new HookStateScope([this], true);

        public IDisposable DisabledScope() => new HookStateScope([this], false);

        public void Dispose() => IsDisposed = true;
    }
}
