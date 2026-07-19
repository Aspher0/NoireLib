namespace NoireLib.UI;

/// <summary>
/// What a toast is telling the user, which decides its accent stripe, its icon and its default duration.
/// </summary>
public enum ToastSeverity
{
    /// <summary>Something happened that is worth mentioning and needs no action.</summary>
    Info,

    /// <summary>Something finished successfully.</summary>
    Success,

    /// <summary>Something needs attention but nothing failed.</summary>
    Warning,

    /// <summary>Something failed. Shown for longer than the rest, because it is the one nobody should miss.</summary>
    Error,
}
