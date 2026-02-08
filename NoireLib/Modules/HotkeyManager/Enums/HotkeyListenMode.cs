namespace NoireLib.HotkeyManager;

/// <summary>
/// Defines which input source is used while listening for a new binding.
/// </summary>
public enum HotkeyListenMode
{
    /// <summary>
    /// Listen for keyboard input.
    /// </summary>
    Keyboard,
    /// <summary>
    /// Listen for gamepad input.
    /// </summary>
    Gamepad,
}
