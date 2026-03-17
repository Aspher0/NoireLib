namespace NoireLib.Events;

/// <summary>
/// Represents a lifecycle callback event raised by an event wrapper.
/// </summary>
public enum EventCallbackKind
{
    /// <summary>
    /// The wrapped event subscription was enabled.
    /// </summary>
    Enabled,

    /// <summary>
    /// The wrapped event subscription was disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// The wrapped event subscription was disposed.
    /// </summary>
    Disposed,
}
