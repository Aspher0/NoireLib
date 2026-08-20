using Dalamud.Hooking;
using FluentAssertions;
using NoireLib.Hooking;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the call shape a consumer types, which no other test covers because creating a hook needs the game.
/// The detour must stay the first parameter and must stay typed as the delegate itself: that is what lets an
/// IDE generate the detour method with the right signature from a call written before the method exists.
/// Reordering these parameters compiles and breaks nothing visible, so it is locked here instead.
/// </summary>
public sealed class NoireHookConstructorTests
{
    private delegate int SampleDelegate(int value);

    private static readonly Type HookType = typeof(NoireHook<SampleDelegate>);

    [Fact]
    public void TheEverydayConstructor_TakesTheDetourFirstAndAutoEnableSecond()
    {
        var parameters = FindConstructor(typeof(SampleDelegate), typeof(bool));

        parameters[0].ParameterType.Should().Be(typeof(SampleDelegate), "an IDE generates the detour from this parameter's type");
        parameters[0].HasDefaultValue.Should().BeFalse();
        parameters[1].Name.Should().Be("autoEnable");
        parameters[1].HasDefaultValue.Should().BeTrue("new(Detour) has to compile as well as new(Detour, true)");
    }

    [Fact]
    public void ASignatureConstructor_TakesTheBytesThenTheDetour()
    {
        var parameters = FindConstructor(typeof(string), typeof(SampleDelegate));

        parameters[1].ParameterType.Should().Be(typeof(SampleDelegate));
        parameters[2].Name.Should().Be("autoEnable");
    }

    [Fact]
    public void AnAddressConstructor_TakesThePointerThenTheDetour()
    {
        var parameters = FindConstructor(typeof(nint), typeof(SampleDelegate));

        parameters[1].ParameterType.Should().Be(typeof(SampleDelegate));
        parameters[2].Name.Should().Be("autoEnable");
    }

    [Fact]
    public void ATargetConstructor_TakesTheTargetThenTheDetour()
    {
        var parameters = FindConstructor(typeof(HookTarget), typeof(SampleDelegate));

        parameters[1].ParameterType.Should().Be(typeof(SampleDelegate));
    }

    [Fact]
    public void AnAdoptingConstructor_TakesAnExistingDalamudHook()
    {
        var parameters = FindConstructor(typeof(Hook<SampleDelegate>), typeof(SampleDelegate));

        parameters[1].ParameterType.Should().Be(typeof(SampleDelegate));
    }

    [Fact]
    public void AnOptionsConstructor_ExistsForEveryPathThatNeedsOne()
    {
        FindConstructor(typeof(SampleDelegate), typeof(HookOptions)).Should().HaveCount(2);
        FindConstructor(typeof(HookTarget), typeof(SampleDelegate), typeof(HookOptions)).Should().HaveCount(3);
    }

    private static ParameterInfo[] FindConstructor(params Type[] leadingParameterTypes)
    {
        foreach (var candidate in HookType.GetConstructors())
        {
            var parameters = candidate.GetParameters();

            if (parameters.Length < leadingParameterTypes.Length)
                continue;

            if (leadingParameterTypes.Where((type, index) => parameters[index].ParameterType != type).Any())
                continue;

            return parameters;
        }

        throw new InvalidOperationException(
            $"NoireHook<T> declares no constructor starting with ({string.Join(", ", leadingParameterTypes.Select(type => type.Name))}).");
    }
}
