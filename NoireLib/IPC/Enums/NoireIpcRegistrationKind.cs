namespace NoireLib.IPC;

/// <summary>
/// Defines how an IPC provider delegate should be registered.
/// </summary>
public enum NoireIpcRegistrationKind
{
    /// <summary>
    /// Chooses the registration kind automatically from the delegate return type.
    /// </summary>
    Auto,

    /// <summary>
    /// Registers the delegate as an action IPC.
    /// </summary>
    Action,

    /// <summary>
    /// Registers the delegate as a function IPC.
    /// </summary>
    Function,
}
