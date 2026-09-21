using NoireLib.Draw3D.Geometry;
using NoireLib.Helpers;

namespace NoireLib.Draw3D.Scene;

internal static class Culling
{
    public static bool Intersects(this in FrustumPlanes planes, in BoundingSphere sphere)
        => planes.Intersects(sphere.Center, sphere.Radius);
}
