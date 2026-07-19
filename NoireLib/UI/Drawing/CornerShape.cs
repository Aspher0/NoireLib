namespace NoireLib.UI;

/// <summary>
/// How the corners of a rectangle are cut.
/// </summary>
public enum CornerShape
{
    /// <summary>A right angle, left as it is.</summary>
    Square,

    /// <summary>An arc, the ordinary rounded corner.</summary>
    Rounded,

    /// <summary>A straight cut across the corner at forty-five degrees, the chamfer art deco is built on.</summary>
    Notched,
}
