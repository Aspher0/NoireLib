using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System;
using System.Collections.Generic;
using System.Numerics;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace NoireLib.Helpers;

// Framework thread only. The game's update mutates the scene under an SRW lock.
internal static unsafe class WorldCollisionSource
{
    private const int MaxColliders = 8192;

    private const int TreeStackDepth = 512;

    // A layerMask of zero takes every layer.
    public static int CollectTriangles(
        Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, bool includeAnalytic,
        ulong layerMask)
    {
        if (outTriangles == null || maxTriangles <= 0)
            return 0;

        var added = 0;
        try
        {
            var framework = CSFramework.Instance();
            if (framework == null)
                return 0;

            var module = framework->BGCollisionModule;
            if (module == null)
                return 0;

            var sceneManager = module->SceneManager;
            if (sceneManager == null)
                return 0;

            var visited = 0;
            foreach (var wrapper in sceneManager->Scenes)
            {
                if (wrapper == null)
                    continue;
                var scene = wrapper->Scene;
                if (scene == null)
                    continue;

                foreach (var collider in scene->Colliders)
                {
                    if (collider == null || added >= maxTriangles || visited >= MaxColliders)
                        break;
                    visited++;

                    try
                    {
                        // The client masks its collision queries by layer.
                        if (layerMask == 0 || (collider->LayerMask & layerMask) != 0)
                            AppendCollider(collider, boxMin, boxMax, outTriangles, maxTriangles, includeAnalytic, ref added);
                    }
                    catch
                    {
                        // A collider read mid-load can be torn.
                    }
                }

                if (added >= maxTriangles)
                    break;
            }
        }
        catch
        {
        }

        return added;
    }

    public static int CollectTriangles(
        Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, bool includeAnalytic,
        Collider* collider)
    {
        if (outTriangles == null || maxTriangles <= 0 || collider == null)
            return 0;

        var added = 0;

        try
        {
            AppendCollider(collider, boxMin, boxMax, outTriangles, maxTriangles, includeAnalytic, ref added);
        }
        catch
        {
            // A collider read mid-load can be torn.
        }

        return added;
    }

    private static void AppendCollider(Collider* collider, Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, bool includeAnalytic, ref int added)
    {
        var type = collider->GetColliderType();
        switch (type)
        {
            case ColliderType.Mesh:
                AppendMesh((ColliderMesh*)collider, boxMin, boxMax, outTriangles, maxTriangles, ref added);
                break;

            // A streamed collider owns no geometry. It swaps mesh colliders into this same list.
            case ColliderType.Streamed:
                break;

            case ColliderType.Box when includeAnalytic:
                AppendUnitBox(((ColliderBox*)collider)->World.FullMatrix(), boxMin, boxMax, outTriangles, maxTriangles, ref added);
                break;
            case ColliderType.Cylinder when includeAnalytic:
                AppendUnitCylinder(((ColliderCylinder*)collider)->World.FullMatrix(), boxMin, boxMax, outTriangles, maxTriangles, ref added);
                break;
            case ColliderType.Sphere when includeAnalytic:
                AppendUnitSphere(((ColliderSphere*)collider)->World.FullMatrix(), boxMin, boxMax, outTriangles, maxTriangles, ref added);
                break;
            case ColliderType.Plane or ColliderType.PlaneTwoSided when includeAnalytic:
                AppendUnitPlane(((ColliderPlane*)collider)->World.FullMatrix(), boxMin, boxMax, outTriangles, maxTriangles, ref added);
                break;
        }
    }

    private static void AppendMesh(ColliderMesh* collider, Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, ref int added)
    {
        var mesh = (MeshPCB*)collider->Mesh;
        if (mesh == null)
            return;

        var wb = collider->WorldBoundingBox;
        if (IsValidAabb(wb.Min, wb.Max) && !Geometry3DHelper.AabbOverlap(wb.Min, wb.Max, boxMin, boxMax))
            return;

        var root = mesh->RootNode;
        if (root == null)
            return;

        var world = collider->World.FullMatrix();

        var stack = stackalloc MeshPCB.FileNode*[TreeStackDepth];
        var sp = 0;
        stack[sp++] = root;

        while (sp > 0 && added < maxTriangles)
        {
            var node = stack[--sp];
            if (node == null)
                continue;

            var vertCount = node->NumVertsRaw + node->NumVertsCompressed;
            var prims = node->Primitives;
            for (var p = 0; p < prims.Length && added < maxTriangles; p++)
            {
                int i0 = prims[p].V1, i1 = prims[p].V2, i2 = prims[p].V3;
                if (i0 >= vertCount || i1 >= vertCount || i2 >= vertCount)
                    continue;

                var v0 = Vector3.Transform(node->Vertex(i0), world);
                var v1 = Vector3.Transform(node->Vertex(i1), world);
                var v2 = Vector3.Transform(node->Vertex(i2), world);
                if (!TriangleOverlapsBox(v0, v1, v2, boxMin, boxMax))
                    continue;

                outTriangles.Add(v0);
                outTriangles.Add(v1);
                outTriangles.Add(v2);
                added++;
            }

            var c1 = node->Child1;
            var c2 = node->Child2;
            if (c1 != null && sp < TreeStackDepth)
                stack[sp++] = c1;
            if (c2 != null && sp < TreeStackDepth)
                stack[sp++] = c2;
        }
    }

    // Local bounds are the cube from -1 to 1.
    private static void AppendUnitBox(Matrix4x4 world, Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, ref int added)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        for (var i = 0; i < 8; i++)
            c[i] = Vector3.Transform(new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1), world);

        ReadOnlySpan<int> faces = stackalloc int[]
        {
            0,2,3, 0,3,1,  4,5,7, 4,7,6,  0,1,5, 0,5,4,
            2,6,7, 2,7,3,  0,4,6, 0,6,2,  1,3,7, 1,7,5,
        };
        for (var f = 0; f < faces.Length && added < maxTriangles; f += 3)
            AddIfOverlap(c[faces[f]], c[faces[f + 1]], c[faces[f + 2]], boxMin, boxMax, outTriangles, ref added);
    }

    // Local bounds span -1 to 1 in X and Y at Z zero, normal along +Z.
    private static void AppendUnitPlane(Matrix4x4 world, Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, ref int added)
    {
        var a = Vector3.Transform(new Vector3(-1, -1, 0), world);
        var b = Vector3.Transform(new Vector3(1, -1, 0), world);
        var cc = Vector3.Transform(new Vector3(1, 1, 0), world);
        var d = Vector3.Transform(new Vector3(-1, 1, 0), world);
        AddIfOverlap(a, b, cc, boxMin, boxMax, outTriangles, ref added);
        if (added < maxTriangles)
            AddIfOverlap(a, cc, d, boxMin, boxMax, outTriangles, ref added);
    }

    // Radius 1, half-height 1, about local Y.
    private static void AppendUnitCylinder(Matrix4x4 world, Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, ref int added)
    {
        const int seg = 12;
        for (var s = 0; s < seg && added < maxTriangles; s++)
        {
            var (s0, c0) = MathF.SinCos(s * MathF.Tau / seg);
            var (s1, c1) = MathF.SinCos((s + 1) * MathF.Tau / seg);
            var b0 = Vector3.Transform(new Vector3(c0, -1, s0), world);
            var b1 = Vector3.Transform(new Vector3(c1, -1, s1), world);
            var t0 = Vector3.Transform(new Vector3(c0, 1, s0), world);
            var t1 = Vector3.Transform(new Vector3(c1, 1, s1), world);
            AddIfOverlap(b0, b1, t1, boxMin, boxMax, outTriangles, ref added);
            if (added < maxTriangles) AddIfOverlap(b0, t1, t0, boxMin, boxMax, outTriangles, ref added);
        }
    }

    private static void AppendUnitSphere(Matrix4x4 world, Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, int maxTriangles, ref int added)
    {
        const int rings = 6, sectors = 10;
        for (var r = 0; r < rings && added < maxTriangles; r++)
        {
            var (sy0, cy0) = MathF.SinCos(MathF.PI * (r / (float)rings - 0.5f));
            var (sy1, cy1) = MathF.SinCos(MathF.PI * ((r + 1) / (float)rings - 0.5f));
            for (var s = 0; s < sectors && added < maxTriangles; s++)
            {
                var (sx0, cx0) = MathF.SinCos(s * MathF.Tau / sectors);
                var (sx1, cx1) = MathF.SinCos((s + 1) * MathF.Tau / sectors);
                var p00 = Vector3.Transform(new Vector3(cy0 * cx0, sy0, cy0 * sx0), world);
                var p01 = Vector3.Transform(new Vector3(cy0 * cx1, sy0, cy0 * sx1), world);
                var p10 = Vector3.Transform(new Vector3(cy1 * cx0, sy1, cy1 * sx0), world);
                var p11 = Vector3.Transform(new Vector3(cy1 * cx1, sy1, cy1 * sx1), world);
                AddIfOverlap(p00, p01, p11, boxMin, boxMax, outTriangles, ref added);
                if (added < maxTriangles) AddIfOverlap(p00, p11, p10, boxMin, boxMax, outTriangles, ref added);
            }
        }
    }

    private static void AddIfOverlap(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 boxMin, Vector3 boxMax, List<Vector3> outTriangles, ref int added)
    {
        if (!TriangleOverlapsBox(v0, v1, v2, boxMin, boxMax))
            return;
        outTriangles.Add(v0);
        outTriangles.Add(v1);
        outTriangles.Add(v2);
        added++;
    }

    private static bool TriangleOverlapsBox(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 boxMin, Vector3 boxMax)
    {
        var triMin = Vector3.Min(v0, Vector3.Min(v1, v2));
        var triMax = Vector3.Max(v0, Vector3.Max(v1, v2));
        return Geometry3DHelper.AabbOverlap(triMin, triMax, boxMin, boxMax);
    }

    private static bool IsValidAabb(Vector3 min, Vector3 max)
        => max.X >= min.X && max.Y >= min.Y && max.Z >= min.Z
        && float.IsFinite(min.X) && float.IsFinite(max.X);
}
