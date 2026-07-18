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
    /// <summary>
    /// Waits for an initial hold, then activates repeatedly, like <see cref="Repeat"/> preceded by a
    /// <see cref="Held"/>-style delay.<br/>
    /// The initial delay is <see cref="HotkeyEntry.HoldDelay"/>; the repeat cadence is
    /// <see cref="HotkeyEntry.FixedRepeatDelay"/>, or the <see cref="HotkeyEntry.RepeatDelayMin"/> to
    /// <see cref="HotkeyEntry.RepeatDelayMax"/> range when <see cref="HotkeyEntry.UseRandomRepeatDelay"/> is set.
    /// </summary>
    HoldAndRepeat,
}
