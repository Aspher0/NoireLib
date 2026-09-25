namespace NoireLib.UI;

/// <summary>What a piece of text is, for <see cref="IFontSkin.Push"/>.</summary>
public enum TextRole
{
    /// <summary>Running text.</summary>
    Body,

    /// <summary>Secondary text.</summary>
    Small,

    /// <summary>A heading.</summary>
    Heading,

    /// <summary>A large title.</summary>
    Title,

    /// <summary>Code, commands and ids.</summary>
    Mono,
}
