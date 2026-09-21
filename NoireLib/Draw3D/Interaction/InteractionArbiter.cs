using System;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

// Latched at press for the whole gesture.
internal enum PointerOwner
{
    None,

    // The press began over an interactable target. The gesture blocks the game.
    Interact,

    // The press began over empty world or foreign UI. The game owns it.
    Foreign,
}

internal readonly struct PointerSample
{
    public readonly Vector2 Position;

    public readonly bool LeftDown, RightDown, MiddleDown;

    // Null when none.
    public readonly object? HoverToken;

    public readonly bool HoverDraggable;

    // Not our own capture.
    public readonly bool ForeignCapturing;

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

// Implementations must not throw.
internal interface IArbiterSink
{
    void HoverEnter(object token);

    void HoverExit(object token);

    // Fired before any click or drag.
    void Press(object token, MouseButton button);

    void Click(object token, MouseButton button);

    // A left press and release on empty world that never became a camera pan.
    void BackgroundClick();

    void DragStart(object token);

    void Drag(object token);

    void DragEnd(object token);
}

// A camera pan that crosses an interactable never becomes a click.
internal sealed class InteractionArbiter
{
    public float DragThresholdPx { get; set; } = 4f;

    private struct ButtonState
    {
        public PointerOwner Owner;
        public object? Node;
        public Vector2 PressPos;
        public bool Dragging;       // left only
        public bool Moved;          // disqualifies the click
        public bool Draggable;
        public bool Background;     // left only, began over empty world
        public bool Down;           // previous frame
    }

    // Left, right, middle. Only left drags.
    private readonly ButtonState[] buttons = new ButtonState[3];
    private object? hover;

    public object? Hover => hover;

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

    public bool Update(in PointerSample s, IArbiterSink sink)
    {
        // Hover is frozen during an owned gesture.
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
            if (s.ForeignCapturing || s.HoverToken == null)
            {
                st.Owner = PointerOwner.Foreign;
                st.Node = null;
                st.PressPos = s.Position;
                st.Moved = false;
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
                st.Moved = true;
            }
        }
        else if (wasDown && !isDown)
        {
            if (st.Owner == PointerOwner.Interact && st.Node != null)
            {
                if (st.Dragging)
                    sink.DragEnd(st.Node);
                else if (!st.Moved && ReferenceEquals(s.HoverToken, st.Node))
                    sink.Click(st.Node, button);
            }
            else if (st.Owner == PointerOwner.Foreign && st.Background && !st.Moved && !s.ForeignCapturing)
            {
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
                // A plain press only captures until it crosses the drag threshold. Past it, it is a camera gesture.
                if (b.Dragging || b.Draggable || !b.Moved || s.BlockOnHover)
                    return true;
            }
            else if (b.Owner == PointerOwner.Foreign)
            {
                foreignPressActive = true;
            }
        }

        // Never steals an in-progress foreign gesture.
        if (s.HoverToken != null && !s.ForeignCapturing && !foreignPressActive && (s.BlockOnHover || s.HoverDraggable))
            return true;

        return false;
    }

    public void Reset()
    {
        Array.Clear(buttons);
        hover = null;
    }
}
