namespace NoireLib.Draw3D.Enums;

/// <summary>The footprint shape a <see cref="Materials.MaterialDomain.GroundDecal"/> material projects onto the ground.</summary>
public enum DecalShape
{
    /// <summary>Filled circle covering the decal footprint.</summary>
    Circle = 0,

    /// <summary>Ring whose inner radius ratio (0..1) is <see cref="Materials.Material.ShapeParams"/>.X.</summary>
    Ring = 1,

    /// <summary>Pie slice centered on the node's local +Z, with ShapeParams.X the half angle in radians and ShapeParams.Y the inner radius ratio.</summary>
    Sector = 2,

    /// <summary>Filled rectangle covering the decal footprint.</summary>
    Rect = 3,

    /// <summary>The material's texture stamped over the footprint (UV = footprint space).</summary>
    Texture = 4,

    /// <summary>Chevron pointing along the node's local +Z, with ShapeParams.X the half stroke width as a footprint ratio (zero means 0.22).</summary>
    Chevron = 5,
}
