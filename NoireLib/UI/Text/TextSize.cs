namespace NoireLib.UI;

/// <summary>
/// The named steps of the type scale <see cref="NoireTheme"/> owns.<br/>
/// Text is asked for by role rather than by number, which is what lets a skin re-scale the whole interface from one
/// place instead of thirty call sites each having picked their own 24.
/// </summary>
public enum TextSize
{
    /// <summary>
    /// A masthead: the one piece of type on a window that is meant to be seen before it is read. Rare by design.
    /// </summary>
    Display,

    /// <summary>
    /// A section heading, above a block of controls or prose.
    /// </summary>
    Heading,

    /// <summary>
    /// Running text, and the default. Matches the host's font size unless the theme moves it, so a plugin that never
    /// touches the type scale looks exactly as it did.
    /// </summary>
    Body,

    /// <summary>
    /// Supporting text: descriptions, units, footnotes, anything read after the thing it belongs to.
    /// </summary>
    Caption,
}
