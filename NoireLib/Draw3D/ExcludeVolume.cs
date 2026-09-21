using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// A world-space column selecting an actor a ground decal does not paint on, cut exactly along the actor's
/// <see cref="NoireDraw3D.CharacterStencilValue"/> silhouette so the ground under it keeps its paint.
/// </summary>
/// <param name="Position">The actor's world position, of which only X and Z are read.</param>
/// <param name="Radius">The horizontal radius of the column in world units, typically the actor's hitbox radius.</param>
public readonly record struct ExcludeVolume(Vector3 Position, float Radius);
