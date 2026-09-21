namespace NoireLib.UI;

/// <summary>
/// Which piece of a <see cref="NoireWindowMenu"/> a run of text is.
/// </summary>
public enum WindowMenuTextRole
{
    /// <summary>A group heading.</summary>
    Heading,

    /// <summary>A slider's name.</summary>
    SliderLabel,

    /// <summary>A slider's value.</summary>
    SliderValue,

    /// <summary>A switch's name.</summary>
    Toggle,

    /// <summary>The note under the switches, wrapped to its box.</summary>
    Note,
}
