using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Visual and placement options for a custom tooltip drawn with <see cref="NoireTooltip"/>.
/// </summary>
public sealed class TooltipStyle
{
    /// <summary>
    /// The background color of the tooltip. When <see langword="null"/>, the current ImGui popup background color is used.
    /// </summary>
    public Vector4? BackgroundColor { get; set; } = null;

    /// <summary>
    /// The background opacity of the tooltip, from 0 (fully transparent) to 1 (fully opaque).<br/>
    /// When <see langword="null"/>, the alpha of <see cref="BackgroundColor"/> (or of the current style) is used.
    /// </summary>
    public float? BackgroundOpacity { get; set; } = null;

    /// <summary>
    /// The default text color inside the tooltip. When <see langword="null"/>, the current ImGui text color is used.
    /// </summary>
    public Vector4? TextColor { get; set; } = null;

    /// <summary>
    /// The border color of the tooltip. When <see langword="null"/>, the current ImGui border color is used.
    /// </summary>
    public Vector4? BorderColor { get; set; } = null;

    /// <summary>
    /// The border thickness of the tooltip. When <see langword="null"/>, the current ImGui window border size is used.
    /// </summary>
    public float? BorderSize { get; set; } = null;

    /// <summary>
    /// The corner rounding of the tooltip. When <see langword="null"/>, the current ImGui window rounding is used.
    /// </summary>
    public float? Rounding { get; set; } = null;

    /// <summary>
    /// The inner padding of the tooltip. When <see langword="null"/>, the current ImGui window padding is used.
    /// </summary>
    public Vector2? Padding { get; set; } = null;

    /// <summary>
    /// Where the tooltip is placed. See <see cref="TooltipPlacement"/>.
    /// </summary>
    public TooltipPlacement Placement { get; set; } = TooltipPlacement.Mouse;

    /// <summary>
    /// The offset from the mouse cursor when <see cref="Placement"/> is <see cref="TooltipPlacement.Mouse"/>.
    /// </summary>
    public Vector2 MouseOffset { get; set; } = new(16f, 16f);

    /// <summary>
    /// The gap in pixels between the tooltip and the item when using an item-relative <see cref="Placement"/>.
    /// </summary>
    public float ItemGap { get; set; } = 6f;

    /// <summary>
    /// Whether the tooltip should be kept fully inside the viewport. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ClampToViewport { get; set; } = true;
}
