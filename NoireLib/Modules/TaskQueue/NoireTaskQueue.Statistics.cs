using System;
using System.Linq;

namespace NoireLib.TaskQueue;

/// <summary>
/// Statistics and progress tracking partial class for <see cref="NoireTaskQueue"/>.
/// </summary>
public partial class NoireTaskQueue
{
    /// <summary>
    /// Gets the current progress of the queue, including tasks within batches.
    /// </summary>
    /// <returns>A <see cref="TaskQueueStatistics"/> object containing progress information.</returns>
    public TaskQueueStatistics GetStatistics(bool getCopyOfCurrentTask = false, bool getCopyOfCurrentBatch = false)
    {
        lock (queueLock)
        {
            TaskBatch? currBatch = null;
            if (currentBatch != null)
                currBatch = getCopyOfCurrentBatch ? currentBatch.Clone() : currentBatch;

            QueuedTask? currTask = null;
            if (currentTask != null)
                currTask = getCopyOfCurrentTask ? currentTask.Clone() : currentTask;

            int totalTasks = 0;
            int completedTasks = 0;
            int failedTasks = 0;
            int cancelledTasks = 0;
            int queuedTasks = 0;
            int executingTasks = 0;

            var countedTasks = new System.Collections.Generic.HashSet<QueuedTask>();

            void CountTask(QueuedTask task)
            {
                if (!countedTasks.Add(task))
                    return;

                totalTasks++;

                switch (task.Status)
                {
                    case TaskStatus.Completed:
                        completedTasks++;
                        break;
                    case TaskStatus.Failed:
                        failedTasks++;
                        break;
                    case TaskStatus.Cancelled:
                        cancelledTasks++;
                        break;
                    case TaskStatus.Queued:
                        queuedTasks++;
                        break;
                    case TaskStatus.Executing:
                    case TaskStatus.WaitingForCompletion:
                    case TaskStatus.WaitingForPostDelay:
                        executingTasks++;
                        break;
                }
            }

            if (currentTask != null && currentBatch == null)
                CountTask(currentTask);

            if (currentBatch != null)
                foreach (var task in currentBatch.Tasks)
                    CountTask(task);

            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                    CountTask(item.AsTask());
                else if (item.IsBatch)
                {
                    var batch = item.AsBatch();

                    foreach (var task in batch.Tasks)
                        CountTask(task);
                }
            }

            int finishedTasks = completedTasks + failedTasks + cancelledTasks;
            double progressPercentage = totalTasks > 0 ? (double)finishedTasks / totalTasks * 100.0 : 0.0;

            return new TaskQueueStatistics(
                TotalTasks: totalTasks,
                QueuedTasks: queuedTasks,
                CompletedTasks: completedTasks,
                CancelledTasks: cancelledTasks,
                FailedTasks: failedTasks,
                ExecutingTasks: executingTasks,
                CurrentTask: currTask,
                TotalBatchesQueued: totalBatchesQueued,
                BatchesCompleted: batchesCompleted,
                BatchesCancelled: batchesCancelled,
                BatchesFailed: batchesFailed,
                CurrentBatchQueueSize: unifiedQueue.Count(item => item.IsBatch),
                CurrentBatch: currBatch,
                QueueState: QueueState,
                CurrentQueueSize: unifiedQueue.Count,
                ProgressPercentage: progressPercentage,
                TotalProcessingTime: TimeSpan.FromMilliseconds(GetTotalProcessingTime()));
        }
    }

    /// <summary>
    /// Gets the progress of the queue as a percentage (0 to 100).
    /// </summary>
    public double GetQueueProgressPercentage(int decimals = 0)
    {
        lock (queueLock)
        {
            var stats = GetStatistics();
            return Math.Round(stats.ProgressPercentage, decimals);
        }
    }

    /// <summary>
    /// Gets the total number of items (tasks + batches) in the queue.
    /// </summary>
    public int GetQueueSize()
    {
        lock (queueLock)
        {
            return unifiedQueue.Count;
        }
    }

    /// <summary>
    /// Gets the number of tasks in the queue (not including tasks within batches).
    /// </summary>
    public int GetTaskQueueSize()
    {
        lock (queueLock)
        {
            return unifiedQueue.Count(item => item.IsTask);
        }
    }

    /// <summary>
    /// Gets the number of batches in the queue.
    /// </summary>
    public int GetBatchQueueSize()
    {
        lock (queueLock)
        {
            return unifiedQueue.Count(item => item.IsBatch);
        }
    }

    /// <summary>
    /// Gets the total active processing time in milliseconds (excluding paused time).
    /// </summary>
    public long GetTotalProcessingTime()
    {
        lock (queueLock)
        {
            long total = accumulatedProcessingMillis;
            if (QueueState == QueueState.Running && processingStartTimeTicks > 0)
                total += Environment.TickCount64 - processingStartTimeTicks;

            return total;
        }
    }

    /// <summary>
    /// Determines whether the queue is currently in the running state.
    /// </summary>
    /// <returns>true if the queue is running; otherwise, false.</returns>
    public bool IsQueueRunning()
    {
        lock (queueLock)
        {
            return QueueState switch
            {
                QueueState.Running => true,
                _ => false,
            };
        }
    }

    /// <summary>
    /// Determines whether the queue is currently processing items, including both running and paused states.<br/>
    /// This method considers the queue to be processing if it is either actively running or temporarily paused.<br/>
    /// Use this method to check if the queue is engaged in processing, regardless of whether it is momentarily paused.
    /// </summary>
    /// <returns>true if the queue is in a running or paused state; otherwise, false.</returns>
    public bool IsQueueProcessing()
    {
        lock (queueLock)
        {
            return QueueState switch
            {
                QueueState.Running => true,
                QueueState.Paused => true,
                _ => false,
            };
        }
    }
}
