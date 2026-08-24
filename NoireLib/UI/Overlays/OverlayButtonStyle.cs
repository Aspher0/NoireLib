using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Visual options for a <see cref="NoireOverlayButton"/>. Every <see langword="null"/> value falls back to the
/// corresponding current ImGui style value. Pixel values are written at 100% and scaled at draw time
/// (see <see cref="NoireUI.Scale"/>).
/// </summary>
public sealed class OverlayButtonStyle
{
    /// <summary>
    /// The background color of the button.
    /// </summary>
    public Vector4? Background { get; set; } = null;

    /// <summary>
    /// The background color of the button while hovered.
    /// </summary>
    public Vector4? BackgroundHovered { get; set; } = null;

    /// <summary>
    /// The background color of the button while pressed.
    /// </summary>
    public Vector4? BackgroundActive { get; set; } = null;

    /// <summary>
    /// The text color of the button.
    /// </summary>
    public Vector4? TextColor { get; set; } = null;

    /// <summary>
    /// The icon color of the button, or <see cref="TextColor"/> when <see langword="null"/>.
    /// </summary>
    public Vector4? IconColor { get; set; } = null;

    /// <summary>
    /// The tint applied to the image content of the button.
    /// </summary>
    public Vector4 ImageTint { get; set; } = Vector4.One;

    /// <summary>
    /// The border color of the button.
    /// </summary>
    public Vector4? BorderColor { get; set; } = null;

    /// <summary>
    /// The border thickness of the button, at 100%.
    /// </summary>
    public float BorderSize { get; set; } = 0f;

    /// <summary>
    /// The corner rounding of the button, at 100%.
    /// </summary>
    public float? Rounding { get; set; } = null;

    /// <summary>
    /// The inner padding between the content and the edges of the button, used when no explicit size is set.
    /// </summary>
    public Vector2? Padding { get; set; } = null;

    /// <summary>
    /// The horizontal spacing between the icon, text and image parts of the content, at 100%.
    /// </summary>
    public float ContentSpacing { get; set; } = 4f;

    /// <summary>
    /// The global opacity of the button, from 0 (invisible) to 1 (opaque).
    /// </summary>
    public float Alpha { get; set; } = 1f;

    /// <summary>
    /// The opacity multiplier applied when the button is disabled.
    /// </summary>
    public float DisabledAlpha { get; set; } = 0.5f;

    /// <summary>
    /// The font scale applied to the text and icon content of the button.
    /// </summary>
    public float FontScale { get; set; } = 1f;

    /// <summary>
    /// Replaces the button's own painting entirely (background, border and content), while NoireUI keeps the sizing,
    /// the hit testing, the dragging, the clicks and the tooltip.
    /// </summary>
    public Action<UiOverlayButtonDraw>? CustomDraw { get; set; }

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public OverlayButtonStyle Clone() => (OverlayButtonStyle)MemberwiseClone();

    // The values the button draws with, in real pixels.

    internal float ScaledBorderSize => NoireUI.Scaled(BorderSize);

    internal float ScaledContentSpacing => NoireUI.Scaled(ContentSpacing);

    internal float ResolveRounding()
        => Rounding.HasValue ? NoireUI.Scaled(Rounding.Value) : ImGui.GetStyle().FrameRounding;

    internal Vector2 ResolvePadding()
        => Padding.HasValue ? NoireUI.Scaled(Padding.Value) : ImGui.GetStyle().FramePadding;
}
