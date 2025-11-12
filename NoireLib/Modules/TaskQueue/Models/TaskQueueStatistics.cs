using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Provides statistics about the task queue.
/// </summary>
public record TaskQueueStatistics(
    int TotalTasksQueued,
    int TasksCompleted,
    int TasksCancelled,
    int TasksFailed,
    int CurrentQueueSize,
    QueueState QueueState,
    QueuedTask? CurrentTask,
    TimeSpan TotalProcessingTime
);
