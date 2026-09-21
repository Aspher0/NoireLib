namespace NoireLib.Draw3D.Materials;

/// <summary>Whether a material's pixels are occluded by the game's world geometry.</summary>
public enum DepthMode
{
    /// <summary>Pixels behind world geometry are hidden (or faded, see <see cref="Material.DepthFade"/>). The default.</summary>
    TestOnly = 0,

    /// <summary>Pixels ignore world geometry entirely and draw on top of it (x-ray).</summary>
    Ignore = 1,

    /// <summary>Occluded by the game world like <see cref="TestOnly"/> but drawn over other Draw3D objects. Transparent bucket only.</summary>
    WorldOnly = 2,
}
