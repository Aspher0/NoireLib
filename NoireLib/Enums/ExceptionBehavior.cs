namespace NoireLib.Enums;

/// <summary>
/// Defines the behavior when an exception occurs during safe execution.
/// </summary>
public enum ExceptionBehavior
{
    /// <summary>
    /// Log the exception and continue execution (default behavior).
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// Log the exception and re-throw it.
    /// </summary>
    LogAndThrow,

    /// <summary>
    /// Suppress the exception without logging.
    /// </summary>
    Suppress,

    /// <summary>
    /// Throw the exception without logging.
    /// </summary>
    Throw
}
