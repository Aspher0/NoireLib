namespace NoireLib.Draw3D.Enums;

/// <summary>
/// How the game's own nameplates layer against Draw3D content. Always letter-exact: under the game UI the game
/// itself draws the plates against depth Draw3D stamped, and over everything the visible boundary is the UI's own
/// pixels (the plate rects only decide <i>where</i> that per-pixel mask applies, and are never drawn).
/// <br/>
/// Both layering modes honour this, by different mechanisms - see each value for what it costs where.
/// </summary>
public enum NameplateOcclusion
{
    /// <summary>
    /// Default. A plate standing behind your 3D content is covered by it; a plate in front stays readable.
    /// <br/>
    /// Under the game UI this is the game's own depth test, against depth Draw3D stamps before the plate pass.
    /// Over everything it compares each plate's world distance against the content covering it, then lets the
    /// per-pixel UI mask do the actual cutting, so the letters keep their own shape.
    /// </summary>
    DepthAware = 0,

    /// <summary>
    /// Nameplate letters always read on top of the layer regardless of world depth. Under the game UI, Draw3D
    /// simply stamps no depth for the plate pass to test against.
    /// </summary>
    AlwaysVisible = 1,

    /// <summary>
    /// The layer covers plate letters everywhere, at any distance.
    /// <br/>
    /// <b>Requires <see cref="Draw3DLayering.OverEverything"/>.</b> Under the game UI the plates are drawn after the
    /// layer by the game itself, so there is no way to paint over them; this behaves as
    /// <see cref="AlwaysVisible"/> there.
    /// </summary>
    Covered = 2,
}
