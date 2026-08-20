using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Visual and placement options for a custom tooltip drawn with <see cref="NoireTooltip"/>. Every pixel value here is
/// written at 100% and scaled by <see cref="NoireUI.Scale"/> when it is drawn.
/// </summary>
public sealed class TooltipStyle
{
    /// <summary>The background colour of the tooltip, or <see langword="null"/> for the current ImGui popup background.</summary>
    public Vector4? BackgroundColor { get; set; } = null;

    /// <summary>
    /// The background opacity of the tooltip, from 0 to 1. When <see langword="null"/>, the alpha of
    /// <see cref="BackgroundColor"/> or of the current style is used.
    /// </summary>
    public float? BackgroundOpacity { get; set; } = null;

    /// <summary>The default text colour inside the tooltip, or <see langword="null"/> for the current ImGui text colour.</summary>
    public Vector4? TextColor { get; set; } = null;

    /// <summary>The border colour of the tooltip, or <see langword="null"/> for the current ImGui border colour.</summary>
    public Vector4? BorderColor { get; set; } = null;

    /// <summary>The border thickness of the tooltip at 100%, or <see langword="null"/> for the current ImGui window border size.</summary>
    public float? BorderSize { get; set; } = null;

    /// <summary>The corner rounding of the tooltip at 100%, or <see langword="null"/> for the current ImGui window rounding.</summary>
    public float? Rounding { get; set; } = null;

    /// <summary>The inner padding of the tooltip at 100%, or <see langword="null"/> for the current ImGui window padding.</summary>
    public Vector2? Padding { get; set; } = null;

    /// <summary>Where the tooltip is placed.</summary>
    public TooltipPlacement Placement { get; set; } = TooltipPlacement.Mouse;

    /// <summary>The offset from the mouse cursor at 100%, used when <see cref="Placement"/> is <see cref="TooltipPlacement.Mouse"/>.</summary>
    public Vector2 MouseOffset { get; set; } = new(16f, 16f);

    /// <summary>
    /// The gap at 100% between the tooltip and the item under any item-relative <see cref="Placement"/>, applied along
    /// the placement axis so it reads the same whichever side the tooltip lands on.
    /// </summary>
    public float ItemGap { get; set; } = 6f;

    /// <summary>
    /// An additional offset at 100% applied on both axes under any item-relative <see cref="Placement"/>, on top of
    /// <see cref="ItemGap"/>.
    /// </summary>
    public Vector2 ItemOffset { get; set; } = Vector2.Zero;

    /// <summary>Whether the tooltip is kept fully inside the viewport.</summary>
    public bool ClampToViewport { get; set; } = true;

    /// <summary>
    /// Replaces the tooltip's background and border with custom painting, while NoireUI keeps placement, measuring and
    /// the content. The window is begun with no chrome of its own and the hook paints from its draw list, before the
    /// content.
    /// </summary>
    public Action<UiTooltipDraw>? CustomDraw { get; set; }

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public TooltipStyle Clone() => (TooltipStyle)MemberwiseClone();

    // The values the tooltip draws from. Each logical value above is scaled here and nowhere else.

    internal Vector2 ScaledMouseOffset => NoireUI.Scaled(MouseOffset);

    internal float ScaledItemGap => NoireUI.Scaled(ItemGap);

    internal Vector2 ScaledItemOffset => NoireUI.Scaled(ItemOffset);

    internal float? ScaledBorderSize => BorderSize.HasValue ? NoireUI.Scaled(BorderSize.Value) : null;

    internal float? ScaledRounding => Rounding.HasValue ? NoireUI.Scaled(Rounding.Value) : null;

    internal Vector2? ScaledPadding => Padding.HasValue ? NoireUI.Scaled(Padding.Value) : null;
}
