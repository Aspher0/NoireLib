using System;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>Who a mouse-button gesture belongs to, latched at press time for the whole press.</summary>
internal enum PointerOwner
{
    /// <summary>No button held for this slot.</summary>
    None,

    /// <summary>The press began over an interactable target: this whole gesture is ours (blocks the game).</summary>
    Interact,

    /// <summary>The press began over empty world, or while foreign UI held the mouse: the game owns it (camera pan, targeting).</summary>
    Foreign,
}

/// <summary>The immutable per-frame pointer state fed to <see cref="InteractionArbiter"/>.</summary>
internal readonly struct PointerSample
{
    /// <summary>Cursor position in screen pixels.</summary>
    public readonly Vector2 Position;

    /// <summary>Left / right / middle button held this frame.</summary>
    public readonly bool LeftDown, RightDown, MiddleDown;

    /// <summary>The topmost interactable target under the cursor this frame (null = nothing grabbable). Stable identity across frames.</summary>
    public readonly object? HoverToken;

    /// <summary>Whether <see cref="HoverToken"/> supports dragging (gizmo handles always do; nodes opt in).</summary>
    public readonly bool HoverDraggable;

    /// <summary>Whether another UI surface (a plugin window, not our own capture) currently owns the mouse.</summary>
    public readonly bool ForeignCapturing;

    /// <summary>Policy: also claim the mouse merely on hovering a plain (non-draggable) interactable, so its click is consumed from the game.</summary>
    public readonly bool BlockOnHover;

    public PointerSample(Vector2 position, bool leftDown, bool rightDown, bool middleDown, object? hoverToken, bool hoverDraggable, bool foreignCapturing, bool blockOnHover)
    {
        Position = position;
        LeftDown = leftDown;
        RightDown = rightDown;
        MiddleDown = middleDown;
        HoverToken = hoverToken;
        HoverDraggable = hoverDraggable;
        ForeignCapturing = foreignCapturing;
        BlockOnHover = blockOnHover;
    }
}

/// <summary>Receives the semantic events the arbiter derives from raw pointer state. Implementations must not throw (the arbiter is pure logic; error containment lives in the dispatcher).</summary>
internal interface IArbiterSink
{
    /// <summary>The cursor started hovering <paramref name="token"/>.</summary>
    void HoverEnter(object token);

    /// <summary>The cursor stopped hovering <paramref name="token"/>.</summary>
    void HoverExit(object token);

    /// <summary>A button pressed down while over <paramref name="token"/> (the gesture latched to us). Fired before any click/drag so the drag layer can snapshot the grab ray.</summary>
    void Press(object token, MouseButton button);

    /// <summary>A press+release on the same target without crossing the drag threshold.</summary>
    void Click(object token, MouseButton button);

    /// <summary>A left press+release on empty world (no target, not over foreign UI) that never became a camera pan: a click on the background.</summary>
    void BackgroundClick();

    /// <summary>A left-button press on a draggable target crossed the drag threshold.</summary>
    void DragStart(object token);

    /// <summary>Continues a drag this frame.</summary>
    void Drag(object token);

    /// <summary>The dragged button was released.</summary>
    void DragEnd(object token);
}

/// <summary>
/// The pure interaction state machine: turns raw per-frame pointer state into hover / click / drag events and
/// decides when Draw3D must claim the mouse from the game. It owns the two behaviours the renderer deliberately
/// refuses to (Law 11):
/// <list type="bullet">
/// <item><b>Click vs. camera-pan.</b> A gesture is latched to its owner at press time. A press that begins over an
/// interactable is ours; a press that begins over empty world is the game's (its camera pan), and it stays the game's
/// even if it later drags across an interactable, so a pan is never mistaken for a click, and a click never fires
/// after a pan. A left press that moves past <see cref="DragThresholdPx"/> is a drag, not a click.</item>
/// <item><b>Drag takes the lead.</b> Pressing a draggable target (for example a gizmo handle) claims the mouse from the very
/// first frame, so the game never pans the camera underneath the drag.</item>
/// </list>
/// Deliberately free of ImGui / renderer state so the whole decision table is unit-tested headlessly.
/// </summary>
internal sealed class InteractionArbiter
{
    /// <summary>Movement past this many screen pixels turns a left press into a drag (and disqualifies it as a click).</summary>
    public float DragThresholdPx { get; set; } = 4f;

    private struct ButtonState
    {
        public PointerOwner Owner;
        public object? Node;
        public Vector2 PressPos;
        public bool Dragging;       // left only: crossed the threshold on a draggable target
        public bool Moved;          // crossed the threshold (any target); disqualifies the click
        public bool Draggable;      // the pressed target accepts drags
        public bool Background;     // left only: this Foreign press began over empty world (not UI); a click here is a background click
        public bool Down;           // previous-frame held state, for edge detection
    }

    // Index 0 = left, 1 = right, 2 = middle. Only the left button produces drags.
    private readonly ButtonState[] buttons = new ButtonState[3];
    private object? hover;

    /// <summary>The target currently hovered (null when none). Exposed for highlight/debug.</summary>
    public object? Hover => hover;

    /// <summary>True while a gesture the arbiter owns is in progress (a press latched to <see cref="PointerOwner.Interact"/>).</summary>
    public bool HasActiveInteraction
    {
        get
        {
            foreach (var b in buttons)
            {
                if (b.Owner == PointerOwner.Interact)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Advances the machine by one frame, emitting events to <paramref name="sink"/>, and returns whether Draw3D
    /// should claim the mouse this frame (i.e. block the game's camera/targeting).
    /// </summary>
    public bool Update(in PointerSample s, IArbiterSink sink)
    {
        // Hover enter/exit is frozen while we own a gesture, so a drag never flickers hover onto whatever it passes over.
        var activeInteract = HasActiveInteraction;
        var effectiveHover = s.ForeignCapturing ? null : s.HoverToken;
        if (!activeInteract && !ReferenceEquals(hover, effectiveHover))
        {
            if (hover != null)
                sink.HoverExit(hover);
            hover = effectiveHover;
            if (hover != null)
                sink.HoverEnter(hover);
        }

        ProcessButton(0, s.LeftDown, allowDrag: true, MouseButton.Left, in s, sink);
        ProcessButton(1, s.RightDown, allowDrag: false, MouseButton.Right, in s, sink);
        ProcessButton(2, s.MiddleDown, allowDrag: false, MouseButton.Middle, in s, sink);

        return ComputeWantCapture(in s);
    }

    private void ProcessButton(int index, bool isDown, bool allowDrag, MouseButton button, in PointerSample s, IArbiterSink sink)
    {
        ref var st = ref buttons[index];
        var wasDown = st.Down;

        if (!wasDown && isDown)
        {
            // Press edge: latch the owner for the whole gesture.
            if (s.ForeignCapturing || s.HoverToken == null)
            {
                st.Owner = PointerOwner.Foreign;
                st.Node = null;
                st.PressPos = s.Position;
                st.Moved = false;
                // A left press over genuinely empty world (not over UI) is a background-click candidate: if it never
                // pans the camera, its release deselects. Right/middle (allowDrag=false) never count.
                st.Background = allowDrag && !s.ForeignCapturing;
            }
            else
            {
                st.Owner = PointerOwner.Interact;
                st.Node = s.HoverToken;
                st.PressPos = s.Position;
                st.Dragging = false;
                st.Moved = false;
                st.Background = false;
                st.Draggable = s.HoverDraggable;
                sink.Press(st.Node, button);
            }
        }
        else if (wasDown && isDown)
        {
            // Held: grow a drag once the cursor leaves the click tolerance.
            if (st.Owner == PointerOwner.Interact)
            {
                if (!st.Moved && Vector2.Distance(s.Position, st.PressPos) > DragThresholdPx)
                {
                    st.Moved = true;
                    if (allowDrag && st.Draggable && st.Node != null)
                    {
                        st.Dragging = true;
                        sink.DragStart(st.Node);
                    }
                }

                if (st.Dragging && st.Node != null)
                    sink.Drag(st.Node);
            }
            else if (st.Owner == PointerOwner.Foreign && st.Background && !st.Moved
                     && Vector2.Distance(s.Position, st.PressPos) > DragThresholdPx)
            {
                // The empty-world press has grown into a camera pan: it is no longer a background click.
                st.Moved = true;
            }
        }
        else if (wasDown && !isDown)
        {
            // Release edge.
            if (st.Owner == PointerOwner.Interact && st.Node != null)
            {
                if (st.Dragging)
                    sink.DragEnd(st.Node);
                else if (!st.Moved && ReferenceEquals(s.HoverToken, st.Node))
                    sink.Click(st.Node, button);
            }
            else if (st.Owner == PointerOwner.Foreign && st.Background && !st.Moved && !s.ForeignCapturing)
            {
                // A left press+release on empty world that never became a pan: a click on the background (deselect).
                sink.BackgroundClick();
            }

            st.Owner = PointerOwner.None;
            st.Node = null;
            st.Dragging = false;
            st.Moved = false;
            st.Background = false;
        }

        st.Down = isDown;
    }

    private bool ComputeWantCapture(in PointerSample s)
    {
        var foreignPressActive = false;
        foreach (var b in buttons)
        {
            if (b.Owner == PointerOwner.Interact)
            {
                // A draggable press/drag is captured from frame one (a gizmo grab, so the camera never moves under it).
                // A plain press captures too while it could still be a click, so the click is delivered to us and the
                // game doesn't pan/target under it; the moment that plain press crosses the drag threshold it is clearly
                // a camera gesture, not a click, so we stop capturing and let the game have it.
                if (b.Dragging || b.Draggable || !b.Moved || s.BlockOnHover)
                    return true;
            }
            else if (b.Owner == PointerOwner.Foreign)
            {
                foreignPressActive = true;
            }
        }

        // Pre-press hover claim, so the imminent press lands on us instead of the game. Never steal an in-progress
        // foreign gesture (a camera pan that wandered over the target).
        if (s.HoverToken != null && !s.ForeignCapturing && !foreignPressActive && (s.BlockOnHover || s.HoverDraggable))
            return true;

        return false;
    }

    /// <summary>Drops all latched state (e.g. when interaction is disabled or the scene is cleared).</summary>
    public void Reset()
    {
        Array.Clear(buttons);
        hover = null;
    }
}
