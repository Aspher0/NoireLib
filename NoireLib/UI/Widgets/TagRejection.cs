namespace NoireLib.UI;

/// <summary>
/// Why a tag was not accepted.
/// </summary>
/// <remarks>
/// Reported rather than swallowed so the field can say what happened. A tag that simply vanishes when the user presses
/// Enter reads as the widget being broken, whichever rule actually rejected it.
/// </remarks>
public enum TagRejection
{
    /// <summary>It was accepted.</summary>
    None,

    /// <summary>It was empty, or nothing but whitespace.</summary>
    Empty,

    /// <summary>That tag is already in the list.</summary>
    Duplicate,

    /// <summary>It was longer than the field allows.</summary>
    TooLong,

    /// <summary>The field already holds as many tags as it allows.</summary>
    Full,

    /// <summary>The field's own validation refused it. See <see cref="NoireTagInput.LastError"/>.</summary>
    Invalid,
}
