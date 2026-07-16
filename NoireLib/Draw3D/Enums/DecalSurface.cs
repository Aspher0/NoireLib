namespace NoireLib.Draw3D.Enums;

/// <summary>
/// Locks a <see cref="Materials.MaterialDomain.GroundDecal"/> to a surface by constraining how its box may be oriented.
/// The decal always projects its shape along the box's local Y (the projection is a single, orientation-driven rule);
/// this simply forbids rotating the box out of the plane that keeps it on the intended surface - so a ground decal can
/// never be tipped onto a wall and a wall decal can never be laid on the floor. The box's heading (yaw), scale and
/// position are preserved; only the disallowed pitch/roll is dropped.
/// </summary>
public enum DecalSurface
{
    /// <summary>
    /// A ground decal: the box is kept horizontal (footprint flat, projecting straight down onto the floor/terrain).
    /// Rotating it toward vertical has no effect. The default and the classic behavior.
    /// </summary>
    Ground = 0,

    /// <summary>
    /// A wall decal: the box is kept vertical (footprint upright, projecting horizontally into the wall it faces - aim it
    /// with yaw). Rotating it toward flat has no effect.
    /// </summary>
    Wall = 1,

    /// <summary>
    /// Unconstrained: the box may be rotated freely, and whichever way it is oriented decides the surface it projects onto
    /// (upright = ground, tipped 90° = wall, anything between = a hybrid).
    /// </summary>
    Both = 2,
}
