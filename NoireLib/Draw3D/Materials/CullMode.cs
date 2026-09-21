namespace NoireLib.Draw3D.Materials;

/// <summary>Which triangle faces of a mesh are rasterized.</summary>
public enum CullMode
{
    /// <summary>Culls back faces, with clockwise winding as front (the default).</summary>
    Back = 0,

    /// <summary>Culls front faces.</summary>
    Front = 1,

    /// <summary>Renders both sides.</summary>
    None = 2,
}
