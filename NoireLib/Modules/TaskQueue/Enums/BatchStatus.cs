namespace NoireLib.TaskQueue;

/// <summary>
/// Represents the status of a task batch.
/// </summary>
public enum BatchStatus
{
    /// <summary>
    /// The batch is queued and waiting to be processed.
    /// </summary>
    Queued,

    /// <summary>
    /// The batch is currently processing its tasks.
    /// </summary>
    Processing,

    /// <summary>
    /// The batch is waiting for post-completion delay to finish.
    /// </summary>
    WaitingForPostDelay,

    /// <summary>
    /// The batch has completed successfully (all tasks completed).
    /// </summary>
    Completed,

    /// <summary>
    /// The batch was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The batch failed (one or more tasks failed).
    /// </summary>
    Failed
}
