namespace NoireLib.Hooking;

/// <summary>
/// Decides what happens when a hook delegate does not match the function at the resolved address.
/// </summary>
public enum HookVerificationPolicy
{
    /// <summary>
    /// Throw before the hook is created, so a mismatched delegate never becomes a live hook.
    /// </summary>
    Throw,

    /// <summary>
    /// Log an error naming the expected delegate and create the hook anyway.
    /// </summary>
    LogError,

    /// <summary>
    /// Log a warning naming the expected delegate and create the hook anyway.
    /// </summary>
    LogWarning,

    /// <summary>
    /// Create the hook without checking the delegate.
    /// </summary>
    Ignore,
}
