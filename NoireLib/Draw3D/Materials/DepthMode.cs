namespace NoireLib.Draw3D.Materials;

/// <summary>
/// Whether a material's pixels are occluded by the game's world geometry.<br/>
/// This governs the world-depth test; for the transparent bucket it also decides the Draw3D-vs-Draw3D test (see
/// <see cref="WorldOnly"/>), which is otherwise a bucket property handled automatically.
/// </summary>
public enum DepthMode
{
    /// <summary>Pixels behind world geometry are hidden (or faded, see <see cref="Material.DepthFade"/>); the default.</summary>
    TestOnly = 0,

    /// <summary>Pixels ignore world geometry entirely and draw on top of it (x-ray).</summary>
    Ignore = 1,

    /// <summary>
    /// Occluded by the game world like <see cref="TestOnly"/>, but drawn on top of other Draw3D objects since the
    /// transparent-bucket depth test is skipped; only meaningful in the transparent bucket.
    /// </summary>
    WorldOnly = 2,
}
