namespace NoireLib.Draw3D.Materials;

/// <summary>
/// Whether a material's pixels are occluded by the game's world geometry.<br/>
/// This governs the world-depth test only; Draw3D-vs-Draw3D depth is a bucket property handled automatically.
/// </summary>
public enum DepthMode
{
    /// <summary>Pixels behind world geometry are hidden (or faded, see <see cref="Material.DepthFade"/>). The default.</summary>
    TestOnly = 0,

    /// <summary>Pixels ignore world geometry entirely and draw on top of it (x-ray).</summary>
    Ignore = 1,
}
