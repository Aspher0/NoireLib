using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the game's loaded collision world through named struct fields, without calling into game code.
/// Framework thread only: the game's update frees and swaps collider geometry, so a read from another thread can
/// follow a dead pointer. Every method here returns nothing off the framework thread.
/// </summary>
public static class GameCollisionHelper
{
    /// <summary>
    /// Whether the collision scene exists and the caller is on the framework thread.
    /// </summary>
    public static bool Available => OnFrameworkThread && WorldCollisionInspect.Available;

    /// <summary>
    /// Whether the caller is on the thread that owns the collision scene, false when the library is uninitialized.
    /// </summary>
    private static bool OnFrameworkThread
        => NoireService.IsInitialized() && NoireService.Framework.IsInFrameworkUpdateThread;

    /// <summary>
    /// Describes every collider whose bounds reach a point.
    /// </summary>
    /// <param name="centre">The middle of the area to look at.</param>
    /// <param name="radius">How far around it to reach, in world units.</param>
    /// <param name="into">Receives the colliders; cleared first.</param>
    /// <param name="limit">The most to describe.</param>
    /// <returns>How many were described.</returns>
    public static int Collect(Vector3 centre, float radius, List<ColliderInfo> into, int limit = 512)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        if (radius <= 0f || !OnFrameworkThread)
            return 0;

        var reach = new Vector3(radius);
        return WorldCollisionInspect.Collect(centre - reach, centre + reach, into, limit, out _);
    }

    /// <summary>
    /// Finds the nearest collision along a ray, with the triangle, its material and the collider that owns it.
    /// </summary>
    /// <param name="origin">Where the ray starts.</param>
    /// <param name="direction">Which way it points; need not be normalised.</param>
    /// <param name="maxDistance">How far to look, in world units.</param>
    /// <param name="includeAnalytic">
    /// Whether box, cylinder, sphere and plane colliders count, which are the invisible walls and the proxies for
    /// pillars and doors.
    /// </param>
    /// <returns>What was struck.</returns>
    public static CollisionHit Raycast(
        Vector3 origin, Vector3 direction, float maxDistance = 2000f, bool includeAnalytic = true)
        => OnFrameworkThread
           && WorldCollisionInspect.Raycast(origin, direction, maxDistance, includeAnalytic, out var hit)
            ? hit
            : default;

    /// <summary>
    /// Collects one collider's triangles in world space.
    /// </summary>
    /// <param name="collider">The collider, as described by <see cref="Collect"/>.</param>
    /// <param name="into">Receives the corners, three per triangle; cleared first.</param>
    /// <param name="limit">The most triangles to collect.</param>
    /// <returns>How many were collected.</returns>
    public static int CollectTriangles(in ColliderInfo collider, List<Vector3> into, int limit = 20000)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        return OnFrameworkThread ? WorldCollisionInspect.TrianglesOf(collider.Handle, into, limit) : 0;
    }

    /// <summary>
    /// Collects the collision triangles around a point.
    /// </summary>
    /// <param name="centre">The middle of the area to read.</param>
    /// <param name="radius">How far around it to reach, in world units.</param>
    /// <param name="into">Receives the corners, three per triangle; cleared first.</param>
    /// <param name="limit">The most triangles to collect.</param>
    /// <param name="includeAnalytic">Whether box, cylinder, sphere and plane colliders are tessellated too.</param>
    /// <returns>How many triangles were collected.</returns>
    public static int CollectTrianglesNear(
        Vector3 centre, float radius, List<Vector3> into, int limit = 60000, bool includeAnalytic = true)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        if (radius <= 0f || !OnFrameworkThread)
            return 0;

        var reach = new Vector3(radius);
        return WorldCollisionSource.CollectTriangles(
            centre - reach, centre + reach, into, limit, includeAnalytic);
    }
}
