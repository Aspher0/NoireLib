using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Published when a task is added to the queue.
/// </summary>
public record TaskQueuedEvent(QueuedTask Task);

/// <summary>
/// Published when a task starts executing.
/// </summary>
public record TaskStartedEvent(QueuedTask Task);

/// <summary>
/// Published when a task completes successfully.
/// </summary>
public record TaskCompletedEvent(QueuedTask Task);

/// <summary>
/// Published when a task is cancelled.
/// </summary>
public record TaskCancelledEvent(QueuedTask Task);

/// <summary>
/// Published when a task fails.
/// </summary>
public record TaskFailedEvent(QueuedTask Task, Exception Exception);

/// <summary>
/// Published when the queue starts processing.
/// </summary>
public record QueueStartedEvent();

/// <summary>
/// Published when the queue is paused.
/// </summary>
public record QueuePausedEvent();

/// <summary>
/// Published when the queue is resumed from pause.
/// </summary>
public record QueueResumedEvent();

/// <summary>
/// Published when the queue is stopped.
/// </summary>
public record QueueStoppedEvent();

/// <summary>
/// Published when the queue is cleared.
/// </summary>
public record QueueClearedEvent(int TasksCleared);

/// <summary>
/// Published when all tasks in the queue are completed.
/// </summary>
public record QueueCompletedEvent(int TasksCompleted);
