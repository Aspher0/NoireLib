using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TaskQueue;

/// <summary>Queue processing partial class for <see cref="NoireTaskQueue"/>.</summary>
public partial class NoireTaskQueue
{
    private void ProcessQueue()
    {
        if (currentBatch != null)
        {
            var processedBatch = currentBatch;
            ProcessBatch(processedBatch);

            // A non-blocking batch lets the rest of the queue move alongside it. Processing, not Queued, it is never re-selected.
            if (currentBatch == null || processedBatch.IsBlocking)
                return;
        }

        var allWaitingTasks = RentTaskList();
        var waitingTasksToComplete = RentTaskList();
        var waitingTasksToFail = RentTaskList();

        try
        {
            ProcessQueueItems(allWaitingTasks, waitingTasksToComplete, waitingTasksToFail);
        }
        finally
        {
            ReturnTaskList(allWaitingTasks);
            ReturnTaskList(waitingTasksToComplete);
            ReturnTaskList(waitingTasksToFail);
        }
    }

    private void ProcessQueueItems(List<QueuedTask> allWaitingTasks, List<QueuedTask> waitingTasksToComplete, List<QueuedTask> waitingTasksToFail)
    {
        QueuedTask? taskToProcess = null;
        TaskBatch? batchToProcess = null;
        bool shouldWaitForBlocking = false;
        bool shouldCheckCompletion = false;
        bool earlyReturn = false;

        lock (queueLock)
        {
            if (currentTask != null)
                AdvanceCurrentTask(currentTask, null, ref taskToProcess, ref earlyReturn, ref shouldWaitForBlocking);

            // Materialized first: a completion condition can enqueue.
            foreach (var item in unifiedQueue)
            {
                if (!item.IsTask)
                    continue;

                var task = item.AsTask();
                if (IsWaiting(task) && !ReferenceEquals(task, currentTask))
                    allWaitingTasks.Add(task);
            }

            CollectWaitingTaskOutcomes(allWaitingTasks, waitingTasksToComplete, waitingTasksToFail);

            if (!shouldWaitForBlocking && !earlyReturn && taskToProcess == null)
            {
                QueueItemWrapper? nextItem = null;

                foreach (var item in unifiedQueue)
                {
                    if ((item.IsTask && item.AsTask().Status == TaskStatus.Queued) ||
                        (item.IsBatch && item.AsBatch().Status == BatchStatus.Queued))
                    {
                        nextItem = item;
                        break;
                    }
                }

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

                        return;
                    }
                }
                else
                {
                    if (currentTask == null)
                    {
                        QueueItemWrapper? firstWaitingItem = null;

                        foreach (var item in unifiedQueue)
                        {
                            if (item.IsTask && IsWaiting(item.AsTask()))
                            {
                                firstWaitingItem = item;
                                break;
                            }
                        }

                        if (firstWaitingItem != null)
                        {
                            currentTask = firstWaitingItem.AsTask();
                            currentItem = firstWaitingItem;
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

        ApplyWaitingTaskOutcomes(null, waitingTasksToComplete, waitingTasksToFail);

        if (taskToProcess != null)
        {
            ExecuteTask(taskToProcess);
        }
        else if (shouldCheckCompletion)
        {
            CheckQueueCompletion();
        }
    }

    private void ReconcileConsumerWrittenStatuses()
    {
        var tasksToFinalize = RentTaskList();
        var batchesToFinalize = spareBatchList ?? new List<TaskBatch>();
        spareBatchList = null;

        try
        {
            ReconcileConsumerWrittenStatuses(tasksToFinalize, batchesToFinalize);
        }
        finally
        {
            ReturnTaskList(tasksToFinalize);
            batchesToFinalize.Clear();
            spareBatchList = batchesToFinalize;
        }
    }

    private void ReconcileConsumerWrittenStatuses(List<QueuedTask> tasksToFinalize, List<TaskBatch> batchesToFinalize)
    {
        lock (queueLock)
        {
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                {
                    var task = item.AsTask();
                    if (IsInTerminalStatus(task) && !task.QueueFinalized)
                        tasksToFinalize.Add(task);
                }
                else if (item.IsBatch)
                {
                    var batch = item.AsBatch();

                    foreach (var task in batch.Tasks)
                    {
                        if (IsInTerminalStatus(task) && !task.QueueFinalized)
                            tasksToFinalize.Add(task);
                    }

                    if (batch.Status is BatchStatus.Completed or BatchStatus.Cancelled or BatchStatus.Failed &&
                        !batch.QueueFinalized)
                    {
                        batchesToFinalize.Add(batch);
                    }
                }
            }
        }

        foreach (var task in tasksToFinalize)
            FinalizeConsumerWrittenTask(task);

        foreach (var batch in batchesToFinalize)
            FinalizeConsumerWrittenBatch(batch);
    }

    private void FinalizeConsumerWrittenTask(QueuedTask task)
    {
        task.QueueFinalized = true;
        task.FinishedAtTicks ??= Environment.TickCount64;

        UnsubscribeTask(task);

        switch (task.Status)
        {
            case TaskStatus.Completed:
                tasksCompleted++;
                InvokeGuarded(() => task.OnCompleted?.Invoke(task), "OnCompleted");
                PublishEvent(new TaskCompletedEvent(task));
                break;

            case TaskStatus.Cancelled:
                tasksCancelled++;
                InvokeGuarded(() => task.OnCancelled?.Invoke(task), "OnCancelled");
                PublishEvent(new TaskCancelledEvent(task));
                break;

            case TaskStatus.Failed:
                tasksFailed++;
                task.FailureException ??= new Exception($"Task was marked failed without an exception: {task}");
                InvokeGuarded(() => task.OnFailed?.Invoke(task, task.FailureException), "OnFailed");
                PublishEvent(new TaskFailedEvent(task, task.FailureException));
                break;
        }

        ClearCurrentTaskReference(task);

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Reconciled a directly written task status: {task}");
    }

    private void FinalizeConsumerWrittenBatch(TaskBatch batch)
    {
        batch.QueueFinalized = true;
        batch.FinishedAtTicks ??= Environment.TickCount64;

        // Its remaining tasks are cancelled through the ordinary reconciliation, raising their own callbacks.
        foreach (var task in batch.Tasks)
        {
            if (IsInTerminalStatus(task))
                continue;

            UnsubscribeTask(task);
            task.Status = TaskStatus.Cancelled;
            task.FinishedAtTicks = Environment.TickCount64;
            task.QueueFinalized = true;
            tasksCancelled++;

            InvokeGuarded(() => task.OnCancelled?.Invoke(task), "Task OnCancelled");
            PublishEvent(new TaskCancelledEvent(task));
        }

        switch (batch.Status)
        {
            case BatchStatus.Completed:
                batchesCompleted++;
                batch.QueueFinalized = true;
                InvokeGuarded(() => batch.OnCompleted?.Invoke(batch), "Batch OnCompleted");
                PublishEvent(new BatchCompletedEvent(batch));
                break;

            case BatchStatus.Cancelled:
                batchesCancelled++;
                batch.QueueFinalized = true;
                InvokeGuarded(() => batch.OnCancelled?.Invoke(batch), "Batch OnCancelled");
                PublishEvent(new BatchCancelledEvent(batch));
                break;

            case BatchStatus.Failed:
                batchesFailed++;
                batch.QueueFinalized = true;
                batch.FailureException ??= new Exception($"Batch was marked failed without an exception: {batch}");
                InvokeGuarded(() => batch.OnFailed?.Invoke(batch, batch.FailureException), "Batch OnFailed");
                PublishEvent(new BatchFailedEvent(batch, batch.FailureException));
                break;
        }

        if (ReferenceEquals(currentBatch, batch))
        {
            currentBatch = null;
            currentItem = null;
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Reconciled a directly written batch status: {batch}");
    }

    private void InvokeGuarded(Action callback, string description)
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"{description} callback threw an exception.");
        }
    }

    private void AdvanceCurrentTask(
        QueuedTask current,
        TaskBatch? batch,
        ref QueuedTask? taskToProcess,
        ref bool earlyReturn,
        ref bool shouldWaitForBlocking)
    {
        if (current.Status == TaskStatus.Queued && current.Metadata is RetryDelayMetadata)
        {
            // Queue level only: a retry-delayed batch task is never current and is re-picked by ordinary selection.
            taskToProcess = current;
            current.Status = TaskStatus.Executing;
        }
        else if (current.Status == TaskStatus.WaitingForCompletion)
        {
            bool earlyReturnTask = ProcessWaitingTaskStatus(current, out bool complete, out bool fail);

            if (complete)
            {
                CompleteTask(current);
                earlyReturn = true;
            }
            else if (fail)
            {
                if (!HandleWaitingTaskFinalization(current, true))
                {
                    if (batch != null)
                        FailBatchTask(batch, current, CreateTaskTimeoutOrRetryException(current));
                    else
                        FailTask(current, CreateTaskTimeoutOrRetryException(current));
                }

                earlyReturn = true;
            }
            else if (earlyReturnTask)
            {
                earlyReturn = true;
            }
        }
        else if (current.Status == TaskStatus.WaitingForPostDelay && current.HasPostDelayCompleted())
        {
            if (current.FailureException != null)
            {
                if (batch != null)
                {
                    FinalizeBatchTaskFailure(batch, current);
                }
                else
                {
                    currentTask = null;
                    FinalizeTaskFailure(current);
                }
            }
            else if (current.ApplyPostDelayOnCancellation)
            {
                if (batch != null)
                {
                    FinalizeBatchTaskCancellation(batch, current);
                }
                else
                {
                    currentTask = null;
                    FinalizeTaskCancellation(current);
                }
            }
            else
            {
                CompleteTask(current);
            }

            earlyReturn = true;
        }

        if (!earlyReturn && taskToProcess == null && current.IsBlocking &&
            current.Status != TaskStatus.Completed &&
            current.Status != TaskStatus.Cancelled &&
            current.Status != TaskStatus.Failed)
        {
            shouldWaitForBlocking = true;
        }
    }

    private void CollectWaitingTaskOutcomes(List<QueuedTask> candidates, List<QueuedTask> toComplete, List<QueuedTask> toFail)
    {
        foreach (var wt in candidates)
        {
            ProcessWaitingTaskStatus(wt, out bool complete, out bool fail);

            if (complete)
                toComplete.Add(wt);

            if (fail)
                toFail.Add(wt);
        }
    }

    private void ApplyWaitingTaskOutcomes(TaskBatch? batch, List<QueuedTask> toComplete, List<QueuedTask> toFail)
    {
        foreach (var wt in toComplete)
        {
            if (WasFinishedWithoutCompleting(wt))
                continue;

            if (wt.FailureException != null)
            {
                if (batch != null)
                    FinalizeBatchTaskFailure(batch, wt);
                else
                    FinalizeTaskFailure(wt);
            }
            else if (wt.ApplyPostDelayOnCancellation && wt.Status == TaskStatus.WaitingForPostDelay)
            {
                if (batch != null)
                    FinalizeBatchTaskCancellation(batch, wt);
                else
                    FinalizeTaskCancellation(wt);
            }
            else
            {
                CompleteTask(wt);
            }
        }

        foreach (var wt in toFail)
        {
            if (IsInTerminalStatus(wt))
                continue;

            if (HandleWaitingTaskFinalization(wt, true))
                continue;

            if (batch != null)
                FailBatchTask(batch, wt, CreateTaskTimeoutOrRetryException(wt));
            else
                FailTask(wt, CreateTaskTimeoutOrRetryException(wt));
        }
    }

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
                    batch.QueueFinalized = true;

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
                    batch.QueueFinalized = true;

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
                    batch.QueueFinalized = true;

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
            return;
        }

        if (batch.Tasks.Count == 0)
        {
            CompleteBatch(batch);
            return;
        }

        var batchWaitingTasks = RentTaskList();
        var waitingTasksToComplete = RentTaskList();
        var waitingTasksToFail = RentTaskList();

        try
        {
            ProcessBatchTasks(batch, batchWaitingTasks, waitingTasksToComplete, waitingTasksToFail);
        }
        finally
        {
            ReturnTaskList(batchWaitingTasks);
            ReturnTaskList(waitingTasksToComplete);
            ReturnTaskList(waitingTasksToFail);
        }
    }

    private void ProcessBatchTasks(TaskBatch batch, List<QueuedTask> batchWaitingTasks, List<QueuedTask> waitingTasksToComplete, List<QueuedTask> waitingTasksToFail)
    {
        QueuedTask? taskToProcess = null;
        bool shouldWaitForBlocking = false;
        bool shouldCheckCompletion = false;
        bool earlyReturn = false;

        QueuedTask? batchCurrentTask = null;

        lock (queueLock)
        {
            foreach (var task in batch.Tasks)
            {
                if (task.Status == TaskStatus.Executing || IsWaiting(task))
                {
                    batchCurrentTask = task;
                    break;
                }
            }

            if (batchCurrentTask != null)
                AdvanceCurrentTask(batchCurrentTask, batch, ref taskToProcess, ref earlyReturn, ref shouldWaitForBlocking);

            // Materialized first: a completion condition can add a task to this batch.
            foreach (var task in batch.Tasks)
            {
                if (IsWaiting(task) && !ReferenceEquals(task, batchCurrentTask))
                    batchWaitingTasks.Add(task);
            }

            CollectWaitingTaskOutcomes(batchWaitingTasks, waitingTasksToComplete, waitingTasksToFail);

            if (!shouldWaitForBlocking && !earlyReturn && taskToProcess == null)
            {
                foreach (var task in batch.Tasks)
                {
                    if (task.Status == TaskStatus.Queued)
                    {
                        taskToProcess = task;
                        break;
                    }
                }

                if (taskToProcess != null)
                {
                    taskToProcess.Status = TaskStatus.Executing;
                    taskToProcess.StartedAtTicks = Environment.TickCount64;
                }
                else
                {
                    if (batchCurrentTask == null)
                    {
                        if (!AnyWaiting(batch.Tasks))
                            shouldCheckCompletion = true;
                    }
                    else
                        shouldCheckCompletion = true;
                }
            }
        }

        if (earlyReturn)
            return;

        ApplyWaitingTaskOutcomes(batch, waitingTasksToComplete, waitingTasksToFail);

        if (taskToProcess != null)
        {
            ExecuteTask(taskToProcess);
        }
        else if (shouldCheckCompletion)
        {
            CheckBatchCompletion(batch);
        }
    }

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
                }
                else if (task.CompletionCondition?.Type == CompletionConditionType.Immediate)
                {
                    if (task.PostCompletionDelayProvider != null)
                        task.PostCompletionDelay = task.PostCompletionDelayProvider(task);

                    if (task.Status != TaskStatus.Executing)
                    {
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

    private static bool IsInTerminalStatus(QueuedTask task)
    {
        return task.Status is TaskStatus.Completed or TaskStatus.Cancelled or TaskStatus.Failed;
    }

    private static bool IsWaiting(QueuedTask task)
    {
        return task.Status is TaskStatus.WaitingForCompletion or TaskStatus.WaitingForPostDelay;
    }

    private static bool AnyWaiting(List<QueuedTask> tasks)
    {
        foreach (var task in tasks)
        {
            if (IsWaiting(task))
                return true;
        }

        return false;
    }

    private List<QueuedTask> RentTaskList()
    {
        return taskListPool.Count > 0 ? taskListPool.Pop() : new List<QueuedTask>();
    }

    private void ReturnTaskList(List<QueuedTask> list)
    {
        list.Clear();
        taskListPool.Push(list);
    }

    private static bool WasFinishedWithoutCompleting(QueuedTask task)
    {
        return task.Status is TaskStatus.Cancelled or TaskStatus.Failed;
    }

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
            bool conditionMet;

            try
            {
                conditionMet = task.CompletionCondition?.IsMet() == true;
            }
            catch (Exception ex)
            {
                // A throwing completion condition fails its task instead of unwinding the whole pass.
                if (EnableLogging)
                    NoireLogger.LogError(this, ex, $"Completion condition threw for task: {task}");

                task.FailureException = ex;
                shouldFail = true;
                return false;
            }

            if (task.Status != TaskStatus.WaitingForCompletion)
                return false;

            if (conditionMet)
            {
                if (!task.PostDelayStartTicks.HasValue)
                {
                    if (task.PostCompletionDelayProvider != null)
                        task.PostCompletionDelay = task.PostCompletionDelayProvider(task);

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

    private void CompleteTask(QueuedTask task)
    {
        task.Status = TaskStatus.Completed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCompleted++;
        task.QueueFinalized = true;

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

                return;
            }
        }

        task.Status = TaskStatus.Failed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksFailed++;
        task.QueueFinalized = true;

        ClearCurrentTaskReference(task);

        if (HandleTaskParentBatchPolicies(task, isFailure: true, isCancellation: false))
        {
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

    private void FinalizeTaskFailure(QueuedTask task)
    {
        task.Status = TaskStatus.Failed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksFailed++;
        task.QueueFinalized = true;

        ClearCurrentTaskReference(task);

        if (HandleTaskParentBatchPolicies(task, isFailure: true, isCancellation: false))
        {
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

    private void FinalizeTaskCancellation(QueuedTask task)
    {
        task.Status = TaskStatus.Cancelled;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCancelled++;
        task.QueueFinalized = true;

        ClearCurrentTaskReference(task);

        if (HandleTaskParentBatchPolicies(task, isFailure: false, isCancellation: true))
        {
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

    private void FinalizeBatchTaskFailure(TaskBatch batch, QueuedTask task)
    {
        task.Status = TaskStatus.Failed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksFailed++;
        task.QueueFinalized = true;

        if (EnableLogging && task.FailureException != null)
            NoireLogger.LogError(this, task.FailureException, $"Batch task failed after post-failure delay: {task}");

        if (HandleTaskParentBatchPolicies(task, isFailure: true, isCancellation: false))
            return;

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

    private void FinalizeBatchTaskCancellation(TaskBatch batch, QueuedTask task)
    {
        task.Status = TaskStatus.Cancelled;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCancelled++;
        task.QueueFinalized = true;

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Batch task cancelled after post-cancellation delay: {task}");

        if (HandleTaskParentBatchPolicies(task, isFailure: false, isCancellation: true))
            return;

        HandleBatchTaskCancellation(batch, task);
    }

    private void CheckBatchCompletion(TaskBatch batch)
    {
        bool batchCompleted = false;
        bool batchFailed = false;
        bool batchCancelled = false;

        lock (queueLock)
        {
            var hasUnfinishedTasks = false;
            var anyFailed = false;
            var allCancelled = true;

            foreach (var task in batch.Tasks)
            {
                if (task.Status is TaskStatus.Queued or TaskStatus.Executing || IsWaiting(task))
                    hasUnfinishedTasks = true;
                else if (task.Status == TaskStatus.Failed)
                    anyFailed = true;

                if (task.Status != TaskStatus.Cancelled)
                    allCancelled = false;
            }

            if (!hasUnfinishedTasks)
            {
                if (anyFailed &&
                    batch.TaskFailureMode != BatchTaskFailureMode.ContinueRemaining)
                {
                    batchFailed = true;
                }
                else if (batch.Tasks.Count > 0 && allCancelled)
                {
                    // Only an all-cancelled batch reports Cancelled. Some completed tasks still make it Completed.
                    batchCancelled = true;
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
        else if (batchCancelled)
        {
            CancelBatchInternal(batch);
        }
    }

    private void CompleteBatch(TaskBatch batch)
    {
        if (!batch.PostDelayStartTicks.HasValue)
        {
            if (batch.PostCompletionDelayProvider != null)
                batch.PostCompletionDelay = batch.PostCompletionDelayProvider(batch);

            if (batch.Status is BatchStatus.Cancelled or BatchStatus.Failed or BatchStatus.Completed or BatchStatus.WaitingForPostDelay)
                return;

            if (batch.PostCompletionDelay.HasValue)
            {
                batch.PostDelayStartTicks = Environment.TickCount64;
                batch.Status = BatchStatus.WaitingForPostDelay;

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Batch entering post-completion delay: {batch}");

                return;
            }
        }

        batch.Status = BatchStatus.Completed;
        batch.FinishedAtTicks = Environment.TickCount64;
        batchesCompleted++;
        batch.QueueFinalized = true;

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

        if (batch.Status is BatchStatus.Cancelled or BatchStatus.Failed or BatchStatus.Completed or BatchStatus.WaitingForPostDelay)
            return;

        PublishEvent(new BatchFailedEvent(batch, exception));

        if (batch.ApplyPostDelayOnFailure && !batch.PostDelayStartTicks.HasValue)
        {
            if (batch.PostCompletionDelayProvider != null)
                batch.PostCompletionDelay = batch.PostCompletionDelayProvider(batch);

            if (batch.Status is BatchStatus.Cancelled or BatchStatus.Failed or BatchStatus.Completed or BatchStatus.WaitingForPostDelay)
                return;

            if (batch.PostCompletionDelay.HasValue)
            {
                batch.PostDelayStartTicks = Environment.TickCount64;
                batch.Status = BatchStatus.WaitingForPostDelay;

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Batch entering post-failure delay: {batch}");

                return;
            }
        }

        batch.Status = BatchStatus.Failed;
        batch.FinishedAtTicks = Environment.TickCount64;
        batchesFailed++;
        batch.QueueFinalized = true;

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

    private void CheckQueueCompletion()
    {
        bool queueEmpty = false;
        lock (queueLock)
        {
            var hasUnfinishedItems = false;

            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                {
                    var task = item.AsTask();
                    hasUnfinishedItems = task.Status is TaskStatus.Queued or TaskStatus.Executing || IsWaiting(task);
                }
                else if (item.IsBatch)
                {
                    hasUnfinishedItems = item.AsBatch().Status is BatchStatus.Queued or BatchStatus.Processing or BatchStatus.WaitingForPostDelay;
                }

                if (hasUnfinishedItems)
                    break;
            }

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

    private static Exception CreateTaskTimeoutOrRetryException(QueuedTask task)
    {
        // A recorded failure is the real cause, not a synthesized timeout.
        if (task.FailureException != null)
            return task.FailureException;

        return task.RetryConfiguration != null &&
               task.CurrentRetryAttempt >= (task.RetryConfiguration.MaxAttempts ?? int.MaxValue)
            ? new MaxRetryAttemptsExceededException($"Task exceeded maximum retry attempts ({task.RetryConfiguration.MaxAttempts?.ToString() ?? "Unknown"})")
            : new TimeoutException("Task timed out.");
    }

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
