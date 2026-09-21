using NoireLib.Draw3D.Interaction;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

public sealed partial class SceneNode
{
    private bool interactable;

    /// <summary>Whether this node responds to the pointer and can be picked, starting <see cref="NoireInteract"/> when set.</summary>
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

    /// <summary>Whether a left press on this node begins a drag that takes the mouse from the game camera. Requires <see cref="Interactable"/>.</summary>
    public bool Draggable { get; set; }

    /// <summary>Whether a left-click routes into the scene's <see cref="Scene3D.Selection"/> (default true), while <c>SelectOnClick</c> is on.</summary>
    public bool Selectable { get; set; } = true;

    /// <summary>A node selected in place of this one when it is clicked. Hover and <see cref="OnClick"/> stay on the clicked node.</summary>
    public SceneNode? SelectionProxy { get; set; }

    // Bounded against a proxy cycle.
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

    /// <summary>Free slot for consumer data such as the object this node represents.</summary>
    public object? Tag { get; set; }

    /// <summary>The cursor started hovering this node.</summary>
    public Action<InteractHit>? OnHoverEnter { get; set; }

    /// <summary>The cursor stopped hovering this node.</summary>
    public Action<InteractHit>? OnHoverExit { get; set; }

    /// <summary>A left click without a drag landed on this node.</summary>
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

    /// <summary>True while the cursor is over this node.</summary>
    public bool IsHovered { get; internal set; }

    /// <summary>The default hover highlight, multiplying the renderer tint's RGB by 1.2.</summary>
    public static readonly Func<Vector4, Vector4> DefaultHoverHighlight = static t => new Vector4(t.X * 1.2f, t.Y * 1.2f, t.Z * 1.2f, t.W);

    // Applied around the user's hover callbacks.
    private Func<Vector4, Vector4>? hoverHighlight;

    private Vector4 hoverRestTint;

    // A double apply would compound the tint.
    private bool hoverHighlightActive;

    /// <summary>Opts the node into pointer interaction with a hover highlight but without selection. Fluent.</summary>
    /// <param name="hover">Tint transform applied while hovered, or null for <see cref="DefaultHoverHighlight"/>.</param>
    /// <returns>This node.</returns>
    public SceneNode MakeInteractable(Func<Vector4, Vector4>? hover = null)
    {
        hoverHighlight = hover ?? DefaultHoverHighlight;
        Selectable = false;
        Interactable = true;
        return this;
    }

    /// <summary>Opts the node into click-to-select through its scene's <see cref="Scene3D.Selection"/>, with a hover highlight. Fluent.</summary>
    /// <param name="hover">Tint transform applied while hovered, or null for <see cref="DefaultHoverHighlight"/>.</param>
    /// <returns>This node.</returns>
    public SceneNode MakeSelectable(Func<Vector4, Vector4>? hover = null)
    {
        hoverHighlight = hover ?? DefaultHoverHighlight;
        Selectable = true;
        Interactable = true;
        return this;
    }

    /// <summary>Removes the built-in hover highlight without changing selection behavior. Fluent.</summary>
    /// <returns>This node.</returns>
    public SceneNode ClearHoverHighlight()
    {
        RemoveHoverHighlight();
        hoverHighlight = null;
        return this;
    }

    internal void ApplyHoverHighlight()
    {
        if (hoverHighlight is not { } transform || hoverHighlightActive || Renderer is not { } renderer)
            return;

        hoverRestTint = renderer.Tint;
        hoverHighlightActive = true;
        renderer.Tint = transform(hoverRestTint);
    }

    internal void RemoveHoverHighlight()
    {
        if (!hoverHighlightActive)
            return;

        hoverHighlightActive = false;
        if (Renderer is { } renderer)
            renderer.Tint = hoverRestTint;
    }

    private void ReleaseInteraction()
    {
        RemoveHoverHighlight();
        if (interactable)
        {
            interactable = false;
            IsHovered = false;
            NoireInteract.OnNodeNoLongerInteractable();
        }
    }
}
