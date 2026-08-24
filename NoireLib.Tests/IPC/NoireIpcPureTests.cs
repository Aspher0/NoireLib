using FluentAssertions;
using NoireLib.IPC;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the IPC layers that never touch a call gate: name and prefix resolution, delegate and
/// signature validation, the attribute scan's silent-skip branch, and registration lookup.<br/>
/// <see cref="NoireIPC"/> is a static facade, so every test resets its configuration.
/// </summary>
public sealed class NoireIpcPureTests : IDisposable
{
    public NoireIpcPureTests() => NoireIPC.ResetConfiguration();

    public void Dispose()
    {
        NoireIPC.ResetConfiguration();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// An instance type carrying the class attribute, which automatic registration must skip: only static types
    /// register automatically, instance types go through <see cref="NoireIPC.Initialize(object, string?, bool, Type?, System.Reflection.BindingFlags)"/>.
    /// </summary>
    [NoireIpcClass("PureProbe")]
    public sealed class InstanceAttributedProbe
    {
        [NoireIpc("Ping")]
        public int Ping(int value) => value;
    }

    #region Names and prefixes

    [Fact]
    public void BuildName_WithoutInitializationOrPrefix_ReturnsTheBareName()
    {
        NoireIPC.BuildName("MyChannel").Should().Be("MyChannel",
            "no explicit prefix is configured and the plugin internal name is unreachable before initialization");
    }

    [Fact]
    public void BuildName_WithAnExplicitPrefix_PrependsIt()
    {
        NoireIPC.BuildName("MyChannel", "MyPlugin").Should().Be("MyPlugin.MyChannel");
    }

    [Fact]
    public void BuildName_WhenTheNameAlreadyCarriesThePrefix_DoesNotDoubleIt()
    {
        NoireIPC.BuildName("MyPlugin.MyChannel", "MyPlugin").Should().Be("MyPlugin.MyChannel");
    }

    [Fact]
    public void BuildName_UsesTheConfiguredDefaultPrefixAndSeparator()
    {
        NoireIPC.Configure(defaultPrefix: "Configured", nameSeparator: "::");

        NoireIPC.BuildName("MyChannel").Should().Be("Configured::MyChannel");
    }

    [Fact]
    public void BuildName_TrimsWhitespaceAroundTheName()
    {
        NoireIPC.BuildName("  MyChannel  ", "MyPlugin").Should().Be("MyPlugin.MyChannel");
    }

    [Fact]
    public void BuildName_WithABlankName_Throws()
    {
        var act = () => NoireIPC.BuildName("   ");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Configure_WithABlankSeparator_Throws()
    {
        var act = () => NoireIPC.Configure(nameSeparator: " ");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResetConfiguration_RestoresEveryDefault()
    {
        NoireIPC.Configure(defaultPrefix: "X", usePluginInternalNameAsDefaultPrefix: false, nameSeparator: "::", defaultMessageResultType: typeof(int));

        NoireIPC.ResetConfiguration();

        NoireIPC.DefaultPrefix.Should().BeNull();
        NoireIPC.UsePluginInternalNameAsDefaultPrefix.Should().BeTrue();
        NoireIPC.NameSeparator.Should().Be(".");
        NoireIPC.DefaultMessageResultType.Should().Be(typeof(object));
    }

    #endregion

    #region Signature validation

    [Fact]
    public void ValidateMessageResultType_RejectsVoid()
    {
        var act = () => NoireIPC.ValidateMessageResultType(typeof(void));

        act.Should().Throw<ArgumentException>("a message channel's trailing generic cannot be void");
    }

    [Fact]
    public void InferParameterTypes_WithANullArgument_ExplainsTheExplicitOverload()
    {
        var act = () => NoireIPC.InferParameterTypes([1, null, "three"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*explicit parameter types*", "a null argument has no type to infer and the caller must be pointed at the fix");
    }

    [Fact]
    public void InferParameterTypes_WithMoreThanEightArguments_Throws()
    {
        var act = () => NoireIPC.InferParameterTypes([1, 2, 3, 4, 5, 6, 7, 8, 9]);

        act.Should().Throw<NotSupportedException>("Dalamud call gates stop at eight parameters");
    }

    [Fact]
    public void InferParameterTypes_ReturnsTheRuntimeTypes()
    {
        NoireIPC.InferParameterTypes([1, "two", 3f]).Should().Equal(typeof(int), typeof(string), typeof(float));
    }

    [Fact]
    public void ValidateEventHandlerType_RejectsNonDelegates()
    {
        var act = () => NoireIPC.ValidateEventHandlerType(typeof(string));

        act.Should().Throw<ArgumentException>();
    }

    private delegate void ByRefHandler(ref int value);

    [Fact]
    public void ValidateEventHandlerType_RejectsByRefParameters()
    {
        var act = () => NoireIPC.ValidateEventHandlerType(typeof(ByRefHandler));

        act.Should().Throw<NotSupportedException>("IPC serializes arguments, so ref and out cannot round-trip");
    }

    [Fact]
    public void ValidateEventHandlerType_RejectsReturningDelegates()
    {
        var act = () => NoireIPC.ValidateEventHandlerType(typeof(Func<int>));

        act.Should().Throw<NotSupportedException>("an event has no single receiver to return a value to");
    }

    [Fact]
    public void ValidateEventHandlerType_AcceptsAVoidDelegate()
    {
        var act = () => NoireIPC.ValidateEventHandlerType(typeof(Action<int, string>));

        act.Should().NotThrow();
    }

    [Fact]
    public void IsDelegateType_ClassifiesCorrectly()
    {
        NoireIPC.IsDelegateType(typeof(Action)).Should().BeTrue();
        NoireIPC.IsDelegateType(typeof(Func<int>)).Should().BeTrue();
        NoireIPC.IsDelegateType(typeof(string)).Should().BeFalse();
    }

    [Fact]
    public void IsConsumerWrapperType_RecognizesTheWrapperAndItsDelegate()
    {
        NoireIPC.IsConsumerWrapperType(typeof(NoireIpcConsumer<Action<int>>), out var delegateType).Should().BeTrue();
        delegateType.Should().Be(typeof(Action<int>));

        NoireIPC.IsConsumerWrapperType(typeof(string), out _).Should().BeFalse();
    }

    #endregion

    #region Attribute scanning and registration lookup

    [Fact]
    public void RegisterAttributedTypes_SkipsInstanceTypes_AndReturnsAnEmptyGroup()
    {
        // The probe above is the only attributed type in this assembly; being an instance type it must be skipped
        // before anything touches a call gate, so this runs without NoireLib being initialized.
        var group = NoireIPC.RegisterAttributedTypes(typeof(NoireIpcPureTests).Assembly);

        group.Should().BeEmpty("instance types only register through NoireIPC.Initialize");
    }

    [Fact]
    public void GetRegistration_WhenNothingIsTracked_ReturnsNull()
    {
        NoireIPC.GetRegistration("Nothing.Registered").Should().BeNull();
    }

    #endregion
}
