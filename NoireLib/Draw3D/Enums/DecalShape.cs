namespace NoireLib.Draw3D.Enums;

/// <summary>
/// The footprint shape a <see cref="Materials.MaterialDomain.GroundDecal"/> material projects onto the ground.
/// </summary>
public enum DecalShape
{
    /// <summary>Filled circle covering the decal footprint.</summary>
    Circle = 0,

    /// <summary>Ring (donut). <see cref="Materials.Material.ShapeParams"/>.X = inner radius as a ratio of the outer (0..1).</summary>
    Ring = 1,

    /// <summary>Pie slice centered on the node's local +Z. ShapeParams.X = half angle in radians, ShapeParams.Y = inner radius ratio.</summary>
    Sector = 2,

    /// <summary>Filled rectangle covering the decal footprint (scale the node for lines/walls).</summary>
    Rect = 3,

    /// <summary>The material's texture stamped over the footprint (UV = footprint space).</summary>
    Texture = 4,
}
