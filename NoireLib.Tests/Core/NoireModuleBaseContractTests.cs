using FluentAssertions;
using NoireLib.Core.Modules;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Every module is disposable, and instance counters stay unique among live modules and restart once all are disposed.</summary>
[SupportedOSPlatform("windows")]
public class NoireModuleBaseContractTests
{
    // Each test uses its own module identifier: counters are process-wide per module type and identifier.
    private sealed class ContractModule : NoireModuleBase<ContractModule>
    {
        public ContractModule() : base((string?)null, false, false) { }
        public ContractModule(string moduleId) : base(moduleId, false, false) { }

        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() { /* no-op */ }
        protected override void DisposeInternal() { /* no-op */ }
    }

    [Fact]
    public void Module_IsDisposable_ThroughTheModuleInterface()
    {
        typeof(IDisposable).IsAssignableFrom(typeof(INoireModule)).Should().BeTrue();

        var module = new ContractModule("disposable-through-interface");

        using (IDisposable disposable = module) { }

        module.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Module_CollectedAsDisposable_IsDisposedByTheList()
    {
        var modules = new List<IDisposable> { new ContractModule("disposable-list-a"), new ContractModule("disposable-list-b") };

        foreach (var disposable in modules)
            disposable.Dispose();

        foreach (var disposable in modules)
            ((ContractModule)disposable).IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void InstanceCounter_AfterEveryInstanceIsDisposed_StartsOverAtZero()
    {
        var first = new ContractModule("counter-restart");
        var second = new ContractModule("counter-restart");

        first.InstanceCounter.Should().Be(0);
        second.InstanceCounter.Should().Be(1);

        first.Dispose();
        second.Dispose();

        var next = new ContractModule("counter-restart");

        next.InstanceCounter.Should().Be(0);
        next.GetUniqueIdentifier().Should().EndWith("_counter-restart");
        next.Dispose();
    }

    [Fact]
    public void InstanceCounter_WhileAnotherInstanceIsLive_NeverRepeatsALiveCounter()
    {
        var first = new ContractModule("counter-live");
        var second = new ContractModule("counter-live");

        first.Dispose();

        var third = new ContractModule("counter-live");

        third.InstanceCounter.Should().NotBe(second.InstanceCounter);
        third.InstanceCounter.Should().Be(2);

        second.Dispose();
        third.Dispose();
    }

    [Fact]
    public void InstanceCounter_ModuleIdChangedAfterConstruction_ReleasesTheIdentifierItWasCountedUnder()
    {
        var module = new ContractModule("counter-renamed");
        module.ModuleId = "counter-renamed-elsewhere";

        module.Dispose();

        new ContractModule("counter-renamed").InstanceCounter.Should().Be(0);
    }

    [Fact]
    public void InstanceCounter_DisposedTwice_ReleasesOnlyOnce()
    {
        var first = new ContractModule("counter-double-dispose");
        var second = new ContractModule("counter-double-dispose");

        first.Dispose();
        first.Dispose();

        var third = new ContractModule("counter-double-dispose");

        third.InstanceCounter.Should().Be(2, "the second instance is still live, so its counter must not be released early");

        second.Dispose();
        third.Dispose();
    }
}
