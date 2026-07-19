using NoireLib.Helpers;
using System;

namespace NoireLib.UI;

/// <summary>
/// How a duration field behaves and reads. Every value has a default, so an untouched style is the ordinary field.
/// </summary>
/// <seealso cref="NoireInputs.Duration(string, ref TimeSpan, DurationStyle?)"/>
public sealed class DurationStyle
{
    /// <summary>The hint shown while the field is empty.</summary>
    public string Hint { get; set; } = "1m30s";

    /// <summary>
    /// The unit a number typed with no unit at all is measured in.
    /// </summary>
    /// <remarks>
    /// Set this to whatever the setting is really counted in. A cooldown field where someone types "30" almost
    /// certainly means thirty seconds; a poll interval where they type "500" almost certainly means milliseconds.
    /// </remarks>
    public DurationUnit BareUnit { get; set; } = DurationUnit.Seconds;

    /// <summary>The shortest duration accepted.</summary>
    public TimeSpan Min { get; set; } = TimeSpan.Zero;

    /// <summary>The longest duration accepted.</summary>
    public TimeSpan Max { get; set; } = TimeSpan.MaxValue;

    /// <summary>
    /// The value the field considers unmodified. When set, a dot appears beside the field once the value differs, and
    /// clicking it puts this back.
    /// </summary>
    public TimeSpan? Default { get; set; }

    /// <summary>
    /// Refuses a duration for a reason the field cannot know. Return an error message, or <see langword="null"/> to
    /// accept.
    /// </summary>
    public Func<TimeSpan, string?>? Validate { get; set; }

    /// <summary>
    /// The width of the field in real pixels. Zero uses the space available. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Whether the duration read back is shown beside the field while it is being typed.
    /// </summary>
    /// <remarks>
    /// On by default, and the reason the shorthand is usable at all: "1h30" is only obvious once something confirms it
    /// meant ninety minutes rather than an hour and thirty seconds.
    /// </remarks>
    public bool ShowPreview { get; set; } = true;

    /// <summary>Copies the style, for tweaking one call site without touching the shared object.</summary>
    /// <returns>A copy.</returns>
    public DurationStyle Clone() => new()
    {
        Hint = Hint,
        BareUnit = BareUnit,
        Min = Min,
        Max = Max,
        Default = Default,
        Validate = Validate,
        Width = Width,
        ShowPreview = ShowPreview,
    };
}
