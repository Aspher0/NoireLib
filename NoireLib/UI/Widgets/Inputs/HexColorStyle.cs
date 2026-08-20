using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>How a hex colour field behaves and reads. Every value has a default.</summary>
/// <seealso cref="NoireInputs.HexColor(string, ref Vector4, HexColorStyle?)"/>
public sealed class HexColorStyle
{
    /// <summary>Whether the alpha channel is part of the colour, giving eight digits instead of six. Off by default.</summary>
    public bool ShowAlpha { get; set; }

    /// <summary>Whether clicking the swatch opens a picker. On by default.</summary>
    public bool ShowPicker { get; set; } = true;

    /// <summary>
    /// The value the field considers unmodified, which adds a reset dot beside the field once the colour differs.
    /// </summary>
    public Vector4? Default { get; set; }

    /// <summary>
    /// Refuses a colour, returning an error message or <see langword="null"/> to accept.
    /// </summary>
    public Func<Vector4, string?>? Validate { get; set; }

    /// <summary>
    /// The width of the field in real pixels, or zero for the space available. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// The keyboard focus mark for this field alone. When <see langword="null"/>, <see cref="NoireFocus.Style"/>.
    /// </summary>
    public FocusStyle? Focus { get; set; }

    /// <summary>
    /// Replaces the reset dot's own painting, while its hit testing, layout and tooltip stay NoireUI's.
    /// </summary>
    public Action<UiResetDotDraw>? ResetDotDraw { get; set; }

    /// <summary>Copies the style, for tweaking one call site without touching the shared object.</summary>
    /// <returns>A copy.</returns>
    public HexColorStyle Clone() => new()
    {
        ShowAlpha = ShowAlpha,
        ShowPicker = ShowPicker,
        Default = Default,
        Validate = Validate,
        Width = Width,
        Focus = Focus,
        ResetDotDraw = ResetDotDraw,
    };
}
