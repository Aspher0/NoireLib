namespace NoireLib.UI;

/// <summary>
/// Where a <see cref="NoireRibbonField"/> keeps the smoothed pointer the ribbons lean toward.
/// </summary>
public enum RibbonLeanSpace
{
    /// <summary>
    /// As a fraction of the field, compared against each sample's position along the sampled span. Moving the field
    /// moves the lean with it.
    /// </summary>
    Field,

    /// <summary>
    /// In screen pixels, compared against each sample's distance over the field width. Moving the field makes the lean
    /// catch up with the pointer smoothly.
    /// </summary>
    Screen,
}
