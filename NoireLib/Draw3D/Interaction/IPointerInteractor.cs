using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>A pointer client that hit-tests its own geometry and shares the node arbitration. Register with <see cref="NoireInteract.RegisterInteractor"/>.</summary>
public interface IPointerInteractor
{
    /// <summary>Priority among simultaneous hits, higher winning. Nodes hit-test at priority 0.</summary>
    int Priority { get; }

    /// <summary>Whether the interactor is hit-tested and drawn at all.</summary>
    bool Active { get; }

    /// <summary>Tests the cursor against this interactor's grabbable geometry for the frame.</summary>
    /// <param name="rayOrigin">Cursor ray origin (world).</param>
    /// <param name="rayDirection">Cursor ray direction (world, normalized).</param>
    /// <param name="screen">Cursor position in screen pixels.</param>
    /// <param name="frame">The current frame (projection, viewport).</param>
    /// <param name="token">Receives an identity for the hit element, the same instance across frames.</param>
    /// <param name="distance">Receives the distance to the hit. Nearer wins within equal priority.</param>
    /// <param name="hitPoint">Receives the world point grabbed, used as the drag anchor and for occlusion tests.</param>
    /// <returns>Whether the cursor hits something grabbable.</returns>
    bool HitTest(Vector3 rayOrigin, Vector3 rayDirection, Vector2 screen, in FrameContext frame, out object token, out float distance, out Vector3 hitPoint);

    /// <summary>Whether a token produced by <see cref="HitTest"/> belongs to this interactor.</summary>
    /// <param name="token">The token to test.</param>
    bool OwnsToken(object token);

    /// <summary>Called when the cursor starts hovering one of this interactor's elements.</summary>
    /// <param name="token">The hovered element.</param>
    void OnHoverEnter(object token);

    /// <summary>Called when the cursor stops hovering the element.</summary>
    /// <param name="token">The element no longer hovered.</param>
    void OnHoverExit(object token);

    /// <summary>Called when a press and release without a drag lands on the element.</summary>
    /// <param name="token">The clicked element.</param>
    /// <param name="button">The button clicked.</param>
    void OnClick(object token, MouseButton button);

    /// <summary>Called when a left press on the element crosses the drag threshold.</summary>
    /// <param name="token">The dragged element.</param>
    /// <param name="context">The drag state.</param>
    void OnDragStart(object token, DragContext context);

    /// <summary>Called each frame the drag continues.</summary>
    /// <param name="token">The dragged element.</param>
    /// <param name="context">The drag state.</param>
    void OnDrag(object token, DragContext context);

    /// <summary>Called when the dragging button is released.</summary>
    /// <param name="token">The dragged element.</param>
    /// <param name="context">The drag state.</param>
    void OnDragEnd(object token, DragContext context);

    /// <summary>Draws the interactor's visuals for the frame, after hit-testing, on the UI thread.</summary>
    /// <param name="frame">The current frame snapshot.</param>
    /// <param name="hovered">The element currently hovered (belongs to this interactor), or null.</param>
    void Draw(in FrameContext frame, object? hovered);

    /// <summary>
    /// Optional draw on the render thread for <see cref="Im.ImDraw3D"/> geometry tracking the live camera.<br/>
    /// It can fire inside a game D3D call. Emit geometry only, from state captured on the UI thread.
    /// </summary>
    /// <param name="frame">The current render-frame snapshot.</param>
    void DrawOverlay(in FrameContext frame) { }

    /// <summary>Whether this interactor reads ImGui IO and owns the mouse through its own window. Scene picking is skipped while it does.</summary>
    bool SelfDriven => false;

    /// <summary>Runs a <see cref="SelfDriven"/> interactor's input and draw for this frame and returns whether it owns the mouse.</summary>
    /// <param name="frame">The current frame snapshot.</param>
    bool DrawSelfDriven(in FrameContext frame) => false;

    /// <summary>Whether a hit is blocked by game-drawn obstacles nearer than it under <see cref="NoireInteract.ObstacleOcclusionMode"/> (default false).</summary>
    /// <param name="token">The hit element from <see cref="HitTest"/>.</param>
    bool OccludesBehindObstacles(object token) => false;
}
