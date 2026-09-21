namespace NoireLib.Draw3D.Enums;

/// <summary>Locks a <see cref="Materials.MaterialDomain.GroundDecal"/> to a surface. The decal projects along its box's local Y. Only pitch and roll are dropped.</summary>
public enum DecalSurface
{
    /// <summary>Keeps the box horizontal so the decal projects straight down onto the ground (the default).</summary>
    Ground = 0,

    /// <summary>Keeps the box vertical so the decal projects horizontally into the wall it faces.</summary>
    Wall = 1,

    /// <summary>Leaves the box free to rotate. Its orientation alone decides the surface it projects onto.</summary>
    Both = 2,
}
