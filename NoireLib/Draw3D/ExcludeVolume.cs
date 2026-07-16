using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// A world-space column marking an actor a ground decal <b>does not paint on</b> - supplied per decal (via
/// <see cref="Scene.SceneNode.ExcludeVolumes(System.Collections.Generic.IReadOnlyList{ExcludeVolume})"/> on a retained
/// node, or <see cref="Im.ImShapeStyle.ExcludeVolumes"/> in immediate mode) so a specific actor (character, monster,
/// NPC) standing in the decal is excluded from it <b>without leaving a hole in the ground</b>.
/// <br/>
/// It is only a <b>coarse horizontal gate</b> - it picks <i>which</i> actors to exclude. The cut itself is the actor's
/// exact game-stencil silhouette (<see cref="NoireDraw3D.CharacterStencilValue"/>), which is why the radius can be
/// generous without ever gouging the ground: a pixel is skipped only where the stencil says "character" <i>and</i> it
/// falls inside one of these radii.
/// </summary>
/// <param name="Position">The actor's world position. Only X and Z are read - the stencil supplies the vertical cut, so
/// the height is irrelevant and is not required to be accurate.</param>
/// <param name="Radius">Horizontal radius of the exclusion, in world units (typically the actor's hitbox radius).</param>
public readonly record struct ExcludeVolume(Vector3 Position, float Radius);
