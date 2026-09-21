using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>The drag state: the cursor ray now and at press, the grabbed point, and delta helpers.</summary>
public sealed class DragContext
{
    /// <summary>The button driving the drag (always <see cref="MouseButton.Left"/> today).</summary>
    public MouseButton Button { get; internal set; }

    /// <summary>The node being dragged, or null for gizmo-handle and custom-interactor drags.</summary>
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

    /// <summary>Intersects the current cursor ray with a world plane. Returns false when the ray is parallel to it.</summary>
    /// <param name="planePoint">Any point on the plane (often <see cref="PressWorldPoint"/>).</param>
    /// <param name="planeNormal">The plane normal.</param>
    /// <param name="hit">Receives the world hit point.</param>
    public bool TryRayPlane(Vector3 planePoint, Vector3 planeNormal, out Vector3 hit)
        => Geometry3DHelper.RayPlane(RayOrigin, RayDirection, planePoint, planeNormal, out _, out hit);

    /// <summary>World-space movement since press on a plane through <see cref="PressWorldPoint"/>. Returns false if either ray is parallel.</summary>
    /// <param name="planeNormal">The plane normal.</param>
    /// <param name="delta">Receives the world-space movement on the plane.</param>
    public bool TryPlaneDelta(Vector3 planeNormal, out Vector3 delta)
    {
        delta = Vector3.Zero;
        if (!Geometry3DHelper.RayPlane(PressRayOrigin, PressRayDirection, PressWorldPoint, planeNormal, out _, out var start))
            return false;
        if (!Geometry3DHelper.RayPlane(RayOrigin, RayDirection, PressWorldPoint, planeNormal, out _, out var now))
            return false;

        delta = now - start;
        return true;
    }

    /// <summary>Signed movement since press along a world axis through <see cref="PressWorldPoint"/>. Returns false if a ray is parallel to it.</summary>
    /// <param name="axisDirection">The axis direction (need not be normalized).</param>
    /// <param name="distance">Receives the signed movement along the axis since press.</param>
    public bool TryAxisDelta(Vector3 axisDirection, out float distance)
    {
        distance = 0f;
        var axis = Geometry3DHelper.SafeNormalize(axisDirection, Vector3.UnitX);
        var okStart = Geometry3DHelper.ClosestAxisParam(PressRayOrigin, PressRayDirection, PressWorldPoint, axis, out var start);
        var okNow = Geometry3DHelper.ClosestAxisParam(RayOrigin, RayDirection, PressWorldPoint, axis, out var now);
        if (!okStart || !okNow)
            return false;

        distance = now - start;
        return true;
    }
}
