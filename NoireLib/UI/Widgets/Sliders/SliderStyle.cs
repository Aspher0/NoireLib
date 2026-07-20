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
    /// <summary>How thick the track is, at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public float TrackThickness { get; set; } = 3f;

    /// <summary>The colour of the unfilled track. When <see langword="null"/>, the theme's sunken surface.</summary>
    public Vector4? TrackColor { get; set; }

    /// <summary>The colour the filled part starts at. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? FillColor { get; set; }

    /// <summary>
    /// The colour the filled part ends at, for a fill that ramps along its length.
    /// </summary>
    /// <remarks>Leave <see langword="null"/> for a flat fill, which is what a plain slider wants.</remarks>
    public Vector4? FillTo { get; set; }

    /// <summary>The shape of the handle.</summary>
    public SliderGrab Grab { get; set; } = SliderGrab.Rounded;

    /// <summary>How large the handle is across, at 100%.</summary>
    public float GrabSize { get; set; } = 13f;

    /// <summary>The handle's colour. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? GrabColor { get; set; }

    /// <summary>The colour the handle ramps to, top to bottom. When <see langword="null"/>, it is flat.</summary>
    public Vector4? GrabColorTo { get; set; }

    /// <summary>The colour of the glow behind the handle. When <see langword="null"/>, there is none.</summary>
    public Vector4? GlowColor { get; set; }

    /// <summary>How far the glow reaches past the handle, at 100%.</summary>
    public float GlowSpread { get; set; } = 6f;

    /// <summary>Whether the value is written beside the track.</summary>
    public bool ShowValue { get; set; } = true;

    /// <summary>
    /// How the value is written, as a .NET numeric format string.
    /// </summary>
    /// <remarks>
    /// Null lets the slider choose: whole numbers for an integer slider, two decimals for a float one.
    /// </remarks>
    public string? ValueFormat { get; set; }

    /// <summary>The column the value is written in, at 100%. It is reserved whatever the value reads.</summary>
    public float ValueWidth { get; set; } = 44f;

    /// <summary>The value's colour. When <see langword="null"/>, the theme's muted text.</summary>
    public Vector4? ValueColor { get; set; }

    /// <summary>
    /// The row label's colour. When <see langword="null"/>, the theme's ordinary text.
    /// </summary>
    /// <remarks>
    /// Worth its own value rather than inheriting: a slider usually sits in a run of settings whose labels are quieter
    /// than body text, and one row coming out at full strength reads as emphasis nobody asked for.
    /// </remarks>
    public Vector4? LabelColor { get; set; }

    /// <summary>
    /// How wide the label column is, at 100%. When <see langword="null"/>, <see cref="NoireInputs.LabelWidth"/>.
    /// </summary>
    /// <remarks>
    /// Shared with the input fields by default so a run of settings lines up, and settable here for a design whose
    /// other rows are laid out by hand: a slider that keeps the library's column while its neighbours use another is
    /// the one row in the stack whose control does not start where the rest do.
    /// </remarks>
    public float? LabelWidth { get; set; }

    /// <summary>
    /// Paints the slider instead of the shipped drawing.
    /// </summary>
    /// <remarks>
    /// The widget keeps the sizing, the hit testing, the dragging and the value; the hook only paints. That is what
    /// makes a completely bespoke slider a matter of configuration rather than of writing one from scratch.
    /// </remarks>
    public Action<UiSliderDraw>? CustomDraw { get; set; }

    /// <summary>Returns a copy, so a shared style can be varied for one slider without affecting the rest.</summary>
    /// <returns>A shallow copy.</returns>
    public SliderStyle Clone() => (SliderStyle)MemberwiseClone();
}
