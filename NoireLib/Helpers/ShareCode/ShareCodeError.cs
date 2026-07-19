namespace NoireLib.Helpers;

/// <summary>
/// Why a share code could not be read. Every value is a reason a user can be told, because every one of them is
/// something that happened to the code between being written and being pasted.
/// </summary>
public enum ShareCodeError
{
    /// <summary>The code was read successfully.</summary>
    None,

    /// <summary>Nothing was passed, or only whitespace.</summary>
    Empty,

    /// <summary>The text is not a NoireLib share code at all.</summary>
    NotAShareCode,

    /// <summary>The text is a share code in a format version this build does not know how to read.</summary>
    WrongVersion,

    /// <summary>The code is a share code but is damaged: truncated, re-wrapped, or partially copied.</summary>
    Malformed,

    /// <summary>The code parsed, but its contents do not match its checksum, so it was altered in transit.</summary>
    ChecksumMismatch,

    /// <summary>The code asks for more than <see cref="ShareCodeLimits"/> allows.</summary>
    TooLarge,

    /// <summary>The code is valid, but it carries a different kind of payload than the one being imported.</summary>
    WrongKind,

    /// <summary>The payload decoded, but it is not the shape the requested type expects.</summary>
    Unreadable,
}
