namespace NoireLib.UI;

/// <summary>
/// The named colors a <see cref="NoireTheme"/> is built from.<br/>
/// Every widget resolves the colors it needs through these, so overriding one re-tints everything that uses it.
/// </summary>
public enum ThemeColor
{
    /// <summary>The one color the interface is built around: selected states, focus, emphasis, progress.</summary>
    Accent,

    /// <summary>The color a completed or healthy state is shown in.</summary>
    Success,

    /// <summary>The color a state that needs attention but is not an error is shown in.</summary>
    Warning,

    /// <summary>The color a failure or a destructive action is shown in.</summary>
    Danger,

    /// <summary>The color neutral, purely informational emphasis is shown in.</summary>
    Info,

    /// <summary>The background everything sits on.</summary>
    Surface,

    /// <summary>A surface that reads as lifted off the background: cards, popups, toasts.</summary>
    SurfaceRaised,

    /// <summary>A surface that reads as recessed into the background: input fields, wells, tracks.</summary>
    SurfaceSunken,

    /// <summary>The hairline color separating one surface from another.</summary>
    Border,

    /// <summary>The main text color.</summary>
    Text,

    /// <summary>Secondary text: descriptions, captions, units, anything supporting.</summary>
    TextMuted,

    /// <summary>The text color of something switched off or unavailable.</summary>
    TextDisabled,

    /// <summary>The color of drop shadows and scrims.</summary>
    Shadow,
}
