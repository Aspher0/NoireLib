using NoireLib.Draw3D.Geometry;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Frustum planes extracted from a row-vector ViewProj (Gribb–Hartmann, our convention).<br/>
/// Five planes only: the far plane is degenerate under the game's infinite-far projection and is skipped.
/// </summary>
internal readonly struct FrustumPlanes
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

    /// <summary>Extracts normalized planes from a row-vector view-projection matrix (inside ⇔ a·x+b·y+c·z+d ≥ 0).</summary>
    public static FrustumPlanes FromViewProj(in Matrix4x4 m) => new(
        Normalize(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)),
        Normalize(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)),
        Normalize(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)),
        Normalize(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)),
        Normalize(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43)));

    private static Vector4 Normalize(Vector4 plane)
    {
        var len = new Vector3(plane.X, plane.Y, plane.Z).Length();
        return len > 1e-9f ? plane / len : plane;
    }

    /// <summary>Sphere-vs-frustum test: true when the sphere touches the view volume.</summary>
    public bool Intersects(in BoundingSphere sphere)
    {
        var c = new Vector4(sphere.Center, 1f);
        var negRadius = -sphere.Radius;
        return Vector4.Dot(left, c) >= negRadius
            && Vector4.Dot(right, c) >= negRadius
            && Vector4.Dot(bottom, c) >= negRadius
            && Vector4.Dot(top, c) >= negRadius
            && Vector4.Dot(near, c) >= negRadius;
    }
}
