using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>A virtual camera for render-to-texture views, projecting with reversed-Z and an infinite far plane.</summary>
public struct Camera3D
{
    /// <summary>Camera position in world space.</summary>
    public Vector3 Position;

    /// <summary>The point the camera looks at.</summary>
    public Vector3 Target;

    /// <summary>The camera's up hint (default +Y).</summary>
    public Vector3 Up;

    /// <summary>Vertical field of view in radians (default 60 degrees).</summary>
    public float VerticalFov;

    /// <summary>Near-plane distance (default 0.1).</summary>
    public float NearPlane;

    /// <summary>Creates a camera looking from <paramref name="position"/> at <paramref name="target"/>.</summary>
    /// <param name="position">Camera position in world space.</param>
    /// <param name="target">The point the camera looks at.</param>
    /// <param name="verticalFovRad">Vertical field of view in radians.</param>
    /// <param name="nearPlane">Near-plane distance.</param>
    public Camera3D(Vector3 position, Vector3 target, float verticalFovRad = MathF.PI / 3f, float nearPlane = 0.1f)
    {
        Position = position;
        Target = target;
        Up = Vector3.UnitY;
        VerticalFov = verticalFovRad;
        NearPlane = nearPlane;
    }

    /// <summary>Builds the row-vector view-projection matrix (reversed-Z, infinite far) for a given aspect ratio.</summary>
    /// <param name="aspect">Viewport width divided by height.</param>
    /// <returns>The combined view-projection matrix.</returns>
    public readonly Matrix4x4 BuildViewProj(float aspect)
    {
        var up = Up.LengthSquared() > 1e-9f ? Up : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAtLeftHanded(Position, Target, up);

        var g = 1f / MathF.Tan(VerticalFov * 0.5f);
        var proj = new Matrix4x4(
            g / MathF.Max(aspect, 1e-6f), 0, 0, 0,
            0, g, 0, 0,
            0, 0, 0, 1,
            0, 0, MathF.Max(NearPlane, 1e-4f), 0);

        return view * proj;
    }
}
