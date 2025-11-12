namespace NoireLib.TaskQueue;

/// <summary>
/// Defines the type of completion condition for a <see cref="QueuedTask"/>.
/// </summary>
public enum CompletionConditionType
{
    /// <summary>
    /// Task completes immediately after execution.
    /// </summary>
    Immediate,

    /// <summary>
    /// Task completes when a predicate function returns true.
    /// </summary>
    Predicate,

    /// <summary>
    /// Task completes when a specific EventBus event is received.
    /// </summary>
    EventBusEvent,

    /// <summary>
    /// Task completes after a specified delay.
    /// </summary>
    Delay
}
