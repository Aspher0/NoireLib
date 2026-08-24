using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The shape a slider's handle is drawn as.
/// </summary>
public enum SliderGrab
{
    /// <summary>A plain rectangle.</summary>
    Square,

    /// <summary>A rectangle with rounded corners.</summary>
    Rounded,

    /// <summary>A circle.</summary>
    Circle,

    /// <summary>A square stood on its corner.</summary>
    Diamond,
}

/// <summary>
/// How a <see cref="NoireSliders"/> slider looks. Every value is optional and falls back to the theme.
/// </summary>
public sealed class SliderStyle
{
    /// <summary>How thick the track is, at 100%.</summary>
    public float TrackThickness { get; set; } = 3f;

    /// <summary>The colour of the unfilled track; the theme's sunken surface when <see langword="null"/>.</summary>
    public Vector4? TrackColor { get; set; }

    /// <summary>The colour the filled part starts at; the theme's accent when <see langword="null"/>.</summary>
    public Vector4? FillColor { get; set; }

    /// <summary>
    /// The colour the filled part ends at; <see langword="null"/> for a flat fill.
    /// </summary>
    public Vector4? FillTo { get; set; }

    /// <summary>The shape of the handle.</summary>
    public SliderGrab Grab { get; set; } = SliderGrab.Rounded;

    /// <summary>How large the handle is across, at 100%.</summary>
    public float GrabSize { get; set; } = 13f;

    /// <summary>The handle's colour; the theme's accent when <see langword="null"/>.</summary>
    public Vector4? GrabColor { get; set; }

    /// <summary>The colour the handle ramps to, top to bottom; flat when <see langword="null"/>.</summary>
    public Vector4? GrabColorTo { get; set; }

    /// <summary>The colour of the glow behind the handle; no glow when <see langword="null"/>.</summary>
    public Vector4? GlowColor { get; set; }

    /// <summary>How far the glow reaches past the handle, at 100%.</summary>
    public float GlowSpread { get; set; } = 6f;

    /// <summary>Whether the value is written beside the track.</summary>
    public bool ShowValue { get; set; } = true;

    /// <summary>
    /// How the value is written, as a .NET numeric format string; <see langword="null"/> lets the slider choose.
    /// </summary>
    public string? ValueFormat { get; set; }

    /// <summary>The column the value is written in, at 100%.</summary>
    public float ValueWidth { get; set; } = 44f;

    /// <summary>The value's colour; the theme's muted text when <see langword="null"/>.</summary>
    public Vector4? ValueColor { get; set; }

    /// <summary>
    /// Writes the value as words rather than as a number, overriding <see cref="ValueFormat"/>; when
    /// <see langword="null"/>, the number is written.
    /// </summary>
    public Func<float, string>? ValueText { get; set; }

    /// <summary>The row label's colour; the theme's ordinary text when <see langword="null"/>.</summary>
    public Vector4? LabelColor { get; set; }

    /// <summary>
    /// How wide the label column is, at 100%; <see cref="NoireInputs.LabelWidth"/> when <see langword="null"/>.
    /// </summary>
    public float? LabelWidth { get; set; }

    /// <summary>
    /// Paints the slider instead of the shipped drawing.
    /// </summary>
    public Action<UiSliderDraw>? CustomDraw { get; set; }

    /// <summary>Returns a copy.</summary>
    /// <returns>A shallow copy.</returns>
    public SliderStyle Clone() => (SliderStyle)MemberwiseClone();
}
