using System;

namespace NoireLib.UI;

/// <summary>
/// How a number field behaves and reads. Every value has a default, so an untouched style is the ordinary field.
/// </summary>
/// <seealso cref="NoireInputs.Number(string, ref float, NumberStyle?)"/>
public sealed class NumberStyle
{
    /// <summary>
    /// The unit written after the number, inside the field. For example <c>ms</c>, <c>%</c>, <c>yalms</c>.
    /// </summary>
    /// <remarks>
    /// Inside rather than beside it, because a unit in a separate label is a unit that drifts away from its number the
    /// first time the row is laid out differently.
    /// </remarks>
    public string? Unit { get; set; }

    /// <summary>How much one press of the stepper moves the value. Zero hides the stepper.</summary>
    public float Step { get; set; } = 1f;

    /// <summary>How much a held stepper press moves the value.</summary>
    public float FastStep { get; set; } = 10f;

    /// <summary>The smallest value accepted. Anything typed below it is pulled back up.</summary>
    public float Min { get; set; } = float.MinValue;

    /// <summary>The largest value accepted. Anything typed above it is pulled back down.</summary>
    public float Max { get; set; } = float.MaxValue;

    /// <summary>
    /// How many digits are shown after the decimal point. Ignored by the integer overloads.
    /// </summary>
    public int Decimals { get; set; } = 2;

    /// <summary>
    /// The value the field considers unmodified. When set, a dot appears beside the field once the value differs, and
    /// clicking it puts this back.
    /// </summary>
    /// <remarks>
    /// This is the whole of the "modified from default" affordance: give it the shipped default and the rest happens.
    /// Left unset, no dot is drawn and the field behaves as any other.
    /// </remarks>
    public float? Default { get; set; }

    /// <summary>
    /// Refuses a value for a reason the field cannot know. Return an error message, or <see langword="null"/> to accept.
    /// </summary>
    /// <remarks>
    /// The value is still written. This reports rather than blocks, because a field that silently refuses a keystroke
    /// is a field the user fights: the message slides in under it and says what is wrong.
    /// </remarks>
    public Func<float, string?>? Validate { get; set; }

    /// <summary>
    /// The width of the field in real pixels. Zero uses the space available. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Width { get; set; }

    /// <summary>Copies the style, for tweaking one call site without touching the shared object.</summary>
    /// <returns>A copy.</returns>
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
    };
}
