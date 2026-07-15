using System;
using System.Numerics;
using NoireLib.Draw3D.Interaction;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Interaction surface of a scene node: opt a node into hover / click / drag and it behaves like a button in the
/// world. Off by default (Law 11 stays intact for non-interactive content - zero cost until asked). The events are
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
    /// underneath it. Implies <see cref="Interactable"/> - set both.
    /// </summary>
    public bool Draggable { get; set; }

    /// <summary>
    /// Whether a left-click on this (interactable) node routes into its scene's <see cref="Scene3D.Selection"/>.
    /// Default <b>true</b>: a plain interactable node behaves as selectable. <see cref="MakeInteractable"/> sets it
    /// false (hover/click only); <see cref="MakeSelectable"/> keeps it true. Only consulted while
    /// <see cref="NoireDraw3D.Interaction"/>'s <c>SelectOnClick</c> is on.
    /// </summary>
    public bool Selectable { get; set; } = true;

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

    /// <summary>The default hover highlight: brightens the renderer tint by ×1.2 (RGB), alpha unchanged.</summary>
    public static readonly Func<Vector4, Vector4> DefaultHoverHighlight = static t => new Vector4(t.X * 1.2f, t.Y * 1.2f, t.Z * 1.2f, t.W);

    /// <summary>
    /// One-call opt-in to pointer interaction with a built-in hover highlight, without selection: hovering brightens
    /// the renderer tint (default ×1.2) and restores it on exit; a click still fires <see cref="OnClick"/> but does
    /// <b>not</b> touch any selection. <b>Composes</b> - it adds to whatever <see cref="OnHoverEnter"/> /
    /// <see cref="OnHoverExit"/> you already set, and you can set more afterward. Fluent.
    /// </summary>
    /// <param name="hover">Tint transform applied while hovered; null uses <see cref="DefaultHoverHighlight"/> (×1.2). Return the input unchanged for no visual change.</param>
    public SceneNode MakeInteractable(Func<Vector4, Vector4>? hover = null)
    {
        AddHoverHighlight(hover ?? DefaultHoverHighlight);
        Selectable = false;
        Interactable = true;
        return this;
    }

    /// <summary>
    /// One-call opt-in to click-to-select with a built-in hover highlight: hovering brightens the renderer tint
    /// (default ×1.2), and a left-click routes into this node's scene <see cref="Scene3D.Selection"/> (honouring the
    /// configured Ctrl-toggle / Shift-add modifiers). <b>Composes</b> over your own <see cref="OnHoverEnter"/> /
    /// <see cref="OnHoverExit"/> / <see cref="OnClick"/> - it never clobbers them. Fluent.
    /// </summary>
    /// <param name="hover">Tint transform applied while hovered; null uses <see cref="DefaultHoverHighlight"/> (×1.2). Return the input unchanged for no visual change.</param>
    public SceneNode MakeSelectable(Func<Vector4, Vector4>? hover = null)
    {
        AddHoverHighlight(hover ?? DefaultHoverHighlight);
        Selectable = true;
        Interactable = true;
        return this;
    }

    /// <summary>
    /// Composes a tint-highlight-on-hover onto the node's existing hover handlers: on enter it reads the renderer's
    /// current tint as the base and applies <paramref name="highlight"/>; on exit it restores the captured base.
    /// Runs after any handler already set, and leaves later assignments free to add more.
    /// </summary>
    private void AddHoverHighlight(Func<Vector4, Vector4> highlight)
    {
        var previousEnter = OnHoverEnter;
        var previousExit = OnHoverExit;
        var baseTint = new Vector4(1f, 1f, 1f, 1f);
        var captured = false;

        OnHoverEnter = hit =>
        {
            previousEnter?.Invoke(hit);
            var renderer = Renderer;
            if (renderer == null)
                return;

            baseTint = renderer.Tint;
            captured = true;
            renderer.Tint = highlight(baseTint);
        };

        OnHoverExit = hit =>
        {
            previousExit?.Invoke(hit);
            if (captured && Renderer is { } renderer)
                renderer.Tint = baseTint;
            captured = false;
        };
    }

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
