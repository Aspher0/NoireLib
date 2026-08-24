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
/// <remarks>
/// What is worth pinning is that it survives the awkward types rather than that it makes anything faster: it walks
/// every type in the namespace by reflection, and the namespace holds generic widgets, records, ref structs and
/// abstract bases, any of which refuses to be compiled early. A warmup that threw on one of those would take the
/// plugin down from a background thread at load, which is far worse than the slow first frame it exists to avoid.
/// </remarks>
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
