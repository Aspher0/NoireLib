using System.Numerics;
using NoireLib.Draw3D.Scene;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// The state handed to a drag callback each frame: the cursor ray now and at press, the world point the drag
/// grabbed, and helpers that turn the raw ray into a usable delta on a plane or along an axis. Draw3D owns the
/// mouse for the whole drag (the camera never pans underneath it), so a handler can move things freely.
/// </summary>
public sealed class DragContext
{
    /// <summary>The button driving the drag (always <see cref="MouseButton.Left"/> today).</summary>
    public MouseButton Button { get; internal set; }

    /// <summary>The node being dragged (null for gizmo-handle / custom-interactor drags).</summary>
    public SceneNode? Node { get; internal set; }

    /// <summary>Cursor position in screen pixels at press.</summary>
    public Vector2 ScreenStart { get; internal set; }

    /// <summary>Cursor position in screen pixels this frame.</summary>
    public Vector2 ScreenNow { get; internal set; }

    /// <summary>The world point the drag grabbed at press (on the node, or the anchor plane).</summary>
    public Vector3 PressWorldPoint { get; internal set; }

    /// <summary>Cursor ray origin (world) at press.</summary>
    public Vector3 PressRayOrigin { get; internal set; }

    /// <summary>Cursor ray direction (world, normalized) at press.</summary>
    public Vector3 PressRayDirection { get; internal set; }

    /// <summary>Cursor ray origin (world) this frame.</summary>
    public Vector3 RayOrigin { get; internal set; }

    /// <summary>Cursor ray direction (world, normalized) this frame.</summary>
    public Vector3 RayDirection { get; internal set; }

    /// <summary>The current frame, for projection helpers (world to screen and back).</summary>
    public FrameContext Frame { get; internal set; }

    /// <summary>Total screen-pixel movement since press.</summary>
    public Vector2 ScreenDelta => ScreenNow - ScreenStart;

    /// <summary>
    /// Intersects the current cursor ray with a world plane, giving the point the user is pointing at on it:
    /// the basis for free (plane-constrained) dragging. Returns false when the ray is parallel to the plane.
    /// </summary>
    /// <param name="planePoint">Any point on the plane (often <see cref="PressWorldPoint"/>).</param>
    /// <param name="planeNormal">The plane normal.</param>
    /// <param name="hit">Receives the world hit point.</param>
    public bool TryRayPlane(Vector3 planePoint, Vector3 planeNormal, out Vector3 hit)
        => InteractMath.RayPlane(RayOrigin, RayDirection, planePoint, planeNormal, out _, out hit);

    /// <summary>
    /// World-space movement of the grabbed point across a plane through <see cref="PressWorldPoint"/> with the given
    /// normal, that is how far the drag has moved on that plane since press. Returns false if either ray is parallel.
    /// </summary>
    /// <param name="planeNormal">The plane normal (for example the camera-facing direction for free move, or an axis for a constrained plane).</param>
    /// <param name="delta">Receives the world-space movement on the plane.</param>
    public bool TryPlaneDelta(Vector3 planeNormal, out Vector3 delta)
    {
        delta = Vector3.Zero;
        if (!InteractMath.RayPlane(PressRayOrigin, PressRayDirection, PressWorldPoint, planeNormal, out _, out var start))
            return false;
        if (!InteractMath.RayPlane(RayOrigin, RayDirection, PressWorldPoint, planeNormal, out _, out var now))
            return false;

        delta = now - start;
        return true;
    }

    /// <summary>
    /// Signed distance the drag has moved along a world axis through <see cref="PressWorldPoint"/>: the basis for
    /// axis-constrained dragging (project the cursor ray onto the axis line). Returns false if the ray is parallel to the axis.
    /// </summary>
    /// <param name="axisDirection">The axis direction (need not be normalized).</param>
    /// <param name="distance">Receives the signed movement along the axis since press.</param>
    public bool TryAxisDelta(Vector3 axisDirection, out float distance)
    {
        distance = 0f;
        var axis = InteractMath.SafeNormalize(axisDirection, Vector3.UnitX);
        var okStart = InteractMath.ClosestAxisParam(PressRayOrigin, PressRayDirection, PressWorldPoint, axis, out var start);
        var okNow = InteractMath.ClosestAxisParam(RayOrigin, RayDirection, PressWorldPoint, axis, out var now);
        if (!okStart || !okNow)
            return false;

        distance = now - start;
        return true;
    }
}
