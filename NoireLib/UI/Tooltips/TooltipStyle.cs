using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Visual and placement options for a custom tooltip drawn with <see cref="NoireTooltip"/>.<br/>
/// Every pixel value here is written at 100% and scaled when it is drawn. See <see cref="NoireUI.Scale"/>.
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
    /// The border thickness of the tooltip, at 100%. When <see langword="null"/>, the current ImGui window border size is used.
    /// </summary>
    public float? BorderSize { get; set; } = null;

    /// <summary>
    /// The corner rounding of the tooltip, at 100%. When <see langword="null"/>, the current ImGui window rounding is used.
    /// </summary>
    public float? Rounding { get; set; } = null;

    /// <summary>
    /// The inner padding of the tooltip, at 100%. When <see langword="null"/>, the current ImGui window padding is used.
    /// </summary>
    public Vector2? Padding { get; set; } = null;

    /// <summary>
    /// Where the tooltip is placed. See <see cref="TooltipPlacement"/>.
    /// </summary>
    public TooltipPlacement Placement { get; set; } = TooltipPlacement.Mouse;

    /// <summary>
    /// The offset from the mouse cursor at 100%, when <see cref="Placement"/> is <see cref="TooltipPlacement.Mouse"/>.<br/>
    /// See <see cref="ItemOffset"/> for the item-relative placements.
    /// </summary>
    public Vector2 MouseOffset { get; set; } = new(16f, 16f);

    /// <summary>
    /// The gap at 100% between the tooltip and the item, when using an item-relative <see cref="Placement"/>
    /// (every placement except <see cref="TooltipPlacement.Mouse"/>).<br/>
    /// This pushes the tooltip away from the item along the placement axis, so it grows the same way whichever side the
    /// tooltip is on. See <see cref="ItemOffset"/> to shift it freely instead.
    /// </summary>
    public float ItemGap { get; set; } = 6f;

    /// <summary>
    /// An additional offset at 100% applied when using an item-relative <see cref="Placement"/>
    /// (every placement except <see cref="TooltipPlacement.Mouse"/>), on top of <see cref="ItemGap"/>.<br/>
    /// Where <see cref="ItemGap"/> only moves the tooltip along the placement axis, this shifts it on both axes: use it to
    /// nudge a tooltip placed above an item to the right, for example. Defaults to no offset.
    /// </summary>
    public Vector2 ItemOffset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Whether the tooltip should be kept fully inside the viewport. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ClampToViewport { get; set; } = true;

    // What the tooltip actually draws from. Each logical value above is scaled here and nowhere else.

    internal Vector2 ScaledMouseOffset => NoireUI.Scaled(MouseOffset);

    internal float ScaledItemGap => NoireUI.Scaled(ItemGap);

    internal Vector2 ScaledItemOffset => NoireUI.Scaled(ItemOffset);

    internal float? ScaledBorderSize => BorderSize.HasValue ? NoireUI.Scaled(BorderSize.Value) : null;

    internal float? ScaledRounding => Rounding.HasValue ? NoireUI.Scaled(Rounding.Value) : null;

    internal Vector2? ScaledPadding => Padding.HasValue ? NoireUI.Scaled(Padding.Value) : null;
}
