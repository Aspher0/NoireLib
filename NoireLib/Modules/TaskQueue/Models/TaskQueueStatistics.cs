using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Represents detailed progress information about the queue, including tasks within batches.
/// </summary>
/// <param name="TotalTasks">Total number of tasks (including tasks within batches).</param>
/// <param name="QueuedTasks">Number of tasks currently queued.</param>
/// <param name="CompletedTasks">Number of completed tasks.</param>
/// <param name="CancelledTasks">Number of cancelled tasks.</param>
/// <param name="FailedTasks">Number of failed tasks.</param>
/// <param name="ExecutingTasks">Number of tasks currently executing or waiting.</param>
/// <param name="CurrentTask">The actual task currently being processed, even if it's within a batch.</param>
/// <param name="TotalBatchesQueued">Total number of batches that have been queued.</param>
/// <param name="BatchesCompleted">Number of batches that completed successfully.</param>
/// <param name="BatchesCancelled">Number of batches that were cancelled.</param>
/// <param name="BatchesFailed">Number of batches that failed.</param>
/// <param name="CurrentBatchQueueSize">Current number of batches in the queue.</param>
/// <param name="CurrentBatch">The batch currently being processed, if any.</param>
/// <param name="CurrentItem">The current queue item wrapper (task or batch) being processed, if any.</param>
/// <param name="CurrentTaskDescription">Description of the current task being processed.</param>
/// <param name="QueueState">Current state of the queue.</param>
/// <param name="CurrentQueueSize">Current total number of items (tasks + batches) in the queue.</param>
/// <param name="ProgressPercentage">Overall progress percentage (0-100).</param>
/// <param name="TotalProcessingTime">Total time spent processing tasks so far.</param>
public record TaskQueueStatistics(
    int TotalTasks,
    int QueuedTasks,
    int CompletedTasks,
    int CancelledTasks,
    int FailedTasks,
    int ExecutingTasks,
    QueuedTask? CurrentTask,
    int TotalBatchesQueued,
    int BatchesCompleted,
    int BatchesCancelled,
    int BatchesFailed,
    int CurrentBatchQueueSize,
    TaskBatch? CurrentBatch,
    QueueItemWrapper? CurrentItem,
    string CurrentTaskDescription,
    QueueState QueueState,
    int CurrentQueueSize,
    double ProgressPercentage,
    TimeSpan TotalProcessingTime);
