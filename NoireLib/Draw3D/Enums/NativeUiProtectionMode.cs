namespace NoireLib.Draw3D.Enums;

/// <summary>
/// How nameplates layer against Draw3D content. The visible mask is always the UI's own pixels
/// (letter-exact, from the backbuffer's UI-coverage alpha) — these modes only decide, per plate,
/// whether those pixels read on top of your content or get covered by it.
/// Requires <see cref="NoireDraw3D.ProtectGameUi"/> (on by default).
/// </summary>
public enum NativeUiProtectionMode
{
    /// <summary>Nameplates never punch through — the layer covers plate letters everywhere.</summary>
    Off = 0,

    /// <summary>
    /// Depth-aware (default): each plate's world distance is compared against the Draw3D content
    /// covering it. Plate in front → its letters read on top; plate behind your shape → the shape
    /// covers the letters (see <see cref="NoireDraw3D.NativeUiProtectionDimFactor"/>), like real occlusion.
    /// </summary>
    DepthAware = 1,

    /// <summary>Nameplate letters always read on top of the layer regardless of world depth.</summary>
    AlwaysVisible = 2,
}
