using FluentAssertions;
using System.Runtime.Versioning;
using Xunit;
using NoireLib.Core.Modules;

namespace NoireLib.Tests;

[SupportedOSPlatform("windows")]
public class NoireLibModuleTests
{
    private class DummyModule : NoireModuleBase
    {
        public DummyModule() : base() { }
        public DummyModule(ModuleId moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }
        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() { /* no-op */ }
        public override void Dispose() { /* no-op */ }
    }

    private class DummyModule2 : NoireModuleBase
    {
        public DummyModule2() : base() { }
        public DummyModule2(ModuleId moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }
        protected override void InitializeModule(params object?[] args) { /* no-op */ }
        protected override void OnActivated() { /* no-op */ }
        protected override void OnDeactivated() { /* no-op */ }
        public override void Dispose() { /* no-op */ }
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
}
