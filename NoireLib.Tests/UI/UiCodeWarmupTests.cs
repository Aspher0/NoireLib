using FluentAssertions;
using NoireLib.UI;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The ahead-of-time compilation of the drawing code.
/// </summary>
public sealed class UiCodeWarmupTests
{
    [Fact]
    public async Task WarmDrawPath_CompilesWithoutThrowing()
    {
        await NoireUI.WarmDrawPath();

        NoireUI.DrawPathWarmed.Should().BeTrue();
    }

    [Fact]
    public async Task WarmDrawPath_RunsOnce()
    {
        await NoireUI.WarmDrawPath();

        // A second pass would compile methods that are already compiled: the same cost again for nothing.
        var second = NoireUI.WarmDrawPath();

        second.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task WarmDrawPath_AcceptsConsumerTypes()
    {
        await NoireUI.WarmDrawPath(typeof(UiCodeWarmupTests), typeof(string));

        NoireUI.DrawPathWarmed.Should().BeTrue();
    }

    [Fact]
    public void TheNamespaceItWalks_HoldsTypesThatCannotBeCompiledEarly()
    {
        var types = typeof(NoireUI).Assembly.GetTypes()
            .Where(type => type.Namespace == "NoireLib.UI")
            .ToList();

        // The reason every step is guarded and every failure skipped. If this ever comes back empty the walk is
        // looking at the wrong namespace and the warmup is quietly doing nothing.
        types.Should().NotBeEmpty();
        types.Should().Contain(type => type.IsGenericTypeDefinition, "the widgets are generic");
        types.Should().Contain(type => type.IsAbstract, "the drawable base is abstract");
    }
}
