using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// A world-space cylinder a ground decal <b>does not paint on</b> - supplied per decal (via
/// <see cref="Scene.SceneNode.ExcludeVolumes(System.Collections.Generic.IReadOnlyList{ExcludeVolume})"/> on a retained
/// node, or <see cref="Im.ImShapeStyle.ExcludeVolumes"/> in immediate mode) so a specific actor (character, monster,
/// NPC) standing in the decal is excluded from it <b>without leaving a hole in the ground</b>: only surfaces standing
/// above the actor's feet inside the radius are skipped, the ground around the feet still gets the decal.
/// </summary>
/// <param name="Position">The actor's world position - its feet. Used as the vertical reference that
/// separates the body (not painted on) from the surrounding ground (painted).</param>
/// <param name="Radius">Horizontal radius of the exclusion, in world units (typically the actor's hitbox radius).</param>
public readonly record struct ExcludeVolume(Vector3 Position, float Radius);
