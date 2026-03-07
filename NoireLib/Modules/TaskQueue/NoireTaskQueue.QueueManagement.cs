using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TaskQueue;

/// <summary>
/// Queue management partial class for <see cref="NoireTaskQueue"/>.
/// </summary>
public partial class NoireTaskQueue
{
    /// <summary>
    /// Will create a new <see cref="TaskBuilder{TModule}"/> instance for building and enqueuing a task with chainable methods.<br/>
    /// By default, the task will be of blocking type.
    /// </summary>
    /// <param name="customId">The optional custom ID to give the task.</param>
    /// <returns>The <see cref="TaskBuilder{TModule}"/> instance for chaining.</returns>
    public TaskBuilder<NoireTaskQueue> CreateTask(string? customId = null)
        => TaskBuilder<NoireTaskQueue>.Create(this, customId);

    /// <summary>
    /// Creates a new <see cref="BatchBuilder{TModule}"/> instance for building and enqueuing a batch with chainable methods.
    /// </summary>
    /// <param name="customId">The optional custom ID to give the batch.</param>
    /// <returns>The <see cref="BatchBuilder{TModule}"/> instance for chaining.</returns>
    public BatchBuilder<NoireTaskQueue> CreateBatch(string? customId = null)
        => BatchBuilder<NoireTaskQueue>.Create(this, customId);

    /// <summary>
    /// Adds a task to the unified queue.
    /// </summary>
    /// <param name="task">The <see cref="QueuedTask"/> to add.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue EnqueueTask(QueuedTask task)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot enqueue task - module is not active.");
            return this;
        }

        bool subscribed = false;
        lock (queueLock)
        {
            unifiedQueue.Add(QueueItemWrapper.FromTask(task));
            totalTasksQueued++;
            if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent && task.CompletionCondition.EventType != null)
            {
                SubscribeToEventForTask(task);
                subscribed = true;
            }
        }

        PublishEvent(new TaskQueuedEvent(task));

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Task enqueued: {task}{(subscribed ? " (event subscribed)" : "")}");

        if (ShouldProcessQueueAutomatically && (QueueState == QueueState.Idle || QueueState == QueueState.Stopped))
            StartQueue();

        return this;
    }

    /// <summary>
    /// Adds a batch to the unified queue.
    /// </summary>
    /// <param name="batch">The <see cref="TaskBatch"/> to add.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue EnqueueBatch(TaskBatch batch)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot enqueue batch - module is not active.");
            return this;
        }

        int subscribedCount = 0;
        lock (queueLock)
        {
            unifiedQueue.Add(QueueItemWrapper.FromBatch(batch));
            totalBatchesQueued++;
            batch.QueuedAtTicks = Environment.TickCount64;

            foreach (var task in batch.Tasks)
            {
                if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent && task.CompletionCondition.EventType != null)
                {
                    SubscribeToEventForTask(task);
                    subscribedCount++;
                }
            }
        }

        PublishEvent(new BatchQueuedEvent(batch));

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Batch enqueued: {batch}{(subscribedCount > 0 ? $" ({subscribedCount} tasks subscribed to events)" : "")}");

        if (ShouldProcessQueueAutomatically && (QueueState == QueueState.Idle || QueueState == QueueState.Stopped))
            StartQueue();

        return this;
    }

    /// <summary>
    /// Creates and enqueues a simple task.
    /// </summary>
    /// <param name="customId">Optional custom identifier.</param>
    /// <param name="isBlocking">Whether the task blocks subsequent tasks.</param>
    /// <param name="executeAction">The action to execute.</param>
    /// <param name="completionCondition">The completion condition.</param>
    /// <param name="onCompleted">Optional completion callback.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <returns>The created task.</returns>
    public QueuedTask EnqueueTask(
        string? customId,
        bool isBlocking,
        Action? executeAction,
        TaskCompletionCondition? completionCondition,
        Action<QueuedTask>? onCompleted = null,
        TimeSpan? timeout = null)
    {
        var task = new QueuedTask(customId, isBlocking)
        {
            ExecuteAction = executeAction,
            CompletionCondition = completionCondition ?? TaskCompletionCondition.Immediate(),
            OnCompleted = onCompleted,
            Timeout = timeout
        };

        EnqueueTask(task);
        return task;
    }

    /// <summary>
    /// Inserts a task after another task in the queue.
    /// </summary>
    /// <param name="task">The task to insert.</param>
    /// <param name="afterTaskSystemId">The system ID of the task to insert after.</param>
    /// <returns>True if the task was successfully inserted; false if the target task was not found.</returns>
    public bool InsertTaskAfter(QueuedTask task, Guid afterTaskSystemId)
    {
        return InsertTaskAfterInternal(task, item => item.IsTask && item.AsTask().SystemId == afterTaskSystemId, afterTaskSystemId.ToString());
    }

    /// <summary>
    /// Inserts a task after another task in the queue by custom ID.
    /// </summary>
    /// <param name="task">The task to insert.</param>
    /// <param name="afterTaskCustomId">The custom ID of the task to insert after.</param>
    /// <returns>True if the task was successfully inserted; false if the target task was not found.</returns>
    public bool InsertTaskAfter(QueuedTask task, string afterTaskCustomId)
    {
        return InsertTaskAfterInternal(task, item => item.IsTask && item.AsTask().CustomId == afterTaskCustomId, afterTaskCustomId);
    }

    /// <summary>
    /// Internal method to insert a task after another item matching a predicate.
    /// </summary>
    private bool InsertTaskAfterInternal(QueuedTask task, Func<QueueItemWrapper, bool> predicate, string targetDescription)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot insert task - module is not active.");
            return false;
        }

        bool subscribed = false;
        bool inserted = false;
        lock (queueLock)
        {
            var targetIndex = unifiedQueue.FindIndex(item => predicate(item));
            if (targetIndex == -1)
            {
                if (EnableLogging)
                    NoireLogger.LogWarning(this, $"Cannot insert task - target task with ID '{targetDescription}' not found.");
                return false;
            }

            var currentExecutingIndex = -1;
            if (currentTask != null)
            {
                currentExecutingIndex = unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask));
            }
            else if (currentBatch != null)
            {
                currentExecutingIndex = unifiedQueue.FindIndex(item => item.IsBatch && ReferenceEquals(item.AsBatch(), currentBatch));
            }

            if (targetIndex < currentExecutingIndex)
            {
                if (EnableLogging)
                    NoireLogger.LogWarning(this, $"Cannot insert task - target task '{targetDescription}' is already executed. Can only insert after queued or current items.");
                return false;
            }

            unifiedQueue.Insert(targetIndex + 1, QueueItemWrapper.FromTask(task));
            totalTasksQueued++;
            inserted = true;

            if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent && task.CompletionCondition.EventType != null)
            {
                SubscribeToEventForTask(task);
                subscribed = true;
            }
        }

        if (inserted)
        {
            PublishEvent(new TaskQueuedEvent(task));

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Task inserted after {targetDescription}: {task}{(subscribed ? " (event subscribed)" : "")}");

            if (ShouldProcessQueueAutomatically && (QueueState == QueueState.Idle || QueueState == QueueState.Stopped))
                StartQueue();
        }

        return inserted;
    }

    /// <summary>
    /// Skips the next N tasks in the queue by cancelling them.
    /// </summary>
    /// <param name="count">The number of queued tasks to skip. Needs to be greater than 0.</param>
    /// <param name="includeCurrentTask">If true, will mark the currently executing task for skipping if it exists.<br/>
    /// If true, argument <paramref name="count"/> will include the current task in the skip count.</param>
    /// <param name="boundaryType">Defines how context boundaries are checked.
    /// CrossContext (default): fully cross-context, SameContext: same batch or both standalone, StrictWithBoundaryCheck: no batch separation allowed.</param>
    /// <returns>The number of tasks that were actually skipped.</returns>
    public int SkipNextTasks(int count, bool includeCurrentTask = false, ContextDefinition boundaryType = ContextDefinition.CrossContext)
    {
        if (count <= 0)
            return 0;

        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                int skipped = 0;
                bool isInBatch = currentBatch != null;

                if (includeCurrentTask)
                {
                    if (isInBatch && currentBatch != null)
                    {
                        var currentTaskInBatch = currentBatch.Tasks.FirstOrDefault(t =>
                            t.Status == TaskStatus.Executing ||
                            t.Status == TaskStatus.WaitingForCompletion ||
                            t.Status == TaskStatus.WaitingForPostDelay);

                        if (currentTaskInBatch != null && CancelTaskInternal(currentTaskInBatch))
                        {
                            skipped++;
                            if (EnableLogging)
                                NoireLogger.LogInfo(this, $"Skipped current task in batch: {currentTaskInBatch}");
                        }
                    }
                    else if (currentTask != null)
                    {
                        if (CancelTaskInternal(currentTask))
                        {
                            skipped++;
                            if (EnableLogging)
                                NoireLogger.LogInfo(this, $"Skipped current task: {currentTask}");
                        }
                    }
                }

                if (skipped >= count)
                    return skipped;

                skipped += SkipTasksWithBoundary(count - skipped, isInBatch, boundaryType);

                if (EnableLogging && skipped > 0)
                {
                    var boundaryInfo = boundaryType != ContextDefinition.CrossContext ? $" (boundary: {boundaryType})" : "";
                    NoireLogger.LogInfo(this, $"Skipped {skipped} task(s){(includeCurrentTask ? " (including current task)" : "")}{boundaryInfo}.");
                }

                return skipped;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Skips the current task regardless of its status.
    /// If currently processing a batch, skips the current task within that batch.
    /// </summary>
    /// <returns>true if the current task was skipped; otherwise, false.</returns>
    public bool SkipCurrentTask()
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                if (currentBatch != null)
                {
                    var currentTaskInBatch = currentBatch.Tasks.FirstOrDefault(t =>
                        t.Status == TaskStatus.Executing ||
                        t.Status == TaskStatus.WaitingForCompletion ||
                        t.Status == TaskStatus.WaitingForPostDelay);

                    if (currentTaskInBatch != null)
                    {
                        if (!CancelTaskInternal(currentTaskInBatch))
                            return false;

                        if (EnableLogging)
                            NoireLogger.LogInfo(this, $"Skipped current task in batch: {currentTaskInBatch}");

                        return true;
                    }

                    return false;
                }

                if (currentTask == null)
                    return false;

                if (!CancelTaskInternal(currentTask))
                    return false;

                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Skipped current task: {currentTask}");

                return true;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Skips the current batch regardless of its status.
    /// </summary>
    /// <returns>true if the current batch was skipped; otherwise, false.</returns>
    public bool SkipCurrentBatch()
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                if (currentBatch == null)
                    return false;

                if (!CancelBatchInternal(currentBatch))
                    return false;

                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Skipped current batch: {currentBatch}");

                return true;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Skips the next N batches in the queue by cancelling them.
    /// </summary>
    /// <param name="count">The number of queued batches to skip. Needs to be greater than 0.</param>
    /// <param name="includeCurrentBatch">If true, will mark the currently processing batch for skipping if it exists.</param>
    /// <returns>The number of batches that were actually skipped.</returns>
    public int SkipNextBatches(int count, bool includeCurrentBatch = false)
    {
        if (count <= 0)
            return 0;

        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                int skipped = 0;

                if (includeCurrentBatch && currentBatch != null)
                {
                    if (CancelBatchInternal(currentBatch))
                    {
                        skipped++;
                        if (EnableLogging)
                            NoireLogger.LogInfo(this, $"Skipped current batch: {currentBatch}");
                    }
                }

                var queuedBatches = count > skipped
                    ? unifiedQueue
                        .Where(item => item.IsBatch && item.AsBatch().Status == BatchStatus.Queued)
                        .Take(count - skipped)
                        .Select(item => item.AsBatch())
                        .ToList()
                    : [];

                foreach (var batch in queuedBatches)
                {
                    if (CancelBatchInternal(batch))
                        skipped++;
                }

                if (EnableLogging && skipped > 0)
                    NoireLogger.LogInfo(this, $"Skipped {skipped} batch(es){(includeCurrentBatch ? " (including current batch)" : "")}.");

                return skipped;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Jumps to a specific task by system ID, cancelling all queued items before it.
    /// </summary>
    /// <param name="targetSystemId">The system ID of the task to jump to.</param>
    /// <returns>True if the jump was successful; false if the target task was not found or not in Queued status.</returns>
    public bool JumpToTask(Guid targetSystemId)
    {
        return JumpToTaskInternal(item => item.IsTask && item.AsTask().SystemId == targetSystemId, targetSystemId.ToString());
    }

    /// <summary>
    /// Jumps to a specific task by custom ID, cancelling all queued items before it.
    /// </summary>
    /// <param name="targetCustomId">The custom ID of the task to jump to.</param>
    /// <returns>True if the jump was successful; false if the target task was not found or not in Queued status.</returns>
    public bool JumpToTask(string targetCustomId)
    {
        return JumpToTaskInternal(item => item.IsTask && item.AsTask().CustomId == targetCustomId, targetCustomId);
    }

    /// <summary>
    /// Internal method to jump to a task matching a predicate.
    /// </summary>
    private bool JumpToTaskInternal(Func<QueueItemWrapper, bool> predicate, string targetDescription)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                var targetItem = unifiedQueue.FirstOrDefault(predicate);
                if (targetItem == null)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - task with ID {targetDescription} not found.");
                    return false;
                }

                var targetTask = targetItem.AsTask();
                if (targetTask.Status != TaskStatus.Queued)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - task {targetTask} is not in Queued status (Status: {targetTask.Status}).");
                    return false;
                }

                var targetIndex = unifiedQueue.IndexOf(targetItem);
                var itemsToCancel = unifiedQueue.Take(targetIndex).ToList();
                int cancelled = 0;

                foreach (var item in itemsToCancel)
                {
                    if (item.IsTask)
                    {
                        var task = item.AsTask();
                        if (task.Status == TaskStatus.Queued ||
                            task.Status == TaskStatus.Executing ||
                            task.Status == TaskStatus.WaitingForCompletion ||
                            task.Status == TaskStatus.WaitingForPostDelay)
                        {
                            if (CancelTaskInternal(task))
                                cancelled++;
                        }
                    }
                    else if (item.IsBatch)
                    {
                        var batch = item.AsBatch();
                        if (batch.Status == BatchStatus.Queued || batch.Status == BatchStatus.Processing)
                        {
                            if (CancelBatchInternal(batch))
                                cancelled++;
                        }
                    }
                }

                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Jumped to task {targetTask}, cancelled {cancelled} item(s) before it.");

                return true;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Gets a task by its system ID.
    /// </summary>
    public QueuedTask? GetTaskBySystemId(Guid systemId)
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .FirstOrDefault(t => t.SystemId == systemId);
        }
    }

    /// <summary>
    /// Gets a task by its custom ID.
    /// </summary>
    public QueuedTask? GetTaskByCustomId(string customId)
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .FirstOrDefault(t => t.CustomId == customId);
        }
    }

    /// <summary>
    /// Gets all tasks with a specific custom ID.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetTasksByCustomId(string customId)
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .Where(t => t.CustomId == customId)
                .ToList();
        }
    }

    /// <summary>
    /// Gets a batch by its system ID.
    /// </summary>
    public TaskBatch? GetBatchBySystemId(Guid systemId)
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .FirstOrDefault(b => b.SystemId == systemId);
        }
    }

    /// <summary>
    /// Gets a batch by its custom ID.
    /// </summary>
    public TaskBatch? GetBatchByCustomId(string customId)
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .FirstOrDefault(b => b.CustomId == customId);
        }
    }

    /// <summary>
    /// Gets all batches with a specific custom ID.
    /// </summary>
    public IReadOnlyList<TaskBatch> GetBatchesByCustomId(string customId)
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .Where(b => b.CustomId == customId)
                .ToList();
        }
    }

    /// <summary>
    /// Gets a task from a specific batch by their system IDs.
    /// </summary>
    /// <param name="batchSystemId">The system ID of the batch.</param>
    /// <param name="taskSystemId">The system ID of the task.</param>
    /// <returns>The task if found; otherwise, null.</returns>
    public QueuedTask? GetTaskFromBatch(Guid batchSystemId, Guid taskSystemId)
    {
        var batch = GetBatchBySystemId(batchSystemId);
        return batch?.GetTaskBySystemId(taskSystemId);
    }

    /// <summary>
    /// Gets a task from a specific batch by their custom IDs.
    /// </summary>
    /// <param name="batchCustomId">The custom ID of the batch.</param>
    /// <param name="taskCustomId">The custom ID of the task.</param>
    /// <returns>The task if found; otherwise, null.</returns>
    public QueuedTask? GetTaskFromBatch(string batchCustomId, string taskCustomId)
    {
        var batch = GetBatchByCustomId(batchCustomId);
        return batch?.GetTaskByCustomId(taskCustomId);
    }

    /// <summary>
    /// Gets all tasks with a specific custom ID from a batch identified by system ID.
    /// </summary>
    /// <param name="batchSystemId">The system ID of the batch.</param>
    /// <param name="taskCustomId">The custom ID of the tasks to retrieve.</param>
    /// <returns>A read-only list of tasks with the specified custom ID.</returns>
    public IReadOnlyList<QueuedTask> GetTasksFromBatch(Guid batchSystemId, string taskCustomId)
    {
        var batch = GetBatchBySystemId(batchSystemId);
        return batch?.GetTasksByCustomId(taskCustomId) ?? Array.Empty<QueuedTask>();
    }

    /// <summary>
    /// Gets all tasks with a specific custom ID from a batch identified by custom ID.
    /// </summary>
    /// <param name="batchCustomId">The custom ID of the batch.</param>
    /// <param name="taskCustomId">The custom ID of the tasks to retrieve.</param>
    /// <returns>A read-only list of tasks with the specified custom ID.</returns>
    public IReadOnlyList<QueuedTask> GetTasksFromBatch(string batchCustomId, string taskCustomId)
    {
        var batch = GetBatchByCustomId(batchCustomId);
        return batch?.GetTasksByCustomId(taskCustomId) ?? Array.Empty<QueuedTask>();
    }

    /// <summary>
    /// Gets all tasks in the queue.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetAllTasks()
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .ToList();
        }
    }

    /// <summary>
    /// Gets all batches in the queue.
    /// </summary>
    public IReadOnlyList<TaskBatch> GetAllBatches()
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .ToList();
        }
    }

    /// <summary>
    /// Gets the currently executing task.
    /// </summary>
    public QueuedTask? GetCurrentTask()
    {
        lock (queueLock)
        {
            return currentTask;
        }
    }

    /// <summary>
    /// Gets the currently processing batch.
    /// </summary>
    public TaskBatch? GetCurrentBatch()
    {
        lock (queueLock)
        {
            return currentBatch;
        }
    }

    /// <summary>
    /// Cancels a task by its system ID.
    /// </summary>
    public bool CancelTask(Guid systemId)
    {
        lock (queueLock)
        {
            var task = unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .FirstOrDefault(t => t.SystemId == systemId);
            if (task == null)
                return false;

            return CancelTaskInternal(task);
        }
    }

    /// <summary>
    /// Cancels a task by its custom ID.
    /// </summary>
    public bool CancelTask(string customId)
    {
        lock (queueLock)
        {
            var task = unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .FirstOrDefault(t => t.CustomId == customId);
            if (task == null)
                return false;

            return CancelTaskInternal(task);
        }
    }

    /// <summary>
    /// Cancels all tasks with a specific custom ID.
    /// </summary>
    public int CancelAllTasks(string customId)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                var tasks = unifiedQueue
                    .Where(item => item.IsTask)
                    .Select(item => item.AsTask())
                    .Where(t => t.CustomId == customId)
                    .ToList();
                int cancelled = 0;

                foreach (var task in tasks)
                    if (CancelTaskInternal(task))
                        cancelled++;

                return cancelled;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Cancels a batch by its system ID, cancelling all its tasks.
    /// </summary>
    public bool CancelBatch(Guid systemId)
    {
        lock (queueLock)
        {
            var batch = unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .FirstOrDefault(b => b.SystemId == systemId);
            if (batch == null)
                return false;

            return CancelBatchInternal(batch);
        }
    }

    /// <summary>
    /// Cancels a batch by its custom ID, cancelling all its tasks.
    /// </summary>
    public bool CancelBatch(string customId)
    {
        lock (queueLock)
        {
            var batch = unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .FirstOrDefault(b => b.CustomId == customId);
            if (batch == null)
                return false;

            return CancelBatchInternal(batch);
        }
    }

    /// <summary>
    /// Cancels all batches with a specific custom ID.
    /// </summary>
    public int CancelAllBatches(string customId)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                var batches = unifiedQueue
                    .Where(item => item.IsBatch)
                    .Select(item => item.AsBatch())
                    .Where(b => b.CustomId == customId)
                    .ToList();
                int cancelled = 0;

                foreach (var batch in batches)
                    if (CancelBatchInternal(batch))
                        cancelled++;

                return cancelled;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Internal method to cancel a task.
    /// </summary>
    private bool CancelTaskInternal(QueuedTask task)
    {
        if (task.Status == TaskStatus.Completed || task.Status == TaskStatus.Cancelled || task.Status == TaskStatus.Failed)
            return false;

        UnsubscribeTask(task);

        try
        {
            task.OnCancelled?.Invoke(task);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnCancelled callback threw an exception.");
        }

        PublishEvent(new TaskCancelledEvent(task));

        if (task.ApplyPostDelayOnCancellation && !task.PostDelayStartTicks.HasValue)
        {
            if (task.PostCompletionDelayProvider != null)
                task.PostCompletionDelay = task.PostCompletionDelayProvider(task);

            if (task.PostCompletionDelay.HasValue)
            {
                task.PostDelayStartTicks = Environment.TickCount64;
                task.Status = TaskStatus.WaitingForPostDelay;

                if (task.Timeout.HasValue)
                    task.PauseTimeout();

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Task entering post-cancellation delay: {task}");

                // We'll handle the actual cancellation finalization after the delay
                return true;
            }
        }

        task.Status = TaskStatus.Cancelled;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCancelled++;

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

        if (task.FailParentBatchOnCancel && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Failing parent batch due to task cancellation: {task}");

            FailBatch(task.ParentBatch, new Exception($"Batch failed by task cancellation: {task}"));
        }

        if (task.CancelParentBatchOnCancel && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Cancelling parent batch due to task cancellation: {task}");

            CancelBatchInternal(task.ParentBatch);
        }

        if (task.StopQueueOnCancel)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to task cancellation: {task}");
            StopQueue();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Task cancelled: {task}");

        return true;
    }

    /// <summary>
    /// Internal method to cancel a batch and all its tasks.
    /// </summary>
    private bool CancelBatchInternal(TaskBatch batch)
    {
        if (batch.Status == BatchStatus.Completed || batch.Status == BatchStatus.Cancelled || batch.Status == BatchStatus.Failed)
            return false;

        foreach (var task in batch.Tasks)
        {
            if (task.Status != TaskStatus.Completed && task.Status != TaskStatus.Cancelled && task.Status != TaskStatus.Failed)
            {
                UnsubscribeTask(task);
                task.Status = TaskStatus.Cancelled;
                task.FinishedAtTicks = Environment.TickCount64;
                tasksCancelled++;
            }
        }

        try
        {
            batch.OnCancelled?.Invoke(batch);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Batch OnCancelled callback threw an exception.");
        }

        PublishEvent(new BatchCancelledEvent(batch));

        if (batch.ApplyPostDelayOnCancellation && !batch.PostDelayStartTicks.HasValue)
        {
            if (batch.PostCompletionDelayProvider != null)
                batch.PostCompletionDelay = batch.PostCompletionDelayProvider(batch);

            if (batch.PostCompletionDelay.HasValue)
            {
                batch.PostDelayStartTicks = Environment.TickCount64;
                batch.Status = BatchStatus.WaitingForPostDelay;

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Batch entering post-cancellation delay: {batch}");

                // We'll handle the actual cancellation finalization after the delay
                return true;
            }
        }

        batch.Status = BatchStatus.Cancelled;
        batch.FinishedAtTicks = Environment.TickCount64;
        batchesCancelled++;

        if (ReferenceEquals(currentBatch, batch))
            currentBatch = null;

        if (batch.StopQueueOnCancel)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to batch cancellation: {batch}");
            StopQueue();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Batch cancelled: {batch}");

        return true;
    }

    /// <summary>
    /// Clears all tasks and batches from the queue.
    /// </summary>
    /// <returns>The number of items cleared.</returns>
    public int ClearQueue()
    {
        var previousState = QueueState;

        QueueState = QueueState.Paused;

        List<QueueItemWrapper> removed = new();
        try
        {
            lock (queueLock)
            {
                if (currentTask != null)
                    CancelTaskInternal(currentTask);

                if (currentBatch != null)
                    CancelBatchInternal(currentBatch);

                removed = unifiedQueue.ToList();
                unifiedQueue.Clear();
                currentTask = null;
                currentBatch = null;
            }
        }
        finally
        {
            foreach (var item in removed)
            {
                if (item.IsTask)
                    UnsubscribeTask(item.AsTask());
                else if (item.IsBatch)
                {
                    foreach (var task in item.AsBatch().Tasks)
                        UnsubscribeTask(task);
                }
            }

            var taskCount = removed.Count(item => item.IsTask);
            var batchCount = removed.Count(item => item.IsBatch);

            PublishEvent(new QueueClearedEvent(removed.Count));

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Queue cleared: {taskCount} tasks and {batchCount} batches removed.");

            QueueState = previousState;
        }

        return removed.Count;
    }

    /// <summary>
    /// Removes completed/cancelled/failed tasks from the queue.
    /// </summary>
    /// <returns>The number of tasks removed.</returns>
    public int ClearCompletedTasks()
    {
        List<QueuedTask> toRemove;
        lock (queueLock)
        {
            toRemove = unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .Where(t =>
                    t.Status == TaskStatus.Completed ||
                    t.Status == TaskStatus.Cancelled ||
                    t.Status == TaskStatus.Failed)
                .ToList();

            unifiedQueue.RemoveAll(item => item.IsTask && toRemove.Contains(item.AsTask()));

            if (currentTask != null && toRemove.Contains(currentTask))
                currentTask = null;
        }

        foreach (var task in toRemove)
            UnsubscribeTask(task);

        if (EnableLogging && toRemove.Count > 0)
            NoireLogger.LogDebug(this, $"Cleared {toRemove.Count} completed tasks.");

        return toRemove.Count;
    }

    /// <summary>
    /// Clears all completed/cancelled/failed batches from the queue.
    /// </summary>
    /// <returns>The number of batches removed.</returns>
    public int ClearCompletedBatches()
    {
        List<TaskBatch> toRemove;
        lock (queueLock)
        {
            toRemove = unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .Where(b =>
                    b.Status == BatchStatus.Completed ||
                    b.Status == BatchStatus.Cancelled ||
                    b.Status == BatchStatus.Failed)
                .ToList();

            unifiedQueue.RemoveAll(item => item.IsBatch && toRemove.Contains(item.AsBatch()));

            if (currentBatch != null && toRemove.Contains(currentBatch))
                currentBatch = null;
        }

        if (EnableLogging && toRemove.Count > 0)
            NoireLogger.LogDebug(this, $"Cleared {toRemove.Count} completed batches.");

        return toRemove.Count;
    }

    /// <summary>
    /// Skips tasks based on the specified boundary type.
    /// </summary>
    /// <param name="count">Maximum number of tasks to skip.</param>
    /// <param name="isInBatch">Whether we're currently in a batch.</param>
    /// <param name="boundaryType">The boundary type to use.</param>
    /// <returns>Number of tasks skipped.</returns>
    private int SkipTasksWithBoundary(int count, bool isInBatch, ContextDefinition boundaryType)
    {
        return boundaryType switch
        {
            ContextDefinition.CrossContext => SkipTasksNoBoundary(count, isInBatch),
            ContextDefinition.SameContext => SkipTasksSameContext(count, isInBatch),
            ContextDefinition.StrictWithBoundaryCheck => SkipTasksStrictBoundary(count, isInBatch),
            _ => 0
        };
    }

    /// <summary>
    /// Skips tasks with no boundary checks (fully cross-context).
    /// </summary>
    private int SkipTasksNoBoundary(int count, bool isInBatch)
    {
        int skipped = 0;

        if (isInBatch && currentBatch != null)
        {
            var remainingTasksInBatch = currentBatch.Tasks
                .Where(t => t.Status == TaskStatus.Queued)
                .Take(count)
                .ToList();

            foreach (var task in remainingTasksInBatch)
            {
                if (CancelTaskInternal(task))
                    skipped++;
            }
        }

        if (skipped >= count)
            return skipped;

        foreach (var item in unifiedQueue)
        {
            if (skipped >= count)
                break;

            if (item.IsTask)
            {
                var task = item.AsTask();
                if (task.Status == TaskStatus.Queued)
                {
                    if (CancelTaskInternal(task))
                        skipped++;
                }
            }
            else if (item.IsBatch)
            {
                var batch = item.AsBatch();
                var tasksInBatch = batch.Tasks
                    .Where(t => t.Status == TaskStatus.Queued)
                    .Take(count - skipped)
                    .ToList();

                foreach (var task in tasksInBatch)
                {
                    if (CancelTaskInternal(task))
                        skipped++;
                }
            }
        }

        return skipped;
    }

    /// <summary>
    /// Skips tasks with SameContext boundary (same batch or both standalone with batches in between allowed).
    /// </summary>
    private int SkipTasksSameContext(int count, bool isInBatch)
    {
        int skipped = 0;

        if (isInBatch && currentBatch != null)
        {
            var remainingTasksInBatch = currentBatch.Tasks
                .Where(t => t.Status == TaskStatus.Queued)
                .Take(count)
                .ToList();

            foreach (var task in remainingTasksInBatch)
            {
                if (CancelTaskInternal(task))
                    skipped++;
            }
        }
        else
        {
            foreach (var item in unifiedQueue)
            {
                if (skipped >= count)
                    break;

                if (item.IsTask)
                {
                    var task = item.AsTask();
                    if (task.Status == TaskStatus.Queued)
                    {
                        if (CancelTaskInternal(task))
                            skipped++;
                    }
                }
            }
        }

        return skipped;
    }

    /// <summary>
    /// Skips tasks with StrictWithBoundaryCheck (same batch or standalone with no batch separation).
    /// </summary>
    private int SkipTasksStrictBoundary(int count, bool isInBatch)
    {
        int skipped = 0;

        if (isInBatch && currentBatch != null)
        {
            var remainingTasksInBatch = currentBatch.Tasks
                .Where(t => t.Status == TaskStatus.Queued)
                .Take(count)
                .ToList();

            foreach (var task in remainingTasksInBatch)
            {
                if (CancelTaskInternal(task))
                    skipped++;
            }
        }
        else
        {
            foreach (var item in unifiedQueue)
            {
                if (skipped >= count)
                    break;

                if (item.IsBatch)
                    break;

                if (item.IsTask)
                {
                    var task = item.AsTask();
                    if (task.Status == TaskStatus.Queued)
                    {
                        if (CancelTaskInternal(task))
                            skipped++;
                    }
                }
            }
        }

        return skipped;
    }
}
