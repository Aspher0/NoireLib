using NoireLib.Draw3D.Scene;

namespace NoireLib.Draw3D;

/// <summary>
/// One result of <see cref="NoireDraw3D.Pick"/>: a scene node whose bounds (and optionally triangles)
/// the pick ray hit, ordered nearest-first.
/// </summary>
/// <param name="Node">The hit node.</param>
/// <param name="Distance">Ray distance to the hit, in world units.</param>
/// <param name="TriangleIndex">The exact triangle hit, when the mesh kept CPU data; null for bounds-only hits.</param>
public readonly record struct PickHit(SceneNode Node, float Distance, int? TriangleIndex);
