using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// A pointer client that lives above the scene graph: it hit-tests the cursor ray against its own geometry
/// (gizmo handles, invisible hotspots, custom widgets) and receives hover / click / drag through the same
/// arbitration that governs clickable nodes — so it shares the one mouse-capture authority instead of fighting it.
/// The built-in <see cref="NoireGizmo"/> is such a client; register your own with <see cref="NoireInteract.RegisterInteractor"/>.
/// </summary>
public interface IPointerInteractor
{
    /// <summary>Higher wins when several interactors are hit at once (a gizmo drawn on top should outrank scene nodes). Nodes hit-test at priority 0.</summary>
    int Priority { get; }

    /// <summary>When false the interactor is skipped entirely (no hit-testing, no drawing).</summary>
    bool Active { get; }

    /// <summary>
    /// Tests the cursor ray against this interactor's grabbable geometry for the frame.
    /// </summary>
    /// <param name="rayOrigin">Cursor ray origin (world).</param>
    /// <param name="rayDirection">Cursor ray direction (world, normalized).</param>
    /// <param name="screen">Cursor position in screen pixels.</param>
    /// <param name="frame">The current frame (projection, viewport).</param>
    /// <param name="token">Receives a stable identity for the hit element (same instance across frames — used for hover/press latching).</param>
    /// <param name="distance">Receives the ray distance to the hit (nearer hits win ties within equal priority).</param>
    /// <param name="hitPoint">Receives the world point grabbed, used as the drag anchor.</param>
    /// <returns>True when the ray hits something grabbable.</returns>
    bool HitTest(Vector3 rayOrigin, Vector3 rayDirection, Vector2 screen, in FrameContext frame, out object token, out float distance, out Vector3 hitPoint);

    /// <summary>Whether a token produced by <see cref="HitTest"/> belongs to this interactor (used to route events).</summary>
    bool OwnsToken(object token);

    /// <summary>The cursor started hovering one of this interactor's elements.</summary>
    void OnHoverEnter(object token);

    /// <summary>The cursor stopped hovering the element.</summary>
    void OnHoverExit(object token);

    /// <summary>A click (press+release without a drag) landed on the element.</summary>
    void OnClick(object token, MouseButton button);

    /// <summary>A drag started on the element (left press crossed the threshold).</summary>
    void OnDragStart(object token, DragContext context);

    /// <summary>The drag continued this frame.</summary>
    void OnDrag(object token, DragContext context);

    /// <summary>The drag ended (button released).</summary>
    void OnDragEnd(object token, DragContext context);

    /// <summary>Draw the interactor's visuals for the frame (called after hit-testing, on the UI thread).</summary>
    /// <param name="frame">The current frame snapshot.</param>
    /// <param name="hovered">The element currently hovered (belongs to this interactor), or null.</param>
    void Draw(in FrameContext frame, object? hovered);
}
