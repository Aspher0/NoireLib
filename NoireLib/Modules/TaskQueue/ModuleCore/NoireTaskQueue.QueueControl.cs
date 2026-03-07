using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Queue control partial class for <see cref="NoireTaskQueue"/>.
/// </summary>
public partial class NoireTaskQueue
{
    /// <summary>
    /// Starts processing the queue.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue StartQueue()
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot start queue - module is not active.");
            return this;
        }

        if (QueueState == QueueState.Running)
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, "Queue is already running.");
            return this;
        }

        if (unifiedQueue.Count == 0)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot start queue - no items in the queue.");
            return this;
        }

        // If starting from paused state, resume delay-based tasks and timeouts
        if (QueueState == QueueState.Paused)
        {
            ResumeAllQueueTimers();
        }

        QueueState = QueueState.Running;
        processingStartTimeTicks = Environment.TickCount64;

        PublishEvent(new QueueStartedEvent());

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Queue started.");

        return this;
    }

    /// <summary>
    /// Pauses the queue processing.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue PauseQueue()
    {
        if (QueueState != QueueState.Running)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot pause queue - it is not running.");
            return this;
        }

        accumulatedProcessingMillis += Environment.TickCount64 - processingStartTimeTicks;
        processingStartTimeTicks = 0;

        PauseAllQueueTimers();

        QueueState = QueueState.Paused;
        PublishEvent(new QueuePausedEvent());

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Queue paused.");

        return this;
    }

    /// <summary>
    /// Resumes the queue processing from pause.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue ResumeQueue()
    {
        if (QueueState != QueueState.Paused)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot resume queue - it is not paused.");
            return this;
        }

        QueueState = QueueState.Running;
        processingStartTimeTicks = Environment.TickCount64;

        ResumeAllQueueTimers();

        PublishEvent(new QueueResumedEvent());

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Queue resumed.");

        return this;
    }

    /// <summary>
    /// Stops the queue processing and clears any remaining items.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue StopQueue()
    {
        ClearQueue();

        if (QueueState == QueueState.Idle || QueueState == QueueState.Stopped)
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, "Queue is already stopped or idle.");

            return this;
        }

        if (QueueState == QueueState.Running && processingStartTimeTicks > 0)
        {
            accumulatedProcessingMillis += Environment.TickCount64 - processingStartTimeTicks;
            processingStartTimeTicks = 0;
        }

        QueueState = QueueState.Stopped;

        PublishEvent(new QueueStoppedEvent());

        if (EnableLogging)
        {
            NoireLogger.LogInfo(this, "Queue stopped. " +
                $"Total Tasks Queued: {totalTasksQueued}, " +
                $"Completed: {tasksCompleted}, " +
                $"Cancelled: {tasksCancelled}, " +
                $"Failed: {tasksFailed}, " +
                $"Batches Queued: {totalBatchesQueued}, " +
                $"Batches Completed: {batchesCompleted}, " +
                $"Batches Failed: {batchesFailed}, " +
                $"Total Active Processing Time: {accumulatedProcessingMillis} ms.");
        }

        return this;
    }

    /// <summary>
    /// Pauses all task timers (timeout, stall tracking, post-delay) in the queue.
    /// </summary>
    private void PauseAllQueueTimers()
    {
        lock (queueLock)
        {
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                {
                    PauseTaskTimers(item.AsTask());
                }
                else if (item.IsBatch)
                {
                    var batch = item.AsBatch();
                    PauseBatchTimers(batch);
                }
            }
        }
    }

    /// <summary>
    /// Resumes all task timers (timeout, stall tracking, post-delay) in the queue.
    /// </summary>
    private void ResumeAllQueueTimers()
    {
        lock (queueLock)
        {
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                {
                    ResumeTaskTimers(item.AsTask());
                }
                else if (item.IsBatch)
                {
                    var batch = item.AsBatch();
                    ResumeBatchTimers(batch);
                }
            }
        }
    }

    /// <summary>
    /// Pauses timers for a single task.
    /// </summary>
    private void PauseTaskTimers(QueuedTask task)
    {
        if ((task.Status == TaskStatus.Executing || task.Status == TaskStatus.WaitingForCompletion || task.Status == TaskStatus.WaitingForPostDelay) && task.Timeout.HasValue)
            task.PauseTimeout();

        if (task.Status == TaskStatus.WaitingForCompletion && task.RetryConfiguration != null)
            task.PauseStallTracking();

        if (task.Status == TaskStatus.WaitingForPostDelay && task.PostCompletionDelay.HasValue)
            task.PausePostDelay();
    }

    /// <summary>
    /// Resumes timers for a single task.
    /// </summary>
    private void ResumeTaskTimers(QueuedTask task)
    {
        if (task.Status == TaskStatus.WaitingForPostDelay && task.PostCompletionDelay.HasValue)
            task.ResumePostDelay();

        if ((task.Status == TaskStatus.Executing || task.Status == TaskStatus.WaitingForCompletion) && task.Timeout.HasValue)
            task.ResumeTimeout();

        if (task.Status == TaskStatus.WaitingForCompletion && task.RetryConfiguration != null)
            task.ResumeStallTracking();
    }

    /// <summary>
    /// Pauses timers for a batch and all its tasks.
    /// </summary>
    private void PauseBatchTimers(TaskBatch batch)
    {
        if (batch.Status == BatchStatus.WaitingForPostDelay && batch.PostCompletionDelay.HasValue)
            batch.PausePostDelay();

        foreach (var task in batch.Tasks)
        {
            PauseTaskTimers(task);
        }
    }

    /// <summary>
    /// Resumes timers for a batch and all its tasks.
    /// </summary>
    private void ResumeBatchTimers(TaskBatch batch)
    {
        if (batch.Status == BatchStatus.WaitingForPostDelay && batch.PostCompletionDelay.HasValue)
            batch.ResumePostDelay();

        foreach (var task in batch.Tasks)
        {
            ResumeTaskTimers(task);
        }
    }
}
