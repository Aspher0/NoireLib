namespace NoireLib.TaskQueue;

/// <summary>
/// Represents the current status of a <see cref="QueuedTask"/>.
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// The task is waiting in the queue to be executed.
    /// </summary>
    Queued,

    /// <summary>
    /// The task is currently executing.
    /// </summary>
    Executing,

    /// <summary>
    /// The task is waiting for its completion condition to be met.
    /// </summary>
    WaitingForCompletion,

    /// <summary>
    /// The task has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The task was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The task failed due to an exception or timeout.
    /// </summary>
    Failed
}
