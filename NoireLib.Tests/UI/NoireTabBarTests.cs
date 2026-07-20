using FluentAssertions;
using NoireLib.UI;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for <see cref="NoireTabBar"/> and <see cref="NoireBadge"/>: the switch state machine that the widget
/// exists to get right, and the badge placement arithmetic.
/// </summary>
/// <remarks>
/// The switch resolution is deliberately separable from the drawing, because every case worth testing here is a case
/// that a hand-rolled pending-tab field gets wrong, and none of them need an ImGui context to state.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireTabBarTests
{
    private static List<UiTab> ThreeTabs() =>
    [
        new UiTab("general", "General"),
        new UiTab("filters", "Filters"),
        new UiTab("about", "About"),
    ];

    #region Switch resolution

    [Fact]
    public void ResolveSwitch_AnotherTab_IsAccepted()
    {
        NoireTabBar.ResolveSwitch(ThreeTabs(), "general", "filters").Should().Be(TabSwitch.Accepted);
    }

    [Fact]
    public void ResolveSwitch_TheOpenTab_IsAlreadyOpen()
    {
        NoireTabBar.ResolveSwitch(ThreeTabs(), "filters", "filters")
            .Should().Be(TabSwitch.AlreadyOpen, "switching to where you already are is not a switch");
    }

    [Fact]
    public void ResolveSwitch_BeforeTheBarHasDrawn_IsAccepted()
    {
        NoireTabBar.ResolveSwitch(ThreeTabs(), null, "about")
            .Should().Be(TabSwitch.Accepted, "a switch asked for before the first frame takes effect on it");
    }

    [Fact]
    public void ResolveSwitch_UnknownId_IsRefused()
    {
        NoireTabBar.ResolveSwitch(ThreeTabs(), "general", "nope").Should().Be(TabSwitch.Unknown);
    }

    [Fact]
    public void ResolveSwitch_RemovedTab_IsRefused()
    {
        var tabs = ThreeTabs();
        tabs.RemoveAt(1);

        NoireTabBar.ResolveSwitch(tabs, "general", "filters")
            .Should().Be(TabSwitch.Unknown, "a tab closed since the request was written is gone, not pending");
    }

    [Fact]
    public void ResolveSwitch_NoTabsAtAll_IsRefused()
    {
        NoireTabBar.ResolveSwitch([], null, "general").Should().Be(TabSwitch.Unknown);
    }

    [Fact]
    public void ResolveSwitch_DisabledTab_IsUnreachable()
    {
        var tabs = ThreeTabs();
        tabs[2].Enabled = () => false;

        NoireTabBar.ResolveSwitch(tabs, "general", "about")
            .Should().Be(TabSwitch.Unreachable, "code may not reach a tab a click cannot");
    }

    [Fact]
    public void ResolveSwitch_DisabledTabThatIsAlreadyOpen_IsStillReportedUnreachable()
    {
        var tabs = ThreeTabs();
        tabs[0].Enabled = () => false;

        NoireTabBar.ResolveSwitch(tabs, "general", "general")
            .Should().Be(TabSwitch.Unreachable, "reachability is about the tab, not about where the user happens to be");
    }

    [Fact]
    public void ResolveSwitch_ReEnabledTab_BecomesReachableAgain()
    {
        var tabs = ThreeTabs();
        var hasData = false;
        tabs[2].Enabled = () => hasData;

        NoireTabBar.ResolveSwitch(tabs, "general", "about").Should().Be(TabSwitch.Unreachable);

        hasData = true;

        NoireTabBar.ResolveSwitch(tabs, "general", "about")
            .Should().Be(TabSwitch.Accepted, "the predicate is re-read, not remembered");
    }

    [Fact]
    public void ResolveSwitch_IsCaseSensitive()
    {
        NoireTabBar.ResolveSwitch(ThreeTabs(), "general", "Filters")
            .Should().Be(TabSwitch.Unknown, "ids are ordinal, so a near miss is a miss rather than a silent match");
    }

    #endregion

    #region Tab state

    [Fact]
    public void UiTab_NoPredicate_IsEnabled()
    {
        new UiTab("a", "A").IsEnabled().Should().BeTrue();
    }

    [Fact]
    public void UiTab_NoBadgeDelegate_CountsNothing()
    {
        new UiTab("a", "A").BadgeCount().Should().Be(0);
    }

    [Fact]
    public void UiTab_BadgeDelegate_IsReadEveryTime()
    {
        var count = 1;
        var tab = new UiTab("a", "A") { Badge = () => count };

        tab.BadgeCount().Should().Be(1);
        count = 7;
        tab.BadgeCount().Should().Be(7);
    }

    [Fact]
    public void UiTab_BlankId_GetsOneOfItsOwn()
    {
        var tab = new UiTab(null, "A");

        tab.Id.Should().NotBeNullOrWhiteSpace("a tab with no id could never be switched to, or told apart from another");
    }

    [Fact]
    public void NoireTabBar_BeforeDrawing_HasNoCurrentTab()
    {
        var bar = new NoireTabBar("test") { Tabs = { new UiTab("a", "A") } };

        bar.Current.Should().BeNull("Current answers for what was drawn, and nothing has been");
    }

    #endregion

    #region Badge

    [Fact]
    public void BadgeStyle_CountWithinTheCap_IsItself()
    {
        new BadgeStyle().FormatCount(9).Should().Be("9");
    }

    [Fact]
    public void BadgeStyle_CountOverTheCap_IsCappedWithAPlus()
    {
        new BadgeStyle { MaxCount = 99 }.FormatCount(1200).Should().Be("99+");
    }

    [Fact]
    public void BadgeStyle_NoCap_ShowsEverything()
    {
        new BadgeStyle { MaxCount = 0 }.FormatCount(1200).Should().Be("1200");
    }

    [Fact]
    public void BadgeStyle_Scale_MovesEveryMeasurementTogether()
    {
        var style = new BadgeStyle { Scale = 2f };

        style.Sized(style.DotSize).Should().Be(NoireUI.Scaled(style.DotSize * 2f));
        style.Sized(style.MinSize).Should().Be(NoireUI.Scaled(style.MinSize * 2f));
        style.ResolveTextSize().Should().Be(style.TextSizePx * 2f);
    }

    [Fact]
    public void BadgeStyle_DefaultScale_ChangesNothing()
    {
        var style = new BadgeStyle();

        style.Sized(style.MinSize).Should().Be(NoireUI.Scaled(style.MinSize));
        style.ResolveTextSize().Should().Be(style.TextSizePx);
    }

    [Fact]
    public void BadgeStyle_TinyScale_StillAsksForARealTextSize()
    {
        new BadgeStyle { Scale = 0.01f }.ResolveTextSize()
            .Should().Be(1f, "a font size rounded away to nothing would draw no text at all");
    }

    [Fact]
    public void Badge_Place_ScalesTheOffsetWithTheBadge()
    {
        var target = new UiRect(0f, 0f, 100f, 40f);
        var style = new BadgeStyle { Anchor = Vector2.Zero, Offset = new Vector2(10f, 0f), Scale = 3f };

        NoireBadge.Place(target, new Vector2(10f, 10f), style).Center.X
            .Should().Be(NoireUI.Scaled(30f), "a badge three times the size sits three times as far off its corner");
    }

    [Fact]
    public void BadgeStyle_With_LeavesTheOriginalAlone()
    {
        var original = new BadgeStyle { MaxCount = 99 };
        var copy = original.With(s => s.MaxCount = 9);

        original.MaxCount.Should().Be(99);
        copy.MaxCount.Should().Be(9);
    }

    [Fact]
    public void Badge_Place_StraddlesTheAnchoredCorner()
    {
        var target = new UiRect(100f, 100f, 80f, 20f);
        var style = new BadgeStyle { Anchor = new Vector2(1f, 0f), Offset = Vector2.Zero };

        var placed = NoireBadge.Place(target, new Vector2(10f, 10f), style);

        placed.Center.Should().Be(
            new Vector2(180f, 100f), "the badge is centred on the corner rather than tucked inside or outside it");
    }

    [Fact]
    public void Badge_Place_IsNeverMovedToFit()
    {
        // A tab at the far end of a strip. The badge still straddles its corner and hangs past the end: what keeps it
        // out of the column is the caller's clip, so that a tab going out of view takes its badge with it.
        var lastTab = new UiRect(180f, 0f, 60f, 20f);
        var style = new BadgeStyle { Anchor = new Vector2(1f, 0f), Offset = Vector2.Zero };

        NoireBadge.Place(lastTab, new Vector2(16f, 16f), style).Center.X
            .Should().Be(lastTab.Right, "the badge belongs to the tab, wherever the tab happens to be");
    }

    [Fact]
    public void Badge_Place_AppliesTheOffset()
    {
        var target = new UiRect(0f, 0f, 100f, 40f);
        var style = new BadgeStyle { Anchor = new Vector2(0f, 1f), Offset = new Vector2(4f, -4f) };

        NoireBadge.Place(target, new Vector2(10f, 10f), style).Center
            .Should().Be(new Vector2(4f, 36f));
    }

    #endregion
}
