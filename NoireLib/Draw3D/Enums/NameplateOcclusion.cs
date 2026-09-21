namespace NoireLib.Draw3D.Enums;

/// <summary>How the game's own nameplates layer against Draw3D content, cut at the plates' own letter pixels.</summary>
public enum NameplateOcclusion
{
    /// <summary>Covers a plate standing behind Draw3D content and keeps a plate in front readable (the default).</summary>
    DepthAware = 0,

    /// <summary>Keeps nameplate letters on top of the layer regardless of world depth.</summary>
    AlwaysVisible = 1,

    /// <summary>Covers plate letters at any distance. Needs <see cref="Draw3DLayering.OverEverything"/>.</summary>
    Covered = 2,
}
