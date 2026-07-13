namespace NoireLib.Draw3D.Materials;

/// <summary>
/// Selects which shader family a <see cref="Material"/> renders with.
/// </summary>
public enum MaterialDomain
{
    /// <summary>Flat, unshaded color/texture. The default for markers and shapes.</summary>
    Unlit = 0,

    /// <summary>Half-Lambert stylized shading driven by <see cref="NoireDraw3D.Lighting"/>.</summary>
    Lit = 1,

    /// <summary>Terrain-hugging projected decal (telegraphs). Renders as a unit-box volume that projects its shape onto world geometry.</summary>
    GroundDecal = 2,
}
