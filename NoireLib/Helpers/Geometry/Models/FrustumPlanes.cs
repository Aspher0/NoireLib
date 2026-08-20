using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// The clipping planes of a view volume, extracted from a row-vector view-projection matrix (Gribb-Hartmann).<br/>
/// Five planes only: under an infinite-far projection the far plane is degenerate, so it is skipped rather than
/// extracted and normalized to nothing.
/// </summary>
public readonly struct FrustumPlanes
{
    private readonly Vector4 left, right, bottom, top, near;

    private FrustumPlanes(Vector4 l, Vector4 r, Vector4 b, Vector4 t, Vector4 n)
    {
        left = l;
        right = r;
        bottom = b;
        top = t;
        near = n;
    }

    /// <summary>
    /// Extracts normalized planes from a row-vector view-projection matrix, oriented so a point is inside when
    /// <c>a*x + b*y + c*z + d</c> is at least zero.
    /// </summary>
    /// <param name="viewProj">The combined view-projection matrix.</param>
    /// <returns>The five planes.</returns>
    public static FrustumPlanes FromViewProj(in Matrix4x4 viewProj) => new(
        Normalize(new Vector4(viewProj.M14 + viewProj.M11, viewProj.M24 + viewProj.M21, viewProj.M34 + viewProj.M31, viewProj.M44 + viewProj.M41)),
        Normalize(new Vector4(viewProj.M14 - viewProj.M11, viewProj.M24 - viewProj.M21, viewProj.M34 - viewProj.M31, viewProj.M44 - viewProj.M41)),
        Normalize(new Vector4(viewProj.M14 + viewProj.M12, viewProj.M24 + viewProj.M22, viewProj.M34 + viewProj.M32, viewProj.M44 + viewProj.M42)),
        Normalize(new Vector4(viewProj.M14 - viewProj.M12, viewProj.M24 - viewProj.M22, viewProj.M34 - viewProj.M32, viewProj.M44 - viewProj.M42)),
        Normalize(new Vector4(viewProj.M14 - viewProj.M13, viewProj.M24 - viewProj.M23, viewProj.M34 - viewProj.M33, viewProj.M44 - viewProj.M43)));

    /// <summary>
    /// Whether a sphere touches the view volume.
    /// </summary>
    /// <param name="center">Sphere center.</param>
    /// <param name="radius">Sphere radius.</param>
    /// <returns>True when any part of the sphere is inside; a conservative test, so a sphere just outside a corner can still report true.</returns>
    public bool Intersects(Vector3 center, float radius)
    {
        var c = new Vector4(center, 1f);
        var negRadius = -radius;
        return Vector4.Dot(left, c) >= negRadius
            && Vector4.Dot(right, c) >= negRadius
            && Vector4.Dot(bottom, c) >= negRadius
            && Vector4.Dot(top, c) >= negRadius
            && Vector4.Dot(near, c) >= negRadius;
    }

    private static Vector4 Normalize(Vector4 plane)
    {
        var len = new Vector3(plane.X, plane.Y, plane.Z).Length();
        return len > 1e-9f ? plane / len : plane;
    }
}
