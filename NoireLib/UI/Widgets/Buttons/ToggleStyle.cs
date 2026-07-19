using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The look of an animated toggle drawn with <see cref="NoireButtons.Toggle(string, ref bool, ToggleStyle)"/>.<br/>
/// Every value left <see langword="null"/> resolves through <see cref="NoireTheme.Current"/>, so a toggle matches the
/// rest of the interface without being told anything.
/// </summary>
public sealed class ToggleStyle
{
    /// <summary>The track color when the toggle is on. When <see langword="null"/>, the theme accent is used.</summary>
    public Vector4? OnColor { get; set; }

    /// <summary>The track color when the toggle is off. When <see langword="null"/>, the sunken surface color is used.</summary>
    public Vector4? OffColor { get; set; }

    /// <summary>The knob color. When <see langword="null"/>, a color legible on the track is chosen.</summary>
    public Vector4? KnobColor { get; set; }

    /// <summary>The track border color. When <see langword="null"/>, the theme border color is used.</summary>
    public Vector4? BorderColor { get; set; }

    /// <summary>The track border thickness at 100%. When <see langword="null"/>, the theme border size is used.</summary>
    public float? BorderSize { get; set; }

    /// <summary>The track height at 100%. When <see langword="null"/>, it matches the current line height.</summary>
    public float? Height { get; set; }

    /// <summary>
    /// The track width as a multiple of its height. Defaults to a shape wide enough for the knob to visibly travel.
    /// </summary>
    public float WidthRatio { get; set; } = 1.85f;

    /// <summary>
    /// The track corner radius at 100%. When <see langword="null"/>, the track is a full pill. Set it to 0 for a square
    /// switch.
    /// </summary>
    public float? Rounding { get; set; }

    /// <summary>
    /// How long the knob takes to travel, in seconds. Ignored under <see cref="NoireUI.ReducedMotion"/>, where the knob
    /// simply appears at its destination.
    /// </summary>
    public float AnimationDuration { get; set; } = 0.14f;

    /// <summary>
    /// Whether the label is drawn before the switch rather than after it.
    /// </summary>
    public bool LabelFirst { get; set; }

    /// <summary>
    /// Replaces the toggle's own painting entirely, while NoireUI keeps doing the sizing, the hit testing, the state
    /// and the animation.
    /// </summary>
    public Action<UiToggleDraw>? CustomDraw { get; set; }

    // What the painter actually draws from. Each logical value above is scaled here and nowhere else.

    internal float ResolveHeight()
        => Height.HasValue ? NoireUI.Scaled(Height.Value) : ImGui.GetFrameHeight();

    internal float ResolveBorderSize()
        => BorderSize.HasValue ? NoireUI.Scaled(BorderSize.Value) : NoireTheme.Current.ResolveBorderSize();

    /// <summary>
    /// The track corner radius, defaulting to a full pill at whatever height the track resolved to.
    /// </summary>
    internal float ResolveRounding(float trackHeight)
        => Rounding.HasValue ? NoireUI.Scaled(Rounding.Value) : trackHeight * 0.5f;

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public ToggleStyle Clone() => new()
    {
        OnColor = OnColor,
        OffColor = OffColor,
        KnobColor = KnobColor,
        BorderColor = BorderColor,
        BorderSize = BorderSize,
        Height = Height,
        WidthRatio = WidthRatio,
        Rounding = Rounding,
        AnimationDuration = AnimationDuration,
        LabelFirst = LabelFirst,
        CustomDraw = CustomDraw,
    };
}

/// <summary>
/// Everything a <see cref="ToggleStyle.CustomDraw"/> hook needs to paint a toggle itself.
/// </summary>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left corner of the track.</param>
/// <param name="Max">The bottom right corner of the track.</param>
/// <param name="On">Whether the toggle is currently on.</param>
/// <param name="Travel">How far the knob has travelled, from 0 (off) to 1 (on). Animated, so it is fractional mid-flight.</param>
/// <param name="Hovered">Whether the mouse is over the toggle.</param>
/// <param name="TrackColor">The track color for the current state, already blended between the on and off colors.</param>
/// <param name="KnobCenter">Where the knob sits this frame.</param>
/// <param name="KnobRadius">The knob radius in pixels.</param>
/// <param name="KnobColor">The knob color.</param>
public readonly record struct UiToggleDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    bool On,
    float Travel,
    bool Hovered,
    Vector4 TrackColor,
    Vector2 KnobCenter,
    float KnobRadius,
    Vector4 KnobColor)
{
    /// <summary>The size of the track in pixels.</summary>
    public Vector2 Size => Max - Min;
}
