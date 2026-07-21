using FluentAssertions;
using NoireLib.UI;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the profiler's accounting: that it is free when off, that it attributes cost to the scope that spent it, and
/// that a frame's totals are closed off when the frame moves rather than accumulating forever.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public class NoireUiProfilerTests
{
    /// <summary>
    /// Runs a body with the profiler on and the frame counter under the test's control, and puts both back afterwards.
    /// </summary>
    private static void WithProfiler(System.Action<UiProfiler, System.Func<int>> body)
    {
        var profiler = NoireUI.Profiler;
        var previousEnabled = profiler.Enabled;
        var previousFrame = NoireUI.FrameOverride;

        var frame = 0;
        NoireUI.FrameOverride = () => frame;

        profiler.Reset();
        profiler.Enabled = true;

        try
        {
            body(profiler, () => ++frame);
        }
        finally
        {
            profiler.Enabled = previousEnabled;
            profiler.Reset();
            NoireUI.FrameOverride = previousFrame;
        }
    }

    [Fact]
    public void Snapshot_WhileDisabled_StaysEmpty()
    {
        var profiler = NoireUI.Profiler;
        var previous = profiler.Enabled;

        profiler.Reset();
        profiler.Enabled = false;

        try
        {
            using (profiler.Measure("ignored"))
            {
            }

            profiler.Snapshot().Should().BeEmpty("a disabled profiler must not even record that a scope ran");
        }
        finally
        {
            profiler.Enabled = previous;
            profiler.Reset();
        }
    }

    [Fact]
    public void Measure_RecordsTheScopeByName()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("widget"))
            {
            }

            // The totals for a frame are closed off when the frame number moves, so the snapshot reports the frame
            // that finished rather than the one still being built.
            advance();

            using (profiler.Measure("widget"))
            {
            }

            var entry = profiler.Snapshot().Single(e => e.Name == "widget");

            entry.Calls.Should().Be(1);
            entry.LastMs.Should().BeGreaterThanOrEqualTo(0d);
        });
    }

    [Fact]
    public void Measure_CountsEveryCallWithinAFrame()
    {
        WithProfiler((profiler, advance) =>
        {
            for (var i = 0; i < 5; i++)
            {
                using (profiler.Measure("row"))
                {
                }
            }

            advance();

            using (profiler.Measure("row"))
            {
            }

            profiler.Snapshot().Single(e => e.Name == "row").Calls.Should().Be(5);
        });
    }

    [Fact]
    public void Measure_RecordsWhatTheScopeAllocated()
    {
        WithProfiler((profiler, advance) =>
        {
            // Comfortably under the 85 KB large-object threshold, so this is an ordinary gen 0 allocation and the
            // counter reports it the way it reports a widget's garbage.
            const int size = 10_000;

            byte[]? held = null;

            using (profiler.Measure("allocating"))
            {
                held = new byte[size];
            }

            advance();

            using (profiler.Measure("allocating"))
            {
            }

            // Asserted rather than discarded: without a use the allocation is a candidate for elimination, and the
            // test would then be measuring whether the JIT bothered.
            held!.Length.Should().Be(size);

            var entry = profiler.Snapshot().Single(e => e.Name == "allocating");

            // Approximately, not exactly: the array carries a header on top of its elements. The point is that the
            // figure is the caller's allocation rather than a count of something else.
            entry.SelfLastBytes.Should().BeGreaterThanOrEqualTo(size);
            entry.SelfLastBytes.Should().BeLessThan(size + 512);
        });
    }

    [Fact]
    public void Measure_ReportsZeroForAScopeThatAllocatesNothing()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("quiet"))
            {
            }

            advance();

            using (profiler.Measure("quiet"))
            {
            }

            // Exactly zero rather than nearly zero, and the strictness is the point: measuring must not cost the thing
            // being measured anything at all.
            profiler.Snapshot().Single(e => e.Name == "quiet").SelfLastBytes.Should().Be(0L);
        });
    }

    [Fact]
    public void Measure_ChargesNestedAllocationToTheScopeThatSpentIt()
    {
        WithProfiler((profiler, advance) =>
        {
            const int size = 10_000;

            byte[]? held = null;

            using (profiler.Measure("parent"))
            {
                using (profiler.Measure("child"))
                {
                    held = new byte[size];
                }
            }

            advance();

            using (profiler.Measure("parent"))
            {
            }

            held!.Length.Should().Be(size);

            var snapshot = profiler.Snapshot();
            var parent = snapshot.Single(e => e.Name == "parent");
            var child = snapshot.Single(e => e.Name == "child");

            child.SelfLastBytes.Should().BeGreaterThanOrEqualTo(size);

            // The parent's total includes the child's allocation and its self excludes it, which is what stops a page
            // from looking responsible for the garbage of every widget on it.
            parent.LastBytes.Should().BeGreaterThanOrEqualTo(size);
            parent.SelfLastBytes.Should().Be(0L);
        });
    }

    [Fact]
    public void TotalAverageBytes_WhileDisabled_ReadsZero()
    {
        var profiler = NoireUI.Profiler;
        var previousEnabled = profiler.Enabled;
        var previousFrame = NoireUI.FrameOverride;

        var frame = 0;
        NoireUI.FrameOverride = () => frame;

        profiler.Reset();
        profiler.Enabled = true;

        try
        {
            byte[]? held = null;

            using (profiler.Measure("allocating"))
            {
                held = new byte[10_000];
            }

            frame++;

            using (profiler.Measure("allocating"))
            {
            }

            held!.Length.Should().Be(10_000);
            profiler.TotalAverageBytes.Should().BeGreaterThan(0d);

            // Switched off without a reset, so the nodes still hold their figures. The reading has to stop rather than
            // keep reporting the last one, because a consumer displaying it every frame cannot tell a live number from
            // a number that stopped moving.
            profiler.Enabled = false;

            profiler.TotalAverageBytes.Should().Be(0d);
        }
        finally
        {
            profiler.Enabled = previousEnabled;
            profiler.Reset();
            NoireUI.FrameOverride = previousFrame;
        }
    }

    [Fact]
    public void Measure_KeepsScopesApart()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("first"))
            {
            }

            using (profiler.Measure("second"))
            {
            }

            advance();

            using (profiler.Measure("first"))
            {
            }

            profiler.Snapshot().Select(e => e.Name).Should().Contain(["first", "second"]);
        });
    }

    [Fact]
    public void AScopeThatStopsRunning_FallsBackToZero()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("transient"))
            {
            }

            // Totals are rolled up lazily, on the next measurement, so it takes two measured frames for a scope that
            // stopped running to fall away: the first closes off the frame it ran in, the second the frame it did not.
            advance();

            using (profiler.Measure("other"))
            {
            }

            profiler.Snapshot().Single(e => e.Name == "transient").Calls.Should().Be(1,
                "the frame that has just been closed off is the one it last ran in");

            advance();

            using (profiler.Measure("other"))
            {
            }

            profiler.Snapshot().Single(e => e.Name == "transient").Calls.Should().Be(0,
                "a widget that has been closed must stop looking expensive rather than holding its last reading");
        });
    }

    [Fact]
    public void Measure_DisposedTwice_IsCountedOnce()
    {
        // The shape that shipped broken: `using var scope = ...` followed by an explicit Dispose() closes once by hand
        // and once at the end of the block. Counted twice, and the second reading ran from the original start to the
        // end of the enclosing block, so the scope swallowed everything drawn after it.
        WithProfiler((profiler, advance) =>
        {
            var scope = profiler.Measure("closed twice");
            scope.Dispose();
            scope.Dispose();

            advance();

            using (profiler.Measure("other"))
            {
            }

            profiler.Snapshot().Single(e => e.Name == "closed twice").Calls.Should().Be(1);
        });
    }

    [Fact]
    public void Nested_ScopesDoNotChargeTheirTimeToTheParentsSelf()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("outer"))
            {
                Spin();

                using (profiler.Measure("inner"))
                    Spin();
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var outer = profiler.Snapshot().Single(e => e.Name == "outer");
            var inner = profiler.Snapshot().Single(e => e.Name == "inner");

            // The parent's total covers the child; its self time does not. This is what makes the column addable:
            // summing totals counts the child once for itself and again for every scope around it.
            outer.LastMs.Should().BeGreaterThanOrEqualTo(inner.LastMs);
            outer.SelfLastMs.Should().BeLessThan(outer.LastMs);
            inner.SelfLastMs.Should().BeApproximately(inner.LastMs, 0.0001d);
        });
    }

    [Fact]
    public void Nested_SelfTimesSumToTheOutermostTotal()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("page"))
            {
                Spin();

                using (profiler.Measure("widget a"))
                    Spin();

                using (profiler.Measure("widget b"))
                    Spin();
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var rows = profiler.Snapshot();
            var page = rows.Single(e => e.Name == "page");

            var selfTotal = rows.Where(e => e.Name is "page" or "widget a" or "widget b").Sum(e => e.SelfLastMs);

            // Every piece of work counted exactly once, which is the property the window's totals line rests on.
            selfTotal.Should().BeApproximately(page.LastMs, page.LastMs * 0.05d);
        });
    }

    /// <summary>
    /// Burns a little wall clock, so a scope has something to measure. A profiler test that measured nothing would pass
    /// against arithmetic that never ran.
    /// </summary>
    private static void Spin()
    {
        var until = System.Diagnostics.Stopwatch.GetTimestamp();

        while (System.Diagnostics.Stopwatch.GetElapsedTime(until).TotalMilliseconds < 1d)
        {
        }
    }

    [Fact]
    public void AScopeCalledFromTwoPlaces_IsOneRowPerCaller()
    {
        // Keyed by name alone, a helper had a single row whose parent was whichever caller ran last. Every other
        // caller then showed a self time reduced by a child that was nowhere underneath it, and the difference
        // disappeared from the tree. Keyed by path, each caller owns its own node and every branch adds up.
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("page a"))
            {
                using (profiler.Measure("shared helper"))
                    Spin();
            }

            using (profiler.Measure("page b"))
            {
                using (profiler.Measure("shared helper"))
                    Spin();
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var rows = profiler.Snapshot();
            var helpers = rows.Where(e => e.Name == "shared helper").ToList();

            helpers.Should().HaveCount(2, "one node per call path");

            var pageA = rows.Single(e => e.Name == "page a");
            var pageB = rows.Single(e => e.Name == "page b");

            helpers.Select(h => h.ParentId).Should().BeEquivalentTo([pageA.Id, pageB.Id]);

            // Each caller's own branch is complete: its total is its self plus the helper actually nested in it.
            foreach (var page in new[] { pageA, pageB })
            {
                var child = helpers.Single(h => h.ParentId == page.Id);

                (page.SelfLastMs + child.LastMs).Should()
                    .BeApproximately(page.LastMs, page.LastMs * 0.05d, "{0}'s branch has to add up on its own", page.Name);
            }
        });
    }

    [Fact]
    public void TheSameScopeUnderOneParent_StaysOneRow()
    {
        // The other half of the rule: repeated calls from the same place aggregate, or a list of two hundred rows
        // would become two hundred nodes.
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("page"))
            {
                for (var i = 0; i < 3; i++)
                {
                    using (profiler.Measure("row"))
                    {
                    }
                }
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var rows = profiler.Snapshot().Where(e => e.Name == "row").ToList();

            rows.Should().HaveCount(1);
            rows[0].Calls.Should().Be(3);
        });
    }

    [Fact]
    public void AScopeOutsideTheRoot_IsAdoptedByIt()
    {
        // NoireUI's own frame pass runs from a second draw handler, alongside the host's rather than inside it, so its
        // scopes open with nothing above them. They still belong to the frame the host is timing, so the root takes
        // them and the tree keeps one trunk.
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure(UiProfiler.RootScopeName))
                Spin();

            using (profiler.Measure("hub pass"))
                Spin();

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var rows = profiler.Snapshot();
            var root = rows.Single(e => e.Name == UiProfiler.RootScopeName);
            var adopted = rows.Single(e => e.Name == "hub pass");

            adopted.ParentId.Should().Be(root.Id, "an orphan belongs under the frame it ran in");

            // The root's total has to cover what it adopted, or the branch would not add up, while its self time must
            // not: the adopted scope ran outside the root's own clock.
            root.LastMs.Should().BeGreaterThanOrEqualTo(adopted.LastMs);
            (root.SelfLastMs + adopted.LastMs).Should().BeApproximately(root.LastMs, root.LastMs * 0.05d);
        });
    }

    [Fact]
    public void AScopeMeasuredBeforeTheRootExisted_IsRehomedUnderIt()
    {
        // NoireUI's own draw handler can run before the host's, so on the first frames its scopes open with no root to
        // be adopted by. Left alone they sit outside the tree forever and the same scope appears twice, once stranded
        // and once adopted, which is exactly what shipped.
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("hub pass"))
                Spin();

            advance();

            using (profiler.Measure(UiProfiler.RootScopeName))
                Spin();

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var rows = profiler.Snapshot();
            var root = rows.Single(e => e.Name == UiProfiler.RootScopeName);

            rows.Where(e => e.Name == "hub pass").Should().ContainSingle("the stranded node is rehomed, not duplicated")
                .Which.ParentId.Should().Be(root.Id);
        });
    }

    [Fact]
    public void SetExcluded_TakesTheScopeOutOfTheTotals()
    {
        WithProfiler((profiler, advance) =>
        {
            byte[]? held = null;

            using (profiler.Measure("kept"))
                Spin();

            using (profiler.Measure("noisy"))
            {
                held = new byte[10_000];
                Spin();
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            held!.Length.Should().Be(10_000);

            var noisy = profiler.Snapshot().Single(e => e.Name == "noisy");

            var msBefore = profiler.TotalAverageMs;
            var bytesBefore = profiler.TotalAverageBytes;

            profiler.SetExcluded(noisy.Id, true).Should().BeTrue();

            // Both totals, because both are what a reader judges a change by, and an exclusion that moved the
            // milliseconds while leaving the bytes counting the same scope would be worse than none at all.
            profiler.TotalAverageMs.Should().BeApproximately(msBefore - noisy.SelfAverageMs, 0.0001d);
            profiler.TotalAverageBytes.Should().BeApproximately(bytesBefore - noisy.SelfAverageBytes, 0.0001d);

            profiler.ExcludedCount.Should().Be(1);
            profiler.IsExcluded(noisy.Id).Should().BeTrue();
        });
    }

    [Fact]
    public void AnExcludedScope_IsStillMeasuredAndStillReported()
    {
        // The point of the mark is the sums, not the row. A scope dropped from the table or frozen at its last reading
        // would leave no way to see what the thing being excluded actually costs.
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("window"))
                Spin();

            advance();

            using (profiler.Measure("window"))
                Spin();

            var before = profiler.Snapshot().Single(e => e.Name == "window");

            profiler.SetExcluded(before.Id, true);

            advance();

            using (profiler.Measure("window"))
                Spin();

            var after = profiler.Snapshot().Single(e => e.Name == "window");

            after.Excluded.Should().BeTrue("the row has to be able to draw itself as excluded");
            after.Calls.Should().Be(1);
            after.SelfLastMs.Should().BeGreaterThan(0d);
        });
    }

    [Fact]
    public void ToggleExcluded_PutsTheScopeBack()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("scope"))
                Spin();

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var id = profiler.Snapshot().Single(e => e.Name == "scope").Id;
            var total = profiler.TotalAverageMs;

            profiler.ToggleExcluded(id).Should().BeTrue("the first right-click excludes it");
            profiler.ToggleExcluded(id).Should().BeFalse("the second puts it back");

            profiler.ExcludedCount.Should().Be(0);
            profiler.TotalAverageMs.Should().BeApproximately(total, 0.0001d);
        });
    }

    [Fact]
    public void SetExcluded_MovesTheGenerationOnlyWhenItChangedSomething()
    {
        // A reader rebuilds on the generation, so a mark applied between two measured frames has to move it or the
        // totals on screen would keep including the scope until something else happened to roll a frame.
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("scope"))
            {
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var id = profiler.Snapshot().Single(e => e.Name == "scope").Id;
            var before = profiler.Generation;

            profiler.SetExcluded(id, true);
            profiler.Generation.Should().NotBe(before);

            var settled = profiler.Generation;

            profiler.SetExcluded(id, true);
            profiler.Generation.Should().Be(settled, "nothing changed, so nothing needs redrawing");
        });
    }

    [Fact]
    public void SetExcluded_ForAScopeThatIsNotThere_ReportsItRatherThanThrowing()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("scope"))
            {
            }

            advance();

            profiler.SetExcluded(int.MaxValue, true).Should().BeFalse();
            profiler.IsExcluded(int.MaxValue).Should().BeFalse();
            profiler.ToggleExcluded(int.MaxValue).Should().BeFalse();
        });
    }

    [Fact]
    public void ClearExclusions_CountsEveryScopeAgainWithoutForgettingAnything()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("first"))
                Spin();

            using (profiler.Measure("second"))
                Spin();

            advance();

            using (profiler.Measure("tick"))
            {
            }

            var rows = profiler.Snapshot();
            var total = profiler.TotalAverageMs;

            foreach (var row in rows)
                profiler.SetExcluded(row.Id, true);

            profiler.TotalAverageMs.Should().Be(0d, "every scope is excluded");

            profiler.ClearExclusions();

            profiler.ExcludedCount.Should().Be(0);
            profiler.TotalAverageMs.Should().BeApproximately(total, 0.0001d,
                "the marks are lifted without discarding a single measurement");
        });
    }

    [Fact]
    public void Reset_ForgetsTheMarksWithTheNodesTheyWereOn()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("scope"))
            {
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            profiler.SetExcluded(profiler.Snapshot().Single(e => e.Name == "scope").Id, true);
            profiler.Reset();

            // The ids start again from the beginning after a reset, so a mark that outlived its node would land on
            // whichever scope happened to be handed the id next.
            profiler.ExcludedCount.Should().Be(0);

            using (profiler.Measure("scope"))
            {
            }

            advance();

            using (profiler.Measure("tick"))
            {
            }

            profiler.Snapshot().Single(e => e.Name == "scope").Excluded.Should().BeFalse();
        });
    }

    [Fact]
    public void Reset_ForgetsEverything()
    {
        WithProfiler((profiler, advance) =>
        {
            using (profiler.Measure("gone"))
            {
            }

            advance();
            profiler.Reset();

            profiler.Snapshot().Should().BeEmpty();
        });
    }
}
