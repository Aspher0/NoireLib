using Dalamud.Interface;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The look of a button drawn with <see cref="NoireButtons"/>. Every value left <see langword="null"/> resolves
/// through <see cref="Tone"/> and <see cref="NoireTheme.Current"/>; setting one overrides only that value.
/// </summary>
/// <example>
/// <code>
/// NoireButtons.Button("Save", ButtonTone.Accent);
/// NoireButtons.Button("Save", new ButtonStyle { Tone = ButtonTone.Accent, Rounding = 0f, Icon = FontAwesomeIcon.Save });
/// </code>
/// </example>
public sealed class ButtonStyle
{
    /// <summary>
    /// What the button means, which decides its colors when they are not set explicitly.
    /// </summary>
    public ButtonTone Tone { get; set; } = ButtonTone.Neutral;

    /// <summary>The fill color. When <see langword="null"/>, it comes from <see cref="Tone"/>.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The hovered fill color. When <see langword="null"/>, it is derived from the fill by the theme.</summary>
    public Vector4? HoveredColor { get; set; }

    /// <summary>The held fill color. When <see langword="null"/>, it is derived from the fill by the theme.</summary>
    public Vector4? ActiveColor { get; set; }

    /// <summary>The label color. When <see langword="null"/>, a color legible on the fill is chosen.</summary>
    public Vector4? TextColor { get; set; }

    /// <summary>The border color. When <see langword="null"/>, the theme border color is used.</summary>
    public Vector4? BorderColor { get; set; }

    /// <summary>The border thickness at 100%. When <see langword="null"/>, the theme border size is used.</summary>
    public float? BorderSize { get; set; }

    /// <summary>The corner radius at 100%. When <see langword="null"/>, the theme rounding is used.</summary>
    public float? Rounding { get; set; }

    /// <summary>The padding between the label and the button edge, at 100%. When <see langword="null"/>, the theme frame padding is used.</summary>
    public Vector2? Padding { get; set; }

    /// <summary>An icon drawn before the label.</summary>
    public FontAwesomeIcon? Icon { get; set; }

    /// <summary>The icon color. When <see langword="null"/>, the label color is used.</summary>
    public Vector4? IconColor { get; set; }

    /// <summary>Whether the label is centred in the button. Defaults to <see langword="true"/>.</summary>
    public bool CenterLabel { get; set; } = true;

    /// <summary>
    /// How a hold-to-confirm button shows its progress. See <see cref="HoldFillMode"/>.
    /// </summary>
    public HoldFillMode HoldFill { get; set; } = HoldFillMode.LeftToRight;

    /// <summary>
    /// The colour a hold-to-confirm button fills with. When <see langword="null"/>, a markedly brighter form of the
    /// button's own colour is used.
    /// </summary>
    public Vector4? HoldFillColor { get; set; }

    /// <summary>
    /// The thickness at 100% of the traced outline when <see cref="HoldFill"/> is <see cref="HoldFillMode.Border"/>.
    /// </summary>
    public float HoldBorderThickness { get; set; } = 2.5f;

    /// <summary>
    /// Replaces the button's own painting entirely, while NoireUI keeps doing the sizing, the hit testing and the state
    /// tracking.
    /// </summary>
    /// <remarks>
    /// The label is not drawn for you when this is set. Draw it yourself from the arguments, or leave it out.
    /// </remarks>
    public Action<UiButtonDraw>? CustomDraw { get; set; }

    // What the painter draws from: each logical value above is scaled here, and only here.

    internal float ResolveBorderSize()
        => BorderSize.HasValue ? NoireUI.Scaled(BorderSize.Value) : NoireTheme.Current.ResolveBorderSize();

    internal float ResolveRounding()
        => Rounding.HasValue ? NoireUI.Scaled(Rounding.Value) : NoireTheme.Current.ResolveRounding();

    internal Vector2 ResolvePadding()
        => Padding.HasValue ? NoireUI.Scaled(Padding.Value) : NoireTheme.Current.ResolveFramePadding();

    internal float ScaledHoldBorderThickness => NoireUI.Scaled(HoldBorderThickness);

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public ButtonStyle Clone() => new()
    {
        Tone = Tone,
        Color = Color,
        HoveredColor = HoveredColor,
        ActiveColor = ActiveColor,
        TextColor = TextColor,
        BorderColor = BorderColor,
        BorderSize = BorderSize,
        Rounding = Rounding,
        Padding = Padding,
        Icon = Icon,
        IconColor = IconColor,
        CenterLabel = CenterLabel,
        HoldFill = HoldFill,
        HoldFillColor = HoldFillColor,
        HoldBorderThickness = HoldBorderThickness,
        CustomDraw = CustomDraw,
    };

    /// <summary>
    /// Copies every field of <paramref name="source"/> into this style, leaving no reference to it.
    /// </summary>
    /// <remarks>
    /// Copies into a reused style instead of allocating via <see cref="Clone"/>, for a per-item variant drawn every
    /// frame. Keep this field list identical to <see cref="Clone"/>'s.
    /// </remarks>
    /// <param name="source">The style to copy from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    internal void CopyFrom(ButtonStyle source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Tone = source.Tone;
        Color = source.Color;
        HoveredColor = source.HoveredColor;
        ActiveColor = source.ActiveColor;
        TextColor = source.TextColor;
        BorderColor = source.BorderColor;
        BorderSize = source.BorderSize;
        Rounding = source.Rounding;
        Padding = source.Padding;
        Icon = source.Icon;
        IconColor = source.IconColor;
        CenterLabel = source.CenterLabel;
        HoldFill = source.HoldFill;
        HoldFillColor = source.HoldFillColor;
        HoldBorderThickness = source.HoldBorderThickness;
        CustomDraw = source.CustomDraw;
    }
}
