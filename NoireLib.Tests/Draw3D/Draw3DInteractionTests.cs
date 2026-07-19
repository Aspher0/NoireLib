using FluentAssertions;
using NoireLib.Draw3D.Interaction;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the interaction spine's decision table: the click-vs-camera-pan discipline, the drag-takes-the-lead
/// capture policy, hover transitions and the selection model, all exercised through the pure state machine so the
/// behaviour is verified without ImGui or a running game.
/// </summary>
public class Draw3DInteractionTests
{
    private sealed class RecordingSink : IArbiterSink
    {
        public readonly List<(string Kind, object Token, MouseButton Button)> Events = new();

        public void HoverEnter(object token) => Events.Add(("HoverEnter", token, MouseButton.Left));
        public void HoverExit(object token) => Events.Add(("HoverExit", token, MouseButton.Left));
        public void Press(object token, MouseButton button) => Events.Add(("Press", token, button));
        public void Click(object token, MouseButton button) => Events.Add(("Click", token, button));
        public void BackgroundClick() => Events.Add(("BackgroundClick", new object(), MouseButton.Left));
        public void DragStart(object token) => Events.Add(("DragStart", token, MouseButton.Left));
        public void Drag(object token) => Events.Add(("Drag", token, MouseButton.Left));
        public void DragEnd(object token) => Events.Add(("DragEnd", token, MouseButton.Left));

        public int Count(string kind) => Events.FindAll(e => e.Kind == kind).Count;
        public bool Has(string kind) => Events.Exists(e => e.Kind == kind);
    }

    private static PointerSample Sample(Vector2 pos, bool left, object? hover, bool draggable, bool foreign = false, bool blockOnHover = true, bool right = false, bool middle = false)
        => new(pos, left, right, middle, hover, draggable, foreign, blockOnHover);

    // ---------------------------------------------------------------- click vs. pan

    [Fact]
    public void Press_Release_WithoutMoving_IsAClick()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false), sink);        // hover
        arbiter.Update(Sample(new Vector2(10, 10), left: true, a, draggable: false), sink);         // press
        arbiter.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false), sink);        // release

        sink.Count("Click").Should().Be(1);
        sink.Has("DragStart").Should().BeFalse();
        sink.Events.Should().Contain(e => e.Kind == "Press" && ReferenceEquals(e.Token, a));
    }

    [Fact]
    public void PressOnEmptyWorld_ThenDrag_IsNeverAClick()
    {
        // The FFXIV camera pan: a press that begins over nothing belongs to the game and stays the game's,
        // even when it later drags across an interactable. It must never fire a click or a drag on that object.
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: true, hover: null, draggable: false), sink);   // press on empty
        arbiter.Update(Sample(new Vector2(40, 30), left: true, hover: a, draggable: false), sink);      // dragged over A
        arbiter.Update(Sample(new Vector2(40, 30), left: false, hover: a, draggable: false), sink);     // release over A

        sink.Has("Click").Should().BeFalse();
        sink.Has("Press").Should().BeFalse();
        sink.Has("DragStart").Should().BeFalse();
    }

    [Fact]
    public void LeftPress_MovedPastThreshold_OnClickTarget_DoesNotClick()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: true, a, draggable: false), sink);
        arbiter.Update(Sample(new Vector2(40, 10), left: true, a, draggable: false), sink);   // moved > 4 px
        arbiter.Update(Sample(new Vector2(40, 10), left: false, a, draggable: false), sink);

        sink.Has("Click").Should().BeFalse();
        sink.Has("DragStart").Should().BeFalse("a non-draggable target does not start a drag");
    }

    // ---------------------------------------------------------------- background click (deselect)

    [Fact]
    public void LeftClickOnEmptyWorld_WithoutMoving_FiresBackgroundClick()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();

        arbiter.Update(Sample(new Vector2(10, 10), left: true, hover: null, draggable: false), sink);   // press on empty
        arbiter.Update(Sample(new Vector2(11, 10), left: false, hover: null, draggable: false), sink);  // release, within tolerance

        sink.Count("BackgroundClick").Should().Be(1);
        sink.Has("Click").Should().BeFalse("empty world produces a background click, not a node click");
    }

    [Fact]
    public void LeftPressOnEmptyWorld_ThenPan_DoesNotFireBackgroundClick()
    {
        // A click-and-drag on empty world is the FFXIV camera pan; it must never read as a deselect.
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();

        arbiter.Update(Sample(new Vector2(10, 10), left: true, hover: null, draggable: false), sink);   // press on empty
        arbiter.Update(Sample(new Vector2(60, 40), left: true, hover: null, draggable: false), sink);   // pan > 4 px
        arbiter.Update(Sample(new Vector2(60, 40), left: false, hover: null, draggable: false), sink);  // release

        sink.Has("BackgroundClick").Should().BeFalse();
    }

    [Fact]
    public void LeftClickOverForeignUi_DoesNotFireBackgroundClick()
    {
        // Clicking a plugin/HUD window is not a background click; the game/UI owns it, the selection stays.
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();

        arbiter.Update(Sample(new Vector2(10, 10), left: true, hover: null, draggable: false, foreign: true), sink);
        arbiter.Update(Sample(new Vector2(10, 10), left: false, hover: null, draggable: false, foreign: true), sink);

        sink.Has("BackgroundClick").Should().BeFalse();
    }

    [Fact]
    public void ClickOnNode_DoesNotFireBackgroundClick()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false), sink);
        arbiter.Update(Sample(new Vector2(10, 10), left: true, a, draggable: false), sink);
        arbiter.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false), sink);

        sink.Count("Click").Should().Be(1);
        sink.Has("BackgroundClick").Should().BeFalse();
    }

    [Fact]
    public void RightClickOnEmptyWorld_DoesNotFireBackgroundClick()
    {
        // Only left produces a background click; right-click on empty world is the game's camera control.
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();

        arbiter.Update(Sample(new Vector2(10, 10), left: false, hover: null, draggable: false, right: true), sink);
        arbiter.Update(Sample(new Vector2(10, 10), left: false, hover: null, draggable: false, right: false), sink);

        sink.Has("BackgroundClick").Should().BeFalse();
    }

    // ---------------------------------------------------------------- drag takes the lead

    [Fact]
    public void PressDraggable_ThenMove_ProducesDragLifecycle()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: true, a, draggable: true), sink);   // press
        arbiter.Update(Sample(new Vector2(20, 10), left: true, a, draggable: true), sink);   // cross threshold
        arbiter.Update(Sample(new Vector2(30, 10), left: true, a, draggable: true), sink);   // continue
        arbiter.Update(Sample(new Vector2(30, 10), left: false, a, draggable: true), sink);  // release

        sink.Count("DragStart").Should().Be(1);
        sink.Count("Drag").Should().Be(2);
        sink.Count("DragEnd").Should().Be(1);
        sink.Has("Click").Should().BeFalse();
    }

    [Fact]
    public void PressingDraggable_ClaimsMouseFromTheFirstFrame()
    {
        // A gizmo handle: pressing it must block the game immediately, so the camera never pans under the drag,
        // even before the drag threshold is crossed and even when the on-hover block policy is off.
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        var capture = arbiter.Update(Sample(new Vector2(10, 10), left: true, a, draggable: true, blockOnHover: false), sink);
        capture.Should().BeTrue();
    }

    // ---------------------------------------------------------------- capture policy

    [Fact]
    public void HoveringPlainTarget_ClaimsMouse_OnlyWhenBlockOnHover()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false, blockOnHover: true), sink)
            .Should().BeTrue();

        var idle = new InteractionArbiter();
        idle.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false, blockOnHover: false), new RecordingSink())
            .Should().BeFalse("plain targets coexist with the game when on-hover blocking is off");
    }

    [Fact]
    public void HoveringDraggable_ClaimsMouse_EvenWithoutBlockOnHover()
    {
        var arbiter = new InteractionArbiter();
        arbiter.Update(Sample(new Vector2(10, 10), left: false, new object(), draggable: true, blockOnHover: false), new RecordingSink())
            .Should().BeTrue();
    }

    [Fact]
    public void PlainPress_CapturesUntilItBecomesADrag()
    {
        // With on-hover blocking off, hovering a plain object must NOT claim the mouse (the camera/zoom/world stay
        // usable), but the moment it is pressed we claim it so the click is actually delivered to us rather than lost
        // to the game, then we let go the instant the press turns into a drag, handing the camera gesture back.
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false, blockOnHover: false), sink)
            .Should().BeFalse("plain hover must never steal the mouse");
        arbiter.Update(Sample(new Vector2(10, 10), left: true, a, draggable: false, blockOnHover: false), sink)
            .Should().BeTrue("a fresh press on an object is captured so its click is not lost to the game");
        arbiter.Update(Sample(new Vector2(40, 10), left: true, a, draggable: false, blockOnHover: false), sink)
            .Should().BeFalse("once a plain press crosses the drag threshold it is a camera gesture, so we release it");
    }

    [Fact]
    public void ForeignCameraPanInProgress_DoesNotClaimMouseEvenWhenHovering()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: true, hover: null, draggable: false), sink);        // foreign press begins
        var capture = arbiter.Update(Sample(new Vector2(30, 20), left: true, hover: a, draggable: true), sink); // wanders over a handle

        capture.Should().BeFalse("an in-progress camera pan must never be hijacked mid-gesture");
    }

    [Fact]
    public void ForeignUiCapturing_SuppressesHoverAndPress()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(10, 10), left: false, a, draggable: false, foreign: true), sink);
        arbiter.Update(Sample(new Vector2(10, 10), left: true, a, draggable: false, foreign: true), sink);

        sink.Has("HoverEnter").Should().BeFalse();
        sink.Has("Press").Should().BeFalse();
    }

    // ---------------------------------------------------------------- hover transitions

    [Fact]
    public void MovingBetweenTargets_FiresExitThenEnter()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();
        var b = new object();

        arbiter.Update(Sample(new Vector2(1, 1), left: false, a, draggable: false), sink);
        arbiter.Update(Sample(new Vector2(2, 2), left: false, b, draggable: false), sink);

        sink.Events.Should().ContainInOrder(
            ("HoverEnter", a, MouseButton.Left),
            ("HoverExit", a, MouseButton.Left),
            ("HoverEnter", b, MouseButton.Left));
    }

    [Fact]
    public void RightClick_IsReported()
    {
        var arbiter = new InteractionArbiter();
        var sink = new RecordingSink();
        var a = new object();

        arbiter.Update(Sample(new Vector2(5, 5), left: false, a, draggable: false, right: false), sink);
        arbiter.Update(Sample(new Vector2(5, 5), left: false, a, draggable: false, right: true), sink);
        arbiter.Update(Sample(new Vector2(5, 5), left: false, a, draggable: false, right: false), sink);

        sink.Events.Should().Contain(e => e.Kind == "Click" && e.Button == MouseButton.Right);
    }
}
