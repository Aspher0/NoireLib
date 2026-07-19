using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Visual options for a <see cref="NoireOverlayButton"/>.<br/>
/// Every <see langword="null"/> value falls back to the corresponding current ImGui style value.<br/>
/// Every pixel value here is written at 100% and scaled when it is drawn. See <see cref="NoireUI.Scale"/>.
/// </summary>
public sealed class OverlayButtonStyle
{
    /// <summary>
    /// The background color of the button. When <see langword="null"/>, the current ImGui button color is used.
    /// </summary>
    public Vector4? Background { get; set; } = null;

    /// <summary>
    /// The background color of the button while hovered. When <see langword="null"/>, the current ImGui hovered button color is used.
    /// </summary>
    public Vector4? BackgroundHovered { get; set; } = null;

    /// <summary>
    /// The background color of the button while pressed. When <see langword="null"/>, the current ImGui active button color is used.
    /// </summary>
    public Vector4? BackgroundActive { get; set; } = null;

    /// <summary>
    /// The text color of the button. When <see langword="null"/>, the current ImGui text color is used.
    /// </summary>
    public Vector4? TextColor { get; set; } = null;

    /// <summary>
    /// The icon color of the button. When <see langword="null"/>, <see cref="TextColor"/> is used.
    /// </summary>
    public Vector4? IconColor { get; set; } = null;

    /// <summary>
    /// The tint applied to the image content of the button. Defaults to white (no tint).
    /// </summary>
    public Vector4 ImageTint { get; set; } = Vector4.One;

    /// <summary>
    /// The border color of the button. When <see langword="null"/>, the current ImGui border color is used.
    /// </summary>
    public Vector4? BorderColor { get; set; } = null;

    /// <summary>
    /// The border thickness of the button, at 100%. Defaults to 0 (no border).
    /// </summary>
    public float BorderSize { get; set; } = 0f;

    /// <summary>
    /// The corner rounding of the button, at 100%. When <see langword="null"/>, the current ImGui frame rounding is used.
    /// </summary>
    public float? Rounding { get; set; } = null;

    /// <summary>
    /// The inner padding between the content and the edges of the button, used when no explicit size is set.<br/>
    /// When <see langword="null"/>, the current ImGui frame padding is used.
    /// </summary>
    public Vector2? Padding { get; set; } = null;

    /// <summary>
    /// The horizontal spacing between the icon, text and image parts of the content, at 100%. Defaults to 4 pixels.
    /// </summary>
    public float ContentSpacing { get; set; } = 4f;

    /// <summary>
    /// The global opacity of the button, from 0 (invisible) to 1 (opaque). Defaults to 1.
    /// </summary>
    public float Alpha { get; set; } = 1f;

    /// <summary>
    /// The opacity multiplier applied when the button is disabled. Defaults to 0.5.
    /// </summary>
    public float DisabledAlpha { get; set; } = 0.5f;

    /// <summary>
    /// The font scale applied to the text and icon content of the button. Defaults to 1.
    /// </summary>
    public float FontScale { get; set; } = 1f;

    // What the button actually draws from. Each logical value above is scaled here and nowhere else.

    internal float ScaledBorderSize => NoireUI.Scaled(BorderSize);

    internal float ScaledContentSpacing => NoireUI.Scaled(ContentSpacing);

    internal float ResolveRounding()
        => Rounding.HasValue ? NoireUI.Scaled(Rounding.Value) : ImGui.GetStyle().FrameRounding;

    internal Vector2 ResolvePadding()
        => Padding.HasValue ? NoireUI.Scaled(Padding.Value) : ImGui.GetStyle().FramePadding;
}
