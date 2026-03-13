namespace NoireLib.Localizer;

/// <summary>
/// Defines how the localizer resolves its default locale.
/// </summary>
public enum DefaultLocaleSource
{
    /// <summary>
    /// Uses <see cref="NoireLocalizer.DefaultLocale"/> as explicitly configured by the plugin developer.
    /// </summary>
    Custom = 0,

    /// <summary>
    /// Uses the current Windows UI locale.
    /// </summary>
    Windows = 1,

    /// <summary>
    /// Uses the game client language exposed by Dalamud.
    /// </summary>
    GameClient = 2,
}
