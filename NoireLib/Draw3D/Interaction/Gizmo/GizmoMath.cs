using System;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>
/// Pure drag solvers for the gizmo: given the cursor ray at press and now, each returns the transform delta a handle
/// implies (axis distance, plane vector, rotation angle, scale factor) and the screen-constant handle size. No renderer
/// or ImGui state, so every solver is unit-tested headlessly; the gizmo shell only wires these to the target transform.
/// </summary>
public static class GizmoMath
{
    /// <summary>The world length that spans <paramref name="pixels"/> screen pixels at <paramref name="origin"/>, keeping a handle a constant on-screen size. Falls back to a distance-scaled estimate behind the camera.</summary>
    public static float ScreenConstantLength(in FrameContext frame, Vector3 origin, float pixels)
    {
        if (InteractMath.WorldPerPixel(in frame, origin, out var worldPerPixel, out _, out _))
            return worldPerPixel * pixels;

        // Behind/at the camera: a soft fallback so the gizmo never collapses to zero size.
        return MathF.Max(0.001f, Vector3.Distance(origin, frame.EyePos) * 0.001f) * pixels;
    }

    /// <summary>
    /// Signed distance the cursor moved along an axis between press and now, tracking the cursor so the grabbed point
    /// stays under it. Each ray is intersected with the plane that contains the axis and faces the camera most (its
    /// normal is the eye-to-gizmo direction with the axis component removed), and the hit-point difference is projected
    /// onto the axis. A closest-point-between-lines measure (the earlier approach) over-slid the object when the axis
    /// ran oblique to the view, moving it farther than the cursor; this matches how ImGuizmo tracks an axis drag.
    /// </summary>
    public static float AxisTranslationDelta(Vector3 axis, Vector3 origin, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        axis = InteractMath.SafeNormalize(axis, Vector3.UnitX);

        var viewDir = InteractMath.SafeNormalize(origin - curRayO, curRayD);
        var normal = viewDir - axis * Vector3.Dot(viewDir, axis);
        if (normal.LengthSquared() >= 1e-6f)
        {
            normal = Vector3.Normalize(normal);
            if (InteractMath.RayPlane(pressRayO, pressRayD, origin, normal, out _, out var a) &&
                InteractMath.RayPlane(curRayO, curRayD, origin, normal, out _, out var b))
                return Vector3.Dot(b - a, axis);
        }

        // Axis nearly edge-on to the view (the tracking plane is ill-conditioned): fall back to projecting each ray
        // onto the axis line, which stays finite when a facing plane cannot be built.
        InteractMath.ClosestAxisParam(pressRayO, pressRayD, origin, axis, out var p0);
        InteractMath.ClosestAxisParam(curRayO, curRayD, origin, axis, out var p1);
        return p1 - p0;
    }

    /// <summary>World movement of the grabbed point across a plane (through <paramref name="planePoint"/>, normal <paramref name="planeNormal"/>) between press and now.</summary>
    public static Vector3 PlaneTranslationDelta(Vector3 planePoint, Vector3 planeNormal, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        if (!InteractMath.RayPlane(pressRayO, pressRayD, planePoint, planeNormal, out _, out var a) ||
            !InteractMath.RayPlane(curRayO, curRayD, planePoint, planeNormal, out _, out var b))
            return Vector3.Zero;

        return b - a;
    }

    /// <summary>Signed rotation (radians) about <paramref name="axis"/> at <paramref name="center"/> between press and now, measured where each ray meets the axis plane.</summary>
    public static float RotationAngle(Vector3 center, Vector3 axis, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        axis = InteractMath.SafeNormalize(axis, Vector3.UnitY);
        if (!InteractMath.RayPlane(pressRayO, pressRayD, center, axis, out _, out var a) ||
            !InteractMath.RayPlane(curRayO, curRayD, center, axis, out _, out var b))
            return 0f;

        return InteractMath.SignedAngleOnPlane(center, axis, a, b);
    }

    /// <summary>
    /// Scale factor for an axis handle: 1 at press, growing linearly as the cursor is dragged out along the axis
    /// (moving a full handle-length doubles it). Clamped to a small positive minimum so scale never flips or hits zero.
    /// </summary>
    /// <param name="axis">Handle axis, normalized.</param>
    /// <param name="origin">Gizmo origin.</param>
    /// <param name="referenceLength">The handle's world length (a full drag of this length ⇒ factor 2).</param>
    public static float AxisScaleFactor(Vector3 axis, Vector3 origin, float referenceLength, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        if (referenceLength < 1e-6f)
            return 1f;

        var delta = AxisTranslationDelta(axis, origin, pressRayO, pressRayD, curRayO, curRayD);
        return MathF.Max(0.01f, 1f + delta / referenceLength);
    }

    /// <summary>Uniform scale factor from a center-knob drag: the ratio of the cursor's screen distance from the gizmo center now vs. at press.</summary>
    public static float UniformScaleFactor(Vector2 originScreen, Vector2 pressScreen, Vector2 curScreen)
    {
        var press = Vector2.Distance(pressScreen, originScreen);
        var cur = Vector2.Distance(curScreen, originScreen);
        if (press < 1e-3f)
            return 1f;

        return MathF.Max(0.01f, cur / press);
    }

    /// <summary>Applies per-component translation snapping (used on the final position, so axis drags land on the grid).</summary>
    public static Vector3 SnapTranslation(Vector3 position, Vector3 snap) => InteractMath.Snap(position, snap);

    /// <summary>Rounds a rotation angle (radians) to the nearest snap increment given in degrees (≤ 0 = free).</summary>
    public static float SnapAngle(float radians, float snapDegrees)
    {
        if (snapDegrees <= 1e-4f)
            return radians;

        var step = snapDegrees * (MathF.PI / 180f);
        return MathF.Round(radians / step) * step;
    }

    /// <summary>Rounds a scale component to the nearest snap increment (≤ 0 = free), never below the increment itself.</summary>
    public static float SnapScale(float value, float snap)
    {
        if (snap <= 1e-6f)
            return value;

        return MathF.Max(snap, MathF.Round(value / snap) * snap);
    }
}
