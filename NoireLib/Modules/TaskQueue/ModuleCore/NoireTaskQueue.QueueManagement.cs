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
            batch.OwningQueue = this;
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
    /// Inserts a task after another task in the queue with context boundary checking.
    /// </summary>
    /// <param name="task">The task to insert.</param>
    /// <param name="afterTaskSystemId">The system ID of the task to insert after.</param>
    /// <param name="contextDefinition">Defines how context boundaries are checked.</param>
    /// <returns>True if the task was successfully inserted; false if the target task was not found.</returns>
    public bool InsertTaskAfter(QueuedTask task, Guid afterTaskSystemId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
        => InsertTaskAfterInternal(task, t => t.SystemId == afterTaskSystemId, afterTaskSystemId.ToString(), contextDefinition);


    /// <summary>
    /// Inserts a task after another task in the queue by custom ID with context boundary checking.
    /// </summary>
    /// <param name="task">The task to insert.</param>
    /// <param name="afterTaskCustomId">The custom ID of the task to insert after.</param>
    /// <param name="contextDefinition">Defines how context boundaries are checked.</param>
    /// <returns>True if the task was successfully inserted; false if the target task was not found.</returns>
    public bool InsertTaskAfter(QueuedTask task, string afterTaskCustomId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
        => InsertTaskAfterInternal(task, t => t.CustomId == afterTaskCustomId, afterTaskCustomId, contextDefinition);

    /// <summary>
    /// Internal method to insert a task after another item matching a predicate.
    /// </summary>
    private bool InsertTaskAfterInternal(QueuedTask task, Func<QueuedTask, bool> predicate, string targetDescription, ContextDefinition contextDefinition)
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
            bool isInBatch = currentBatch != null;
            QueuedTask? targetTask = GetTaskInternal(predicate, contextDefinition);

            if (targetTask == null)
            {
                if (EnableLogging)
                    NoireLogger.LogWarning(this, $"Cannot insert task - target task with ID '{targetDescription}' not found in specified context.");
                return false;
            }

            if (targetTask.ParentBatch != null)
            {
                var batch = targetTask.ParentBatch;
                var taskIndex = batch.Tasks.IndexOf(targetTask);
                if (taskIndex == -1)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot insert task - target task not found in its parent batch.");
                    return false;
                }

                var currentGlobalIndex = GetCurrentTaskGlobalIndex();
                if (currentGlobalIndex >= 0)
                {
                    var targetGlobalIndex = GetTaskGlobalIndex(targetTask);
                    if (targetGlobalIndex < currentGlobalIndex)
                    {
                        if (EnableLogging)
                            NoireLogger.LogWarning(this, $"Cannot insert task - target task '{targetDescription}' is before the currently executing task (global index {targetGlobalIndex} < {currentGlobalIndex}). Can only insert after current or queued items.");
                        return false;
                    }
                }

                batch.Tasks.Insert(taskIndex + 1, task);
                task.ParentBatch = batch;
                totalTasksQueued++;
                inserted = true;
            }
            else
            {
                var targetIndex = unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), targetTask));
                if (targetIndex == -1)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot insert task - target task with ID '{targetDescription}' not found.");
                    return false;
                }

                var currentExecutingIndex = -1;

                if (currentTask != null)
                    currentExecutingIndex = unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask));
                else if (currentBatch != null)
                    currentExecutingIndex = unifiedQueue.FindIndex(item => item.IsBatch && ReferenceEquals(item.AsBatch(), currentBatch));

                if (targetIndex < currentExecutingIndex)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot insert task - target task '{targetDescription}' is already executed. Can only insert after queued or current items.");
                    return false;
                }

                unifiedQueue.Insert(targetIndex + 1, QueueItemWrapper.FromTask(task));
                totalTasksQueued++;
                inserted = true;
            }

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
    /// CrossContext (default): fully cross-context, SameContext: same batch or both standalone, SameContextStrict: no batch separation allowed.</param>
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
        return ExecuteWithPauseResume(() =>
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
        });
    }

    /// <summary>
    /// Skips the current batch regardless of its status.
    /// </summary>
    /// <returns>true if the current batch was skipped; otherwise, false.</returns>
    public bool SkipCurrentBatch()
    {
        return ExecuteWithPauseResume(() =>
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
        });
    }

    /// <summary>
    /// Executes an action with pause/resume pattern.<br/>
    /// The queue will be paused before executing the action and resumed afterwards if it was running before.<br/>
    /// </summary>
    private T ExecuteWithPauseResume<T>(Func<T> action)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            return action();
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
    /// Jumps to a specific task by system ID with context boundary checking.
    /// </summary>
    /// <param name="targetSystemId">The system ID of the task to jump to.</param>
    /// <param name="boundaryType">Defines how context boundaries are checked.
    /// CrossContext (default): fully cross-context, SameContext: same batch or both standalone, SameContextStrict: no batch separation allowed.</param>
    /// <returns>True if the jump was successful; false if the target task was not found, not in Queued status, or context boundary was violated.</returns>
    public bool JumpToTask(Guid targetSystemId, ContextDefinition boundaryType = ContextDefinition.CrossContext)
    {
        return JumpToTaskWithBoundaryInternal(
            item => item.IsTask && item.AsTask().SystemId == targetSystemId,
            targetSystemId.ToString(),
            boundaryType);
    }

    /// <summary>
    /// Jumps to a specific task by custom ID with context boundary checking.
    /// </summary>
    /// <param name="targetCustomId">The custom ID of the task to jump to.</param>
    /// <param name="boundaryType">Defines how context boundaries are checked.
    /// CrossContext (default): fully cross-context, SameContext: same batch or both standalone, SameContextStrict: no batch separation allowed.</param>
    /// <returns>True if the jump was successful; false if the target task was not found, not in Queued status, or context boundary was violated.</returns>
    public bool JumpToTask(string targetCustomId, ContextDefinition boundaryType = ContextDefinition.CrossContext)
    {
        return JumpToTaskWithBoundaryInternal(
            item => item.IsTask && item.AsTask().CustomId == targetCustomId,
            targetCustomId,
            boundaryType);
    }

    /// <summary>
    /// Jumps to a specific task by system ID within a specific batch, cancelling all queued items before it.
    /// </summary>
    /// <param name="batchSystemId">The system ID of the batch containing the task.</param>
    /// <param name="taskSystemId">The system ID of the task to jump to.</param>
    /// <returns>True if the jump was successful; false if the batch or task was not found or not in appropriate status.</returns>
    public bool JumpToTaskInBatch(Guid batchSystemId, Guid taskSystemId)
    {
        return JumpToTaskInBatchInternal(
            batchSystemId,
            batch => batch.GetTaskBySystemId(taskSystemId),
            $"{batchSystemId}/{taskSystemId}");
    }

    /// <summary>
    /// Jumps to a specific task by custom ID within a specific batch, cancelling all queued items before it.
    /// </summary>
    /// <param name="batchCustomId">The custom ID of the batch containing the task.</param>
    /// <param name="taskCustomId">The custom ID of the task to jump to.</param>
    /// <returns>True if the jump was successful; false if the batch or task was not found or not in appropriate status.</returns>
    public bool JumpToTaskInBatch(string batchCustomId, string taskCustomId)
    {
        return JumpToTaskInBatchInternal(
            batchCustomId,
            batch => batch.GetTaskByCustomId(taskCustomId),
            $"{batchCustomId}/{taskCustomId}");
    }

    /// <summary>
    /// Internal method to jump to a task within a specific batch.
    /// </summary>
    private bool JumpToTaskInBatchInternal<TId>(TId batchId, Func<TaskBatch, QueuedTask?> taskFinder, string targetDescription)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                var batch = batchId is Guid guid
                    ? GetBatchBySystemId(guid)
                    : GetBatchByCustomId(batchId?.ToString() ?? string.Empty);

                if (batch == null)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - batch with ID {batchId} not found.");
                    return false;
                }

                var targetTask = taskFinder(batch);
                if (targetTask == null)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - task with ID {targetDescription} not found in batch.");
                    return false;
                }

                if (targetTask.Status != TaskStatus.Queued)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - task {targetTask} is not in Queued status (Status: {targetTask.Status}).");
                    return false;
                }

                var batchWrapper = QueueItemWrapper.FromBatch(batch);
                var batchIndex = unifiedQueue.FindIndex(item => item.IsBatch && ReferenceEquals(item.AsBatch(), batch));

                if (batchIndex == -1)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - batch not found in queue.");
                    return false;
                }

                int cancelled = 0;

                var itemsBeforeBatch = unifiedQueue.Take(batchIndex).ToList();
                foreach (var item in itemsBeforeBatch)
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
                        var batchItem = item.AsBatch();
                        if (batchItem.Status == BatchStatus.Queued || batchItem.Status == BatchStatus.Processing)
                        {
                            if (CancelBatchInternal(batchItem))
                                cancelled++;
                        }
                    }
                }

                var tasksBeforeTarget = batch.Tasks.TakeWhile(t => !ReferenceEquals(t, targetTask)).ToList();
                foreach (var task in tasksBeforeTarget)
                {
                    if (task.Status == TaskStatus.Queued ||
                        task.Status == TaskStatus.Executing ||
                        task.Status == TaskStatus.WaitingForCompletion ||
                        task.Status == TaskStatus.WaitingForPostDelay)
                    {
                        if (CancelTaskInternal(task))
                            cancelled++;
                    }
                }

                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Jumped to task {targetTask} in batch {batch}, cancelled {cancelled} item(s) before it.");

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
    /// Internal method to jump to a task with context boundary checking.
    /// </summary>
    private bool JumpToTaskWithBoundaryInternal(Func<QueueItemWrapper, bool> predicate, string targetDescription, ContextDefinition boundaryType)
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

                bool isInBatch = currentBatch != null;
                bool targetIsInBatch = targetTask.ParentBatch != null;

                if (boundaryType == ContextDefinition.SameContext)
                {
                    if (isInBatch != targetIsInBatch)
                    {
                        if (EnableLogging)
                            NoireLogger.LogWarning(this, $"Cannot jump to task - context boundary violation (SameContext). Current is {(isInBatch ? "in batch" : "standalone")}, target is {(targetIsInBatch ? "in batch" : "standalone")}.");
                        return false;
                    }

                    if (isInBatch && currentBatch != null && !ReferenceEquals(targetTask.ParentBatch, currentBatch))
                    {
                        if (EnableLogging)
                            NoireLogger.LogWarning(this, $"Cannot jump to task - target task is in a different batch.");
                        return false;
                    }
                }
                else if (boundaryType == ContextDefinition.SameContextStrict)
                {
                    if (isInBatch != targetIsInBatch)
                    {
                        if (EnableLogging)
                            NoireLogger.LogWarning(this, $"Cannot jump to task - context boundary violation (SameContextStrict). Current is {(isInBatch ? "in batch" : "standalone")}, target is {(targetIsInBatch ? "in batch" : "standalone")}.");
                        return false;
                    }

                    if (isInBatch && currentBatch != null && !ReferenceEquals(targetTask.ParentBatch, currentBatch))
                    {
                        if (EnableLogging)
                            NoireLogger.LogWarning(this, $"Cannot jump to task - target task is in a different batch.");
                        return false;
                    }

                    if (!isInBatch)
                    {
                        var currentIndex = currentTask != null
                            ? unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
                            : -1;
                        var targetTaskIndex = unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), targetTask));

                        if (currentIndex >= 0 && targetTaskIndex > currentIndex)
                        {
                            var itemsBetween = unifiedQueue.Skip(currentIndex + 1).Take(targetTaskIndex - currentIndex - 1);
                            if (itemsBetween.Any(item => item.IsBatch))
                            {
                                if (EnableLogging)
                                    NoireLogger.LogWarning(this, $"Cannot jump to task - batch boundary found between current and target task (SameContextStrict).");
                                return false;
                            }
                        }
                    }
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

                var boundaryInfo = boundaryType != ContextDefinition.CrossContext ? $" (boundary: {boundaryType})" : "";
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Jumped to task {targetTask}, cancelled {cancelled} item(s) before it{boundaryInfo}.");

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
    /// Gets a task by its system ID with context boundary checking.
    /// </summary>
    public QueuedTask? GetTaskBySystemId(Guid systemId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
        => GetTaskInternal(t => t.SystemId == systemId, contextDefinition);

    /// <summary>
    /// Gets a task by its custom ID with context boundary checking.
    /// </summary>
    public QueuedTask? GetTaskByCustomId(string customId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
        => GetTaskInternal(t => t.CustomId == customId, contextDefinition);

    /// <summary>
    /// Internal method to get a task matching a predicate with context boundary checking.
    /// </summary>
    private QueuedTask? GetTaskInternal(Func<QueuedTask, bool> predicate, ContextDefinition contextDefinition)
    {
        lock (queueLock)
        {
            bool isInBatch = currentBatch != null;

            return contextDefinition switch
            {
                ContextDefinition.CrossContext => GetTaskCrossContext(predicate),
                ContextDefinition.SameContext => GetTaskSameContext(predicate, isInBatch),
                ContextDefinition.SameContextStrict => GetTaskSameContextStrict(predicate, isInBatch),
                _ => null
            };
        }
    }

    private QueuedTask? GetTaskCrossContext(Func<QueuedTask, bool> predicate)
    {
        foreach (var item in unifiedQueue)
        {
            if (item.IsTask)
            {
                var task = item.AsTask();
                if (predicate(task))
                    return task;
            }
            else if (item.IsBatch)
            {
                var batch = item.AsBatch();
                var matchingTask = batch.Tasks.FirstOrDefault(predicate);
                if (matchingTask != null)
                    return matchingTask;
            }
        }

        return null;
    }

    private QueuedTask? GetTaskSameContext(Func<QueuedTask, bool> predicate, bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
            return currentBatch.Tasks.FirstOrDefault(predicate);

        return unifiedQueue
            .Where(item => item.IsTask)
            .Select(item => item.AsTask())
            .FirstOrDefault(predicate);
    }

    private QueuedTask? GetTaskSameContextStrict(Func<QueuedTask, bool> predicate, bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
            return currentBatch.Tasks.FirstOrDefault(predicate);

        var currentIndex = currentTask != null
            ? unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
            : -1;

        for (int i = 0; i < unifiedQueue.Count; i++)
        {
            var item = unifiedQueue[i];

            if (item.IsBatch && (currentIndex == -1 || i > currentIndex))
                break;

            if (item.IsTask)
            {
                var task = item.AsTask();
                if (predicate(task))
                    return task;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all tasks matching a predicate with context boundary checking.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetTasksByPredicate(Func<QueuedTask, bool> predicate, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
    {
        lock (queueLock)
        {
            bool isInBatch = currentBatch != null;

            return contextDefinition switch
            {
                ContextDefinition.CrossContext => GetTasksByPredicateCrossContext(predicate),
                ContextDefinition.SameContext => GetTasksByPredicateSameContext(predicate, isInBatch),
                ContextDefinition.SameContextStrict => GetTasksByPredicateSameContextStrict(predicate, isInBatch),
                _ => Array.Empty<QueuedTask>()
            };
        }
    }

    private IReadOnlyList<QueuedTask> GetTasksByPredicateCrossContext(Func<QueuedTask, bool> predicate)
    {
        var tasks = new List<QueuedTask>();

        foreach (var item in unifiedQueue)
        {
            if (item.IsTask)
            {
                var task = item.AsTask();
                if (predicate(task))
                    tasks.Add(task);
            }
            else if (item.IsBatch)
            {
                var batch = item.AsBatch();
                tasks.AddRange(batch.Tasks.Where(predicate));
            }
        }

        return tasks;
    }

    private IReadOnlyList<QueuedTask> GetTasksByPredicateSameContext(Func<QueuedTask, bool> predicate, bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
        {
            return currentBatch.Tasks.Where(predicate).ToList();
        }

        return unifiedQueue
            .Where(item => item.IsTask)
            .Select(item => item.AsTask())
            .Where(predicate)
            .ToList();
    }

    private IReadOnlyList<QueuedTask> GetTasksByPredicateSameContextStrict(Func<QueuedTask, bool> predicate, bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
        {
            return currentBatch.Tasks.Where(predicate).ToList();
        }

        var currentIndex = currentTask != null
            ? unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
            : -1;

        var tasks = new List<QueuedTask>();

        for (int i = 0; i < unifiedQueue.Count; i++)
        {
            var item = unifiedQueue[i];

            if (item.IsBatch && (currentIndex == -1 || i > currentIndex))
                break;

            if (item.IsTask)
            {
                var task = item.AsTask();
                if (predicate(task))
                    tasks.Add(task);
            }
        }

        return tasks;
    }

    /// <summary>
    /// Gets all tasks with a specific custom ID with context boundary checking.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetTasksByCustomId(string customId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
    {
        lock (queueLock)
        {
            bool isInBatch = currentBatch != null;

            return contextDefinition switch
            {
                ContextDefinition.CrossContext => GetTasksByCustomIdCrossContext(customId),
                ContextDefinition.SameContext => GetTasksByCustomIdSameContext(customId, isInBatch),
                ContextDefinition.SameContextStrict => GetTasksByCustomIdSameContextStrict(customId, isInBatch),
                _ => Array.Empty<QueuedTask>()
            };
        }
    }

    private IReadOnlyList<QueuedTask> GetTasksByCustomIdCrossContext(string customId)
    {
        var tasks = new List<QueuedTask>();

        foreach (var item in unifiedQueue)
        {
            if (item.IsTask)
            {
                var task = item.AsTask();
                if (task.CustomId == customId)
                    tasks.Add(task);
            }
            else if (item.IsBatch)
            {
                var batch = item.AsBatch();
                tasks.AddRange(batch.Tasks.Where(t => t.CustomId == customId));
            }
        }

        return tasks;
    }

    private IReadOnlyList<QueuedTask> GetTasksByCustomIdSameContext(string customId, bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
        {
            return currentBatch.Tasks.Where(t => t.CustomId == customId).ToList();
        }

        return unifiedQueue
            .Where(item => item.IsTask)
            .Select(item => item.AsTask())
            .Where(t => t.CustomId == customId)
            .ToList();
    }

    private IReadOnlyList<QueuedTask> GetTasksByCustomIdSameContextStrict(string customId, bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
        {
            return currentBatch.Tasks.Where(t => t.CustomId == customId).ToList();
        }

        var currentIndex = currentTask != null
            ? unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
            : -1;

        var tasks = new List<QueuedTask>();

        for (int i = 0; i < unifiedQueue.Count; i++)
        {
            var item = unifiedQueue[i];

            if (item.IsBatch && (currentIndex == -1 || i > currentIndex))
                break;

            if (item.IsTask)
            {
                var task = item.AsTask();
                if (task.CustomId == customId)
                    tasks.Add(task);
            }
        }

        return tasks;
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
    /// Gets a batch by its system ID.
    /// </summary>
    public TaskBatch? GetBatchBySystemId(Guid systemId)
        => GetBatchInternal(b => b.SystemId == systemId);

    /// <summary>
    /// Gets a batch by its custom ID.
    /// </summary>
    public TaskBatch? GetBatchByCustomId(string customId)
        => GetBatchInternal(b => b.CustomId == customId);

    /// <summary>
    /// Internal method to get a batch matching a predicate.
    /// </summary>
    private TaskBatch? GetBatchInternal(Func<TaskBatch, bool> predicate)
    {
        lock (queueLock)
        {
            return unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .FirstOrDefault(predicate);
        }
    }

    /// <summary>
    /// Gets all tasks in the queue.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetAllTasks()
        => GetAllTasks(ContextDefinition.CrossContext);

    /// <summary>
    /// Gets all tasks in the queue with context boundary checking.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetAllTasks(ContextDefinition contextDefinition)
    {
        lock (queueLock)
        {
            bool isInBatch = currentBatch != null;

            return contextDefinition switch
            {
                ContextDefinition.CrossContext => GetAllTasksCrossContext(),
                ContextDefinition.SameContext => GetAllTasksSameContext(isInBatch),
                ContextDefinition.SameContextStrict => GetAllTasksSameContextStrict(isInBatch),
                _ => Array.Empty<QueuedTask>()
            };
        }
    }

    private IReadOnlyList<QueuedTask> GetAllTasksCrossContext()
    {
        var tasks = new List<QueuedTask>();

        foreach (var item in unifiedQueue)
        {
            if (item.IsTask)
                tasks.Add(item.AsTask());
            else if (item.IsBatch)
                tasks.AddRange(item.AsBatch().Tasks);
        }

        return tasks;
    }

    private IReadOnlyList<QueuedTask> GetAllTasksSameContext(bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
            return currentBatch.Tasks.ToList();

        return unifiedQueue
            .Where(item => item.IsTask)
            .Select(item => item.AsTask())
            .ToList();
    }

    private IReadOnlyList<QueuedTask> GetAllTasksSameContextStrict(bool isInBatch)
    {
        if (isInBatch && currentBatch != null)
            return currentBatch.Tasks.ToList();

        var currentIndex = currentTask != null
            ? unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
            : -1;

        var tasks = new List<QueuedTask>();

        for (int i = 0; i < unifiedQueue.Count; i++)
        {
            var item = unifiedQueue[i];

            if (item.IsBatch && (currentIndex == -1 || i > currentIndex))
                break;

            if (item.IsTask)
                tasks.Add(item.AsTask());
        }

        return tasks;
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
    /// Gets the current queue item wrapper (task or batch) being processed, if any.
    /// </summary>
    /// <returns>The current <see cref="QueueItemWrapper"/>, or null if no item is currently being processed.</returns>
    public QueueItemWrapper? GetCurrentQueueItem()
    {
        lock (queueLock)
        {
            return currentItem;
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
    /// Cancels a task by its system ID with context boundary checking.
    /// </summary>
    public bool CancelTask(Guid systemId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
        => CancelTaskByIdInternal(t => t.SystemId == systemId, contextDefinition);

    /// <summary>
    /// Cancels a task by its custom ID with context boundary checking.
    /// </summary>
    public bool CancelTask(string customId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
        => CancelTaskByIdInternal(t => t.CustomId == customId, contextDefinition);

    /// <summary>
    /// Internal method to cancel a task found by predicate with context boundary checking.
    /// </summary>
    private bool CancelTaskByIdInternal(Func<QueuedTask, bool> predicate, ContextDefinition contextDefinition)
    {
        lock (queueLock)
        {
            var task = GetTaskInternal(predicate, contextDefinition);
            if (task == null)
                return false;

            return CancelTaskInternal(task);
        }
    }

    /// <summary>
    /// Cancels all tasks with a specific custom ID with context boundary checking.
    /// </summary>
    public int CancelAllTasks(string customId, ContextDefinition contextDefinition = ContextDefinition.CrossContext)
    {
        return CancelAllItemsInternal(
            item => item.IsTask && item.AsTask().CustomId == customId,
            item => CancelTaskInternal(item.AsTask()),
            contextDefinition
        );
    }

    /// <summary>
    /// Cancels a batch by its system ID, cancelling all its tasks.
    /// </summary>
    public bool CancelBatch(Guid systemId)
        => CancelBatchByIdInternal(b => b.SystemId == systemId);

    /// <summary>
    /// Cancels a batch by its custom ID, cancelling all its tasks.
    /// </summary>
    public bool CancelBatch(string customId)
        => CancelBatchByIdInternal(b => b.CustomId == customId);

    /// <summary>
    /// Fails a batch by its system ID with the specified exception.
    /// </summary>
    /// <param name="systemId">The system ID of the batch to fail.</param>
    /// <param name="exception">The exception that caused the batch to fail.</param>
    /// <returns>True if the batch was found and failed; otherwise, false.</returns>
    public bool FailBatch(Guid systemId, Exception exception)
        => FailBatchByIdInternal(b => b.SystemId == systemId, exception);

    /// <summary>
    /// Fails a batch by its custom ID with the specified exception.
    /// </summary>
    /// <param name="customId">The custom ID of the batch to fail.</param>
    /// <param name="exception">The exception that caused the batch to fail.</param>
    /// <returns>True if the batch was found and failed; otherwise, false.</returns>
    public bool FailBatch(string customId, Exception exception)
        => FailBatchByIdInternal(b => b.CustomId == customId, exception);

    /// <summary>
    /// Internal method to fail a batch found by predicate.
    /// </summary>
    private bool FailBatchByIdInternal(Func<TaskBatch, bool> predicate, Exception exception)
    {
        lock (queueLock)
        {
            var batch = unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .FirstOrDefault(predicate);
            if (batch == null)
                return false;

            if (batch.Status == BatchStatus.Completed || batch.Status == BatchStatus.Cancelled || batch.Status == BatchStatus.Failed)
                return false;

            FailBatch(batch, exception);
            return true;
        }
    }

    /// <summary>
    /// Internal method to cancel a batch found by predicate.
    /// </summary>
    private bool CancelBatchByIdInternal(Func<TaskBatch, bool> predicate)
    {
        lock (queueLock)
        {
            var batch = unifiedQueue
                .Where(item => item.IsBatch)
                .Select(item => item.AsBatch())
                .FirstOrDefault(predicate);
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
        return CancelAllItemsInternal(
            item => item.IsBatch && item.AsBatch().CustomId == customId,
            item => CancelBatchInternal(item.AsBatch()),
            ContextDefinition.CrossContext
        );
    }

    /// <summary>
    /// Internal method to cancel all items matching a predicate with context boundary checking.
    /// </summary>
    private int CancelAllItemsInternal(Func<QueueItemWrapper, bool> predicate, Func<QueueItemWrapper, bool> cancelAction, ContextDefinition contextDefinition)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                List<QueueItemWrapper> items;
                bool isInBatch = currentBatch != null;

                if (contextDefinition == ContextDefinition.CrossContext)
                {
                    items = new List<QueueItemWrapper>();

                    foreach (var item in unifiedQueue)
                    {
                        if (item.IsTask && predicate(item))
                        {
                            items.Add(item);
                        }
                        else if (item.IsBatch)
                        {
                            var batch = item.AsBatch();
                            foreach (var task in batch.Tasks)
                            {
                                var taskWrapper = QueueItemWrapper.FromTask(task);
                                if (predicate(taskWrapper))
                                    items.Add(taskWrapper);
                            }
                        }
                    }
                }
                else if (contextDefinition == ContextDefinition.SameContext)
                {
                    if (isInBatch && currentBatch != null)
                    {
                        items = currentBatch.Tasks
                            .Select(t => QueueItemWrapper.FromTask(t))
                            .Where(predicate)
                            .ToList();
                    }
                    else
                    {
                        items = unifiedQueue
                            .Where(item => item.IsTask)
                            .Where(predicate)
                            .ToList();
                    }
                }
                else // SameContextStrict
                {
                    if (isInBatch && currentBatch != null)
                    {
                        items = currentBatch.Tasks
                            .Select(t => QueueItemWrapper.FromTask(t))
                            .Where(predicate)
                            .ToList();
                    }
                    else
                    {
                        var currentIndex = currentTask != null
                            ? unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
                            : -1;

                        items = new List<QueueItemWrapper>();
                        for (int i = 0; i < unifiedQueue.Count; i++)
                        {
                            var item = unifiedQueue[i];

                            if (item.IsBatch && (currentIndex == -1 || i > currentIndex))
                                break;

                            if (item.IsTask && predicate(item))
                                items.Add(item);
                        }
                    }
                }

                int cancelled = 0;

                foreach (var item in items)
                    if (cancelAction(item))
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
        {
            currentTask = null;
            if (currentBatch == null)
                currentItem = null;
        }

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
        {
            currentBatch = null;
            currentItem = null;
        }

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
                currentItem = null;
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
            {
                currentTask = null;
                if (currentBatch == null)
                    currentItem = null;
            }
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
            {
                currentBatch = null;
                currentItem = null;
            }
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
            ContextDefinition.SameContextStrict => SkipTasksStrictBoundary(count, isInBatch),
            _ => 0
        };
    }

    /// <summary>
    /// Skips tasks with no boundary checks (fully cross-context).
    /// </summary>
    private int SkipTasksNoBoundary(int count, bool isInBatch)
    {
        int skipped = SkipRemainingTasksInCurrentBatch(count, isInBatch);

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
        int skipped = SkipRemainingTasksInCurrentBatch(count, isInBatch);

        if (isInBatch)
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
        }

        return skipped;
    }

    /// <summary>
    /// Skips tasks with SameContextStrict (same batch or standalone with no batch separation).
    /// </summary>
    private int SkipTasksStrictBoundary(int count, bool isInBatch)
    {
        int skipped = SkipRemainingTasksInCurrentBatch(count, isInBatch);

        if (isInBatch)
            return skipped;

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

        return skipped;
    }

    /// <summary>
    /// Helper method to skip remaining queued tasks in the current batch.
    /// </summary>
    private int SkipRemainingTasksInCurrentBatch(int count, bool isInBatch)
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

        return skipped;
    }

    /// <summary>
    /// Gets the global index of the currently executing task across the entire queue.
    /// </summary>
    /// <returns>The global index of the currently executing task, or -1 if no task is currently executing.</returns>
    private int GetCurrentTaskGlobalIndex()
    {
        QueuedTask? executingTask = null;

        if (currentBatch != null)
        {
            executingTask = currentBatch.Tasks.FirstOrDefault(t =>
                t.Status == TaskStatus.Executing ||
                t.Status == TaskStatus.WaitingForCompletion ||
                t.Status == TaskStatus.WaitingForPostDelay);
        }
        else if (currentTask != null)
        {
            executingTask = currentTask;
        }

        if (executingTask == null)
            return -1;

        return GetTaskGlobalIndex(executingTask);
    }

    /// <summary>
    /// Gets the global index of a specific task across the entire queue.
    /// </summary>
    /// <param name="targetTask">The task to find the global index for.</param>
    /// <returns>The global index of the task, or -1 if the task is not found.</returns>
    private int GetTaskGlobalIndex(QueuedTask targetTask)
    {
        int globalIndex = 0;

        foreach (var item in unifiedQueue)
        {
            if (item.IsTask)
            {
                var task = item.AsTask();
                if (ReferenceEquals(task, targetTask))
                    return globalIndex;
                globalIndex++;
            }
            else if (item.IsBatch)
            {
                var batch = item.AsBatch();
                foreach (var task in batch.Tasks)
                {
                    if (ReferenceEquals(task, targetTask))
                        return globalIndex;
                    globalIndex++;
                }
            }
        }

        return -1;
    }
}
