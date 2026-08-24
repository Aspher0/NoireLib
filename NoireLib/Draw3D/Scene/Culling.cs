using NoireLib.Draw3D.Geometry;
using NoireLib.Helpers;

namespace NoireLib.Draw3D.Scene;

// Scene-side sugar over FrustumPlanes: the renderer culls against BoundingSphere, which is a Draw3D shape, while the
// planes themselves take loose values.
internal static class Culling
{
    /// <summary>Sphere-vs-frustum test: true when the sphere touches the view volume.</summary>
    public static bool Intersects(this in FrustumPlanes planes, in BoundingSphere sphere)
        => planes.Intersects(sphere.Center, sphere.Radius);
}
