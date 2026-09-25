using FluentAssertions;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the headless state machines skins draw: clicks, holds, drags, reorders, pickers, selection, menus and confirmations.</summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireHeadlessStateTests : IDisposable
{
    private static readonly NoireString LabelA = new("test.headless.a", "A");
    private static readonly NoireString LabelB = new("test.headless.b", "B");
    private static readonly NoireString Hint = new("test.headless.hint", "Hint");

    private readonly bool? originalReducedMotion = NoireUI.HasReducedMotionOverride ? NoireUI.ReducedMotion : null;

    private float time;
    private int frame;

    public NoireHeadlessStateTests()
    {
        NoireUI.TimeOverride = () => time;
        NoireUI.FrameOverride = () => frame;
        NoireUI.ReducedMotion = false;
    }

    public void Dispose()
    {
        NoireUI.TimeOverride = null;
        NoireUI.FrameOverride = null;
        NoireUI.LeaveWindowMotion();
        NoireDismiss.ResetSampling();

        if (originalReducedMotion is { } motion)
            NoireUI.ReducedMotion = motion;
        else
            NoireUI.ClearReducedMotion();
    }

    #region Clicks

    [Fact]
    public void Clicks_SecondClickInsideTheWindow_IsADoubleClick()
    {
        var clicks = new NoireClicks<string>();
        var row = "row";

        clicks.Click(row).Should().BeFalse();
        time = 0.15f;
        clicks.Click(row).Should().BeTrue();

        time = 1f;
        clicks.TakeSingle().Should().BeNull("a completed double click leaves no single click behind");
    }

    [Fact]
    public void Clicks_SecondClickAfterTheWindow_IsTwoSingles()
    {
        var clicks = new NoireClicks<string>();
        var row = "row";

        clicks.Click(row);
        time = 0.3f;
        clicks.Click(row).Should().BeFalse();
    }

    [Fact]
    public void Clicks_SingleIsHeldUntilTheWindowPasses_ThenGivenOnce()
    {
        var clicks = new NoireClicks<string>();
        var row = "row";

        clicks.Click(row);
        time = 0.1f;
        clicks.TakeSingle().Should().BeNull();

        time = 0.25f;
        clicks.TakeSingle().Should().BeSameAs(row);
        clicks.TakeSingle().Should().BeNull();
    }

    [Fact]
    public void Clicks_OnAnotherTarget_StartOver()
    {
        var clicks = new NoireClicks<string>();

        clicks.Click("first");
        time = 0.1f;
        clicks.Click("second").Should().BeFalse();
    }

    [Fact]
    public void Clicks_Cancel_DropsThePendingSingle()
    {
        var clicks = new NoireClicks<string>();

        clicks.Click("row");
        clicks.Cancel();
        time = 1f;
        clicks.TakeSingle().Should().BeNull();
    }

    #endregion

    #region Hold

    [Fact]
    public void Hold_FiresOnceWhenFull_ThenWaitsForARelease()
    {
        var progress = 0f;
        var armed = true;
        var fired = 0;

        for (var i = 0; i < 20; i++)
        {
            if (NoireHold.Advance(ref progress, ref armed, true, 1f, 0.1f))
                fired++;
        }

        fired.Should().Be(1);
        progress.Should().Be(0f);

        NoireHold.Advance(ref progress, ref armed, false, 1f, 0.1f);
        armed.Should().BeTrue();
    }

    [Fact]
    public void Hold_Released_DrainsFasterThanItFilled()
    {
        var progress = 0f;
        var armed = true;

        NoireHold.Advance(ref progress, ref armed, true, 1f, 0.5f);
        progress.Should().BeApproximately(0.5f, 0.0001f);

        NoireHold.Advance(ref progress, ref armed, false, 1f, 0.1f);
        progress.Should().BeApproximately(0.25f, 0.0001f);
    }

    [Fact]
    public void Hold_Update_UsesTheFrameTime()
    {
        var hold = new NoireHold();

        hold.Update(true, 1f);

        hold.Progress.Should().BeApproximately(1f / 60f, 0.0001f);
    }

    #endregion

    #region Drag

    [Fact]
    public void Drag_PressReleasedWithoutMoving_IsNoDrag()
    {
        var drag = new NoireDrag<string>();

        drag.Press("row", Vector2.Zero);
        drag.Step(new Vector2(1f, 1f), true).Should().Be(DragPhase.Pressed);
        drag.Step(new Vector2(1f, 1f), false).Should().Be(DragPhase.None);
        drag.Payload.Should().BeNull();
    }

    [Fact]
    public void Drag_PastTheThreshold_DragsThenDropsForOneFrame()
    {
        var drag = new NoireDrag<string>();

        drag.Press("row", Vector2.Zero);
        drag.Step(new Vector2(3f, 0f), true).Should().Be(DragPhase.Pressed);
        drag.Step(new Vector2(4f, 0f), true).Should().Be(DragPhase.Dragging);
        drag.Step(new Vector2(40f, 0f), false).Should().Be(DragPhase.Dropped);
        drag.Payload.Should().Be("row");
        drag.Position.Should().Be(new Vector2(40f, 0f));

        drag.Step(new Vector2(40f, 0f), false).Should().Be(DragPhase.None);
        drag.Payload.Should().BeNull();
    }

    #endregion

    #region Reorder

    [Fact]
    public void Reorder_EndOnAnotherRow_ReportsTheMove()
    {
        var reorder = new NoireReorder();
        var list = new List<string> { "a", "b", "c", "d" };

        reorder.Begin(0);
        reorder.Over(2);
        reorder.End(out var from, out var to).Should().BeTrue();
        NoireReorder.Apply(list, from, to).Should().BeTrue();

        list.Should().Equal("b", "c", "a", "d");
        reorder.Active.Should().BeFalse();
    }

    [Fact]
    public void Reorder_EndOnItsOwnRow_ReportsNothing()
    {
        var reorder = new NoireReorder();

        reorder.Begin(1);
        reorder.End(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Reorder_OverWhileInactive_IsIgnored()
    {
        var reorder = new NoireReorder();

        reorder.Over(3);

        reorder.To.Should().Be(-1);
    }

    #endregion

    #region Picker

    [Fact]
    public void Picker_FiltersBySearch_InSourceOrder()
    {
        var source = new List<string> { "Wave", "Dance", "Bow", "Wink" };
        var picker = new NoirePicker<string>(() => source, static (item, query) => item.Contains(query, StringComparison.OrdinalIgnoreCase));

        picker.Visible.Should().Equal("Wave", "Dance", "Bow", "Wink");

        picker.Search = "w";
        picker.Visible.Should().Equal("Wave", "Bow", "Wink");
    }

    [Fact]
    public void Picker_RefiltersOnlyWhenTheSearchOrVersionMoves()
    {
        var source = new List<string> { "Wave" };
        var calls = 0;
        var picker = new NoirePicker<string>(() => { calls++; return source; }, static (_, _) => true);

        _ = picker.Visible;
        _ = picker.Visible;
        calls.Should().Be(1);

        source.Add("Bow");
        _ = picker.Visible;
        calls.Should().Be(1, "the source changing alone does not refilter");

        picker.Version++;
        picker.Visible.Should().Equal("Wave", "Bow");
        calls.Should().Be(2);
    }

    [Fact]
    public void Picker_NewResults_ScrollBackToTheTop()
    {
        var source = new List<string>();

        for (var i = 0; i < 100; i++)
            source.Add("item" + i);

        var picker = new NoirePicker<string>(() => source, static (item, query) => item.Contains(query, StringComparison.Ordinal));
        _ = picker.Visible;

        picker.List.Layout(100, 10f, 100f);
        picker.List.ScrollTo(500f);

        picker.Search = "item1";
        _ = picker.Visible;

        picker.List.Scroll.Should().Be(0f);
    }

    [Fact]
    public void Picker_Marked_IsReadFromTheOwner()
    {
        var picker = new NoirePicker<string>(() => [], static (_, _) => true) { Marked = static item => item == "added" };

        picker.IsMarked("added").Should().BeTrue();
        picker.IsMarked("other").Should().BeFalse();
    }

    #endregion

    #region Selection

    private static readonly string[] Items = ["a", "b", "c", "d", "e"];

    [Fact]
    public void Selection_PlainClick_SelectsOnlyThatItem()
    {
        var selection = new NoireSelection<string>();

        selection.Click(0, Items, false, false);
        selection.Click(2, Items, false, false);

        selection.Selected.Should().BeEquivalentTo(["c"]);
    }

    [Fact]
    public void Selection_CtrlClick_Toggles()
    {
        var selection = new NoireSelection<string>();

        selection.Click(0, Items, false, false);
        selection.Click(2, Items, true, false);
        selection.Selected.Should().BeEquivalentTo(["a", "c"]);

        selection.Click(0, Items, true, false);
        selection.Selected.Should().BeEquivalentTo(["c"]);
    }

    [Fact]
    public void Selection_ShiftClick_SelectsTheRangeFromTheAnchor_EitherDirection()
    {
        var selection = new NoireSelection<string>();

        selection.Click(3, Items, false, false);
        selection.Click(1, Items, false, true);
        selection.Selected.Should().BeEquivalentTo(["b", "c", "d"]);

        selection.Click(4, Items, false, true);
        selection.Selected.Should().BeEquivalentTo(["d", "e"], "the anchor stays on the last plain click");
    }

    [Fact]
    public void Selection_CtrlShiftClick_AddsTheRange()
    {
        var selection = new NoireSelection<string>();

        selection.Click(0, Items, false, false);
        selection.Click(4, Items, true, false);
        selection.Click(2, Items, true, true);

        selection.Selected.Should().BeEquivalentTo(["a", "c", "d", "e"]);
    }

    [Fact]
    public void Selection_AnchorFollowsItsItem_AfterTheListIsReordered()
    {
        var selection = new NoireSelection<string>();
        selection.Click(1, Items, false, false);

        string[] reordered = ["e", "d", "c", "b", "a"];
        selection.Click(1, reordered, false, true);

        selection.Selected.Should().BeEquivalentTo(["d", "c", "b"]);
    }

    [Fact]
    public void Selection_ShiftWithoutAnchor_IsAPlainClick()
    {
        var selection = new NoireSelection<string>();

        selection.Click(2, Items, false, true);

        selection.Selected.Should().BeEquivalentTo(["c"]);
    }

    [Fact]
    public void Selection_Focus_KeepsASelectionThatHoldsTheItem()
    {
        var selection = new NoireSelection<string>();
        selection.Click(0, Items, false, false);
        selection.Click(1, Items, true, false);

        selection.Focus("b");
        selection.Count.Should().Be(2);

        selection.Focus("d");
        selection.Selected.Should().BeEquivalentTo(["d"]);
    }

    [Fact]
    public void Selection_Retain_DropsWhatIsGone_AndAnAnchorThatIsGone()
    {
        var selection = new NoireSelection<string>();
        selection.Click(0, Items, false, false);
        selection.Click(3, Items, true, false);

        selection.Retain(new HashSet<string> { "a", "b", "c" });
        selection.Selected.Should().BeEquivalentTo(["a"]);

        selection.Click(2, Items, false, true);
        selection.Selected.Should().BeEquivalentTo(["c"], "the anchor was on the removed item");
    }

    #endregion

    #region List state

    [Fact]
    public void ListState_ClampsTheScroll_WhenTheListShrinks()
    {
        NoireUI.ReducedMotion = true;
        var list = new NoireListState();

        list.Layout(100, 10f, 100f);
        list.ScrollTo(800f);
        list.Scroll.Should().Be(800f);

        list.Layout(20, 10f, 100f);

        list.Scroll.Should().Be(100f);
        list.First.Should().Be(10);
        list.Last.Should().Be(20);
    }

    [Fact]
    public void ListState_DrawsOnlyTheVisibleRows()
    {
        NoireUI.ReducedMotion = true;
        var list = new NoireListState();

        list.Layout(1000, 20f, 100f);
        list.ScrollTo(205f);
        list.Layout(1000, 20f, 100f);

        list.First.Should().Be(10);
        list.Last.Should().Be(17);
        list.RowTop(10).Should().Be(-5f);
    }

    [Fact]
    public void ListState_Reveal_BringsARowIntoView()
    {
        NoireUI.ReducedMotion = true;
        var list = new NoireListState();
        list.Layout(100, 10f, 50f);

        list.Reveal(30);
        list.Layout(100, 10f, 50f);
        list.Scroll.Should().Be(260f, "the row lands at the bottom edge");

        list.Reveal(2);
        list.Layout(100, 10f, 50f);
        list.Scroll.Should().Be(20f, "the row lands at the top edge");
    }

    [Fact]
    public void ListState_Reveal_EasesTowardsTheRow()
    {
        var list = new NoireListState();
        list.Layout(100, 10f, 50f);

        list.Reveal(7);
        list.Layout(100, 10f, 50f);

        list.Scroll.Should().BeGreaterThan(0f).And.BeLessThan(30f);
    }

    [Fact]
    public void ListState_Reveal_UnderReducedMotion_Snaps()
    {
        NoireUI.ReducedMotion = true;
        var list = new NoireListState();
        list.Layout(100, 10f, 50f);

        list.Reveal(7);
        list.Layout(100, 10f, 50f);

        list.Scroll.Should().Be(30f);
    }

    #endregion

    #region Menu

    [Fact]
    public void Menu_DropsLeadingTrailingAndDoubledSeparators()
    {
        var painter = new RecordingPainter();
        var menu = Begin(painter);

        menu.Separator();
        menu.Item(LabelA, NoireIcon.Play);
        menu.Separator();
        menu.Separator();
        menu.Item(LabelB, NoireIcon.Star);
        menu.Separator();

        painter.Lines.Should().Equal("item:A", "separator", "item:B");
    }

    [Fact]
    public void Menu_HiddenActions_LeaveNoSeparatorBehind()
    {
        var painter = new RecordingPainter();
        var menu = Begin(painter);
        var hidden = new UiAction<int>(LabelB, NoireIcon.Trash, static _ => { }) { Available = static _ => false };

        menu.Item(new UiAction<int>(LabelA, NoireIcon.Play, static _ => { }), 1);
        menu.Separator();
        menu.Item(hidden, 1);

        painter.Lines.Should().Equal("item:A");
    }

    [Fact]
    public void Menu_UnavailableActionShownDisabled_CarriesItsHint()
    {
        var painter = new RecordingPainter();
        var menu = Begin(painter);
        var disabled = new UiAction<int>(LabelA, NoireIcon.Cube, static _ => { })
        {
            Available = static _ => false,
            HideWhenUnavailable = false,
            Hint = Hint,
        };

        menu.Item(disabled, 1);

        painter.Lines.Should().Equal("item:A:disabled:Hint");
    }

    [Fact]
    public void Menu_ClickedAction_RunsOnItsTarget()
    {
        var ran = 0;
        var painter = new RecordingPainter { Click = "A" };
        var menu = Begin(painter);

        menu.Item(new UiAction<int>(LabelA, NoireIcon.Play, value => ran = value), 7);

        ran.Should().Be(7);
        menu.Clicked.Should().BeTrue();
    }

    [Fact]
    public void MenuState_IgnoresTheOpeningFrame_ThenClosesOnADismissal()
    {
        var state = new NoireMenuState();
        frame = 5;
        state.Open(new Vector2(10f, 10f));

        state.AfterDraw(false, true);
        state.IsOpen.Should().BeTrue("the press that opened it is still down");

        frame = 6;
        state.AfterDraw(false, false);
        state.IsOpen.Should().BeTrue();

        state.AfterDraw(false, true);
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void MenuState_ClosesOnAnItemClick_EvenOnTheOpeningFrame()
    {
        var state = new NoireMenuState();
        state.Open(Vector2.Zero);

        state.AfterDraw(true, false);

        state.IsOpen.Should().BeFalse();
    }

    private static NoireMenu Begin(RecordingPainter painter)
    {
        var menu = new NoireMenu();
        menu.Begin(painter);
        return menu;
    }

    private sealed class RecordingPainter : IOverlaySkin
    {
        public List<string> Lines { get; } = [];

        public string? Click { get; init; }

        public ToastStyle? Toasts => null;

        public void MenuStyle()
        {
        }

        public void MenuTitle(string title, uint gameIcon) => Lines.Add("title:" + title);

        public bool MenuItem(string label, NoireIcon? icon, bool enabled, string? hint, bool requiresCtrl)
        {
            Lines.Add("item:" + label + (enabled ? string.Empty : ":disabled") + (hint == null ? string.Empty : ":" + hint));
            return enabled && label == Click;
        }

        public void MenuSeparator() => Lines.Add("separator");

        public void Tooltip(string text, Vector2 anchorMin, Vector2 anchorMax)
        {
        }

        public bool CardBegin(string id, Vector2 anchorMin, Vector2 anchorMax, float width) => false;

        public void CardEnd()
        {
        }

        public void Pill(NoirePill pill, Vector2 windowMin, Vector2 windowMax)
        {
        }

        public ConfirmAnswer Confirm(in ConfirmState state, Vector2 bodyMin, Vector2 bodyMax) => ConfirmAnswer.Pending;
    }

    #endregion

    #region Dismiss

    [Fact]
    public void Dismiss_PressOutsideBothAreas_Closes()
    {
        var min = new Vector2(0f, 0f);
        var max = new Vector2(10f, 10f);
        var spareMin = new Vector2(20f, 0f);
        var spareMax = new Vector2(30f, 10f);

        NoireDismiss.ShouldClose(false, true, new Vector2(50f, 5f), min, max, spareMin, spareMax).Should().BeTrue();
        NoireDismiss.ShouldClose(false, true, new Vector2(5f, 5f), min, max, spareMin, spareMax).Should().BeFalse();
        NoireDismiss.ShouldClose(false, true, new Vector2(25f, 5f), min, max, spareMin, spareMax).Should().BeFalse();
        NoireDismiss.ShouldClose(false, false, new Vector2(50f, 5f), min, max, spareMin, spareMax).Should().BeFalse();
        NoireDismiss.ShouldClose(true, false, new Vector2(5f, 5f), min, max, spareMin, spareMax).Should().BeTrue();
    }

    [Fact]
    public void Dismiss_Edge_OnlyAgainstTheFrameBefore()
    {
        NoireDismiss.ResetSampling();

        NoireDismiss.Edge(1, false).Should().BeFalse();
        NoireDismiss.Edge(2, true).Should().BeTrue();
        NoireDismiss.Edge(3, true).Should().BeFalse("the button is still held");
        NoireDismiss.Edge(10, false).Should().BeFalse();
        NoireDismiss.Edge(20, true).Should().BeFalse("frames went unsampled, so the press may be old");
    }

    #endregion

    #region Placement

    private static readonly Vector2 ScreenMin = Vector2.Zero;
    private static readonly Vector2 ScreenMax = new(1000f, 800f);

    [Fact]
    public void Placement_Above_CentresOverTheAnchor()
    {
        var at = NoirePlacement.Above(new Vector2(100f, 400f), new Vector2(200f, 420f), new Vector2(60f, 30f), 8f, ScreenMin, ScreenMax);

        at.Should().Be(new Vector2(120f, 362f));
    }

    [Fact]
    public void Placement_Above_FlipsBelowWithoutRoom()
    {
        var at = NoirePlacement.Above(new Vector2(100f, 10f), new Vector2(200f, 30f), new Vector2(60f, 30f), 8f, ScreenMin, ScreenMax);

        at.Y.Should().Be(38f);
    }

    [Fact]
    public void Placement_Above_StaysOnScreenHorizontally()
    {
        var at = NoirePlacement.Above(new Vector2(980f, 400f), new Vector2(1000f, 420f), new Vector2(100f, 30f), 8f, ScreenMin, ScreenMax);

        at.X.Should().Be(900f);
    }

    [Fact]
    public void Placement_Beside_PrefersTheRight_ThenTheLeft()
    {
        var right = NoirePlacement.Beside(new Vector2(100f, 100f), new Vector2(400f, 500f), new Vector2(200f, 100f), 10f, 300f, ScreenMin, ScreenMax);
        right.Should().Be(new Vector2(410f, 250f));

        var left = NoirePlacement.Beside(new Vector2(600f, 100f), new Vector2(900f, 500f), new Vector2(200f, 100f), 10f, 300f, ScreenMin, ScreenMax);
        left.X.Should().Be(390f);
    }

    [Fact]
    public void Placement_Beside_ClampsTheHeight()
    {
        var at = NoirePlacement.Beside(new Vector2(100f, 100f), new Vector2(400f, 500f), new Vector2(200f, 100f), 10f, 790f, ScreenMin, ScreenMax);

        at.Y.Should().Be(700f);
    }

    #endregion

    #region Ask

    private static readonly NoireConfirm Countdown = new(LabelA, LabelB) { CountdownSeconds = 3 };

    [Fact]
    public void Ask_CountsDown_AndRefusesAnEarlyConfirm()
    {
        var ask = new NoireAsk();
        bool? answer = null;

        ask.Ask(Countdown, value => answer = value);
        ask.State.SecondsLeft.Should().Be(3);

        time = 1.5f;
        ask.State.SecondsLeft.Should().Be(2);
        ask.Answer(ConfirmAnswer.Confirmed).Should().BeFalse();
        answer.Should().BeNull();

        time = 3f;
        ask.State.SecondsLeft.Should().Be(0);
        ask.Answer(ConfirmAnswer.Confirmed).Should().BeTrue();
        answer.Should().BeTrue();
        ask.Open.Should().BeFalse();
    }

    [Fact]
    public void Ask_CancelIsAlwaysAccepted()
    {
        var ask = new NoireAsk();
        bool? answer = null;

        ask.Ask(Countdown, value => answer = value);

        ask.Answer(ConfirmAnswer.Cancelled).Should().BeTrue();
        answer.Should().BeFalse();
    }

    [Fact]
    public void Ask_Replacing_CancelsThePendingOne()
    {
        var ask = new NoireAsk();
        bool? first = null;

        ask.Ask(Countdown, value => first = value);
        ask.Ask(new NoireConfirm(LabelB, LabelA), static _ => { });

        first.Should().BeFalse();
        ask.Current!.Title.Should().BeSameAs(LabelB);
    }

    [Fact]
    public void Ask_PendingAnswer_ChangesNothing()
    {
        var ask = new NoireAsk();
        ask.Ask(Countdown, static _ => { });

        ask.Answer(ConfirmAnswer.Pending).Should().BeFalse();
        ask.Open.Should().BeTrue();
    }

    #endregion

    #region Pill

    [Fact]
    public void Pill_LivesForItsDuration()
    {
        var pill = new NoirePill();
        pill.Active.Should().BeFalse();

        pill.Show("Done", seconds: 2f);
        pill.Active.Should().BeTrue();

        time = 1f;
        pill.Progress.Should().BeApproximately(0.5f, 0.0001f);

        time = 2.5f;
        pill.Active.Should().BeFalse();
        pill.Progress.Should().Be(1f);
    }

    [Fact]
    public void Pill_Hide_EndsItAtOnce()
    {
        var pill = new NoirePill();
        pill.Show("Done");

        pill.Hide();

        pill.Active.Should().BeFalse();
    }

    #endregion

    #region Actions and motion

    [Fact]
    public void Action_RunsOnlyWhenAvailable()
    {
        var ran = 0;
        var action = new UiAction<int>(LabelA, NoireIcon.Play, value => ran += value) { Available = static value => value > 0 };

        action.Run(0).Should().BeFalse();
        action.Run(3).Should().BeTrue();

        ran.Should().Be(3);
    }

    [Fact]
    public void ReducedMotion_WindowLevel_AppliesOnlyWhileTheWindowDraws()
    {
        NoireUI.ReducedMotion = false;

        NoireUI.EnterWindowMotion(true);
        NoireUI.ReducedMotion.Should().BeTrue();

        NoireUI.LeaveWindowMotion();
        NoireUI.ReducedMotion.Should().BeFalse();
    }

    [Fact]
    public void Icons_EveryValueButTheBrandMarks_HasAGlyph()
    {
        foreach (var icon in Enum.GetValues<NoireIcon>())
        {
            var brand = icon is NoireIcon.Discord or NoireIcon.Kofi;

            NoireIcons.Glyph(icon).HasValue.Should().Be(!brand, $"{icon} must be drawable");
            (NoireIcons.Source(icon, 24f) != null).Should().Be(brand, $"only brand marks carry artwork, {icon} checked");
        }
    }

    #endregion
}
