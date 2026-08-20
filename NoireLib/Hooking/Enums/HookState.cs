namespace NoireLib.Hooking;

/// <summary>
/// Represents the lifecycle state of a <see cref="NoireHook{TDelegate}"/>.
/// </summary>
public enum HookState
{
    /// <summary>
    /// The target address has not resolved yet and the hook is waiting to install.
    /// </summary>
    Pending,

    /// <summary>
    /// The hook is installed and can be enabled or disabled.
    /// </summary>
    Installed,

    /// <summary>
    /// The hook could not be installed and will not retry.
    /// </summary>
    Failed,

    /// <summary>
    /// The hook has been disposed.
    /// </summary>
    Disposed,
}
