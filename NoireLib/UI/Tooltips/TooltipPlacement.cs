namespace NoireLib.UI;

/// <summary>
/// Where a custom tooltip should be placed on screen.
/// </summary>
public enum TooltipPlacement
{
    /// <summary>
    /// The tooltip follows the mouse cursor, like a regular ImGui tooltip.
    /// </summary>
    Mouse,

    /// <summary>
    /// The tooltip is centered above the last drawn ImGui item.
    /// </summary>
    AboveItem,

    /// <summary>
    /// The tooltip is centered below the last drawn ImGui item.
    /// </summary>
    BelowItem,

    /// <summary>
    /// The tooltip is placed to the left of the last drawn ImGui item, vertically centered.
    /// </summary>
    LeftOfItem,

    /// <summary>
    /// The tooltip is placed to the right of the last drawn ImGui item, vertically centered.
    /// </summary>
    RightOfItem,
}
