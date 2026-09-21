using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Drives the reflection handle and the typed facade against a stand-in for another plugin.</summary>
public sealed class ReflectedObjectTests
{
    private sealed class Probe
    {
        private int hidden = 7;

        public static string StaticName = "static";

        public string Name { get; set; } = "probe";

        public int ReadOnlyNumber => 42;

        public int Hidden => hidden;

        public string Describe() => $"{Name}:{hidden}";

        public string Overloaded(int value) => $"int {value}";

        public string Overloaded(string value) => $"string {value}";

        public void Bump() => hidden++;
    }

    private class BaseProbe
    {
        private int baseHidden = 3;

        protected string Protected { get; set; } = "protected";

        public int BaseNumber => 11;

        public int PeekBaseHidden() => baseHidden;

        private string BasePrivate() => "base private";

        public virtual string Overridden() => "base";
    }

    private sealed class DerivedProbe : BaseProbe
    {
        public string OwnValue = "derived";

        public override string Overridden() => "derived";
    }

    private interface IProbeContract
    {
        string Name { get; set; }

        int ReadOnlyNumber { get; }

        string Describe();

        void Bump();
    }

    private interface IMissingContract
    {
        string NotThere();

        int AlsoNotThere { get; }
    }

    [Fact]
    public void Get_ReadsPublicAndPrivateMembersAlike()
    {
        var reflected = new ReflectedObject(new Probe());

        reflected.Get<string>("Name").Should().Be("probe");
        reflected.Get<int>("ReadOnlyNumber").Should().Be(42);
        reflected.Get<int>("hidden").Should().Be(7, "a private field is reachable the same way");
    }

    [Fact]
    public void Get_OnAMissingMember_YieldsTheFallbackInsteadOfThrowing()
    {
        var reflected = new ReflectedObject(new Probe());

        reflected.Get("nope").Should().BeNull();
        reflected.Get("nope", -1).Should().Be(-1);
        reflected.TryGet<string>("nope", out _).Should().BeFalse();
        reflected.Has("nope").Should().BeFalse();
    }

    [Fact]
    public void Set_WritesAProperty_AndRefusesOneWithNoSetter()
    {
        var probe = new Probe();
        var reflected = new ReflectedObject(probe);

        reflected.Set("Name", "changed").Should().BeTrue();
        probe.Name.Should().Be("changed");

        reflected.Set("ReadOnlyNumber", 1).Should().BeFalse("the property has no setter");
        reflected.Set("hidden", 99).Should().BeTrue("a private field is still writable");
        probe.Hidden.Should().Be(99);
    }

    [Fact]
    public void Call_PicksTheOverloadMatchingTheArgumentTypes()
    {
        var reflected = new ReflectedObject(new Probe());

        reflected.Call<string>("Overloaded", 5).Should().Be("int 5");
        reflected.Call<string>("Overloaded", "five").Should().Be("string five");
        reflected.Call<string>("Describe").Should().Be("probe:7");
    }

    [Fact]
    public void Call_OnAMissingMethodOrAWrongArity_YieldsNull()
    {
        var reflected = new ReflectedObject(new Probe());

        reflected.Call("nope").Should().BeNull();
        reflected.Call("Describe", "unexpected").Should().BeNull("no overload takes an argument");
    }

    [Fact]
    public void AType_ReachesItsStaticMembers()
    {
        var reflected = new ReflectedObject(typeof(Probe));

        reflected.IsStatic.Should().BeTrue();
        reflected.Target.Should().BeNull();
        reflected.Get<string>("StaticName").Should().Be("static");
    }

    [Fact]
    public void MemberNames_ListsWhatIsReachable_WithoutAccessors()
    {
        var names = new ReflectedObject(new Probe()).MemberNames();

        names.Should().Contain(["Name", "Describe", "hidden", "Overloaded"]);
        names.Should().NotContain(name => name.StartsWith("get_", StringComparison.Ordinal));
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Bind_RoutesEveryContractMemberToTheMemberOfTheSameName()
    {
        var probe = new Probe();
        var plugin = ReflectedProxyFacade<IProbeContract>(probe);

        plugin.Name.Should().Be("probe");
        plugin.ReadOnlyNumber.Should().Be(42);
        plugin.Describe().Should().Be("probe:7");

        plugin.Name = "bound";
        probe.Name.Should().Be("bound");

        plugin.Bump();
        probe.Hidden.Should().Be(8);
    }

    [Fact]
    public void Bind_Lenient_ReadsAMissingMemberAsTheDefault()
    {
        var facade = ReflectedProxyFacade<IMissingContract>(new Probe());

        facade.NotThere().Should().BeNull();
        facade.AlsoNotThere.Should().Be(0);
    }

    [Fact]
    public void Bind_Strict_ThrowsOnAMissingMember()
    {
        var facade = ReflectedProxyFacade<IMissingContract>(new Probe(), strict: true);

        facade.Invoking(f => f.NotThere()).Should().Throw<MissingMemberException>();
        facade.Invoking(f => _ = f.AlsoNotThere).Should().Throw<MissingMemberException>();
    }

    [Fact]
    public void Each_WalksASequenceHeldByAMember()
    {
        var holder = new { Items = new List<Probe> { new(), new() } };

        new ReflectedObject(holder).Each("Items").Should().HaveCount(2);
        new ReflectedObject(holder).Each("Missing").Should().BeEmpty();
    }

    /// <summary>A private member of a base class is reached from the derived type. FlattenHierarchy does not reach it.</summary>
    [Fact]
    public void Members_OfABaseClass_AreReachedFromTheDerivedType()
    {
        var derived = new DerivedProbe();
        var reflected = new ReflectedObject(derived);

        reflected.Get<string>("OwnValue").Should().Be("derived");
        reflected.Get<int>("BaseNumber").Should().Be(11);
        reflected.Get<int>("baseHidden").Should().Be(3, "a private field of the base class is still a member");
        reflected.Get<string>("Protected").Should().Be("protected");

        reflected.Has("baseHidden").Should().BeTrue();
        reflected.Has("BasePrivate").Should().BeTrue();

        reflected.Set("baseHidden", 8).Should().BeTrue();
        derived.PeekBaseHidden().Should().Be(8);

        reflected.Call<string>("BasePrivate").Should().Be("base private");
        reflected.MemberNames().Should().Contain(["OwnValue", "baseHidden", "BaseNumber", "BasePrivate", "Protected"]);
    }

    [Fact]
    public void AnOverride_ResolvesOnceAndRunsTheDerivedBody()
    {
        var reflected = new ReflectedObject(new DerivedProbe());

        reflected.Call<string>("Overridden").Should().Be("derived");
    }

    private static TContract ReflectedProxyFacade<TContract>(object target, bool strict = false)
        where TContract : class
        => (TContract)ReflectedProxy.Create(typeof(TContract), new ReflectedObject(target), strict);
}
