namespace NoireLib.Draw3D.Enums;

/// <summary>How a ground decal resolves stacked surfaces, such as a tabletop above a floor. Needs <see cref="NoireDraw3D.CollisionHeightMap"/>.</summary>
public enum DecalProjection
{
    /// <summary>Paints every surface the footprint covers (the default).</summary>
    AllSurfaces = 0,

    /// <summary>Paints only the topmost collision surface per column. An object without collision hides nothing.</summary>
    HighestOnly = 1,
}
