namespace NoireLib.UI;

/// <summary>
/// The named steps of the type scale <see cref="NoireTheme"/> owns, asked for by role rather than by number.
/// </summary>
public enum TextSize
{
    /// <summary>
    /// A masthead: the one piece of type on a window that is meant to be seen before it is read.
    /// </summary>
    Display,

    /// <summary>
    /// A section heading, above a block of controls or prose.
    /// </summary>
    Heading,

    /// <summary>
    /// Running text, and the default, matching the host's font size unless the theme moves it.
    /// </summary>
    Body,

    /// <summary>
    /// Supporting text: descriptions, units, footnotes, anything read after the thing it belongs to.
    /// </summary>
    Caption,
}
