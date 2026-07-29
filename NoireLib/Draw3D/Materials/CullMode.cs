namespace NoireLib.Draw3D.Materials;

/// <summary>
/// Which triangle faces of a mesh are rasterized.
/// </summary>
public enum CullMode
{
    /// <summary>Back faces are culled (default; Draw3D meshes use clockwise-front winding).</summary>
    Back = 0,

    /// <summary>Front faces are culled; used internally for decal volume boxes, rarely useful otherwise.</summary>
    Front = 1,

    /// <summary>No culling - both sides render (ribbons, planes meant to be seen from both sides).</summary>
    None = 2,
}
