namespace NoireLib.Hooking;

/// <summary>
/// Decides what happens when a detour throws, which would otherwise crash the game.
/// </summary>
public enum HookGuardMode
{
    /// <summary>
    /// Install the detour as given, with no wrapper and no added cost.
    /// </summary>
    None,

    /// <summary>
    /// Log the exception and call the original function, repeating any effect the detour applied before throwing.
    /// </summary>
    CallOriginal,

    /// <summary>
    /// Log the exception and return the default value without calling the original function.
    /// </summary>
    ReturnDefault,

    /// <summary>
    /// Log the exception and let it propagate, matching an unguarded hook.
    /// </summary>
    Rethrow,
}
