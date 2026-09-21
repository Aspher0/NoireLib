using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>Pure gizmo drag solvers turning the cursor ray at press and now into a transform delta.</summary>
public static class GizmoMath
{
    /// <summary>The world length spanning <paramref name="pixels"/> screen pixels at <paramref name="origin"/>, estimated from distance behind the camera.</summary>
    public static float ScreenConstantLength(in FrameContext frame, Vector3 origin, float pixels)
    {
        if (frame.TryWorldPerPixel(origin, out var worldPerPixel, out _, out _))
            return worldPerPixel * pixels;

        // Behind the camera: never zero size.
        return MathF.Max(0.001f, Vector3.Distance(origin, frame.EyePos) * 0.001f) * pixels;
    }

    /// <summary>Signed distance the cursor moved along an axis between press and now, measured on the camera-facing plane containing the axis.</summary>
    public static float AxisTranslationDelta(Vector3 axis, Vector3 origin, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        axis = Geometry3DHelper.SafeNormalize(axis, Vector3.UnitX);

        var viewDir = Geometry3DHelper.SafeNormalize(origin - curRayO, curRayD);
        var normal = viewDir - axis * Vector3.Dot(viewDir, axis);
        if (normal.LengthSquared() >= 1e-6f)
        {
            normal = Vector3.Normalize(normal);
            if (Geometry3DHelper.RayPlane(pressRayO, pressRayD, origin, normal, out _, out var a) &&
                Geometry3DHelper.RayPlane(curRayO, curRayD, origin, normal, out _, out var b))
                return Vector3.Dot(b - a, axis);
        }

        // Axis nearly edge-on to the view.
        Geometry3DHelper.ClosestAxisParam(pressRayO, pressRayD, origin, axis, out var p0);
        Geometry3DHelper.ClosestAxisParam(curRayO, curRayD, origin, axis, out var p1);
        return p1 - p0;
    }

    /// <summary>World movement of the grabbed point across a plane (through <paramref name="planePoint"/>, normal <paramref name="planeNormal"/>) between press and now.</summary>
    public static Vector3 PlaneTranslationDelta(Vector3 planePoint, Vector3 planeNormal, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        if (!Geometry3DHelper.RayPlane(pressRayO, pressRayD, planePoint, planeNormal, out _, out var a) ||
            !Geometry3DHelper.RayPlane(curRayO, curRayD, planePoint, planeNormal, out _, out var b))
            return Vector3.Zero;

        return b - a;
    }

    /// <summary>Signed rotation (radians) about <paramref name="axis"/> at <paramref name="center"/> between press and now, measured where each ray meets the axis plane.</summary>
    public static float RotationAngle(Vector3 center, Vector3 axis, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        axis = Geometry3DHelper.SafeNormalize(axis, Vector3.UnitY);
        if (!Geometry3DHelper.RayPlane(pressRayO, pressRayD, center, axis, out _, out var a) ||
            !Geometry3DHelper.RayPlane(curRayO, curRayD, center, axis, out _, out var b))
            return 0f;

        return Geometry3DHelper.SignedAngleOnPlane(center, axis, a, b);
    }

    /// <summary>Scale factor for an axis handle. 1 at press, doubled by a full handle-length drag, clamped above zero.</summary>
    /// <param name="axis">Handle axis, normalized.</param>
    /// <param name="origin">Gizmo origin.</param>
    /// <param name="referenceLength">The handle's world length (a full drag of this length gives a factor of 2).</param>
    /// <param name="pressRayO">Pointer ray origin at press.</param>
    /// <param name="pressRayD">Pointer ray direction at press.</param>
    /// <param name="curRayO">Pointer ray origin now.</param>
    /// <param name="curRayD">Pointer ray direction now.</param>
    /// <returns>The scale factor, above zero.</returns>
    public static float AxisScaleFactor(Vector3 axis, Vector3 origin, float referenceLength, Vector3 pressRayO, Vector3 pressRayD, Vector3 curRayO, Vector3 curRayD)
    {
        if (referenceLength < 1e-6f)
            return 1f;

        var delta = AxisTranslationDelta(axis, origin, pressRayO, pressRayD, curRayO, curRayD);
        return MathF.Max(0.01f, 1f + delta / referenceLength);
    }

    /// <summary>Uniform scale factor from a center-knob drag, the cursor's screen distance from the center now over at press.</summary>
    public static float UniformScaleFactor(Vector2 originScreen, Vector2 pressScreen, Vector2 curScreen)
    {
        var press = Vector2.Distance(pressScreen, originScreen);
        var cur = Vector2.Distance(curScreen, originScreen);
        if (press < 1e-3f)
            return 1f;

        return MathF.Max(0.01f, cur / press);
    }

    /// <summary>Applies per-component translation snapping.</summary>
    public static Vector3 SnapTranslation(Vector3 position, Vector3 snap) => Geometry3DHelper.Snap(position, snap);

    /// <summary>Rounds a rotation angle (radians) to the nearest snap increment given in degrees (&lt;= 0 = free).</summary>
    public static float SnapAngle(float radians, float snapDegrees)
    {
        if (snapDegrees <= 1e-4f)
            return radians;

        var step = snapDegrees * (MathF.PI / 180f);
        return MathF.Round(radians / step) * step;
    }

    /// <summary>Rounds a scale component to the nearest snap increment (&lt;= 0 = free), never below the increment itself.</summary>
    public static float SnapScale(float value, float snap)
    {
        if (snap <= 1e-6f)
            return value;

        return MathF.Max(snap, MathF.Round(value / snap) * snap);
    }
}
