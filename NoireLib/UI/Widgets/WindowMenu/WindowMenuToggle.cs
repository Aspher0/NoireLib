namespace NoireLib.UI;

/// <summary>
/// The switches of a <see cref="NoireWindowMenu"/>, in the order its grids draw them.
/// </summary>
public enum WindowMenuToggle
{
    /// <summary>Keeps the window above every other.</summary>
    AlwaysOnTop,

    /// <summary>Stops the window's animations.</summary>
    ReducedMotion,

    /// <summary>Stops the window from being moved.</summary>
    LockPosition,

    /// <summary>Lets clicks through the window's body to the game.</summary>
    ClickThrough,

    /// <summary>Stops the window's width from being resized.</summary>
    LockWidth,

    /// <summary>Stops the window's height from being resized.</summary>
    LockHeight,

    /// <summary>Keeps the window drawn in group pose.</summary>
    StayInGpose,

    /// <summary>Keeps the window drawn while the game UI is hidden.</summary>
    StayWhenUiHidden,

    /// <summary>Keeps the window drawn during cutscenes.</summary>
    StayInCutscenes,

    /// <summary>Keeps the window drawn whenever the game hides its own UI.</summary>
    StayAutoHide,
}
