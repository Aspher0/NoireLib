using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// A world-space cylinder a ground decal skips painting on — supplied per decal via
/// <see cref="Im.ImShapeStyle.ExcludeVolumes"/> so the decal cuts around a specific actor
/// (character, monster, NPC) <b>without leaving a hole in the ground</b>: only surfaces standing
/// above the actor's feet inside the radius are rejected, the ground around the feet keeps the decal.
/// </summary>
/// <param name="Position">The actor's world position — its feet. Used as the vertical reference that
/// separates the body (kept out) from the surrounding ground (kept in).</param>
/// <param name="Radius">Horizontal radius of the exclusion, in world units (typically the actor's hitbox radius).</param>
public readonly record struct ExcludeVolume(Vector3 Position, float Radius);
