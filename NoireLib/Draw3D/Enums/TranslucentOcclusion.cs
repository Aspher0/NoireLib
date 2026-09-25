namespace NoireLib.Draw3D.Enums;

/// <summary>Whether surfaces the game draws after its opaque pass, water above all, hide Draw3D content behind them.</summary>
public enum TranslucentOcclusion
{
    /// <summary>Only opaque world geometry occludes: content under water stays visible from above. The default.</summary>
    SeeThrough = 0,

    /// <summary>Water and every other depth-writing surface drawn after the opaque pass occlude like solid geometry.</summary>
    Occlude = 1,
}
