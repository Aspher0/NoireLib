using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TaskQueue;

/// <summary>
/// Queue processing partial class for <see cref="NoireTaskQueue"/>.
/// </summary>
public partial class NoireTaskQueue
{
    /// <summary>
    /// Main queue processing method called every frame - processes unified queue of tasks and batches.
    /// </summary>
    private void ProcessQueue()
    {
        if (currentBatch != null)
        {
            ProcessBatch(currentBatch);
            return;
        }

        QueuedTask? taskToProcess = null;
        TaskBatch? batchToProcess = null;
        bool shouldWaitForBlocking = false;
        bool shouldCheckCompletion = false;
        bool earlyReturn = false;

        List<QueuedTask> waitingTasksToComplete = new();
        List<QueuedTask> waitingTasksToFail = new();

        lock (queueLock)
        {
            if (currentTask != null)
            {
                if (currentTask.Status == TaskStatus.Queued && currentTask.Metadata is RetryDelayMetadata)
                {
                    taskToProcess = currentTask;
                    taskToProcess.Status = TaskStatus.Executing;
                }
                else if (currentTask.Status == TaskStatus.WaitingForCompletion)
                {
                    bool earlyReturnTask = ProcessWaitingTaskStatus(currentTask, out bool complete, out bool fail);
                    if (complete)
                    {
                        CompleteTask(currentTask);
                        earlyReturn = true;
                    }
                    else if (fail)
                    {
                        if (HandleWaitingTaskFinalization(currentTask, true))
                        {
                            earlyReturn = true;
                        }
                        else
                        {
                            FailTask(currentTask, CreateTaskTimeoutOrRetryException(currentTask));
                            earlyReturn = true;
                        }
                    }
                    else if (earlyReturnTask)
                    {
                        earlyReturn = true;
                    }
                }
                else if (currentTask.Status == TaskStatus.WaitingForPostDelay)
                {
                    if (currentTask.HasPostDelayCompleted())
                    {
                        // Check if this was actually a failure or cancellation with post-delay
                        if (currentTask.FailureException != null)
                        {
                            var failedTask = currentTask;
                            currentTask = null;
                            FinalizeTaskFailure(failedTask);
                            earlyReturn = true;
                        }
                        else if (currentTask.ApplyPostDelayOnCancellation)
                        {
                            var cancelledTask = currentTask;
                            currentTask = null;
                            FinalizeTaskCancellation(cancelledTask);
                            earlyReturn = true;
                        }
                        else
                        {
                            CompleteTask(currentTask);
                            earlyReturn = true;
                        }
                    }
                }

                if (!earlyReturn && taskToProcess == null && currentTask != null &&
                    currentTask.IsBlocking &&
                    currentTask.Status != TaskStatus.Completed &&
                    currentTask.Status != TaskStatus.Cancelled &&
                    currentTask.Status != TaskStatus.Failed)
                {
                    shouldWaitForBlocking = true;
                }
            }

            var allWaitingTasks = unifiedQueue
                .Where(item => item.IsTask)
                .Select(item => item.AsTask())
                .Where(t => (t.Status == TaskStatus.WaitingForCompletion || t.Status == TaskStatus.WaitingForPostDelay) && !ReferenceEquals(t, currentTask))
                .ToList();

            foreach (var wt in allWaitingTasks)
            {
                ProcessWaitingTaskStatus(wt, out bool complete, out bool fail);
                if (complete) waitingTasksToComplete.Add(wt);
                if (fail) waitingTasksToFail.Add(wt);
            }

            if (!shouldWaitForBlocking && !earlyReturn && taskToProcess == null)
            {
                var nextItem = unifiedQueue.FirstOrDefault(item =>
                    (item.IsTask && item.AsTask().Status == TaskStatus.Queued) ||
                    (item.IsBatch && item.AsBatch().Status == BatchStatus.Queued));

                if (nextItem != null)
                {
                    if (nextItem.IsTask)
                    {
                        taskToProcess = nextItem.AsTask();
                        currentTask = taskToProcess;
                        currentItem = nextItem;
                        taskToProcess.Status = TaskStatus.Executing;
                        taskToProcess.StartedAtTicks = Environment.TickCount64;
                    }
                    else if (nextItem.IsBatch)
                    {
                        batchToProcess = nextItem.AsBatch();
                        currentBatch = batchToProcess;
                        currentItem = nextItem;
                        batchToProcess.Status = BatchStatus.Processing;
                        batchToProcess.StartedAtTicks = Environment.TickCount64;

                        try
                        {
                            batchToProcess.OnStarted?.Invoke(batchToProcess);
                        }
                        catch (Exception ex)
                        {
                            if (EnableLogging)
                                NoireLogger.LogError(this, ex, "Batch OnStarted callback threw an exception.");
                        }

                        PublishEvent(new BatchStartedEvent(batchToProcess));

                        if (EnableLogging)
                            NoireLogger.LogDebug(this, $"Batch started: {batchToProcess}");

                        // The batch will be processed in the next frame
                        return;
                    }
                }
                else
                {
                    if (currentTask == null)
                    {
                        var firstWaitingTask = unifiedQueue
                            .Where(item => item.IsTask)
                            .Select(item => item.AsTask())
                            .FirstOrDefault(t =>
                                t.Status == TaskStatus.WaitingForCompletion ||
                                t.Status == TaskStatus.WaitingForPostDelay);
                        if (firstWaitingTask != null)
                        {
                            currentTask = firstWaitingTask;
                            currentItem = unifiedQueue.FirstOrDefault(item => item.IsTask && ReferenceEquals(item.AsTask(), firstWaitingTask));
                        }
                        else
                            shouldCheckCompletion = true;
                    }
                    else
                        shouldCheckCompletion = true;
                }
            }
        }

        if (earlyReturn)
            return;

        foreach (var wt in waitingTasksToComplete)
        {
            if (wt.FailureException != null)
            {
                FinalizeTaskFailure(wt);
            }
            else if (wt.ApplyPostDelayOnCancellation && wt.Status == TaskStatus.WaitingForPostDelay)
            {
                FinalizeTaskCancellation(wt);
            }
            else
            {
                CompleteTask(wt);
            }
        }

        foreach (var wt in waitingTasksToFail)
        {
            if (HandleWaitingTaskFinalization(wt, true))
                continue;

            FailTask(wt, CreateTaskTimeoutOrRetryException(wt));
        }

        if (taskToProcess != null)
        {
            ExecuteTask(taskToProcess);
        }
        else if (shouldCheckCompletion)
        {
            CheckQueueCompletion();
        }
    }

    /// <summary>
    /// Processes a batch by executing its tasks like a mini task queue with configured failure/cancellation handling.
    /// </summary>
    private void ProcessBatch(TaskBatch batch)
    {
        if (batch.Status == BatchStatus.Cancelled || batch.Status == BatchStatus.Failed || batch.Status == BatchStatus.Completed)
        {
            currentBatch = null;
            currentItem = null;
            return;
        }

        if (batch.Status == BatchStatus.WaitingForPostDelay)
        {
            if (batch.HasPostDelayCompleted())
            {
                if (batch.FailureException != null)
                {
                    batch.Status = BatchStatus.Failed;
                    batch.FinishedAtTicks = Environment.TickCount64;
                    batchesFailed++;

                    currentBatch = null;
                    currentItem = null;

                    if (batch.StopQueueOnFail)
                    {
                        if (EnableLogging)
                            NoireLogger.LogInfo(this, $"Stopping queue due to batch failure: {batch}");

                        StopQueue();
                    }

                    if (EnableLogging)
                        NoireLogger.LogError(this, batch.FailureException, $"Batch failed after post-failure delay: {batch}");
                }
                else if (batch.Tasks.Any(t => t.Status == TaskStatus.Cancelled))
                {
                    batch.Status = BatchStatus.Cancelled;
                    batch.FinishedAtTicks = Environment.TickCount64;
                    batchesCancelled++;

                    currentBatch = null;
                    currentItem = null;

                    if (batch.StopQueueOnCancel)
                    {
                        if (EnableLogging)
                            NoireLogger.LogInfo(this, $"Stopping queue due to batch cancellation: {batch}");
                        StopQueue();
                    }

                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Batch cancelled after post-cancellation delay: {batch}");
                }
                else
                {
                    batch.Status = BatchStatus.Completed;
                    batch.FinishedAtTicks = Environment.TickCount64;
                    batchesCompleted++;

                    try
                    {
                        batch.OnCompleted?.Invoke(batch);
                    }
                    catch (Exception ex)
                    {
                        if (EnableLogging)
                            NoireLogger.LogError(this, ex, "Batch OnCompleted callback threw an exception.");
                    }

                    PublishEvent(new BatchCompletedEvent(batch));

                    currentBatch = null;
                    currentItem = null;

                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Batch completed after post-completion delay: {batch} (Duration: {batch.GetExecutionTime()})");
                }
            }
            return; // Still waiting for delay to complete
        }

        if (batch.Tasks.Count == 0)
        {
            CompleteBatch(batch);
            return;
        }

        QueuedTask? taskToProcess = null;
        bool shouldWaitForBlocking = false;
        bool shouldCheckCompletion = false;
        bool earlyReturn = false;

        List<QueuedTask> waitingTasksToComplete = new();
        List<QueuedTask> waitingTasksToFail = new();

        QueuedTask? batchCurrentTask = null;

        lock (queueLock)
        {
            batchCurrentTask = batch.Tasks.FirstOrDefault(t =>
                t.Status == TaskStatus.Executing ||
                t.Status == TaskStatus.WaitingForCompletion ||
                t.Status == TaskStatus.WaitingForPostDelay);

            if (batchCurrentTask != null)
            {
                if (batchCurrentTask.Status == TaskStatus.Queued && batchCurrentTask.Metadata is RetryDelayMetadata)
                {
                    taskToProcess = batchCurrentTask;
                    taskToProcess.Status = TaskStatus.Executing;
                }
                else if (batchCurrentTask.Status == TaskStatus.WaitingForCompletion)
                {
                    bool earlyReturnTask = ProcessWaitingTaskStatus(batchCurrentTask, out bool complete, out bool fail);
                    if (complete)
                    {
                        CompleteTask(batchCurrentTask);
                        earlyReturn = true;
                    }
                    else if (fail)
                    {
                        FailBatchTask(batch, batchCurrentTask, CreateTaskTimeoutOrRetryException(batchCurrentTask));
                        earlyReturn = true;
                    }
                    else if (earlyReturnTask)
                    {
                        earlyReturn = true;
                    }
                }
                else if (batchCurrentTask.Status == TaskStatus.WaitingForPostDelay)
                {
                    if (batchCurrentTask.HasPostDelayCompleted())
                    {
                        if (batchCurrentTask.FailureException != null)
                        {
                            FinalizeBatchTaskFailure(batch, batchCurrentTask);
                            earlyReturn = true;
                        }
                        else if (batchCurrentTask.ApplyPostDelayOnCancellation)
                        {
                            FinalizeBatchTaskCancellation(batch, batchCurrentTask);
                            earlyReturn = true;
                        }
                        else
                        {
                            CompleteTask(batchCurrentTask);
                            earlyReturn = true;
                        }
                    }
                }

                if (!earlyReturn && taskToProcess == null && batchCurrentTask.IsBlocking &&
                    batchCurrentTask.Status != TaskStatus.Completed &&
                    batchCurrentTask.Status != TaskStatus.Cancelled &&
                    batchCurrentTask.Status != TaskStatus.Failed)
                {
                    shouldWaitForBlocking = true;
                }
            }

            foreach (var wt in batch.Tasks.Where(t =>
                (t.Status == TaskStatus.WaitingForCompletion || t.Status == TaskStatus.WaitingForPostDelay) &&
                !ReferenceEquals(t, batchCurrentTask)))
            {
                ProcessWaitingTaskStatus(wt, out bool complete, out bool fail);
                if (complete) waitingTasksToComplete.Add(wt);
                if (fail) waitingTasksToFail.Add(wt);
            }

            if (!shouldWaitForBlocking && !earlyReturn && taskToProcess == null)
            {
                taskToProcess = batch.Tasks.FirstOrDefault(t => t.Status == TaskStatus.Queued);

                if (taskToProcess != null)
                {
                    taskToProcess.Status = TaskStatus.Executing;
                    taskToProcess.StartedAtTicks = Environment.TickCount64;
                }
                else
                {
                    if (batchCurrentTask == null)
                    {
                        var firstWaitingTask = batch.Tasks.FirstOrDefault(t =>
                            t.Status == TaskStatus.WaitingForCompletion ||
                            t.Status == TaskStatus.WaitingForPostDelay);

                        if (firstWaitingTask == null)
                            shouldCheckCompletion = true;
                    }
                    else
                        shouldCheckCompletion = true;
                }
            }
        }

        if (earlyReturn)
            return;

        foreach (var wt in waitingTasksToComplete)
        {
            if (wt.FailureException != null)
            {
                FinalizeBatchTaskFailure(batch, wt);
            }
            else if (wt.ApplyPostDelayOnCancellation && wt.Status == TaskStatus.WaitingForPostDelay)
            {
                FinalizeBatchTaskCancellation(batch, wt);
            }
            else
            {
                CompleteTask(wt);
            }
        }

        foreach (var wt in waitingTasksToFail)
        {
            FailBatchTask(batch, wt, CreateTaskTimeoutOrRetryException(wt));
        }

        if (taskToProcess != null)
        {
            ExecuteTask(taskToProcess);
        }
        else if (shouldCheckCompletion)
        {
            CheckBatchCompletion(batch);
        }
    }

    /// <summary>
    /// Executes a task.
    /// </summary>
    private void ExecuteTask(QueuedTask task)
    {
        if (task.Status != TaskStatus.Executing)
        {
            if (ReferenceEquals(currentTask, task))
                lock (queueLock)
                    if (ReferenceEquals(currentTask, task))
                    {
                        currentTask = null;
                        if (currentBatch == null)
                            currentItem = null;
                    }

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Skipping execution for task no longer executing: {task} (Status: {task.Status})");

            return;
        }

        if (task.Metadata is RetryDelayMetadata delayMetadata)
        {
            if (Environment.TickCount64 < delayMetadata.DelayUntilTicks)
            {
                task.Status = TaskStatus.Queued;
                return;
            }

            task.Metadata = delayMetadata.OriginalMetadata;

            if (!ExecuteRetryAction(task))
                return;

            return;
        }

        if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent &&
            task.CompletionCondition.EventType != null &&
            task.EventSubscriptionToken == null)
        {
            lock (queueLock)
            {
                if (task.EventSubscriptionToken == null)
                {
                    SubscribeToEventForTask(task);
                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Subscribed batch task to event: {task}");
                }
            }
        }

        try
        {
            PublishEvent(new TaskStartedEvent(task));

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Executing task: {task}");

            task.ExecuteAction?.Invoke();

            lock (queueLock)
            {
                if (task.Status != TaskStatus.Executing)
                {
                    // Task status changed during execution (e.g., task.Cancel() called inside the action)
                }
                else if (task.CompletionCondition?.Type == CompletionConditionType.Immediate)
                {
                    if (task.PostCompletionDelayProvider != null)
                        task.PostCompletionDelay = task.PostCompletionDelayProvider(task);

                    if (task.Status != TaskStatus.Executing)
                    {
                        // Task status changed during PostCompletionDelayProvider evaluation
                    }
                    else if (task.PostCompletionDelay.HasValue)
                    {
                        task.PostDelayStartTicks = Environment.TickCount64;
                        task.Status = TaskStatus.WaitingForPostDelay;

                        if (task.Timeout.HasValue)
                            task.PauseTimeout();

                        if (EnableLogging)
                            NoireLogger.LogDebug(this, $"Task entering post-completion delay: {task}");
                    }
                    else
                    {
                        CompleteTask(task);
                    }
                }
                else
                {
                    task.Status = TaskStatus.WaitingForCompletion;

                    if (task.RetryConfiguration != null)
                        task.ResetStallTracking();

                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Task waiting for completion: {task}");
                }
            }
        }
        catch (Exception ex)
        {
            lock (queueLock)
                FailTask(task, ex);
        }
    }

    /// <summary>
    /// Processes a waiting task's status and determines if it should complete, fail, or continue waiting.
    /// </summary>
    /// <param name="task">The task to process.</param>
    /// <param name="shouldComplete">Set to true if the task should be added to completion list.</param>
    /// <param name="shouldFail">Set to true if the task should be added to failure list.</param>
    /// <returns>True if early return is needed (task was completed or failed immediately).</returns>
    private bool ProcessWaitingTaskStatus(QueuedTask task, out bool shouldComplete, out bool shouldFail)
    {
        shouldComplete = false;
        shouldFail = false;

        if (task.Status == TaskStatus.WaitingForPostDelay)
        {
            if (task.HasPostDelayCompleted())
            {
                shouldComplete = true;
            }
            return false;
        }

        if (task.Status == TaskStatus.WaitingForCompletion)
        {
            bool conditionMet = task.CompletionCondition?.IsMet() == true;

            // Check if task status changed during condition evaluation (e.g., cancelled from predicate)
            if (task.Status != TaskStatus.WaitingForCompletion)
                return false;

            if (conditionMet)
            {
                if (!task.PostDelayStartTicks.HasValue)
                {
                    if (task.PostCompletionDelayProvider != null)
                        task.PostCompletionDelay = task.PostCompletionDelayProvider(task);

                    // Check if task status changed during delay provider evaluation
                    if (task.Status != TaskStatus.WaitingForCompletion)
                        return false;

                    if (task.PostCompletionDelay.HasValue)
                    {
                        task.PostDelayStartTicks = Environment.TickCount64;
                        task.Status = TaskStatus.WaitingForPostDelay;

                        if (task.Timeout.HasValue)
                            task.PauseTimeout();

                        if (EnableLogging)
                            NoireLogger.LogDebug(this, $"Task entering post-completion delay: {task}");
                    }
                    else
                    {
                        shouldComplete = true;
                    }
                }
            }
            else if (task.HasTimedOut())
            {
                shouldFail = true;
            }
            else if (task.HasConditionStalled())
            {
                if (TryRetryTask(task))
                {
                    // Retry was initiated
                }
                else if (!task.RetryConfiguration!.MaxAttempts.HasValue ||
                    task.CurrentRetryAttempt < task.RetryConfiguration.MaxAttempts.Value)
                {
                    task.ResetStallTracking();
                }
                else
                {
                    try
                    {
                        task.RetryConfiguration?.OnMaxRetriesExceeded?.Invoke(task);
                    }
                    catch (Exception ex)
                    {
                        if (EnableLogging)
                            NoireLogger.LogError(this, ex, "OnMaxRetriesExceeded callback threw an exception.");
                    }

                    // Check if task status changed during OnMaxRetriesExceeded callback
                    if (task.Status != TaskStatus.WaitingForCompletion)
                        return false;

                    shouldFail = true;
                }
            }
            else
            {
                if (task.RetryConfiguration != null && task.CompletionCondition?.Type == CompletionConditionType.Predicate)
                {
                    if (!task.LastConditionCheckTicks.HasValue)
                        task.ResetStallTracking();
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Handles the completion or failure of a waiting task, respecting parent batch policies for max retries.
    /// </summary>
    /// <param name="task">The task to finalize.</param>
    /// <param name="isFailing">True if the task is failing.</param>
    /// <returns>True if parent batch was failed/cancelled and task was handled, false if normal processing should continue.</returns>
    private bool HandleWaitingTaskFinalization(QueuedTask task, bool isFailing)
    {
        if (!isFailing)
            return false;

        bool isMaxRetryFailure = task.RetryConfiguration != null &&
            task.CurrentRetryAttempt >= (task.RetryConfiguration.MaxAttempts ?? int.MaxValue);

        if (isMaxRetryFailure && task.ParentBatch != null)
        {
            var exception = new MaxRetryAttemptsExceededException(
                $"Task exceeded maximum retry attempts ({task.RetryConfiguration!.MaxAttempts})");

            if (task.FailParentBatchOnMaxRetries)
            {
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Failing parent batch due to max retries exceeded: {task}");

                FailTask(task, exception);
                FailBatch(task.ParentBatch, new Exception($"Batch failed by task max retries exceeded: {task}", exception));
                return true;
            }
            else if (task.CancelParentBatchOnMaxRetries)
            {
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Cancelling parent batch due to max retries exceeded: {task}");

                FailTask(task, exception);
                CancelBatchInternal(task.ParentBatch);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to retry a stalled task by re-executing its action or retry override.
    /// </summary>
    /// <param name="task">The task to retry.</param>
    /// <returns>True if retry was initiated, false if max retries exceeded.</returns>
    private bool TryRetryTask(QueuedTask task)
    {
        if (task.RetryConfiguration == null)
            return false;

        if (task.RetryConfiguration.MaxAttempts.HasValue &&
            task.CurrentRetryAttempt >= task.RetryConfiguration.MaxAttempts.Value)
            return false;

        task.CurrentRetryAttempt++;

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Retrying task {task} (Attempt {task.CurrentRetryAttempt}/{task.RetryConfiguration.MaxAttempts?.ToString() ?? "∞"})");

        try
        {
            task.RetryConfiguration.OnBeforeRetry?.Invoke(task, task.CurrentRetryAttempt);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnBeforeRetry callback threw an exception.");
        }

        // Check if task status changed during OnBeforeRetry callback
        if (task.Status is TaskStatus.Cancelled or TaskStatus.Failed or TaskStatus.Completed or TaskStatus.WaitingForPostDelay)
            return true;

        task.ResetStallTracking();

        if (task.RetryConfiguration.RetryDelay.HasValue)
        {
            task.Status = TaskStatus.Queued;

            task.Metadata = new RetryDelayMetadata
            {
                DelayUntilTicks = Environment.TickCount64 + (long)task.RetryConfiguration.RetryDelay.Value.TotalMilliseconds,
                OriginalMetadata = task.Metadata is RetryDelayMetadata rdm ? rdm.OriginalMetadata : task.Metadata
            };

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Task retry scheduled after delay: {task}");

            return true;
        }

        return ExecuteRetryAction(task);
    }

    /// <summary>
    /// Executes the retry action for a task.
    /// </summary>
    /// <param name="task">The task to execute retry action for.</param>
    /// <returns>True if retry action executed successfully, false if it failed.</returns>
    private bool ExecuteRetryAction(QueuedTask task)
    {
        try
        {
            PublishEvent(new TaskRetryingEvent(task, task.CurrentRetryAttempt));

            if (task.RetryConfiguration?.OverrideRetryAction != null)
            {
                task.RetryConfiguration.OverrideRetryAction(task, task.CurrentRetryAttempt);
            }
            else if (task.ExecuteAction != null)
            {
                task.ExecuteAction();
            }

            // Check if task status changed during retry action (e.g., cancelled/failed)
            if (task.Status is TaskStatus.Cancelled or TaskStatus.Failed or TaskStatus.Completed or TaskStatus.WaitingForPostDelay)
            {
                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Task status changed during retry action: {task}");
                return true;
            }

            if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent)
            {
                task.CompletionCondition.EventConditionMet = false;
            }
            else if (task.PostCompletionDelay.HasValue)
            {
                task.PostDelayStartTicks = Environment.TickCount64;
                task.AccumulatedPostDelayMillis = 0;
                task.PostDelayPausedAtTicks = null;
            }

            task.Status = TaskStatus.WaitingForCompletion;
            task.ResetStallTracking();

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Task retry executed: {task}");

            return true;
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"Retry action failed for task {task}");

            FailTask(task, ex);
            return false;
        }
    }

    /// <summary>
    /// Completes a task successfully.
    /// </summary>
    private void CompleteTask(QueuedTask task)
    {
        task.Status = TaskStatus.Completed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCompleted++;

        UnsubscribeTask(task);

        try
        {
            task.OnCompleted?.Invoke(task);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnCompleted callback threw an exception.");
        }

        PublishEvent(new TaskCompletedEvent(task));

        ClearCurrentTaskReference(task);

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Task completed: {task} (Duration: {task.GetExecutionTime()})");
    }

    /// <summary>
    /// Marks a task as failed.
    /// </summary>
    private void FailTask(QueuedTask task, Exception exception)
    {
        task.FailureException = exception;

        UnsubscribeTask(task);

        try
        {
            task.OnFailed?.Invoke(task, exception);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnFailed callback threw an exception.");
        }

        // Check if task status changed during OnFailed callback (e.g., cancelled by callback)
        if (task.Status is TaskStatus.Cancelled or TaskStatus.Failed or TaskStatus.Completed or TaskStatus.WaitingForPostDelay)
            return;

        PublishEvent(new TaskFailedEvent(task, exception));

        if (task.ApplyPostDelayOnFailure && !task.PostDelayStartTicks.HasValue)
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
                    NoireLogger.LogDebug(this, $"Task entering post-failure delay: {task}");

                return; // Don't finalize yet, wait for delay
            }
        }

        task.Status = TaskStatus.Failed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksFailed++;

        ClearCurrentTaskReference(task);

        if (HandleTaskParentBatchPolicies(task, isFailure: true, isCancellation: false))
        {
            // Parent batch was affected, already logged
        }

        if (task.StopQueueOnFail)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to task failure: {task}");

            StopQueue();
        }

        if (EnableLogging)
            NoireLogger.LogError(this, exception, $"Task failed: {task}");
    }

    /// <summary>
    /// Finalizes a task failure without invoking callbacks (used after post-delay completion).
    /// </summary>
    private void FinalizeTaskFailure(QueuedTask task)
    {
        task.Status = TaskStatus.Failed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksFailed++;

        ClearCurrentTaskReference(task);

        if (HandleTaskParentBatchPolicies(task, isFailure: true, isCancellation: false))
        {
            // Parent batch was affected, already logged
        }

        if (task.StopQueueOnFail)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to task failure: {task}");

            StopQueue();
        }

        if (EnableLogging && task.FailureException != null)
            NoireLogger.LogError(this, task.FailureException, $"Task failed after post-failure delay: {task}");
    }

    /// <summary>
    /// Finalizes a task cancellation without invoking callbacks (used after post-delay completion).
    /// </summary>
    private void FinalizeTaskCancellation(QueuedTask task)
    {
        task.Status = TaskStatus.Cancelled;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCancelled++;

        ClearCurrentTaskReference(task);

        if (HandleTaskParentBatchPolicies(task, isFailure: false, isCancellation: true))
        {
            // Parent batch was affected, already logged
        }

        if (task.StopQueueOnCancel)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to task cancellation: {task}");
            StopQueue();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Task cancelled after post-cancellation delay: {task}");
    }

    /// <summary>
    /// Fails a task within a batch with configured failure mode handling.
    /// </summary>
    private void FailBatchTask(TaskBatch batch, QueuedTask task, Exception exception)
    {
        FailTask(task, exception);

        switch (batch.TaskFailureMode)
        {
            case BatchTaskFailureMode.FailBatch:
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Stopping batch due to task failure (mode: StopBatch): {batch}");
                FailBatch(batch, new Exception($"Batch task failed: {task}", exception));
                break;

            case BatchTaskFailureMode.FailBatchAndStopQueue:
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Stopping batch and queue due to task failure (mode: StopBatchAndQueue): {batch}");
                FailBatch(batch, new Exception($"Batch task failed: {task}", exception));
                StopQueue();
                break;

            case BatchTaskFailureMode.ContinueRemaining:
                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Continuing batch despite task failure (mode: ContinueRemaining): {batch}");
                break;
        }
    }

    /// <summary>
    /// Handles task cancellation within a batch with configured cancellation mode.
    /// </summary>
    private void HandleBatchTaskCancellation(TaskBatch batch, QueuedTask task)
    {
        switch (batch.TaskCancellationMode)
        {
            case BatchTaskCancellationMode.CancelBatch:
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Cancelling batch due to task cancellation (mode: CancelBatch): {batch}");
                CancelBatchInternal(batch);
                break;

            case BatchTaskCancellationMode.CancelBatchAndQueue:
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Cancelling batch and stopping queue due to task cancellation (mode: CancelBatchAndQueue): {batch}");
                CancelBatchInternal(batch);
                StopQueue();
                break;

            case BatchTaskCancellationMode.ContinueRemaining:
                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Continuing batch despite task cancellation (mode: ContinueRemaining): {batch}");
                break;
        }
    }

    /// <summary>
    /// Finalizes a batch task failure without invoking callbacks (used after post-delay completion).
    /// </summary>
    private void FinalizeBatchTaskFailure(TaskBatch batch, QueuedTask task)
    {
        task.Status = TaskStatus.Failed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksFailed++;

        if (EnableLogging && task.FailureException != null)
            NoireLogger.LogError(this, task.FailureException, $"Batch task failed after post-failure delay: {task}");

        if (HandleTaskParentBatchPolicies(task, isFailure: true, isCancellation: false))
            return; // Don't apply the batch's own failure mode if parent was affected

        switch (batch.TaskFailureMode)
        {
            case BatchTaskFailureMode.FailBatch:
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Stopping batch due to task failure (mode: StopBatch): {batch}");
                FailBatch(batch, new Exception($"Batch task failed: {task}", task.FailureException));
                break;

            case BatchTaskFailureMode.FailBatchAndStopQueue:
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Stopping batch and queue due to task failure (mode: StopBatchAndQueue): {batch}");
                FailBatch(batch, new Exception($"Batch task failed: {task}", task.FailureException));
                StopQueue();
                break;

            case BatchTaskFailureMode.ContinueRemaining:
                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Continuing batch despite task failure (mode: ContinueRemaining): {batch}");
                break;
        }
    }

    /// <summary>
    /// Finalizes a batch task cancellation without invoking callbacks (used after post-delay completion).
    /// </summary>
    private void FinalizeBatchTaskCancellation(TaskBatch batch, QueuedTask task)
    {
        task.Status = TaskStatus.Cancelled;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCancelled++;

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Batch task cancelled after post-cancellation delay: {task}");

        if (HandleTaskParentBatchPolicies(task, isFailure: false, isCancellation: true))
            return; // Don't apply the batch's own cancellation mode if parent was affected

        HandleBatchTaskCancellation(batch, task);
    }

    /// <summary>
    /// Checks if a batch has completed all its tasks.
    /// </summary>
    private void CheckBatchCompletion(TaskBatch batch)
    {
        bool batchCompleted = false;
        bool batchFailed = false;

        lock (queueLock)
        {
            var hasUnfinishedTasks = batch.Tasks.Any(t =>
                t.Status == TaskStatus.Queued ||
                t.Status == TaskStatus.Executing ||
                t.Status == TaskStatus.WaitingForCompletion ||
                t.Status == TaskStatus.WaitingForPostDelay);

            if (!hasUnfinishedTasks)
            {
                if (batch.Tasks.Any(t => t.Status == TaskStatus.Failed) &&
                    batch.TaskFailureMode != BatchTaskFailureMode.ContinueRemaining)
                {
                    batchFailed = true;
                }
                else
                {
                    batchCompleted = true;
                }
            }
        }

        if (batchCompleted)
        {
            CompleteBatch(batch);
        }
        else if (batchFailed)
        {
            FailBatch(batch, new Exception("One or more tasks in the batch failed."));
        }
    }

    /// <summary>
    /// Completes a batch successfully.
    /// </summary>
    private void CompleteBatch(TaskBatch batch)
    {
        if (!batch.PostDelayStartTicks.HasValue)
        {
            if (batch.PostCompletionDelayProvider != null)
                batch.PostCompletionDelay = batch.PostCompletionDelayProvider(batch);

            // Check if batch status changed during delay provider evaluation
            if (batch.Status is BatchStatus.Cancelled or BatchStatus.Failed or BatchStatus.Completed or BatchStatus.WaitingForPostDelay)
                return;

            if (batch.PostCompletionDelay.HasValue)
            {
                batch.PostDelayStartTicks = Environment.TickCount64;
                batch.Status = BatchStatus.WaitingForPostDelay;

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Batch entering post-completion delay: {batch}");

                return; // Don't complete yet, wait for delay
            }
        }

        batch.Status = BatchStatus.Completed;
        batch.FinishedAtTicks = Environment.TickCount64;
        batchesCompleted++;

        try
        {
            batch.OnCompleted?.Invoke(batch);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Batch OnCompleted callback threw an exception.");
        }

        PublishEvent(new BatchCompletedEvent(batch));

        currentBatch = null;
        currentItem = null;

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Batch completed: {batch} (Duration: {batch.GetExecutionTime()})");
    }

    /// <summary>
    /// Marks a batch as failed.
    /// </summary>
    private void FailBatch(TaskBatch batch, Exception exception)
    {
        batch.FailureException = exception;

        try
        {
            batch.OnFailed?.Invoke(batch, exception);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Batch OnFailed callback threw an exception.");
        }

        // Check if batch status changed during OnFailed callback
        if (batch.Status is BatchStatus.Cancelled or BatchStatus.Failed or BatchStatus.Completed or BatchStatus.WaitingForPostDelay)
            return;

        PublishEvent(new BatchFailedEvent(batch, exception));

        if (batch.ApplyPostDelayOnFailure && !batch.PostDelayStartTicks.HasValue)
        {
            if (batch.PostCompletionDelayProvider != null)
                batch.PostCompletionDelay = batch.PostCompletionDelayProvider(batch);

            // Check if batch status changed during delay provider evaluation
            if (batch.Status is BatchStatus.Cancelled or BatchStatus.Failed or BatchStatus.Completed or BatchStatus.WaitingForPostDelay)
                return;

            if (batch.PostCompletionDelay.HasValue)
            {
                batch.PostDelayStartTicks = Environment.TickCount64;
                batch.Status = BatchStatus.WaitingForPostDelay;

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Batch entering post-failure delay: {batch}");

                return; // Don't finalize yet, wait for delay
            }
        }

        batch.Status = BatchStatus.Failed;
        batch.FinishedAtTicks = Environment.TickCount64;
        batchesFailed++;

        currentBatch = null;
        currentItem = null;

        if (batch.StopQueueOnFail)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to batch failure: {batch}");

            StopQueue();
        }

        if (EnableLogging)
            NoireLogger.LogError(this, exception, $"Batch failed: {batch}");
    }

    /// <summary>
    /// Checks if the queue has completed all tasks and batches.
    /// </summary>
    private void CheckQueueCompletion()
    {
        bool queueEmpty = false;
        lock (queueLock)
        {
            var hasUnfinishedItems = unifiedQueue.Any(item =>
            {
                if (item.IsTask)
                {
                    var task = item.AsTask();
                    return task.Status == TaskStatus.Queued ||
                           task.Status == TaskStatus.Executing ||
                           task.Status == TaskStatus.WaitingForCompletion ||
                           task.Status == TaskStatus.WaitingForPostDelay;
                }
                else if (item.IsBatch)
                {
                    var batch = item.AsBatch();
                    return batch.Status == BatchStatus.Queued ||
                           batch.Status == BatchStatus.Processing ||
                           batch.Status == BatchStatus.WaitingForPostDelay;
                }
                return false;
            });

            if (!hasUnfinishedItems)
            {
                queueEmpty = true;
                currentTask = null;
                currentBatch = null;
                currentItem = null;
            }
        }

        if (queueEmpty)
        {
            if (ShouldStopQueueOnComplete)
                StopQueue();
            else
            {
                QueueState = QueueState.Idle;

                if (processingStartTimeTicks > 0)
                {
                    accumulatedProcessingMillis += Environment.TickCount64 - processingStartTimeTicks;
                    processingStartTimeTicks = 0;
                }

                PublishEvent(new QueueCompletedEvent(tasksCompleted));

                if (EnableLogging)
                    NoireLogger.LogInfo(this, "Queue processing completed (all items finished).");
            }
        }
    }

    /// <summary>
    /// Creates an exception for task timeout or max retry attempts exceeded.
    /// </summary>
    private static Exception CreateTaskTimeoutOrRetryException(QueuedTask task)
    {
        return task.RetryConfiguration != null &&
               task.CurrentRetryAttempt >= (task.RetryConfiguration.MaxAttempts ?? int.MaxValue)
            ? new MaxRetryAttemptsExceededException($"Task exceeded maximum retry attempts ({task.RetryConfiguration.MaxAttempts?.ToString() ?? "Unknown"})")
            : new TimeoutException("Task timed out.");
    }

    /// <summary>
    /// Handles parent batch policies (FailParentBatchOnFail, CancelParentBatchOnFail, etc.) for a task.
    /// </summary>
    /// <returns>True if parent batch was affected and processing should stop, false otherwise.</returns>
    private bool HandleTaskParentBatchPolicies(QueuedTask task, bool isFailure, bool isCancellation)
    {
        if (task.ParentBatch == null)
            return false;

        if (isFailure)
        {
            if (task.FailParentBatchOnFail)
            {
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Failing parent batch due to task failure: {task}");

                FailBatch(task.ParentBatch, new Exception($"Batch failed by task failure: {task}", task.FailureException));
                return true;
            }

            if (task.CancelParentBatchOnFail)
            {
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Cancelling parent batch due to task failure: {task}");

                CancelBatchInternal(task.ParentBatch);
                return true;
            }
        }
        else if (isCancellation)
        {
            if (task.FailParentBatchOnCancel)
            {
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Failing parent batch due to task cancellation: {task}");

                FailBatch(task.ParentBatch, new Exception($"Batch failed by task cancellation: {task}"));
                return true;
            }

            if (task.CancelParentBatchOnCancel)
            {
                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Cancelling parent batch due to task cancellation: {task}");

                CancelBatchInternal(task.ParentBatch);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Clears the current task reference if it matches the specified task.
    /// </summary>
    private void ClearCurrentTaskReference(QueuedTask task)
    {
        if (ReferenceEquals(currentTask, task))
        {
            currentTask = null;
            if (currentBatch == null)
                currentItem = null;
        }
    }
}
