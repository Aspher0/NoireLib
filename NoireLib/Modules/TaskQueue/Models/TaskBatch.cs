using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TaskQueue;

/// <summary>
/// Represents a batch of tasks that can be processed as a unit within the task queue.
/// Batches can be blocking or non-blocking, and can contain multiple tasks that execute in order.
/// </summary>
public class TaskBatch
{
    /// <summary>
    /// The unique system-generated identifier for this batch.
    /// </summary>
    public Guid SystemId { get; } = Guid.NewGuid();

    /// <summary>
    /// The queue that owns this batch.
    /// </summary>
    public NoireTaskQueue? OwningQueue { get; internal set; }

    /// <summary>
    /// An optional custom identifier for this batch.
    /// </summary>
    public string? CustomId { get; set; }

    /// <summary>
    /// Whether this batch blocks subsequent batches/tasks from starting until it completes.
    /// </summary>
    public bool IsBlocking { get; set; }

    /// <summary>
    /// The current status of the batch.
    /// </summary>
    public BatchStatus Status { get; set; } = BatchStatus.Queued;

    /// <summary>
    /// The list of tasks contained in this batch.
    /// </summary>
    public List<QueuedTask> Tasks { get; } = new();

    /// <summary>
    /// The timestamp (in ticks) when this batch was queued.
    /// </summary>
    public long QueuedAtTicks { get; set; }

    /// <summary>
    /// The timestamp (in ticks) when this batch started processing.
    /// </summary>
    public long? StartedAtTicks { get; set; }

    /// <summary>
    /// The timestamp (in ticks) when this batch finished (completed, cancelled, or failed).
    /// </summary>
    public long? FinishedAtTicks { get; set; }

    /// <summary>
    /// Optional callback invoked when the batch starts processing.
    /// </summary>
    public Action<TaskBatch>? OnStarted { get; set; }

    /// <summary>
    /// Optional callback invoked when the batch completes successfully.
    /// </summary>
    public Action<TaskBatch>? OnCompleted { get; set; }

    /// <summary>
    /// Optional callback invoked when the batch is cancelled.
    /// </summary>
    public Action<TaskBatch>? OnCancelled { get; set; }

    /// <summary>
    /// Optional callback invoked when the batch fails.
    /// </summary>
    public Action<TaskBatch, Exception?>? OnFailed { get; set; }

    /// <summary>
    /// Whether to stop the entire queue if this batch fails.
    /// </summary>
    public bool StopQueueOnFail { get; set; }

    /// <summary>
    /// Whether to stop the entire queue if this batch is cancelled.
    /// </summary>
    public bool StopQueueOnCancel { get; set; }

    /// <summary>
    /// Defines how the batch should handle task failures.
    /// Default is ContinueRemaining.
    /// </summary>
    public BatchTaskFailureMode TaskFailureMode { get; set; } = BatchTaskFailureMode.ContinueRemaining;

    /// <summary>
    /// Defines how the batch should handle task cancellations.
    /// Default is ContinueRemaining.
    /// </summary>
    public BatchTaskCancellationMode TaskCancellationMode { get; set; } = BatchTaskCancellationMode.ContinueRemaining;

    /// <summary>
    /// Optional metadata that can be attached to this batch for custom use.
    /// </summary>
    public object? Metadata { get; set; }

    /// <summary>
    /// Optional delay to wait after the batch completes (successfully or based on flags).
    /// This delay executes after the batch would normally complete.
    /// If <see cref="PostCompletionDelayProvider"/> is set, it will be used to determine the delay at runtime.
    /// </summary>
    public TimeSpan? PostCompletionDelay { get; set; }

    /// <summary>
    /// Optional provider for post-completion delay. If set, this function will be called at the moment the post-completion delay is about to start, and its result will be used as the delay.
    /// </summary>
    public Func<TaskBatch, TimeSpan?>? PostCompletionDelayProvider { get; set; }

    /// <summary>
    /// Whether to apply the post-completion delay when the batch fails.
    /// </summary>
    public bool ApplyPostDelayOnFailure { get; set; }

    /// <summary>
    /// Whether to apply the post-completion delay when the batch is cancelled.
    /// </summary>
    public bool ApplyPostDelayOnCancellation { get; set; }

    /// <summary>
    /// The tick count when the post-completion delay started.
    /// </summary>
    internal long? PostDelayStartTicks { get; set; }

    /// <summary>
    /// Accumulated elapsed time in milliseconds for post-completion delay, excluding paused time.
    /// </summary>
    internal long AccumulatedPostDelayMillis { get; set; }

    /// <summary>
    /// The tick count when the post-completion delay was last paused.
    /// </summary>
    internal long? PostDelayPausedAtTicks { get; set; }

    /// <summary>
    /// The exception that caused this batch to fail, if applicable.
    /// </summary>
    public Exception? FailureException { get; set; }

    /// <summary>
    /// Creates a new task batch with an optional custom ID and blocking behavior.
    /// </summary>
    /// <param name="customId">Optional custom identifier.</param>
    /// <param name="isBlocking">Whether this batch blocks subsequent items.</param>
    public TaskBatch(string? customId = null, bool isBlocking = true)
    {
        CustomId = customId;
        IsBlocking = isBlocking;
        QueuedAtTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Adds a task to this batch.
    /// </summary>
    /// <param name="task">The task to add.</param>
    /// <returns>This batch for chaining.</returns>
    public TaskBatch AddTask(QueuedTask task)
    {
        Tasks.Add(task);
        return this;
    }

    /// <summary>
    /// Gets the total execution time of this batch.
    /// </summary>
    public TimeSpan? GetExecutionTime()
    {
        if (!StartedAtTicks.HasValue)
            return null;

        var endTicks = FinishedAtTicks ?? Environment.TickCount64;
        return TimeSpan.FromMilliseconds(endTicks - StartedAtTicks.Value);
    }

    /// <summary>
    /// Gets the total time this batch has been in the queue (including processing time).
    /// </summary>
    public TimeSpan GetTotalTime()
    {
        var endTicks = FinishedAtTicks ?? Environment.TickCount64;
        return TimeSpan.FromMilliseconds(endTicks - QueuedAtTicks);
    }

    /// <summary>
    /// Gets the number of tasks in this batch.
    /// </summary>
    public int TaskCount => Tasks.Count;

    /// <summary>
    /// Gets the number of completed tasks in this batch.
    /// </summary>
    public int CompletedTaskCount => Tasks.Count(t => t.Status == TaskStatus.Completed);

    /// <summary>
    /// Gets the number of failed tasks in this batch.
    /// </summary>
    public int FailedTaskCount => Tasks.Count(t => t.Status == TaskStatus.Failed);

    /// <summary>
    /// Gets the number of cancelled tasks in this batch.
    /// </summary>
    public int CancelledTaskCount => Tasks.Count(t => t.Status == TaskStatus.Cancelled);

    /// <summary>
    /// Gets the progress of this batch (0.0 to 1.0).
    /// </summary>
    public double GetProgress()
    {
        if (Tasks.Count == 0)
            return 1.0;

        var finishedTasks = Tasks.Count(t =>
            t.Status == TaskStatus.Completed ||
            t.Status == TaskStatus.Cancelled ||
            t.Status == TaskStatus.Failed);

        return (double)finishedTasks / Tasks.Count;
    }

    /// <summary>
    /// Checks if the post-completion delay has finished.
    /// </summary>
    /// <returns>True if the delay has elapsed, false otherwise.</returns>
    internal bool HasPostDelayCompleted()
    {
        if (!PostDelayStartTicks.HasValue || !PostCompletionDelay.HasValue)
            return false;

        // If currently paused, don't mark as completed
        if (PostDelayPausedAtTicks.HasValue)
            return false;

        var totalElapsed = AccumulatedPostDelayMillis + (Environment.TickCount64 - PostDelayStartTicks.Value);
        return totalElapsed >= PostCompletionDelay.Value.TotalMilliseconds;
    }

    /// <summary>
    /// Pauses the post-completion delay timer.
    /// </summary>
    internal void PausePostDelay()
    {
        if (!PostDelayStartTicks.HasValue || PostDelayPausedAtTicks.HasValue)
            return;

        // Accumulate elapsed time
        AccumulatedPostDelayMillis += Environment.TickCount64 - PostDelayStartTicks.Value;
        PostDelayPausedAtTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Resumes the post-completion delay timer.
    /// </summary>
    internal void ResumePostDelay()
    {
        if (!PostDelayPausedAtTicks.HasValue)
            return;

        // Resume from current time
        PostDelayStartTicks = Environment.TickCount64;
        PostDelayPausedAtTicks = null;
    }

    /// <summary>
    /// Creates a shallow copy of this batch (tasks list is shared).
    /// </summary>
    public TaskBatch Clone()
    {
        return new TaskBatch(CustomId, IsBlocking)
        {
            Status = Status,
            QueuedAtTicks = QueuedAtTicks,
            StartedAtTicks = StartedAtTicks,
            FinishedAtTicks = FinishedAtTicks,
            OnStarted = OnStarted,
            OnCompleted = OnCompleted,
            OnCancelled = OnCancelled,
            OnFailed = OnFailed,
            StopQueueOnFail = StopQueueOnFail,
            StopQueueOnCancel = StopQueueOnCancel,
            Metadata = Metadata,
            FailureException = FailureException
        };
    }

    /// <summary>
    /// Returns a string representation of this batch, including the ID, the Status, the number of completed tasks, the number of total tasks and whether it's blocking or non-blocking.
    /// </summary>
    /// <returns>The string representation of the batch.</returns>
    public override string ToString()
    {
        var id = !string.IsNullOrEmpty(CustomId) ? $"'{CustomId}'" : SystemId.ToString();
        double remainingPostDelay = 0;

        if (PostCompletionDelay.HasValue && PostDelayStartTicks.HasValue)
        {
            var elapsedMs = PostDelayPausedAtTicks.HasValue
                ? AccumulatedPostDelayMillis
                : AccumulatedPostDelayMillis + (Environment.TickCount64 - PostDelayStartTicks.Value);

            remainingPostDelay = Math.Max(0, PostCompletionDelay.Value.TotalMilliseconds - elapsedMs);
        }

        return $"Batch[{id}, {Status}{(Status == BatchStatus.WaitingForPostDelay ? $"({remainingPostDelay}ms)" : "")}, {CompletedTaskCount}/{TaskCount} tasks, {(IsBlocking ? "Blocking" : "Non-Blocking")}]";
    }

    /// <summary>
    /// Gets the string representation of this batch or its currently executing task.
    /// </summary>
    /// <returns>The batch identifier if no task is executing or batch is in post-delay, otherwise the executing task's identifier.</returns>
    public string GetCurrentIdentifier()
    {
        if (Status == BatchStatus.WaitingForPostDelay || Status == BatchStatus.Completed || Status == BatchStatus.Cancelled || Status == BatchStatus.Failed)
            return ToString();

        var executingTask = Tasks.FirstOrDefault(t => t.Status == TaskStatus.Executing || t.Status == TaskStatus.WaitingForCompletion || t.Status == TaskStatus.WaitingForPostDelay);
        return executingTask?.ToString() ?? ToString();
    }

    /// <summary>
    /// Gets a task from this batch by its system ID.
    /// </summary>
    /// <param name="systemId">The system ID of the task to retrieve.</param>
    /// <returns>The task if found; otherwise, null.</returns>
    public QueuedTask? GetTaskBySystemId(Guid systemId)
    {
        return Tasks.FirstOrDefault(t => t.SystemId == systemId);
    }

    /// <summary>
    /// Gets a task from this batch by its custom ID.
    /// </summary>
    /// <param name="customId">The custom ID of the task to retrieve.</param>
    /// <returns>The task if found; otherwise, null.</returns>
    public QueuedTask? GetTaskByCustomId(string customId)
    {
        return Tasks.FirstOrDefault(t => t.CustomId == customId);
    }

    /// <summary>
    /// Gets all tasks from this batch with a specific custom ID.
    /// </summary>
    /// <param name="customId">The custom ID to search for.</param>
    /// <returns>A read-only list of tasks with the specified custom ID.</returns>
    public IReadOnlyList<QueuedTask> GetTasksByCustomId(string customId)
    {
        return Tasks.Where(t => t.CustomId == customId).ToList();
    }

    /// <summary>
    /// Cancels this batch immediately if it is still in the queue.
    /// </summary>
    /// <returns>True if the batch was successfully cancelled; otherwise, false.</returns>
    public bool Cancel()
    {
        return OwningQueue?.CancelBatch(SystemId) ?? false;
    }

    /// <summary>
    /// Fails this batch immediately with the specified exception.
    /// </summary>
    /// <param name="exception">The exception that caused the batch to fail.</param>
    /// <returns>True if the batch was successfully failed; otherwise, false.</returns>
    public bool Fail(Exception exception)
    {
        return OwningQueue?.FailBatch(SystemId, exception) ?? false;
    }

    /// <summary>
    /// Fails this batch immediately with a default exception message.
    /// </summary>
    /// <returns>True if the batch was successfully failed; otherwise, false.</returns>
    public bool Fail()
    {
        return Fail(new Exception("Batch manually failed."));
    }
}
