using FluentAssertions;
using NoireLib.Animations.Helpers;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Ported from PapEdit's AnimationPathHelper: locks the human skeleton path shape the game expects
/// under chara/human, and the fallback chain a skeleton walks when it has no copy of an animation
/// another skeleton already answers to (a Highlander borrowing a Miqo'te's a0001 frames, and so on).
/// </summary>
public class EmotePathHelperTests
{
    [Fact]
    public void GetSkeletonPath_BuildsTheGamePathUnderTheSkeletonsAnimationFolder()
        => EmotePathHelper.GetSkeletonPath("c0801", "bt_common/emote/beesknees.pap")
            .Should().Be("chara/human/c0801/animation/a0001/bt_common/emote/beesknees.pap");

    /// <summary>
    /// Every entry of the human skeleton fallback table, written out here rather than read back from the
    /// implementation so an accidental edit to one chain fails this test instead of agreeing with itself.
    /// These are the current chains, and they differ from the older PapEdit table this was first ported
    /// from: c0401, c0601, c0801 and c1001 each carried one extra link there, and c0901 and c1501 did not
    /// yet fall back to one another.
    /// </summary>
    [Fact]
    public void GetFallbackOrder_MatchesTheHumanSkeletonFallbackTable()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["c0101"] = ["c0101"],
            ["c0201"] = ["c0201", "c0801", "c0101"],
            ["c0301"] = ["c0301", "c0101"],
            ["c0401"] = ["c0401", "c0801", "c0101"],
            ["c0501"] = ["c0501", "c0101"],
            ["c0601"] = ["c0601", "c0801", "c0101"],
            ["c0701"] = ["c0701", "c0101"],
            ["c0801"] = ["c0801", "c0101"],
            ["c0901"] = ["c0901", "c1501", "c0101"],
            ["c1001"] = ["c1001", "c0801", "c0101"],
            ["c1101"] = ["c1101", "c0101"],
            ["c1201"] = ["c1201", "c1101", "c0101"],
            ["c1301"] = ["c1301", "c0101"],
            ["c1401"] = ["c1401", "c0801", "c0101"],
            ["c1501"] = ["c1501", "c0901", "c0101"],
            ["c1601"] = ["c1601", "c0801", "c0101"],
            ["c1701"] = ["c1701", "c0101"],
            ["c1801"] = ["c1801", "c0801", "c0101"],
        };

        foreach (var (skeletonId, fallbacks) in expected)
        {
            EmotePathHelper.GetFallbackOrder(skeletonId).Should().Equal(fallbacks,
                $"skeleton {skeletonId} must fall back exactly the way the table says");
        }
    }

    [Fact]
    public void GetFallbackOrder_FallsBackToJustItselfForASkeletonOutsideTheTable()
        => EmotePathHelper.GetFallbackOrder("c9999").Should().Equal("c9999");

    [Fact]
    public void GetFallbackOrder_IsCaseInsensitive()
        => EmotePathHelper.GetFallbackOrder("C0401").Should().Equal("c0401", "c0801", "c0101");
}
