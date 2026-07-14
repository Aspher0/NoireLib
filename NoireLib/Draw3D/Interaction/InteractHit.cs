using System.Numerics;
using NoireLib.Draw3D.Scene;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// The context handed to a node's hover / click callbacks: which node, where the cursor ray met it, and the ray
/// itself — enough for "which face did I click," decal-stamp-at-cursor, or spawning a child exactly where clicked.
/// </summary>
/// <param name="Node">The interacted node.</param>
/// <param name="Button">The mouse button (meaningful for clicks; <see cref="MouseButton.Left"/> for hover).</param>
/// <param name="WorldPoint">The world-space point where the cursor ray met the node (bounds hit when no exact triangle).</param>
/// <param name="TriangleIndex">The exact triangle hit for CPU-retained meshes; null for bounds-only hits.</param>
/// <param name="Distance">Ray distance to the hit, in world units.</param>
/// <param name="ScreenPosition">The cursor position in screen pixels.</param>
/// <param name="RayOrigin">The cursor ray origin (world space).</param>
/// <param name="RayDirection">The cursor ray direction (world space, normalized).</param>
public readonly record struct InteractHit(
    SceneNode Node,
    MouseButton Button,
    Vector3 WorldPoint,
    int? TriangleIndex,
    float Distance,
    Vector2 ScreenPosition,
    Vector3 RayOrigin,
    Vector3 RayDirection);
