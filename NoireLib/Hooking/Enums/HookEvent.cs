namespace NoireLib.Hooking;

/// <summary>
/// Something that happened to a hook, reported to its event and to its keyed state callbacks.
/// </summary>
public enum HookEvent
{
    /// <summary>
    /// The hook resolved its address and installed.
    /// </summary>
    Installed,

    /// <summary>
    /// The hook could not be installed and will not retry.
    /// </summary>
    Failed,

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
