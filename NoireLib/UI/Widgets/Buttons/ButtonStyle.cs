using Dalamud.Interface;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>The look of a button drawn with <see cref="NoireButtons"/>. A <see langword="null"/> value resolves through <see cref="Tone"/> and the theme.</summary>
public sealed class ButtonStyle
{
    /// <summary>
    /// What the button means, deciding its colors when they are not set explicitly.
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

    /// <summary>A FontAwesome icon drawn before the label. Setting it clears <see cref="NoireIcon"/> and <see cref="IconName"/>.</summary>
    public FontAwesomeIcon? Icon
    {
        get => icon;
        set
        {
            icon = value;

            if (value.HasValue)
            {
                noireIcon = null;
                iconName = null;
            }
        }
    }

    /// <summary>A built-in textured mark drawn before the label. Setting it clears <see cref="Icon"/> and <see cref="IconName"/>.</summary>
    public NoireIcon? NoireIcon
    {
        get => noireIcon;
        set
        {
            noireIcon = value;

            if (value.HasValue)
            {
                icon = null;
                iconName = null;
            }
        }
    }

    /// <summary>
    /// Artwork registered with <see cref="NoireIcons.Register"/>, drawn before the label. Setting it clears
    /// <see cref="Icon"/> and <see cref="NoireIcon"/>.
    /// </summary>
    public string? IconName
    {
        get => iconName;
        set
        {
            iconName = value;

            if (value != null)
            {
                icon = null;
                noireIcon = null;
            }
        }
    }

    /// <summary>The side of a textured icon at 100%. When <see langword="null"/>, it matches the label's line height.</summary>
    public float? IconSize { get; set; }

    private FontAwesomeIcon? icon;
    private NoireIcon? noireIcon;
    private string? iconName;

    internal bool HasTexturedIcon => noireIcon.HasValue || iconName != null;

    internal bool HasAnyIcon => icon.HasValue || HasTexturedIcon;

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
    /// Replaces the button's painting. NoireUI still does the sizing, hit testing and state.<br/>
    /// The label is not drawn when this is set.
    /// </summary>
    public Action<UiButtonDraw>? CustomDraw { get; set; }

    // Scaled here, and only here.

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
        NoireIcon = NoireIcon,
        IconName = IconName,
        IconSize = IconSize,
        IconColor = IconColor,
        CenterLabel = CenterLabel,
        HoldFill = HoldFill,
        HoldFillColor = HoldFillColor,
        HoldBorderThickness = HoldBorderThickness,
        CustomDraw = CustomDraw,
    };

    // Keep this field list identical to Clone's.
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
        NoireIcon = source.NoireIcon;
        IconName = source.IconName;
        IconSize = source.IconSize;
        IconColor = source.IconColor;
        CenterLabel = source.CenterLabel;
        HoldFill = source.HoldFill;
        HoldFillColor = source.HoldFillColor;
        HoldBorderThickness = source.HoldBorderThickness;
        CustomDraw = source.CustomDraw;
    }
}
