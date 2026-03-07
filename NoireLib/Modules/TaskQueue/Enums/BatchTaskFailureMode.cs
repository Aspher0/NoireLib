namespace NoireLib.TaskQueue;

/// <summary>
/// Defines how a batch should handle task failures.
/// </summary>
public enum BatchTaskFailureMode
{
    /// <summary>
    /// When a task fails, fail the entire batch immediately and stop processing remaining tasks.
    /// </summary>
    FailBatch,

    /// <summary>
    /// When a task fails, fail the batch and stop the entire queue.
    /// </summary>
    FailBatchAndStopQueue,

    /// <summary>
    /// When a task fails, continue processing the remaining tasks in the batch.
    /// </summary>
    ContinueRemaining
}
