using System;

namespace NoireLib.UI;

/// <summary>
/// How a number field behaves and reads, every value carrying a default.
/// </summary>
/// <seealso cref="NoireInputs.Number(string, ref float, NumberStyle?)"/>
public sealed class NumberStyle
{
    /// <summary>The unit written after the number, inside the field.</summary>
    public string? Unit { get; set; }

    /// <summary>How much one press of the stepper moves the value, with zero hiding the stepper.</summary>
    public float Step { get; set; } = 1f;

    /// <summary>How much a held stepper press moves the value.</summary>
    public float FastStep { get; set; } = 10f;

    /// <summary>The smallest value accepted, anything typed below it being pulled back up.</summary>
    public float Min { get; set; } = float.MinValue;

    /// <summary>The largest value accepted, anything typed above it being pulled back down.</summary>
    public float Max { get; set; } = float.MaxValue;

    /// <summary>
    /// How many digits are shown after the decimal point, ignored by the integer overloads.
    /// </summary>
    public int Decimals { get; set; } = 2;

    /// <summary>
    /// The value the field considers unmodified, which draws a reset dot beside the field once the value differs.
    /// </summary>
    /// <remarks>No dot is drawn while this is unset.</remarks>
    public float? Default { get; set; }

    /// <summary>
    /// Refuses a value for a reason the field cannot know, returning an error message or <see langword="null"/> to
    /// accept.
    /// </summary>
    /// <remarks>The value is still written; this reports rather than blocking the keystroke.</remarks>
    public Func<float, string?>? Validate { get; set; }

    /// <summary>
    /// The width of the field in real pixels, with zero using the space available; see <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// How the keyboard focus mark looks on this field, falling back to <see cref="NoireFocus.Style"/> when
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>A style whose <see cref="FocusStyle.Shape"/> is <see cref="FocusShape.None"/> leaves this one field
    /// unmarked while the rest of the interface keeps its mark.</remarks>
    public FocusStyle? Focus { get; set; }

    /// <summary>
    /// Replaces the reset dot's own painting, while its hit testing, layout and tooltip stay NoireUI's.
    /// </summary>
    /// <remarks>The dot is the only mark the field paints itself; the focus mark has its own hook on
    /// <see cref="FocusStyle"/>.</remarks>
    public Action<UiResetDotDraw>? ResetDotDraw { get; set; }

    /// <summary>Copies the style.</summary>
    /// <returns>An independent copy carrying the same values.</returns>
    public NumberStyle Clone() => new()
    {
        Unit = Unit,
        Step = Step,
        FastStep = FastStep,
        Min = Min,
        Max = Max,
        Decimals = Decimals,
        Default = Default,
        Validate = Validate,
        Width = Width,
        Focus = Focus,
        ResetDotDraw = ResetDotDraw,
    };
}
