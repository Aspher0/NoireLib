using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Globalization;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the profiler window's own draw cost down.
/// </summary>
/// <remarks>
/// The window is library code that is roughly a third of any capture taken while it is open, so its cost distorts
/// every reading taken through it. These measure the instrument rather than what it reports.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireProfilerWindowTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public NoireProfilerWindowTests(UiHarness harness) => this.harness = harness;

    /// <summary>
    /// Draws the window's contents with a few scopes of its own to report on, so the table has rows.
    /// </summary>
    private static void DrawWithRows(NoireProfilerWindow window)
    {
        using (NoireUI.Profiler.Measure("HarnessScopeA"))
        {
            using (NoireUI.Profiler.Measure("HarnessScopeB"))
            {
            }
        }

        using (NoireUI.Profiler.Measure("HarnessScopeC"))
        {
        }

        window.DrawContents();
    }

    /// <summary>
    /// Draws the window inside a fixed-height region with <paramref name="scopes"/> scopes measured ahead of it.
    /// </summary>
    /// <remarks>
    /// The height has to be bounded for the table to scroll, and a table that does not scroll has every row on screen
    /// and nothing to clip. The harness host window auto-sizes, so the constraint is applied here.
    /// </remarks>
    private UiHarnessResult MeasureWithScopes(NoireProfilerWindow window, string[] names)
    {
        return harness.Draw(
            () =>
            {
                foreach (var name in names)
                {
                    using (NoireUI.Profiler.Measure(name))
                    {
                    }
                }

                if (ImGui.BeginChild("###clip", new Vector2(700f, 220f)))
                    window.DrawContents();

                ImGui.EndChild();
            },
            warmUpFrames: 6);
    }

    /// <summary>
    /// Scope names built once, so the measured frame is not charged for composing them.
    /// </summary>
    private static string[] Names(int count)
    {
        var names = new string[count];

        for (var i = 0; i < count; i++)
            names[i] = "HarnessScope" + i.ToString(CultureInfo.InvariantCulture);

        return names;
    }

    [Fact]
    public void DrawContents_WithMoreScopesThanFitOnScreen_DrawsOnlyTheVisibleRows()
    {
        var few = Names(10);
        var many = Names(200);

        var withFew = MeasureWithScopes(new NoireProfilerWindow(), few);
        var withMany = MeasureWithScopes(new NoireProfilerWindow(), many);

        withFew.TotalVtxCount.Should().BeGreaterThan(0);

        // Twenty times the scopes, and the geometry stays within a quarter. The rows past the bottom of the table are
        // never submitted, so the window costs what the table is tall rather than what the data is long. Submitting
        // every row instead puts this an order of magnitude over the ceiling.
        withMany.TotalVtxCount.Should().BeLessThan((int)(withFew.TotalVtxCount * 1.25));
    }

    [Fact]
    public void DrawContents_WithScopesToReport_ProducesVertices()
    {
        var window = new NoireProfilerWindow();

        var result = harness.Draw(() => DrawWithRows(window), warmUpFrames: 4);

        result.TotalVtxCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DrawContents_InSteadyState_StaysUnderTheAllocationCeiling()
    {
        var window = new NoireProfilerWindow();

        var result = harness.Draw(() => DrawWithRows(window), warmUpFrames: 8);

        // The window's own formatting, snapshot and sorting allocate nothing. What remains is ImGui marshalling the
        // UTF-16 literals this window hands it for its checkboxes, headers and tooltips, which is around 72 bytes for
        // the five rows drawn here. The ceiling sits above that so ordinary drift does not fail the suite, and far
        // enough below a per-row formatting cost that reintroducing one does.
        result.AllocatedBytes.Should().BeLessThan(256L);
    }

    [Fact]
    public void DrawContents_WhileTrackingIsOff_AddsNothingToTheMarshallingFloor()
    {
        var window = new NoireProfilerWindow();

        var previous = NoireUI.Profiler.Enabled;

        try
        {
            // Drawn with the profiler holding still: no scope opens, so no frame rolls, so the generation does not
            // move and the window has nothing to snapshot, sort or regroup.
            var paused = harness.Draw(
                () =>
                {
                    NoireUI.Profiler.Enabled = false;
                    window.DrawContents();
                },
                warmUpFrames: 8);

            // Not asserted as zero, and the distinction matters. ImGui marshals this window's UTF-16 literals on every
            // frame no matter what the window does. What is asserted is that tracking being off adds nothing on top of
            // that floor, because none of the snapshot, filter, sort or regroup work runs.
            paused.AllocatedBytes.Should().BeLessThan(256L);
        }
        finally
        {
            NoireUI.Profiler.Enabled = previous;
        }
    }

    [Fact]
    public void DrawContents_WithAnExcludedRow_DrawsItWithoutCostingAnything()
    {
        var window = new NoireProfilerWindow();

        // Marked from inside the draw rather than before it. The harness resets the profiler ahead of its warm-up, so
        // a mark set out here would be on a node that no longer exists by the time the window draws.
        var marked = false;

        void DrawAndMark()
        {
            DrawWithRows(window);

            if (marked)
                return;

            // Reading the snapshot allocates a list, which is why this runs once during the warm-up and never on the
            // measured frame.
            foreach (var entry in NoireUI.Profiler.Snapshot())
            {
                if (entry.Name != "HarnessScopeC")
                    continue;

                NoireUI.Profiler.SetExcluded(entry.Id, true);
                marked = true;
                break;
            }
        }

        var result = harness.Draw(DrawAndMark, warmUpFrames: 8);

        marked.Should().BeTrue("the row being measured has to actually be excluded");
        result.TotalVtxCount.Should().BeGreaterThan(0);

        // The red row is a table background colour and a packed colour, neither of which allocates, so marking a scope
        // must not move the window off the ceiling the rest of the suite holds it to.
        result.AllocatedBytes.Should().BeLessThan(256L);
    }

    [Fact]
    public void DrawContents_AcrossSeparateReads_KeepsDrawing()
    {
        var window = new NoireProfilerWindow();

        // The rebuild gate holds state between calls, so a second read has to produce a table rather than a wedged
        // one that decided nothing had changed.
        var first = harness.Draw(() => DrawWithRows(window), warmUpFrames: 4);
        var second = harness.Draw(() => DrawWithRows(window), warmUpFrames: 4);

        first.TotalVtxCount.Should().BeGreaterThan(0);
        second.TotalVtxCount.Should().BeGreaterThan(0);
    }
}
