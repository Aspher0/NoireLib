namespace NoireLib.Draw3D.Enums;

/// <summary>
/// How a ground decal resolves multiple stacked surfaces inside its projection volume (e.g. a tabletop with the floor
/// visible beneath it). Uses the collision world, so it needs <see cref="NoireDraw3D.WorldOccludedDecals"/> on to take
/// effect (otherwise every decal behaves as <see cref="AllSurfaces"/>).
/// </summary>
public enum DecalProjection
{
    /// <summary>Paint every surface the footprint covers (the default) - floor, tabletop, and the floor under the table all get painted.</summary>
    AllSurfaces = 0,

    /// <summary>
    /// Paint only the <b>topmost</b> surface per column: a surface that sits behind a nearer collision surface along the
    /// view ray (the floor beneath a table) is skipped, so the decal drapes over the highest thing. Relies on the covering
    /// object having collision (tables usually do; a collision-less prop won't hide the floor beneath it).
    /// </summary>
    HighestOnly = 1,
}
