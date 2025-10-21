namespace NoireLib.EventBus;

/// <summary>
/// Defines how the EventBus should handle exceptions thrown by event handlers.
/// </summary>
public enum EventExceptionMode
{
    /// <summary>
    /// Log the exception and continue processing other handlers.
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// Log the exception and re-throw it, stopping further handler execution.
    /// </summary>
    LogAndThrow,

    /// <summary>
    /// Suppress the exception silently (not recommended for production).
    /// </summary>
    Suppress
}
