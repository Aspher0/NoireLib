namespace NoireLib.TaskQueue;

/// <summary>
/// Represents the current state of the task queue.<br/>
/// Meant for use with the <see cref="NoireTaskQueue"/> module.
/// </summary>
public enum QueueState
{
    /// <summary>
    /// The queue is idle and not processing tasks. More specifically, the queue has not been started yet.
    /// </summary>
    Idle,

    /// <summary>
    /// The queue is actively processing tasks.
    /// </summary>
    Running,

    /// <summary>
    /// The queue is paused and will not process tasks until resumed.
    /// </summary>
    Paused,

    /// <summary>
    /// The queue is stopped and needs to be started again.
    /// </summary>
    Stopped
}
