namespace NoireLib.UI;

/// <summary>
/// How a toast shows the time it has left before it dismisses itself.
/// </summary>
/// <remarks>
/// Every mode is inert on a toast with no duration: there is nothing to count down to.
/// </remarks>
public enum ToastTimerMode
{
    /// <summary>No countdown is shown.</summary>
    None,

    /// <summary>A bar along the bottom edge.</summary>
    BottomBar,

    /// <summary>A bar along the top edge.</summary>
    TopBar,

    /// <summary>A bar down the leading edge, beside the severity stripe rather than over it.</summary>
    Stripe,

    /// <summary>An outline traced around the whole toast, unwinding clockwise from the top left.</summary>
    Border,

    /// <summary>The background tinted from the left, the tint retreating as the time runs out.</summary>
    TintLeftToRight,

    /// <summary>The background tinted from the right, the tint retreating as the time runs out.</summary>
    TintRightToLeft,
}
