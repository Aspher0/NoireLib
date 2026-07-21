using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the gate that makes instrumentation structural: a surface cannot obtain a draw list without opening a
/// measurement, and the measurement names itself.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class UiDrawTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public UiDrawTests(UiHarness harness) => this.harness = harness;

    /// <summary>
    /// The scope name the gate derives for calls made from this file.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from <c>nameof</c>, so that a rename of this class which did not rename the file
    /// fails the test rather than quietly agreeing with itself.
    /// </remarks>
    private const string ThisFileScope = "UiDrawTests";

    [Fact]
    public void Begin_FromAnyFile_RegistersAScopeNamedForThatType()
    {
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            {
                using var draw = UiDraw.Begin();
                draw.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);
            }));

        result.HasScope(ThisFileScope).Should().BeTrue("the gate names the scope after the file it was opened in");
        result.TotalVtxCount.Should().BeGreaterThan(0, "the gate must hand back a list that actually paints");
    }

    [Fact]
    public void Begin_InsideARedirect_PaintsIntoTheRedirectedList()
    {
        // Proves the gate resolves through NoireShapes rather than reaching for the window list itself. Without this a
        // gated surface drawn inside On() would paint into the wrong list, which is a rendering bug rather than a
        // profiling one.
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetForegroundDrawList(), static () =>
            {
                using var draw = UiDraw.Begin();
                draw.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);
            }));

        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Begin_WithoutAPlugin_HandsBackANullListRatherThanThrowing()
    {
        // The harness reports ImGui as available, because it owns a real context. Switched off here to reach the case
        // a plugin sees before it has initialized: the gate must hand back nothing and let the drawing be skipped.
        // Throwing would make every gated surface unusable in any context without a plugin behind it.
        var previous = UiDraw.AvailableOverride;
        UiDraw.AvailableOverride = static () => false;

        try
        {
            var act = () => harness.Draw(static () =>
            {
                using var draw = UiDraw.Begin();

                draw.List.IsNull.Should().BeTrue();
            });

            act.Should().NotThrow();
        }
        finally
        {
            UiDraw.AvailableOverride = previous;
        }
    }

    [Fact]
    public void BeginForegroundAndBackground_PaintOntoTheViewportLists()
    {
        var result = harness.Draw(static () =>
        {
            using (var front = UiDraw.BeginForeground())
                front.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);

            using (var back = UiDraw.BeginBackground())
                back.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);
        });

        // Neither follows a NoireShapes redirect, by design: those two calls answer for the whole viewport rather than
        // for the current window. The harness reaches them through the gate's availability seam.
        result.HasScope(ThisFileScope).Should().BeTrue();
        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BeginWindow_StillHandsBackAListWhileARedirectIsInForce()
    {
        // The channel plumbing in NoirePanel depends on this. Channels are split on the list the window's own items
        // land on, so BeginWindow must keep answering with a usable list even inside a redirect, where Begin answers
        // with the redirected one instead.
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetForegroundDrawList(), static () =>
            {
                using var draw = UiDraw.BeginWindow();

                draw.List.IsNull.Should().BeFalse();
                draw.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);
            }));

        result.HasScope(ThisFileScope).Should().BeTrue();
        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Begin_InSteadyState_AllocatesNothing()
    {
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            {
                using var draw = UiDraw.Begin();
                draw.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);
            }));

        // The gate is held once per draw by every surface in the library, so it has to be free. The scope is a ref
        // struct and the derived name is resolved once per file rather than per call.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void BeginMethod_RegistersAScopeNamedForTheCallingMethod()
    {
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            {
                using var draw = UiDraw.BeginMethod();
                draw.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);
            }));

        // The lambda is compiled into a method of its own, so the member name is the compiler's rather than this
        // test's. What matters is that the scope is named for the calling type and then a member, which is the shape
        // that keeps NoireShapes.Sunburst a row of its own.
        result.Scopes.Should().Contain(name => name.StartsWith(ThisFileScope + ".", StringComparison.Ordinal));
    }

    [Fact]
    public void BeginMethod_InSteadyState_AllocatesNothing()
    {
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
            {
                using var draw = UiDraw.BeginMethod();
                draw.List.AddRectFilled(new Vector2(10f, 10f), new Vector2(60f, 40f), 0xFFFFFFFFu);
            }));

        // The composed name is cached against the call site, so only the first frame pays for building it. Every shape
        // helper holds one of these per call, so a string per call would be the largest thing the shapes allocate.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Begin_FromAPartialClassFile_NamesTheTypeRatherThanTheFile()
    {
        // NoireShapes is split across four files, and every one of them has to report as NoireShapes: a scope named
        // for the whole file name would split one surface into four rows and would rename the scopes that already
        // exist. Sunburst lives in NoireShapes.Arcs.cs, so it proves the split is taken at the first dot.
        var result = harness.Draw(static () =>
            NoireShapes.On(ImGui.GetWindowDrawList(), static () =>
                NoireShapes.Sunburst(new Vector2(200f, 200f), 80f, new Vector4(1f, 1f, 1f, 1f))));

        result.HasScope("NoireShapes.Sunburst").Should().BeTrue();
        result.Scopes.Should().NotContain("NoireShapes.Arcs.Sunburst");
    }
}
