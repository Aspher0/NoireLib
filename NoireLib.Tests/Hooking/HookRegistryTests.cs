using FluentAssertions;
using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the registry and the group handles. The scope behaviour is the subtle one: leaving a scope restores what
/// each hook was doing before it was entered, rather than turning the whole group back on.
/// </summary>
public sealed class HookRegistryTests : IDisposable
{
    private readonly string group = $"test-{Guid.NewGuid():N}";
    private readonly HookSpy[] spies;

    public HookRegistryTests()
    {
        spies =
        [
            new HookSpy("first", null, 0x1000),
            new HookSpy("second", null, 0x2000),
        ];
    }

    public void Dispose()
    {
        foreach (var spy in spies)
            HookRegistry.Unregister(spy);
    }

    [Fact]
    public void Register_PutsTheHookInTheSnapshot()
    {
        var hook = Grouped(spies[0]);

        HookRegistry.Register(hook);

        HookRegistry.Snapshot().Should().Contain(hook);
    }

    [Fact]
    public void Unregister_TakesTheHookBackOut()
    {
        var hook = Grouped(spies[0]);
        HookRegistry.Register(hook);

        HookRegistry.Unregister(hook);

        HookRegistry.Snapshot().Should().NotContain(hook);
    }

    [Fact]
    public void Register_MovesTheVersionOnSoACachedViewRebuilds()
    {
        var before = HookRegistry.Version;

        HookRegistry.Register(Grouped(spies[0]));

        HookRegistry.Version.Should().NotBe(before);
    }

    [Fact]
    public void Group_ReturnsOnlyTheHooksCarryingItsName()
    {
        HookRegistry.Register(Grouped(spies[0]));
        HookRegistry.Register(new HookSpy("elsewhere", $"other-{Guid.NewGuid():N}", 0x3000));

        var handle = NoireHook.Group(group);

        handle.Hooks.Should().ContainSingle().Which.Name.Should().Be("first");
    }

    [Fact]
    public void GroupSetEnabled_AppliesToEveryHookInTheGroup()
    {
        HookRegistry.Register(Grouped(spies[0]));
        HookRegistry.Register(Grouped(spies[1]));

        NoireHook.Group(group).Enable();

        spies.Should().OnlyContain(spy => spy.IsEnabled);
    }

    [Fact]
    public void GroupDisabledScope_RestoresEachHooksOwnStateRatherThanABlanketState()
    {
        spies[0].Enable();
        spies[1].Disable();
        HookRegistry.Register(Grouped(spies[0]));
        HookRegistry.Register(Grouped(spies[1]));

        using (NoireHook.Group(group).DisabledScope())
        {
            spies[0].IsEnabled.Should().BeFalse();
            spies[1].IsEnabled.Should().BeFalse();
        }

        spies[0].IsEnabled.Should().BeTrue("it was enabled when the scope was entered");
        spies[1].IsEnabled.Should().BeFalse("it was disabled when the scope was entered");
    }

    [Fact]
    public void StateCallbacks_ReplaceByKeyAndCanBeRemovedWithoutHoldingTheDelegate()
    {
        var spy = spies[0];
        var first = 0;
        var second = 0;

        spy.AddStateCallback("watch", (_, _) => first++);
        spy.AddStateCallback("watch", (_, _) => second++);

        spy.StateCallbackKeys.Should().ContainSingle().Which.Should().Be("watch");

        spy.Dispose();

        first.Should().Be(0, "registering the same key again replaces the callback rather than adding a second");
        second.Should().Be(1);

        spy.RemoveStateCallback("watch").Should().BeTrue();
        spy.ContainsStateCallback("watch").Should().BeFalse();
    }

    /// <summary>
    /// A retry left behind keeps the framework pump attached for the rest of the session, running every frame for
    /// a hook that already installed or was disposed.
    /// </summary>
    [Fact]
    public void RemovePending_TakesTheRetryBackOutSoThePumpCanDetach()
    {
        var before = HookRegistry.PendingCount;
        var retry = new Action(() => { });

        HookRegistry.AddPending(spies[0], retry);
        HookRegistry.PendingCount.Should().Be(before + 1);

        HookRegistry.RemovePending(retry);
        HookRegistry.PendingCount.Should().Be(before);
    }

    [Fact]
    public void RemovePending_OnlyTakesOutTheRetryItWasGiven()
    {
        var before = HookRegistry.PendingCount;
        var first = new Action(() => { });
        var second = new Action(() => { });

        HookRegistry.AddPending(spies[0], first);
        HookRegistry.AddPending(spies[1], second);

        HookRegistry.RemovePending(first);
        HookRegistry.PendingCount.Should().Be(before + 1);

        HookRegistry.RemovePending(second);
        HookRegistry.PendingCount.Should().Be(before);
    }

    [Fact]
    public void Find_LocatesALiveHookByName()
    {
        var hook = Grouped(spies[0]);
        HookRegistry.Register(hook);

        NoireHook.Find("first").Should().BeSameAs(hook);
    }

    [Fact]
    public void AtAddress_LocatesTheHookInstalledOnAnAddress()
    {
        var hook = Grouped(spies[1]);
        HookRegistry.Register(hook);

        NoireHook.AtAddress(0x2000).Should().BeSameAs(hook);
    }

    [Fact]
    public void GroupNames_ListsEveryGroupInUseWithoutRepeatingOne()
    {
        HookRegistry.Register(Grouped(spies[0]));
        HookRegistry.Register(Grouped(spies[1]));

        NoireHook.GroupNames.Count(name => name == group).Should().Be(1);
    }

    private HookSpy Grouped(HookSpy spy)
    {
        spy.Group = group;
        return spy;
    }

    private sealed class HookSpy(string name, string? group, nint address) : INoireHook
    {
        private readonly Dictionary<string, Action<INoireHook, HookEvent>> callbacks = new(StringComparer.Ordinal);

        public string Name { get; } = name;

        public string? Group { get; set; } = group;

        public HookState State { get; private set; } = HookState.Installed;

        public nint Address { get; } = address;

        public HookTarget Target { get; } = HookTarget.Address(address);

        public HookIdentity? Identity => null;

        public HookVerificationResult Verification { get; } = HookVerificationResult.Skipped(typeof(Action));

        public HookStats Stats { get; } = new();

        public bool CollectsStats { get; set; }

        public Type DelegateType => typeof(Action);

        public bool IsEnabled { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool IsGuarded => false;

        public string BackendName => "spy";

        public IReadOnlyCollection<string> StateCallbackKeys => callbacks.Keys.ToArray();

        public event Action<INoireHook, HookEvent>? OnHookEvent;

        public void AddStateCallback(string key, Action<INoireHook, HookEvent> callback) => callbacks[key] = callback;

        public bool ContainsStateCallback(string key) => callbacks.ContainsKey(key);

        public bool RemoveStateCallback(string key) => callbacks.Remove(key);

        public void ClearStateCallbacks() => callbacks.Clear();

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

        public void Dispose()
        {
            IsDisposed = true;
            State = HookState.Disposed;

            foreach (var callback in callbacks.Values.ToArray())
                callback(this, HookEvent.Disposed);

            OnHookEvent?.Invoke(this, HookEvent.Disposed);
        }
    }
}
