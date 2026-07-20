using NoireLib.Draw3D.Interaction;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Interaction surface of a scene node: opt a node into hover / click / drag and it behaves like a button in the
/// world. Off by default, so a scene that never opts a node in never touches the interaction layer - zero cost until
/// asked. The events are raised by <see cref="NoireInteract"/> on the UI thread; every callback is exception-wrapped
/// there so a throwing handler never breaks the frame.
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

    /// <summary>
    /// A node that stands in for this one when a click selects it. Null, the default, selects the clicked
    /// node itself.<br/>
    /// This is how a model made of several meshes selects as one object: parent the mesh nodes under a group
    /// node and point each part's proxy at the group, and clicking any part selects - and gizmo-moves - the
    /// whole. Only the selection routes through: hover feedback, <see cref="OnClick"/> and the hit's
    /// triangle information stay on the part that was actually clicked, because those answer "what is under
    /// the cursor" and the proxy answers "what does picking it mean".<br/>
    /// Chains resolve to their end, so a proxy may itself carry a proxy; a destroyed proxy is ignored and
    /// the clicked node selects itself.
    /// </summary>
    public SceneNode? SelectionProxy { get; set; }

    /// <summary>
    /// The node a selection pick of this node lands on: the end of the <see cref="SelectionProxy"/> chain,
    /// or the node itself. Bounded so a proxy cycle resolves to somewhere instead of hanging.
    /// </summary>
    internal SceneNode ResolveSelectionTarget()
    {
        var target = this;
        for (var hops = 0; hops < 8; hops++)
        {
            if (target.SelectionProxy is not { IsDestroyed: false } next || ReferenceEquals(next, target))
                return target;

            target = next;
        }

        return target;
    }

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

    /// <summary>The default hover highlight: brightens the renderer tint by x1.2 (RGB), alpha unchanged.</summary>
    public static readonly Func<Vector4, Vector4> DefaultHoverHighlight = static t => new Vector4(t.X * 1.2f, t.Y * 1.2f, t.Z * 1.2f, t.W);

    /// <summary>The active hover-tint transform (null = no built-in highlight). Applied around, never composed into, the user's <see cref="OnHoverEnter"/> / <see cref="OnHoverExit"/>.</summary>
    private Func<Vector4, Vector4>? hoverHighlight;

    /// <summary>The renderer tint captured when the current hover began, restored on exit.</summary>
    private Vector4 hoverRestTint;

    /// <summary>Whether the built-in hover highlight is currently applied (guards against a double apply that would compound the tint).</summary>
    private bool hoverHighlightActive;

    /// <summary>
    /// One-call opt-in to pointer interaction with a built-in hover highlight, without selection: hovering brightens
    /// the renderer tint (default x1.2) and restores it on exit; a click still fires <see cref="OnClick"/> but does
    /// <b>not</b> touch any selection. The highlight is applied <b>around</b> your <see cref="OnHoverEnter"/> /
    /// <see cref="OnHoverExit"/> (never composed into them), and calling this again just replaces the transform - it
    /// never stacks. Fluent.
    /// </summary>
    /// <param name="hover">Tint transform applied while hovered; null uses <see cref="DefaultHoverHighlight"/> (x1.2). Return the input unchanged for no visual change.</param>
    public SceneNode MakeInteractable(Func<Vector4, Vector4>? hover = null)
    {
        hoverHighlight = hover ?? DefaultHoverHighlight;
        Selectable = false;
        Interactable = true;
        return this;
    }

    /// <summary>
    /// One-call opt-in to click-to-select with a built-in hover highlight: hovering brightens the renderer tint
    /// (default x1.2), and a left-click routes into this node's scene <see cref="Scene3D.Selection"/> (honouring the
    /// configured Ctrl-toggle / Shift-add modifiers). The highlight is applied <b>around</b> your <see cref="OnHoverEnter"/> /
    /// <see cref="OnHoverExit"/> / <see cref="OnClick"/> (never composed into them), and calling this again just
    /// replaces the transform - it never stacks. Fluent.
    /// </summary>
    /// <param name="hover">Tint transform applied while hovered; null uses <see cref="DefaultHoverHighlight"/> (x1.2). Return the input unchanged for no visual change.</param>
    public SceneNode MakeSelectable(Func<Vector4, Vector4>? hover = null)
    {
        hoverHighlight = hover ?? DefaultHoverHighlight;
        Selectable = true;
        Interactable = true;
        return this;
    }

    /// <summary>Removes the built-in hover highlight (a click still selects; only the tint feedback is dropped). Fluent.</summary>
    public SceneNode ClearHoverHighlight()
    {
        RemoveHoverHighlight();
        hoverHighlight = null;
        return this;
    }

    /// <summary>Applies the built-in hover tint, capturing the resting tint to restore on exit. Idempotent (a second call while active is a no-op, so the tint never compounds). Called by <see cref="NoireInteract"/> on hover-enter.</summary>
    internal void ApplyHoverHighlight()
    {
        if (hoverHighlight is not { } transform || hoverHighlightActive || Renderer is not { } renderer)
            return;

        hoverRestTint = renderer.Tint;
        hoverHighlightActive = true;
        renderer.Tint = transform(hoverRestTint);
    }

    /// <summary>Restores the resting tint captured by <see cref="ApplyHoverHighlight"/>. Idempotent. Called by <see cref="NoireInteract"/> on hover-exit.</summary>
    internal void RemoveHoverHighlight()
    {
        if (!hoverHighlightActive)
            return;

        hoverHighlightActive = false;
        if (Renderer is { } renderer)
            renderer.Tint = hoverRestTint;
    }

    /// <summary>Drops this node from the interaction bookkeeping when it is destroyed while still interactable.</summary>
    private void ReleaseInteraction()
    {
        RemoveHoverHighlight(); // restore the resting tint if we were mid-hover
        if (interactable)
        {
            interactable = false;
            IsHovered = false;
            NoireInteract.OnNodeNoLongerInteractable();
        }
    }
}
