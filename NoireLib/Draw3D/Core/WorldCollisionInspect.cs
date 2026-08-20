using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Walks the game's collision scene keeping each collider's identity rather than only the triangles it
/// contributes, the counterpart to <see cref="WorldCollisionSource"/>. Reads struct fields directly and calls
/// no game code, so a torn or mid-load collider is skipped rather than faulting. Framework thread only.
/// </summary>
internal static unsafe class WorldCollisionInspect
{
    /// <summary>Hard cap on colliders visited in one walk, so a pathological scene can never hang a frame.</summary>
    private const int MaxColliders = 8192;

    /// <summary>Depth of the explicit PCB-tree traversal stack.</summary>
    private const int TreeStackDepth = 512;

    /// <summary>Gets a value indicating whether the collision scene can be reached right now.</summary>
    public static bool Available
    {
        get
        {
            try
            {
                var framework = CSFramework.Instance();
                return framework != null && framework->BGCollisionModule != null
                                         && framework->BGCollisionModule->SceneManager != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Describes every collider whose world bounds reach a box.
    /// </summary>
    /// <param name="boxMin">Query lower corner.</param>
    /// <param name="boxMax">Query upper corner.</param>
    /// <param name="into">Receives the colliders; appended to, never cleared.</param>
    /// <param name="limit">The most to describe.</param>
    /// <param name="scenes">Receives how many scenes were walked.</param>
    /// <returns>How many were described.</returns>
    public static int Collect(
        Vector3 boxMin, Vector3 boxMax, List<NoireCollider> into, int limit, out int scenes)
    {
        scenes = 0;
        if (into == null || limit <= 0)
            return 0;

        var found = 0;

        try
        {
            var manager = CSFramework.Instance()->BGCollisionModule->SceneManager;
            if (manager == null)
                return 0;

            var visited = 0;

            foreach (var wrapper in manager->Scenes)
            {
                if (wrapper == null || wrapper->Scene == null)
                    continue;

                scenes++;

                foreach (var collider in wrapper->Scene->Colliders)
                {
                    if (collider == null || found >= limit || visited >= MaxColliders)
                        break;

                    visited++;

                    try
                    {
                        if (!TryDescribe(collider, boxMin, boxMax, out var described))
                            continue;

                        into.Add(described);
                        found++;
                    }
                    catch
                    {
                        // A collider faulted mid-load; skip it and keep walking.
                    }
                }
            }
        }
        catch
        {
            // The walk faulted; report what was gathered rather than take the frame down.
        }

        return found;
    }

    /// <summary>
    /// Finds the nearest piece of the game's collision along a ray, intersecting the scene's triangles here
    /// rather than calling the game's raycast, so the struck triangle, its material and its collider all
    /// come back together.
    /// </summary>
    /// <param name="origin">Where the ray starts.</param>
    /// <param name="direction">Which way it points; need not be normalised.</param>
    /// <param name="maxDistance">How far to look, in world units.</param>
    /// <param name="includeAnalytic">Whether box, cylinder, sphere and plane colliders are hit too.</param>
    /// <param name="hit">Receives what was struck.</param>
    /// <returns>True when something was struck.</returns>
    public static bool Raycast(
        Vector3 origin, Vector3 direction, float maxDistance, bool includeAnalytic, out NoireCollisionHit hit)
    {
        hit = default;

        if (direction.LengthSquared() < 1e-12f || maxDistance <= 0f)
            return false;

        direction = Vector3.Normalize(direction);

        // The whole ray's bounds, so a collider nowhere near the line is rejected without touching its geometry.
        var end = origin + (direction * maxDistance);
        var boxMin = Vector3.Min(origin, end);
        var boxMax = Vector3.Max(origin, end);

        var best = float.MaxValue;
        var triangles = new List<Vector3>(3072);

        try
        {
            var manager = CSFramework.Instance()->BGCollisionModule->SceneManager;
            if (manager == null)
                return false;

            var visited = 0;

            foreach (var wrapper in manager->Scenes)
            {
                if (wrapper == null || wrapper->Scene == null)
                    continue;

                foreach (var collider in wrapper->Scene->Colliders)
                {
                    if (collider == null || visited >= MaxColliders)
                        break;

                    visited++;

                    try
                    {
                        if (!TryDescribe(collider, boxMin, boxMax, out var described))
                            continue;

                        if (described.Kind != NoireColliderKind.Mesh && !includeAnalytic)
                            continue;

                        triangles.Clear();
                        WorldCollisionSource.CollectTriangles(
                            boxMin, boxMax, triangles, 300000, includeAnalytic, collider);

                        for (var i = 0; i + 2 < triangles.Count; i += 3)
                        {
                            if (!Geometry3DHelper.RayTriangle(
                                    origin, direction, triangles[i], triangles[i + 1], triangles[i + 2],
                                    out var distance)
                                || distance >= best || distance > maxDistance)
                                continue;

                            best = distance;

                            var normal = Vector3.Cross(
                                triangles[i + 1] - triangles[i], triangles[i + 2] - triangles[i]);

                            hit = new NoireCollisionHit
                            {
                                Found = true,
                                Point = origin + (direction * distance),
                                Normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY,
                                Distance = distance,
                                Material = described.MaterialValue,
                                A = triangles[i],
                                B = triangles[i + 1],
                                C = triangles[i + 2],
                                Collider = described,
                            };
                        }
                    }
                    catch
                    {
                        // A collider faulted mid-load; skip it and keep walking.
                    }
                }
            }
        }
        catch
        {
            // The walk faulted; return whatever was found.
        }

        return hit.Found;
    }

    /// <summary>Collects every triangle of one collider.</summary>
    /// <param name="handle">The collider handle carried by its description.</param>
    /// <param name="into">Receives the corners, three per triangle; appended to.</param>
    /// <param name="limit">The most triangles to collect.</param>
    /// <returns>How many were collected.</returns>
    public static int TrianglesOf(nint handle, List<Vector3> into, int limit)
    {
        if (handle == 0 || into == null || limit <= 0)
            return 0;

        try
        {
            var everywhere = new Vector3(float.MinValue / 4f);
            return WorldCollisionSource.CollectTriangles(
                everywhere, -everywhere, into, limit, includeAnalytic: true, (Collider*)handle);
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryDescribe(Collider* collider, Vector3 boxMin, Vector3 boxMax, out NoireCollider described)
    {
        described = default;

        var type = collider->GetColliderType();

        // A streamed collider owns no geometry: it swaps mesh colliders into the same list as the streaming
        // sphere moves, so describing it as well would double every terrain tile in the readout.
        if (type == ColliderType.Streamed)
            return false;

        var world = Matrix4x4.Identity;
        var min = Vector3.Zero;
        var max = Vector3.Zero;
        var translation = Vector3.Zero;
        var rotation = Vector3.Zero;
        var scale = Vector3.One;
        var primitives = 0;
        var loaded = true;
        var path = string.Empty;

        if (type == ColliderType.Mesh)
        {
            var mesh = (ColliderMesh*)collider;
            world = mesh->World.FullMatrix();
            min = mesh->WorldBoundingBox.Min;
            max = mesh->WorldBoundingBox.Max;
            translation = mesh->Translation;
            rotation = mesh->Rotation;
            scale = mesh->Scale;
            primitives = mesh->TotalPrimitives;
            loaded = mesh->Loaded;

            if (mesh->Resource != null)
                path = mesh->Resource->PathString ?? string.Empty;
        }
        else
        {
            world = type switch
            {
                ColliderType.Box => ((ColliderBox*)collider)->World.FullMatrix(),
                ColliderType.Cylinder => ((ColliderCylinder*)collider)->World.FullMatrix(),
                ColliderType.Sphere => ((ColliderSphere*)collider)->World.FullMatrix(),
                _ => ((ColliderPlane*)collider)->World.FullMatrix(),
            };

            // An analytic collider spans minus one to plus one on the axes it occupies, so its own transform
            // bounds it, which is the only extent these carry.
            (min, max) = BoundsOfUnitShape(world);
            translation = world.Translation;
        }

        if (!Overlaps(min, max, boxMin, boxMax))
            return false;

        described = new NoireCollider
        {
            Handle = (nint)collider,
            Kind = (NoireColliderKind)(int)type,
            LayoutObjectId = collider->LayoutObjectId,
            LayerMask = collider->LayerMask,
            MaterialValue = collider->ObjectMaterialValue,
            MaterialMask = collider->ObjectMaterialMask,
            VisibilityFlags = collider->VisibilityFlags,
            References = collider->NumRefs,
            ResourcePath = path,
            Translation = translation,
            Rotation = rotation,
            Scale = scale,
            World = world,
            Min = min,
            Max = max,
            Primitives = primitives,
            Loaded = loaded,
        };

        return true;
    }

    private static (Vector3 Min, Vector3 Max) BoundsOfUnitShape(in Matrix4x4 world)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (var corner = 0; corner < 8; corner++)
        {
            var point = Vector3.Transform(
                new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f),
                world);

            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return (min, max);
    }

    private static bool Overlaps(Vector3 min, Vector3 max, Vector3 boxMin, Vector3 boxMax)
        => max.X >= min.X && float.IsFinite(min.X)
            ? Geometry3DHelper.AabbOverlap(min, max, boxMin, boxMax)
            : true;
}
