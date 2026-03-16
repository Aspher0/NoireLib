namespace NoireLib.Hooking;

/// <summary>
/// Represents a lifecycle callback event raised by a hook wrapper.
/// </summary>
public enum HookCallbackKind
{
    /// <summary>
    /// The hook was enabled.
    /// </summary>
    Enabled,

    /// <summary>
    /// The hook was disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// The hook was disposed.
    /// </summary>
    Disposed,
}
