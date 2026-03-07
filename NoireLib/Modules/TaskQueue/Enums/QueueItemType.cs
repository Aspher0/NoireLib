namespace NoireLib.TaskQueue;

/// <summary>
/// Represents the type of item in the queue.
/// </summary>
public enum QueueItemType
{
    /// <summary>
    /// A single task.
    /// </summary>
    Task,

    /// <summary>
    /// A batch of tasks.
    /// </summary>
    Batch
}
