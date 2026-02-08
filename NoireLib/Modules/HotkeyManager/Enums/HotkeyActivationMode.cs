namespace NoireLib.HotkeyManager;

/// <summary>
/// Defines how a hotkey activates.
/// </summary>
public enum HotkeyActivationMode
{
    /// <summary>
    /// Activates only when the key is pressed.
    /// </summary>
    Pressed,
    /// <summary>
    /// Activates only when the key is released.
    /// </summary>
    Released,
    /// <summary>
    /// Activates while the key is held down.
    /// </summary>
    Held,
    /// <summary>
    /// Activates repeatedly while the key is held down.
    /// </summary>
    Repeat,
}
