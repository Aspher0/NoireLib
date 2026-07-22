using FluentAssertions;
using NoireLib.UI;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the combo family at zero allocation per frame, in the state they spend nearly all of their life in: closed.
/// </summary>
/// <remarks>
/// A dropdown is open for as long as it takes to pick something and closed for every other frame of a session, so the
/// closed draw is the one that runs for every combo on a page sixty times a second. It is also the one nothing was
/// watching: the open path was built with a clipper and a reused scoring list from the start, and measured 0 before
/// this audit, while the closed path quietly rebuilt its preview text every frame.<br/>
/// The widgets are constructed once, in fields, because the harness charges everything the measured delegate does to
/// the surface under test.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireSelectionAllocationTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly string[] Fruit = ["Apple", "Banana", "Cherry", "Date", "Elderberry"];

    private static readonly NoireComboBox<string>[] Combos = BuildCombos();
    private static readonly NoireMultiCombo<string>[] Multis = BuildMultis(2);
    private static readonly NoireMultiCombo<string>[] Overflowing = BuildMultis(5);
    private static readonly NoireTagInput[] Tags = BuildTags();
    private static readonly NoireTagInput FewTags = BuildTagField("alloc_few", 4);
    private static readonly NoireTagInput SomeTags = BuildTagField("alloc_some", 10);
    private static readonly NoireTagInput ScreenfulOfTags = BuildTagField("alloc_screenful", 60);
    private static readonly NoireTagInput FarMoreThanAScreenful = BuildTagField("alloc_offscreen", 1000);

    private readonly UiHarness harness;

    public NoireSelectionAllocationTests(UiHarness harness) => this.harness = harness;

    private static NoireComboBox<string>[] BuildCombos()
    {
        var built = new NoireComboBox<string>[Repeats];

        for (var i = 0; i < Repeats; i++)
        {
            built[i] = new NoireComboBox<string>("alloc_combo" + i, Fruit);
            built[i].Select(1);
        }

        return built;
    }

    private static NoireMultiCombo<string>[] BuildMultis(int chosen)
    {
        var built = new NoireMultiCombo<string>[Repeats];

        for (var i = 0; i < Repeats; i++)
        {
            built[i] = new NoireMultiCombo<string>("alloc_multi" + chosen + "_" + i, Fruit);

            for (var pick = 0; pick < chosen; pick++)
                built[i].Set(Fruit[pick], true);
        }

        return built;
    }

    private static NoireTagInput[] BuildTags()
    {
        var built = new NoireTagInput[3];

        for (var i = 0; i < built.Length; i++)
            built[i] = BuildTagField("alloc_tags" + i, 60);

        return built;
    }

    private static NoireTagInput BuildTagField(string id, int count)
    {
        var names = new List<string>(count);

        for (var i = 0; i < count; i++)
            names.Add("tag" + i);

        return new NoireTagInput(id, names);
    }

    [Fact]
    public void ComboBox_Closed_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                foreach (var combo in Combos)
                    combo.Draw();
            },
            warmUpFrames: 2);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void MultiCombo_Closed_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                foreach (var multi in Multis)
                    multi.Draw();
            },
            warmUpFrames: 2);

        // 136 bytes a combo before this: the preview named the selected items into a string built by repeated
        // concatenation, and reached them through a property that snapshots the selection into a new list on every
        // read. Neither had changed since the last time the user picked something.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void MultiCombo_WithMoreSelectedThanItNames_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                foreach (var multi in Overflowing)
                    multi.Draw();
            },
            warmUpFrames: 2);

        // The "+2 more" tail is composed through a format string, which is its own allocation and its own cache.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TagInput_WithSixtyTags_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                foreach (var tags in Tags)
                    tags.Draw();
            },
            warmUpFrames: 2);

        // Already true before this audit, and held because the chips are a wrapped flow rather than a row list: a
        // clipper cannot cover this surface, so nothing else would notice it growing a per-chip allocation.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TagInput_DrawsEveryChipThatFitsOnScreen()
    {
        var few = harness.Draw(static () => FewTags.Draw(), warmUpFrames: 2);
        var some = harness.Draw(static () => SomeTags.Draw(), warmUpFrames: 2);

        // The other half of the clipping test below, and the reason it is not enough on its own: a skip that dropped
        // every chip would satisfy "two fields submit the same geometry" perfectly.
        some.TotalVtxCount.Should().BeGreaterThan(few.TotalVtxCount);
    }

    [Fact]
    public void TagInput_DoesNotSubmitChipsPastTheClipRect()
    {
        var screenful = harness.Draw(static () => ScreenfulOfTags.Draw(), warmUpFrames: 2);
        var far = harness.Draw(static () => FarMoreThanAScreenful.Draw(), warmUpFrames: 2);

        // Both fields overflow the display, so both submit exactly what fits and nothing beyond it. Before the audit
        // the second one submitted 120,200 vertices against the first's 6,200, every one of them tessellated on the
        // draw thread for ImGui to throw away against the clip rect one line later.
        far.TotalVtxCount.Should().Be(screenful.TotalVtxCount);
    }
}
