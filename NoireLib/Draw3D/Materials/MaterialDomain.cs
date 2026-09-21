namespace NoireLib.Draw3D.Materials;

/// <summary>Selects which shader family a <see cref="Material"/> renders with.</summary>
public enum MaterialDomain
{
    /// <summary>Flat, unshaded color or texture (the default).</summary>
    Unlit = 0,

    /// <summary>Half-Lambert stylized shading driven by <see cref="NoireDraw3D.Lighting"/>.</summary>
    Lit = 1,

    /// <summary>Unit-box volume that projects its shape onto world geometry.</summary>
    GroundDecal = 2,
}
