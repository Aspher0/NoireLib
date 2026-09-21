using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System;
using System.Collections.Generic;
using System.Numerics;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace NoireLib.Helpers;

// Keeps each collider's identity. Reads struct fields and calls no game code. Framework thread only.
internal static unsafe class WorldCollisionInspect
{
    private const int MaxColliders = 8192;

    private const int TreeStackDepth = 512;

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

    public static int Collect(
        Vector3 boxMin, Vector3 boxMax, List<ColliderInfo> into, int limit, out int scenes)
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
                        // A collider faulted mid-load.
                    }
                }
            }
        }
        catch
        {
        }

        return found;
    }

    // Intersects the triangles directly. The game's raycast is never called.
    public static bool Raycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        bool includeAnalytic,
        ulong layerMask,
        out CollisionHit hit)
    {
        hit = default;

        if (direction.LengthSquared() < 1e-12f || maxDistance <= 0f)
            return false;

        direction = Vector3.Normalize(direction);

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

                        if (described.Kind != ColliderKind.Mesh && !includeAnalytic)
                            continue;

                        // The client masks its queries by layer.
                        if (layerMask != 0 && (described.LayerMask & layerMask) == 0)
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

                            hit = new CollisionHit
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
                        // A collider faulted mid-load.
                    }
                }
            }
        }
        catch
        {
        }

        return hit.Found;
    }

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

    private static bool TryDescribe(Collider* collider, Vector3 boxMin, Vector3 boxMax, out ColliderInfo described)
    {
        described = default;

        var type = collider->GetColliderType();

        // A streamed collider owns no geometry. It swaps mesh colliders into this same list.
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

            // An analytic collider spans -1 to 1 on its axes.
            (min, max) = BoundsOfUnitShape(world);
            translation = world.Translation;
        }

        if (!Overlaps(min, max, boxMin, boxMax))
            return false;

        described = new ColliderInfo
        {
            Handle = (nint)collider,
            Kind = (ColliderKind)(int)type,
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
