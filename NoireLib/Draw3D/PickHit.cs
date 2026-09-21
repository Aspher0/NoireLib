using NoireLib.Draw3D.Scene;

namespace NoireLib.Draw3D;

/// <summary>One result of <see cref="NoireDraw3D.Pick"/>: a scene node whose bounds or triangles the pick ray hit.</summary>
/// <param name="Node">The hit node.</param>
/// <param name="Distance">The ray distance to the hit, in world units.</param>
/// <param name="TriangleIndex">The exact triangle hit, when the mesh kept CPU data. Null for bounds-only hits.</param>
public readonly record struct PickHit(SceneNode Node, float Distance, int? TriangleIndex);
