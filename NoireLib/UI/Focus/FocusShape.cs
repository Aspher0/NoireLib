namespace NoireLib.UI;

/// <summary>
/// The mark drawn around the control holding keyboard focus.
/// </summary>
public enum FocusShape
{
    /// <summary>
    /// A hairline outline following the whole edge.
    /// </summary>
    Ring,

    /// <summary>
    /// A short elbow inside each corner.
    /// </summary>
    Corners,

    /// <summary>
    /// A matched pair of square brackets, <c>[</c> and <c>]</c>, one at each side.
    /// </summary>
    Brackets,

    /// <summary>
    /// A bar along the bottom edge alone.
    /// </summary>
    Underline,

    /// <summary>
    /// Nothing at all, for a single widget opting out while the rest of the interface keeps its mark.
    /// </summary>
    None,
}
