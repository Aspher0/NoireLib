using NoireLib.Draw3D.Geometry;
using NoireLib.Helpers;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Scene-side sugar over <see cref="FrustumPlanes"/>: the renderer culls against <see cref="BoundingSphere"/>, which
/// is a Draw3D shape, while the planes themselves take loose values.
/// </summary>
internal static class Culling
{
    /// <summary>Sphere-vs-frustum test: true when the sphere touches the view volume.</summary>
    public static bool Intersects(this in FrustumPlanes planes, in BoundingSphere sphere)
        => planes.Intersects(sphere.Center, sphere.Radius);
}
