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
                    bool conditionMet = currentTask.CompletionCondition?.IsMet() == true;

                    if (conditionMet)
                    {
                        if (!currentTask.PostDelayStartTicks.HasValue)
                        {
                            if (currentTask.PostCompletionDelayProvider != null)
                                currentTask.PostCompletionDelay = currentTask.PostCompletionDelayProvider(currentTask);

                            if (currentTask.PostCompletionDelay.HasValue)
                            {
                                currentTask.PostDelayStartTicks = Environment.TickCount64;
                                currentTask.Status = TaskStatus.WaitingForPostDelay;

                                if (currentTask.Timeout.HasValue)
                                    currentTask.PauseTimeout();

                                if (EnableLogging)
                                    NoireLogger.LogDebug(this, $"Task entering post-completion delay: {currentTask}");
                            }
                            else
                            {
                                CompleteTask(currentTask);
                                earlyReturn = true;
                            }
                        }
                    }
                    else if (currentTask.HasTimedOut())
                    {
                        FailTask(currentTask, new TimeoutException("Task timed out."));
                        earlyReturn = true;
                    }
                    else if (currentTask.HasConditionStalled())
                    {
                        if (!earlyReturn && TryRetryTask(currentTask))
                        {
                            // Retry was initiated, continue processing
                        }
                        else if (!currentTask.RetryConfiguration!.MaxAttempts.HasValue ||
                            currentTask.CurrentRetryAttempt < currentTask.RetryConfiguration.MaxAttempts.Value)
                        {
                            currentTask.ResetStallTracking();
                        }
                        else
                        {
                            // Max retries exceeded
                            try
                            {
                                currentTask.RetryConfiguration?.OnMaxRetriesExceeded?.Invoke(currentTask);
                            }
                            catch (Exception ex)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogError(this, ex, "OnMaxRetriesExceeded callback threw an exception.");
                            }

                            if (currentTask.FailParentBatchOnMaxRetries && currentTask.ParentBatch != null)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogInfo(this, $"Failing parent batch due to max retries exceeded: {currentTask}");

                                var maxRetryException = new MaxRetryAttemptsExceededException(
                                    $"Task exceeded maximum retry attempts ({currentTask.RetryConfiguration?.MaxAttempts.ToString() ?? "Unknown"})");

                                FailTask(currentTask, maxRetryException);
                                FailBatch(currentTask.ParentBatch, new Exception($"Batch failed by task max retries exceeded: {currentTask}", maxRetryException));
                            }
                            else if (currentTask.CancelParentBatchOnMaxRetries && currentTask.ParentBatch != null)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogInfo(this, $"Cancelling parent batch due to max retries exceeded: {currentTask}");

                                var maxRetryException = new MaxRetryAttemptsExceededException(
                                    $"Task exceeded maximum retry attempts ({currentTask.RetryConfiguration?.MaxAttempts.ToString() ?? "Unknown"})");

                                FailTask(currentTask, maxRetryException);
                                CancelBatchInternal(currentTask.ParentBatch);
                            }
                            else
                            {
                                FailTask(currentTask, new MaxRetryAttemptsExceededException(
                                    $"Task exceeded maximum retry attempts ({currentTask.RetryConfiguration?.MaxAttempts.ToString() ?? "Unknown"})"));
                            }

                            earlyReturn = true;
                        }
                    }
                    else
                    {
                        if (currentTask.RetryConfiguration != null && currentTask.CompletionCondition?.Type == CompletionConditionType.Predicate)
                        {
                            if (!currentTask.LastConditionCheckTicks.HasValue)
                                currentTask.ResetStallTracking();
                        }
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
                if (wt.Status == TaskStatus.WaitingForPostDelay)
                {
                    if (wt.HasPostDelayCompleted())
                    {
                        waitingTasksToComplete.Add(wt);
                    }
                }
                else
                {
                    bool conditionMet = wt.CompletionCondition?.IsMet() == true;

                    if (conditionMet)
                    {
                        if (!wt.PostDelayStartTicks.HasValue)
                        {
                            if (wt.PostCompletionDelayProvider != null)
                                wt.PostCompletionDelay = wt.PostCompletionDelayProvider(wt);

                            if (wt.PostCompletionDelay.HasValue)
                            {
                                wt.PostDelayStartTicks = Environment.TickCount64;
                                wt.Status = TaskStatus.WaitingForPostDelay;

                                if (wt.Timeout.HasValue)
                                    wt.PauseTimeout();

                                if (EnableLogging)
                                    NoireLogger.LogDebug(this, $"Task entering post-completion delay: {wt}");
                            }
                            else
                            {
                                waitingTasksToComplete.Add(wt);
                            }
                        }
                        else
                        {
                            waitingTasksToComplete.Add(wt);
                        }
                    }
                    else if (wt.HasTimedOut())
                    {
                        waitingTasksToFail.Add(wt);
                    }
                    else if (wt.HasConditionStalled())
                    {
                        if (TryRetryTask(wt))
                        {
                            // Retry initiated
                        }
                        else if (!wt.RetryConfiguration!.MaxAttempts.HasValue ||
                                 wt.CurrentRetryAttempt < wt.RetryConfiguration.MaxAttempts.Value)
                        {
                            wt.ResetStallTracking();
                        }
                        else
                        {
                            try
                            {
                                wt.RetryConfiguration?.OnMaxRetriesExceeded?.Invoke(wt);
                            }
                            catch (Exception ex)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogError(this, ex, "OnMaxRetriesExceeded callback threw an exception.");
                            }

                            if (wt.FailParentBatchOnMaxRetries && wt.ParentBatch != null)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogInfo(this, $"Failing parent batch due to max retries exceeded: {wt}");

                                waitingTasksToFail.Add(wt);
                            }
                            else if (wt.CancelParentBatchOnMaxRetries && wt.ParentBatch != null)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogInfo(this, $"Cancelling parent batch due to max retries exceeded: {wt}");

                                waitingTasksToFail.Add(wt);
                            }
                            else
                            {
                                waitingTasksToFail.Add(wt);
                            }
                        }
                    }
                    else
                    {
                        if (wt.RetryConfiguration != null && wt.CompletionCondition?.Type == CompletionConditionType.Predicate)
                        {
                            if (!wt.LastConditionCheckTicks.HasValue)
                                wt.ResetStallTracking();
                        }
                    }
                }
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
                        taskToProcess.Status = TaskStatus.Executing;
                        taskToProcess.StartedAtTicks = Environment.TickCount64;
                    }
                    else if (nextItem.IsBatch)
                    {
                        batchToProcess = nextItem.AsBatch();
                        currentBatch = batchToProcess;
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
                            currentTask = firstWaitingTask;
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
            Exception exception;
            bool isMaxRetryFailure = wt.RetryConfiguration != null && wt.CurrentRetryAttempt >= (wt.RetryConfiguration.MaxAttempts ?? int.MaxValue);

            if (isMaxRetryFailure)
                exception = new MaxRetryAttemptsExceededException($"Task exceeded maximum retry attempts ({wt.RetryConfiguration!.MaxAttempts})");
            else
                exception = new TimeoutException("Task timed out.");

            if (isMaxRetryFailure && wt.ParentBatch != null)
            {
                if (wt.FailParentBatchOnMaxRetries)
                {
                    FailTask(wt, exception);
                    FailBatch(wt.ParentBatch, new Exception($"Batch failed by task max retries exceeded: {wt}", exception));
                    continue; // Skip normal FailTask since we already called it
                }
                else if (wt.CancelParentBatchOnMaxRetries)
                {
                    FailTask(wt, exception);
                    CancelBatchInternal(wt.ParentBatch);
                    continue; // Skip normal FailTask since we already called it
                }
            }

            FailTask(wt, exception);
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
                    bool conditionMet = batchCurrentTask.CompletionCondition?.IsMet() == true;

                    if (conditionMet)
                    {
                        if (!batchCurrentTask.PostDelayStartTicks.HasValue)
                        {
                            if (batchCurrentTask.PostCompletionDelayProvider != null)
                                batchCurrentTask.PostCompletionDelay = batchCurrentTask.PostCompletionDelayProvider(batchCurrentTask);

                            if (batchCurrentTask.PostCompletionDelay.HasValue)
                            {
                                batchCurrentTask.PostDelayStartTicks = Environment.TickCount64;
                                batchCurrentTask.Status = TaskStatus.WaitingForPostDelay;

                                if (batchCurrentTask.Timeout.HasValue)
                                    batchCurrentTask.PauseTimeout();

                                if (EnableLogging)
                                    NoireLogger.LogDebug(this, $"Batch task entering post-completion delay: {batchCurrentTask}");
                            }
                            else
                            {
                                CompleteTask(batchCurrentTask);
                                earlyReturn = true;
                            }
                        }
                    }
                    else if (batchCurrentTask.HasTimedOut())
                    {
                        FailBatchTask(batch, batchCurrentTask, new TimeoutException("Task timed out."));
                        earlyReturn = true;
                    }
                    else if (batchCurrentTask.HasConditionStalled())
                    {
                        if (!earlyReturn && TryRetryTask(batchCurrentTask))
                        {
                            // Retry initiated
                        }
                        else if (!batchCurrentTask.RetryConfiguration!.MaxAttempts.HasValue ||
                            batchCurrentTask.CurrentRetryAttempt < batchCurrentTask.RetryConfiguration.MaxAttempts.Value)
                        {
                            batchCurrentTask.ResetStallTracking();
                        }
                        else
                        {
                            try
                            {
                                batchCurrentTask.RetryConfiguration?.OnMaxRetriesExceeded?.Invoke(batchCurrentTask);
                            }
                            catch (Exception ex)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogError(this, ex, "OnMaxRetriesExceeded callback threw an exception.");
                            }

                            FailBatchTask(batch, batchCurrentTask, new MaxRetryAttemptsExceededException(
                                $"Task exceeded maximum retry attempts ({batchCurrentTask.RetryConfiguration?.MaxAttempts.ToString() ?? "Unknown"})"));
                            earlyReturn = true;
                        }
                    }
                    else
                    {
                        if (batchCurrentTask.RetryConfiguration != null && batchCurrentTask.CompletionCondition?.Type == CompletionConditionType.Predicate)
                        {
                            if (!batchCurrentTask.LastConditionCheckTicks.HasValue)
                                batchCurrentTask.ResetStallTracking();
                        }
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
                if (wt.Status == TaskStatus.WaitingForPostDelay)
                {
                    if (wt.HasPostDelayCompleted())
                    {
                        waitingTasksToComplete.Add(wt);
                    }
                }
                else
                {
                    bool conditionMet = wt.CompletionCondition?.IsMet() == true;

                    if (conditionMet)
                    {
                        if (!wt.PostDelayStartTicks.HasValue)
                        {
                            if (wt.PostCompletionDelayProvider != null)
                                wt.PostCompletionDelay = wt.PostCompletionDelayProvider(wt);

                            if (wt.PostCompletionDelay.HasValue)
                            {
                                wt.PostDelayStartTicks = Environment.TickCount64;
                                wt.Status = TaskStatus.WaitingForPostDelay;

                                if (wt.Timeout.HasValue)
                                    wt.PauseTimeout();

                                if (EnableLogging)
                                    NoireLogger.LogDebug(this, $"Batch task entering post-completion delay: {wt}");
                            }
                            else
                            {
                                waitingTasksToComplete.Add(wt);
                            }
                        }
                        else
                        {
                            waitingTasksToComplete.Add(wt);
                        }
                    }
                    else if (wt.HasTimedOut())
                    {
                        waitingTasksToFail.Add(wt);
                    }
                    else if (wt.HasConditionStalled())
                    {
                        if (TryRetryTask(wt))
                        {
                            // Retry initiated
                        }
                        else if (!wt.RetryConfiguration!.MaxAttempts.HasValue ||
                                 wt.CurrentRetryAttempt < wt.RetryConfiguration.MaxAttempts.Value)
                        {
                            wt.ResetStallTracking();
                        }
                        else
                        {
                            try
                            {
                                wt.RetryConfiguration?.OnMaxRetriesExceeded?.Invoke(wt);
                            }
                            catch (Exception ex)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogError(this, ex, "OnMaxRetriesExceeded callback threw an exception.");
                            }

                            if (wt.FailParentBatchOnMaxRetries && wt.ParentBatch != null)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogInfo(this, $"Failing parent batch due to max retries exceeded: {wt}");

                                waitingTasksToFail.Add(wt);
                            }
                            else if (wt.CancelParentBatchOnMaxRetries && wt.ParentBatch != null)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogInfo(this, $"Cancelling parent batch due to max retries exceeded: {wt}");

                                waitingTasksToFail.Add(wt);
                            }
                            else
                            {
                                waitingTasksToFail.Add(wt);
                            }
                        }
                    }
                    else
                    {
                        if (wt.RetryConfiguration != null && wt.CompletionCondition?.Type == CompletionConditionType.Predicate)
                        {
                            if (!wt.LastConditionCheckTicks.HasValue)
                                wt.ResetStallTracking();
                        }
                    }
                }
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
            Exception exception;
            if (wt.RetryConfiguration != null && wt.CurrentRetryAttempt >= (wt.RetryConfiguration.MaxAttempts ?? int.MaxValue))
                exception = new MaxRetryAttemptsExceededException($"Task exceeded maximum retry attempts ({wt.RetryConfiguration.MaxAttempts})");
            else
                exception = new TimeoutException("Task timed out.");

            FailBatchTask(batch, wt, exception);
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
                        currentTask = null;

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
                if (task.CompletionCondition?.Type == CompletionConditionType.Immediate)
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
                            NoireLogger.LogDebug(this, $"Task entering post-completion delay: {task}");
                    }
                    else
                    {
                        CompleteTask(task);
                    }
                }
                else
                {
                    if (task.Status == TaskStatus.Executing)
                    {
                        task.Status = TaskStatus.WaitingForCompletion;

                        if (task.RetryConfiguration != null)
                            task.ResetStallTracking();

                        if (EnableLogging)
                            NoireLogger.LogDebug(this, $"Task waiting for completion: {task}");
                    }
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

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

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

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

        if (task.FailParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Failing parent batch due to task failure: {task}");

            FailBatch(task.ParentBatch, new Exception($"Batch failed by task failure: {task}", exception));
        }

        if (task.CancelParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Cancelling parent batch due to task failure: {task}");

            CancelBatchInternal(task.ParentBatch);
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

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

        if (task.FailParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Failing parent batch due to task failure after delay: {task}");

            FailBatch(task.ParentBatch, new Exception($"Batch failed by task failure: {task}", task.FailureException));
        }

        if (task.CancelParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Cancelling parent batch due to task failure after delay: {task}");

            CancelBatchInternal(task.ParentBatch);
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

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

        if (task.FailParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Failing parent batch due to task cancellation after delay: {task}");

            FailBatch(task.ParentBatch, new Exception($"Batch failed by task cancellation: {task}"));
        }

        if (task.CancelParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Cancelling parent batch due to task cancellation after delay: {task}");

            CancelBatchInternal(task.ParentBatch);
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

        if (task.FailParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Failing parent batch due to batch task failure after delay: {task}");

            FailBatch(task.ParentBatch, new Exception($"Batch failed by task failure: {task}", task.FailureException));
            return; // Don't apply the batch's own failure mode if we're failing parent
        }

        if (task.CancelParentBatchOnFail && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Cancelling parent batch due to batch task failure after delay: {task}");

            CancelBatchInternal(task.ParentBatch);
            return; // Don't apply the batch's own failure mode if we're cancelling parent
        }

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

        if (task.FailParentBatchOnCancel && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Failing parent batch due to batch task cancellation after delay: {task}");

            FailBatch(task.ParentBatch, new Exception($"Batch failed by task cancellation: {task}"));
            return; // Don't apply the batch's own cancellation mode if we're failing parent
        }

        if (task.CancelParentBatchOnCancel && task.ParentBatch != null)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Cancelling parent batch due to batch task cancellation after delay: {task}");

            CancelBatchInternal(task.ParentBatch);
            return; // Don't apply the batch's own cancellation mode if we're cancelling parent
        }

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

        PublishEvent(new BatchFailedEvent(batch, exception));

        if (batch.ApplyPostDelayOnFailure && !batch.PostDelayStartTicks.HasValue)
        {
            if (batch.PostCompletionDelayProvider != null)
                batch.PostCompletionDelay = batch.PostCompletionDelayProvider(batch);

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
}
