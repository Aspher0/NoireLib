using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a hex colour field behaves and reads. Every value has a default, so an untouched style is the ordinary field.
/// </summary>
/// <seealso cref="NoireInputs.HexColor(string, ref Vector4, HexColorStyle?)"/>
public sealed class HexColorStyle
{
    /// <summary>
    /// Whether the alpha channel is part of the colour. Off by default, since most settings are an opaque colour.
    /// </summary>
    /// <remarks>
    /// With it off the field reads and writes six digits and leaves alpha alone; with it on, eight.
    /// </remarks>
    public bool ShowAlpha { get; set; }

    /// <summary>Whether clicking the swatch opens a picker. On by default.</summary>
    public bool ShowPicker { get; set; } = true;

    /// <summary>
    /// The value the field considers unmodified. When set, a dot appears beside the field once the colour differs, and
    /// clicking it puts this back.
    /// </summary>
    public Vector4? Default { get; set; }

    /// <summary>
    /// Refuses a colour for a reason the field cannot know. Return an error message, or <see langword="null"/> to
    /// accept.
    /// </summary>
    public Func<Vector4, string?>? Validate { get; set; }

    /// <summary>
    /// The width of the field in real pixels. Zero uses the space available. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Width { get; set; }

    /// <summary>Copies the style, for tweaking one call site without touching the shared object.</summary>
    /// <returns>A copy.</returns>
    public HexColorStyle Clone() => new()
    {
        ShowAlpha = ShowAlpha,
        ShowPicker = ShowPicker,
        Default = Default,
        Validate = Validate,
        Width = Width,
    };
}
