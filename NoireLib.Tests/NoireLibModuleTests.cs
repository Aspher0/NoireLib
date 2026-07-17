using Dalamud.Interface.Windowing;
using FluentAssertions;
using NoireLib.Core.Modules;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Covers how <see cref="NoireLibMain"/> registers, retrieves and removes modules, and the contract
/// <see cref="NoireModuleBase{TModule}"/> owes every module that derives from it.<br/>
/// The disposal invariants matter library-wide: disposal is terminal and runs a module's teardown exactly once,
/// it does not run the deactivation hook that modules already tear down in, and it leaves no disposed module
/// claiming to be active for the guards that read that flag.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireLibModuleTests
{
    private class DummyModule : NoireModuleBase<DummyModule>
    {
        public DummyModule() : base() { }
        public DummyModule(ModuleId moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }
        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() { /* no-op */ }
        protected override void DisposeInternal() { /* no-op */ }
    }

    /// <summary>
    /// Records every lifecycle hook the base runs, so that a test can state exactly which hooks a given
    /// operation is allowed to reach.
    /// </summary>
    private sealed class CountingModule : NoireModuleBase<CountingModule>
    {
        public int ActivatedCount { get; private set; }
        public int DeactivatedCount { get; private set; }
        public int DisposeInternalCount { get; private set; }

        public CountingModule() : base((string?)null, false, false) { }
        public CountingModule(ModuleId? moduleId, bool active = true, bool enableLogging = false) : base(moduleId, active, enableLogging) { }

        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() => ActivatedCount++;
        protected override void OnDeactivated() => DeactivatedCount++;
        protected override void DisposeInternal() => DisposeInternalCount++;
    }

    /// <summary>
    /// Reproduces the shape of a module that deactivates itself as part of its own teardown, which relies on the
    /// base leaving the module readable as active until <see cref="NoireModuleBase{TModule}.DisposeInternal"/>
    /// has run.
    /// </summary>
    private sealed class SelfDeactivatingModule : NoireModuleBase<SelfDeactivatingModule>
    {
        public int DeactivatedCount { get; private set; }
        public bool WasActiveInsideDisposeInternal { get; private set; }

        public SelfDeactivatingModule() : base((string?)null, false, false) { }
        public SelfDeactivatingModule(ModuleId? moduleId, bool active = true, bool enableLogging = false) : base(moduleId, active, enableLogging) { }

        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() => DeactivatedCount++;

        protected override void DisposeInternal()
        {
            WasActiveInsideDisposeInternal = IsActive;
            SetActive(false);
        }
    }

    /// <summary>
    /// Throws out of its teardown, so that a test can state what the base guarantees when a module fails partway
    /// through disposal.
    /// </summary>
    private sealed class ThrowingDisposeModule : NoireModuleBase<ThrowingDisposeModule>
    {
        public ThrowingDisposeModule() : base((string?)null, false, false) { }
        public ThrowingDisposeModule(ModuleId? moduleId, bool active = true, bool enableLogging = false) : base(moduleId, active, enableLogging) { }

        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() { /* no-op */ }
        protected override void DisposeInternal() => throw new InvalidOperationException("Teardown failed.");
    }

    private class DummyModule2 : NoireModuleBase<DummyModule2>
    {
        public DummyModule2() : base() { }
        public DummyModule2(ModuleId moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }
        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() { /* no-op */ }
        protected override void DisposeInternal() { /* no-op */ }
    }

    [Fact]
    public void AddModule_ShouldAddNewInstance()
    {
        NoireLibMain.ClearAllModules();

        var added = NoireLibMain.AddModule<DummyModule>();

        added.Should().NotBeNull();
        NoireService.ActiveModules.Should().HaveCount(1);
        NoireService.ActiveModules[0].Module.Should().BeOfType<DummyModule>();
        NoireService.ActiveModules[0].Module.ModuleId.Should().BeNull();
    }

    [Fact]
    public void AddModule_ShouldUseProvidedInstance()
    {
        NoireLibMain.ClearAllModules();

        var instance = new DummyModule();
        instance.ModuleId = "m1";
        var added = NoireLibMain.AddModule(instance);

        added.Should().BeSameAs(instance);
        NoireService.ActiveModules.Should().ContainSingle(x => x.Module.ModuleId == "m1" && ReferenceEquals(x.Module, instance));
    }

    [Fact]
    public void AddModules_ShouldUseInstances_ForEachProvidedModuleType()
    {
        NoireLibMain.ClearAllModules();

        var m1 = new DummyModule();
        var m2 = new DummyModule2();

        var added = NoireLibMain.AddModules(m1, m2);

        added.Should().HaveCount(2);
        added.Should().OnlyContain(inst => ReferenceEquals(inst, m1) || ReferenceEquals(inst, m2));
        NoireService.ActiveModules.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveModule_ByType_ShouldDisposeAndRemove()
    {
        NoireLibMain.ClearAllModules();

        var inst = NoireLibMain.AddModule<DummyModule>();
        NoireService.ActiveModules.Should().HaveCount(1);

        var removed = NoireLibMain.RemoveModule<DummyModule>();

        removed.Should().BeTrue();
        NoireService.ActiveModules.Should().BeEmpty();
    }

    [Fact]
    public void RemoveModule_ById_ShouldDisposeAndRemove()
    {
        NoireLibMain.ClearAllModules();

        var inst = NoireLibMain.AddModule<DummyModule>("x");

        var removed = NoireLibMain.RemoveModule<DummyModule>("x");

        removed.Should().BeTrue();
        NoireService.ActiveModules.Should().BeEmpty();
    }

    [Fact]
    public void RemoveModule_ShouldReturnFalse_WhenNotFound()
    {
        NoireLibMain.ClearAllModules();

        var removed = NoireLibMain.RemoveModule<DummyModule>();
        removed.Should().BeFalse();
    }

    [Fact]
    public void IsModuleAdded_ShouldReflectPresence_ByType()
    {
        NoireLibMain.ClearAllModules();

        NoireLibMain.IsModuleAdded<DummyModule>().Should().BeFalse();
        NoireLibMain.AddModule<DummyModule>();
        NoireLibMain.IsModuleAdded<DummyModule>().Should().BeTrue();
    }

    [Fact]
    public void IsModuleAdded_ShouldReflectPresence_ById()
    {
        NoireLibMain.ClearAllModules();

        NoireLibMain.IsModuleAdded<DummyModule>("id1").Should().BeFalse();
        NoireLibMain.AddModule<DummyModule>("id1");
        NoireLibMain.IsModuleAdded<DummyModule>("id1").Should().BeTrue();
    }

    [Fact]
    public void IsModuleActive_ShouldReturnFalse_WhenNotFound()
    {
        NoireLibMain.ClearAllModules();

        NoireLibMain.IsModuleActive<DummyModule>().Should().BeFalse();
    }

    [Fact]
    public void IsModuleActive_ShouldReturnModuleIsActive_WhenFound()
    {
        NoireLibMain.ClearAllModules();

        var inst = NoireLibMain.AddModule<DummyModule>();
        inst!.IsActive = true;

        NoireLibMain.IsModuleActive<DummyModule>().Should().BeTrue();

        inst.IsActive = false;
        NoireLibMain.IsModuleActive<DummyModule>().Should().BeFalse();
    }

    [Fact]
    public void TryGetModule_ByTypeOrId_ShouldReturnInstanceOrNull()
    {
        NoireLibMain.ClearAllModules();

        NoireLibMain.GetModule<DummyModule>().Should().BeNull();

        var inst1 = NoireLibMain.AddModule<DummyModule>("a");
        var inst2 = NoireLibMain.AddModule<DummyModule>("b");

        NoireLibMain.GetModule<DummyModule>().Should().BeSameAs(inst1);
        NoireLibMain.GetModule<DummyModule>("b").Should().BeSameAs(inst2);
        NoireLibMain.GetModule<DummyModule>("c").Should().BeNull();
    }

    [Fact]
    public void TryGetModule_ByTypeOrId_ShouldBeNull_WhenCheckingUnaddedModule()
    {
        NoireLibMain.ClearAllModules();

        var inst1 = NoireLibMain.AddModule<DummyModule>("a");

        NoireLibMain.GetModule<DummyModule2>().Should().BeNull();
        NoireLibMain.GetModule<DummyModule2>("a").Should().BeNull();
        NoireLibMain.GetModule<DummyModule2>().Should().BeNull();
    }

    [Fact]
    public void TryGetModule_ByIndex_ShouldClampIndex()
    {
        NoireLibMain.ClearAllModules();

        var a = NoireLibMain.AddModule<DummyModule>("a");
        var b = NoireLibMain.AddModule<DummyModule>("b");

        NoireLibMain.GetModule<DummyModule>(null, -5).Should().BeSameAs(a);
        NoireLibMain.GetModule<DummyModule>(null, 0).Should().BeSameAs(a);
        NoireLibMain.GetModule<DummyModule>(null, 1).Should().BeSameAs(b);
        NoireLibMain.GetModule<DummyModule>(null, 999).Should().BeSameAs(b);
    }

    #region Disposal contract

    [Fact]
    public void IsDisposed_ShouldBeFalse_BeforeDisposal()
    {
        var module = new CountingModule(new ModuleId("live"), active: true);

        module.IsDisposed.Should().BeFalse();
        module.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Dispose_ShouldReportDisposed()
    {
        var module = new CountingModule(new ModuleId("d1"), active: true);

        module.Dispose();

        module.IsDisposed.Should().BeTrue("a disposed module has to be able to say so, otherwise every module has to invent its own disposal latch");
    }

    [Fact]
    public void Dispose_ShouldClearIsActive_WhenModuleWasActive()
    {
        var module = new CountingModule(new ModuleId("d2"), active: true);
        module.IsActive.Should().BeTrue();

        module.Dispose();

        module.IsActive.Should().BeFalse("a module guarding work on IsActive would otherwise keep building resources nothing is left to tear down");
    }

    [Fact]
    public void Dispose_ShouldNotRunOnDeactivated_WhenModuleWasActive()
    {
        var module = new CountingModule(new ModuleId("d3"), active: true);
        module.ActivatedCount.Should().Be(1);

        module.Dispose();

        module.DeactivatedCount.Should().Be(0, "modules tear down in DisposeInternal, so running the deactivation hook from Dispose as well would run their teardown twice");
        module.DisposeInternalCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_ShouldLeaveIsActiveUntouched_UntilDisposeInternalHasRun()
    {
        var module = new SelfDeactivatingModule(new ModuleId("d4"), active: true);

        module.Dispose();

        module.WasActiveInsideDisposeInternal.Should().BeTrue("a module that deactivates itself during teardown needs to still read as active, or its own SetActive(false) short-circuits and never reaches OnDeactivated");
        module.DeactivatedCount.Should().Be(1, "the module asked for its own deactivation, so it has to get exactly one");
        module.IsActive.Should().BeFalse();
        module.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Dispose_ShouldBeIdempotent()
    {
        var module = new CountingModule(new ModuleId("d5"), active: true);

        module.Dispose();
        module.Dispose();
        module.Dispose();

        module.DisposeInternalCount.Should().Be(1, "a module is reachable for disposal from both its owner and the library, so the teardown has to be claimed once");
        module.DeactivatedCount.Should().Be(0);
        module.IsDisposed.Should().BeTrue();
        module.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Dispose_ShouldClearIsActive_WhenDisposeInternalThrows()
    {
        var module = new ThrowingDisposeModule(new ModuleId("d6"), active: true);

        var act = () => module.Dispose();

        act.Should().Throw<InvalidOperationException>();
        module.IsActive.Should().BeFalse("a teardown that failed partway leaves the module unusable, so it must not still report itself as running");
        module.IsDisposed.Should().BeTrue("disposal is claimed before the teardown runs, so a failed teardown is not retried");
    }

    [Fact]
    public void Dispose_ShouldNotRetryTeardown_AfterDisposeInternalThrew()
    {
        var module = new ThrowingDisposeModule(new ModuleId("d7"), active: true);

        var first = () => module.Dispose();
        first.Should().Throw<InvalidOperationException>();

        var second = () => module.Dispose();
        second.Should().NotThrow("the teardown was already claimed, so a second call has nothing left to run");
    }

    [Fact]
    public void IsActive_ShouldRemainSettable_AfterDisposal()
    {
        var module = new CountingModule(new ModuleId("d8"), active: true);
        module.Dispose();

        module.IsActive = true;

        module.IsActive.Should().BeTrue("IsActive records whether the module is switched on and stays a plain flag");
        module.IsDisposed.Should().BeTrue("IsDisposed is the terminal state, so it is what disposal-sensitive work guards on");
    }

    [Fact]
    public void SetActive_ShouldRefuseActivation_AfterDisposal()
    {
        var module = new CountingModule(new ModuleId("d10"), active: true);
        module.Dispose();
        module.ActivatedCount.Should().Be(1);

        module.SetActive(true);
        module.Activate();

        module.ActivatedCount.Should().Be(1, "OnActivated would wire a disposed module back onto the framework and run it against resources its teardown already released");
        module.IsActive.Should().BeFalse();
    }

    [Fact]
    public void SetActive_ShouldStillAllowDeactivation_WhileDisposalIsClaimed()
    {
        // Disposal is claimed before the teardown runs, so a module deactivating itself from its own teardown is
        // already disposed by the time it does. That deactivation still has to reach the hook.
        var module = new SelfDeactivatingModule(new ModuleId("d11"), active: true);

        module.Dispose();

        module.DeactivatedCount.Should().Be(1, "refusing a deactivation raised while disposal is in progress would strand the teardown of every module that deactivates itself");
    }

    [Fact]
    public void IsActive_ShouldNotRunLifecycleHooks_WhenAssignedDirectly()
    {
        var module = new CountingModule(new ModuleId("d9"), active: false);

        module.IsActive = true;
        module.IsActive = false;

        module.ActivatedCount.Should().Be(0, "assigning the property records the state only, which is what lets OnActivated refuse an activation by clearing it");
        module.DeactivatedCount.Should().Be(0);

        module.Dispose();
    }

    #endregion

    #region Cross-thread visibility

    [Fact]
    public void IsActive_ShouldBeBackedByAVolatileField()
    {
        var field = typeof(NoireModuleBase<DummyModule>).GetField("isActive", BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull("IsActive is read from timer callbacks and worker threads, so it cannot be a plain auto-property");
        field!.GetRequiredCustomModifiers().Contains(typeof(IsVolatile))
            .Should().BeTrue("without the volatile marker a reader can carry on against a stale activation state");
    }

    #endregion
}

/// <summary>
/// Locks the behavior <see cref="NoireModuleWithWindowBase{TModule, TWindow}"/> owes a module whose window is
/// optional and absent: it inherits the same disposal contract as every other module, and neither its teardown
/// nor its window accessors depend on a window it never registered.<br/>
/// Joins the windowed-module collection because it swaps the process-wide window system out and back.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WindowedModuleCollection.Name)]
public class NoireLibWindowedModuleBaseTests
{
    private sealed class WindowlessWindowedModule : NoireModuleWithWindowBase<WindowlessWindowedModule, DummyModuleWindow>
    {
        public int DisposeInternalCount { get; private set; }
        public int DeactivatedCount { get; private set; }

        public WindowlessWindowedModule() : base((string?)null, false, false) { }
        public WindowlessWindowedModule(ModuleId? moduleId, bool active = true, bool enableLogging = false) : base(moduleId, active, enableLogging) { }

        // Registers no window, which is what a module whose window is optional looks like when it is turned off.
        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() => DeactivatedCount++;
        protected override void DisposeInternal() => DisposeInternalCount++;
    }

    private sealed class DummyModuleWindow : NoireModuleWindowBase<WindowlessWindowedModule>
    {
        public DummyModuleWindow(WindowlessWindowedModule parentModule) : base(parentModule) { }
        public override string DisplayWindowName { get; set; } = "Dummy";
        public override void Draw() { /* no-op: these tests never render */ }
        public override void Dispose() { /* no-op */ }
    }

    /// <summary>
    /// Installs the given window system and returns the one that was there, so a test can put it back.
    /// </summary>
    private static WindowSystem? SwapWindowSystem(WindowSystem? replacement)
    {
        var property = typeof(NoireService)
            .GetProperty(nameof(NoireService.NoireWindowSystem), BindingFlags.NonPublic | BindingFlags.Static)!;

        var previous = (WindowSystem?)property.GetValue(null);
        property.GetSetMethod(nonPublic: true)!.Invoke(null, new object?[] { replacement });

        return previous;
    }

    [Fact]
    public void Dispose_ShouldNotThrow_WhenModuleHoldsNoWindowAndNoWindowSystemIsInstalled()
    {
        var previous = SwapWindowSystem(null);

        try
        {
            var module = new WindowlessWindowedModule(new ModuleId("w1"), active: true);

            var act = () => module.Dispose();

            act.Should().NotThrow("a module holding no window has nothing to unregister, and throwing to demand a window system would skip the rest of its teardown");
            module.DisposeInternalCount.Should().Be(1, "the teardown has to run even when there is no window system to unregister from");
            module.IsDisposed.Should().BeTrue();
            module.IsActive.Should().BeFalse();
        }
        finally
        {
            SwapWindowSystem(previous);
        }
    }

    [Fact]
    public void GetFullWindowName_ShouldReturnEmpty_WhenModuleHoldsNoWindow()
    {
        var module = new WindowlessWindowedModule(new ModuleId("w2"), active: false);

        module.HasWindow.Should().BeFalse();
        module.GetFullWindowName().Should().BeEmpty("the accessor falls back to an empty string, matching DisplayWindowName, rather than dereferencing a window that is not there");

        module.Dispose();
    }

    [Fact]
    public void Dispose_ShouldApplyTheSharedDisposalContract_ToWindowedModules()
    {
        var module = new WindowlessWindowedModule(new ModuleId("w3"), active: true);

        module.Dispose();
        module.Dispose();

        module.DisposeInternalCount.Should().Be(1, "the window base has to inherit the same one-shot teardown as every other module rather than reimplementing disposal");
        module.DeactivatedCount.Should().Be(0, "windowed modules tear down in DisposeInternal too, so disposal must not run their deactivation hook as well");
        module.IsDisposed.Should().BeTrue();
        module.IsActive.Should().BeFalse();
    }
}
