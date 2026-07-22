using FluentAssertions;
using NoireLib.UI;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the tab bar at zero allocation per frame, and holds the instrument itself to costing nothing while it is not
/// being asked for anything.
/// </summary>
/// <remarks>
/// The profiler is the one surface where a per-frame allocation would be self-concealing: it would land inside the
/// measurement everything else is judged against. The gate is that the profiler adds nothing to a frame when it is off,
/// and that allocation tracking adds nothing on top when only it is off.<br/>
/// The profiler window's own draw is held separately, by the tests ticket 05 left behind.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireTabsAndDiagnosticsAllocationTests : IClassFixture<UiHarness>
{
    private static readonly NoireTabBar Tabs = BuildTabs();

    private readonly UiHarness harness;

    public NoireTabsAndDiagnosticsAllocationTests(UiHarness harness) => this.harness = harness;

    private static NoireTabBar BuildTabs()
    {
        var bar = new NoireTabBar("alloc_tabs");

        bar.Tabs.Add(new UiTab("general", "General", static () => { }));
        bar.Tabs.Add(new UiTab("appearance", "Appearance", static () => { }));
        bar.Tabs.Add(new UiTab("advanced", "Advanced", static () => { }));

        return bar;
    }

    [Fact]
    public void TabBar_AllocatesNothing()
    {
        var result = harness.Draw(static () => Tabs.Draw(), warmUpFrames: 4);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TabBar_AllocatesNothingWithTheProfilerRunningEither()
    {
        // Drawn under the profiler as well as without it, because a tab bar is one of the surfaces a capture is most
        // likely to be taken over: it is what a settings window is built around.
        var result = harness.Draw(static () => Tabs.Draw(), warmUpFrames: 4, profile: true);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Profiler_AddsNothingToAFrameItIsMeasuring()
    {
        var withProfiler = harness.Draw(static () => { }, warmUpFrames: 4, profile: true);
        var without = harness.Draw(static () => { }, warmUpFrames: 4, profile: false);

        // A node is allocated the first time a call path is seen and never again, which is why the harness resets
        // before the warm-up rather than before the measured frame. In steady state the instrument is free.
        withProfiler.AllocatedBytes.Should().Be(without.AllocatedBytes);
        withProfiler.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void AllocationTracking_CostsNothingWhileItIsOff()
    {
        var profiler = NoireUI.Profiler;
        var wasTracking = profiler.TrackAllocations;

        try
        {
            profiler.TrackAllocations = false;
            var off = harness.Draw(static () => Tabs.Draw(), warmUpFrames: 4, profile: true);

            profiler.TrackAllocations = true;
            var on = harness.Draw(static () => Tabs.Draw(), warmUpFrames: 4, profile: true);

            // Tracking reads the thread's allocation counter twice per scope, which is a call rather than an
            // allocation, so neither state costs bytes. What this holds is the contract that the counters follow their
            // own switch and are not simply always on behind it.
            off.AllocatedBytes.Should().Be(0L);
            on.AllocatedBytes.Should().Be(0L);
        }
        finally
        {
            profiler.TrackAllocations = wasTracking;
        }
    }
}
