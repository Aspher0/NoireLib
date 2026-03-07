namespace NoireLib.TaskQueue;

/// <summary>
/// Defines how a batch should handle task cancellations.
/// </summary>
public enum BatchTaskCancellationMode
{
    /// <summary>
    /// When a task is cancelled, cancel the entire batch immediately and stop processing remaining tasks.
    /// </summary>
    CancelBatch,

    /// <summary>
    /// When a task is cancelled, cancel the batch and stop the entire queue.
    /// </summary>
    CancelBatchAndQueue,

    /// <summary>
    /// When a task is cancelled, continue processing the remaining tasks in the batch.
    /// </summary>
    ContinueRemaining
}
