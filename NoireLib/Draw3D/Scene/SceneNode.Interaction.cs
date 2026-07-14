using System;
using NoireLib.Draw3D.Interaction;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Interaction surface of a scene node: opt a node into hover / click / drag and it behaves like a button in the
/// world. Off by default (Law 11 stays intact for non-interactive content — zero cost until asked). The events are
/// raised by <see cref="NoireInteract"/> on the UI thread; every callback is exception-wrapped there so a throwing
/// handler never breaks the frame.
/// </summary>
public sealed partial class SceneNode
{
    private bool interactable;

    /// <summary>
    /// Whether this node responds to the pointer (hover, click, drag). Default false. Setting it true starts
    /// <see cref="NoireInteract"/> if it isn't already running. A non-interactable node is invisible to picking.
    /// </summary>
    public bool Interactable
    {
        get => interactable;
        set
        {
            if (interactable == value)
                return;

            interactable = value;
            if (value)
            {
                NoireInteract.OnNodeBecameInteractable();
            }
            else
            {
                NoireInteract.OnNodeNoLongerInteractable();
            }
        }
    }

    /// <summary>
    /// Whether a left press on this node begins a drag (rather than only a click). Default false. While true, a drag
    /// that starts on this node takes the mouse from the game the instant it is pressed, so the camera never pans
    /// underneath it. Implies <see cref="Interactable"/> — set both.
    /// </summary>
    public bool Draggable { get; set; }

    /// <summary>Free slot for consumer data (e.g. the domain object this node represents), so callbacks can recover context.</summary>
    public object? Tag { get; set; }

    /// <summary>The cursor started hovering this node.</summary>
    public Action<InteractHit>? OnHoverEnter { get; set; }

    /// <summary>The cursor stopped hovering this node.</summary>
    public Action<InteractHit>? OnHoverExit { get; set; }

    /// <summary>A left click (press+release without a drag) landed on this node.</summary>
    public Action<InteractHit>? OnClick { get; set; }

    /// <summary>A right click landed on this node.</summary>
    public Action<InteractHit>? OnRightClick { get; set; }

    /// <summary>A middle click landed on this node.</summary>
    public Action<InteractHit>? OnMiddleClick { get; set; }

    /// <summary>A drag started on this node (requires <see cref="Draggable"/>). The camera is already blocked.</summary>
    public Action<DragContext>? OnDragStart { get; set; }

    /// <summary>The drag continued this frame.</summary>
    public Action<DragContext>? OnDrag { get; set; }

    /// <summary>The drag ended (button released).</summary>
    public Action<DragContext>? OnDragEnd { get; set; }

    /// <summary>True while the cursor is over this node (maintained by <see cref="NoireInteract"/>).</summary>
    public bool IsHovered { get; internal set; }

    /// <summary>Drops this node from the interaction bookkeeping when it is destroyed while still interactable.</summary>
    private void ReleaseInteraction()
    {
        if (interactable)
        {
            interactable = false;
            IsHovered = false;
            NoireInteract.OnNodeNoLongerInteractable();
        }
    }
}
