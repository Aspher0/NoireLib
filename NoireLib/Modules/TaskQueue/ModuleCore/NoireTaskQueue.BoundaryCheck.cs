using System;
using System.Linq;

namespace NoireLib.TaskQueue;

public partial class NoireTaskQueue
{
    /// <summary>
    /// Checks if a target task is in the same context as the currently executing task based on the boundary type.
    /// </summary>
    /// <param name="targetTask">The task to check.</param>
    /// <param name="boundaryType">The boundary type to use for context checking.</param>
    /// <returns>True if tasks are in the same context; false otherwise.</returns>
    private bool AreTasksInSameContext(QueuedTask targetTask, ContextDefinition boundaryType)
    {
        return boundaryType switch
        {
            ContextDefinition.CrossContext => true,
            ContextDefinition.SameContext => AreTasksInSameContextFlexible(targetTask),
            ContextDefinition.SameContextStrict => AreTasksInSameContextStrict(targetTask),
            _ => true
        };
    }

    /// <summary>
    /// Checks if tasks are in the same context with flexible batch boundaries (SameContext).
    /// </summary>
    private bool AreTasksInSameContextFlexible(QueuedTask targetTask)
    {
        if (currentBatch != null)
        {
            return ReferenceEquals(targetTask.ParentBatch, currentBatch);
        }
        else if (currentTask != null)
        {
            return targetTask.ParentBatch == null;
        }
        else
        {
            return true;
        }
    }

    /// <summary>
    /// Checks if tasks are in the same context with strict boundary checking (SameContextStrict).
    /// </summary>
    private bool AreTasksInSameContextStrict(QueuedTask targetTask)
    {
        if (currentBatch != null)
        {
            return ReferenceEquals(targetTask.ParentBatch, currentBatch);
        }
        else if (currentTask != null)
        {
            if (targetTask.ParentBatch != null)
                return false;

            bool foundCurrent = false;
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
                {
                    foundCurrent = true;
                    continue;
                }

                if (!foundCurrent)
                    continue;

                if (item.IsTask && ReferenceEquals(item.AsTask(), targetTask))
                    return true;

                if (item.IsBatch)
                    return false;
            }

            return false;
        }
        else
        {
            if (targetTask.ParentBatch != null)
                return false;

            foreach (var item in unifiedQueue)
            {
                if (item.IsTask && ReferenceEquals(item.AsTask(), targetTask))
                    return true;

                if (item.IsBatch)
                    return false;
            }

            return false;
        }
    }

    /// <summary>
    /// Counts pending tasks based on the specified boundary type.
    /// </summary>
    private int CountTasksWithBoundary(ContextDefinition boundaryType)
    {
        return boundaryType switch
        {
            ContextDefinition.CrossContext => CountTasksNoBoundary(),
            ContextDefinition.SameContext => CountTasksSameContext(),
            ContextDefinition.SameContextStrict => CountTasksStrictBoundary(),
            _ => 0
        };
    }

    /// <summary>
    /// Counts all pending tasks with no boundary checks.
    /// </summary>
    private int CountTasksNoBoundary()
    {
        int count = 0;
        foreach (var item in unifiedQueue)
        {
            if (item.IsTask)
            {
                if (item.AsTask().Status == TaskStatus.Queued)
                    count++;
            }
            else if (item.IsBatch)
            {
                var batch = item.AsBatch();
                count += batch.Tasks.Count(t => t.Status == TaskStatus.Queued);
            }
        }
        return count;
    }

    /// <summary>
    /// Counts pending tasks with SameContext boundary.
    /// </summary>
    private int CountTasksSameContext()
    {
        return CountTasksInCurrentContext(stopAtBatch: false);
    }

    /// <summary>
    /// Counts pending tasks with SameContextStrict.
    /// </summary>
    private int CountTasksStrictBoundary()
    {
        return CountTasksInCurrentContext(stopAtBatch: true);
    }

    /// <summary>
    /// Counts pending tasks in the current context (batch or standalone).
    /// </summary>
    private int CountTasksInCurrentContext(bool stopAtBatch)
    {
        if (currentBatch != null)
        {
            return currentBatch.Tasks.Count(t => t.Status == TaskStatus.Queued);
        }
        else
        {
            int count = 0;
            foreach (var item in unifiedQueue)
            {
                if (stopAtBatch && item.IsBatch)
                    break;

                if (item.IsTask && item.AsTask().Status == TaskStatus.Queued)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Calculates the depth (distance in tasks) of a given task from the current executing task.
    /// </summary>
    /// <param name="targetTask">The task to calculate depth for.</param>
    /// <param name="boundaryType">The boundary type to use for context checking.</param>
    /// <returns>The depth of the task, or null if the task is not found or is before the current task.</returns>
    internal int? GetTaskDepth(QueuedTask targetTask, ContextDefinition boundaryType)
    {
        return boundaryType switch
        {
            ContextDefinition.CrossContext => GetTaskDepthNoBoundary(targetTask),
            ContextDefinition.SameContext => GetTaskDepthSameContext(targetTask),
            ContextDefinition.SameContextStrict => GetTaskDepthStrictBoundary(targetTask),
            _ => null
        };
    }

    /// <summary>
    /// Calculates task depth with no boundary checks (fully cross-context).
    /// </summary>
    private int? GetTaskDepthNoBoundary(QueuedTask targetTask)
    {
        int depth = 0;
        bool foundCurrent = false;

        if (currentBatch != null)
        {
            var currentTaskInBatch = currentBatch.Tasks.FirstOrDefault(t =>
                t.Status == TaskStatus.Executing ||
                t.Status == TaskStatus.WaitingForCompletion ||
                t.Status == TaskStatus.WaitingForPostDelay);

            if (currentTaskInBatch != null)
            {
                var currentIndex = currentBatch.Tasks.IndexOf(currentTaskInBatch);

                if (ReferenceEquals(targetTask.ParentBatch, currentBatch))
                {
                    var targetIndex = currentBatch.Tasks.IndexOf(targetTask);
                    if (targetIndex > currentIndex)
                    {
                        for (int i = currentIndex + 1; i < targetIndex; i++)
                        {
                            if (currentBatch.Tasks[i].Status == TaskStatus.Queued)
                                depth++;
                        }
                        return depth;
                    }
                    else
                    {
                        return null;
                    }
                }

                for (int i = currentIndex + 1; i < currentBatch.Tasks.Count; i++)
                {
                    if (currentBatch.Tasks[i].Status == TaskStatus.Queued)
                        depth++;
                }

                foundCurrent = true;
            }
        }
        else if (currentTask != null)
        {
            foundCurrent = false;
        }
        else
        {
            foundCurrent = true;
        }

        bool currentTaskFoundInQueue = foundCurrent;
        foreach (var item in unifiedQueue)
        {
            if (item.IsTask)
            {
                var task = item.AsTask();

                if (!foundCurrent)
                {
                    if (ReferenceEquals(task, currentTask))
                    {
                        foundCurrent = true;
                        currentTaskFoundInQueue = true;
                        continue;
                    }
                    continue;
                }

                if (ReferenceEquals(task, targetTask))
                    return depth;

                if (task.Status == TaskStatus.Queued)
                    depth++;
            }
            else if (item.IsBatch)
            {
                var batch = item.AsBatch();

                if (!foundCurrent)
                    continue;

                if (ReferenceEquals(targetTask.ParentBatch, batch))
                {
                    var targetIndex = batch.Tasks.IndexOf(targetTask);
                    if (targetIndex >= 0)
                    {
                        for (int i = 0; i < targetIndex; i++)
                        {
                            if (batch.Tasks[i].Status == TaskStatus.Queued)
                                depth++;
                        }
                        return depth;
                    }
                }

                depth += batch.Tasks.Count(t => t.Status == TaskStatus.Queued);
            }
        }

        if (!currentTaskFoundInQueue && currentTask != null)
        {
            depth = 0;
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                {
                    var task = item.AsTask();
                    if (ReferenceEquals(task, targetTask))
                        return depth;
                    if (task.Status == TaskStatus.Queued)
                        depth++;
                }
                else if (item.IsBatch)
                {
                    var batch = item.AsBatch();
                    if (ReferenceEquals(targetTask.ParentBatch, batch))
                    {
                        var targetIndex = batch.Tasks.IndexOf(targetTask);
                        if (targetIndex >= 0)
                        {
                            for (int i = 0; i < targetIndex; i++)
                            {
                                if (batch.Tasks[i].Status == TaskStatus.Queued)
                                    depth++;
                            }
                            return depth;
                        }
                    }
                    depth += batch.Tasks.Count(t => t.Status == TaskStatus.Queued);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Calculates task depth with SameContext boundary (same batch or both standalone with batches allowed in between).
    /// </summary>
    private int? GetTaskDepthSameContext(QueuedTask targetTask)
    {
        if (currentBatch != null)
        {
            return GetTaskDepthInBatch(targetTask, currentBatch);
        }
        else
        {
            return GetTaskDepthStandalone(targetTask, stopAtBatch: false);
        }
    }

    /// <summary>
    /// Calculates task depth with SameContextStrict (same batch or standalone with no batch separation).
    /// </summary>
    private int? GetTaskDepthStrictBoundary(QueuedTask targetTask)
    {
        if (currentBatch != null)
        {
            return GetTaskDepthInBatch(targetTask, currentBatch);
        }
        else
        {
            return GetTaskDepthStandalone(targetTask, stopAtBatch: true);
        }
    }

    /// <summary>
    /// Calculates task depth when the current context is inside a batch.
    /// </summary>
    private int? GetTaskDepthInBatch(QueuedTask targetTask, TaskBatch batch)
    {
        if (targetTask.ParentBatch != batch)
            return null;

        var currentTaskInBatch = batch.Tasks.FirstOrDefault(t =>
            t.Status == TaskStatus.Executing ||
            t.Status == TaskStatus.WaitingForCompletion ||
            t.Status == TaskStatus.WaitingForPostDelay);

        if (currentTaskInBatch == null)
        {
            var queuedTasks = batch.Tasks.Where(t => t.Status == TaskStatus.Queued).ToList();
            var targetIndex = queuedTasks.IndexOf(targetTask);
            return targetIndex >= 0 ? targetIndex : null;
        }

        var currentIndex = batch.Tasks.IndexOf(currentTaskInBatch);
        var targetIndex2 = batch.Tasks.IndexOf(targetTask);

        if (targetIndex2 <= currentIndex)
            return null;

        int depth = 0;
        for (int i = currentIndex + 1; i < targetIndex2; i++)
        {
            if (batch.Tasks[i].Status == TaskStatus.Queued)
                depth++;
        }

        return depth;
    }

    /// <summary>
    /// Calculates task depth when the current context is standalone (not in a batch).
    /// </summary>
    private int? GetTaskDepthStandalone(QueuedTask targetTask, bool stopAtBatch)
    {
        if (targetTask.ParentBatch != null)
            return null;

        int depth = 0;
        bool foundCurrent = currentTask == null;

        foreach (var item in unifiedQueue)
        {
            if (stopAtBatch && item.IsBatch)
                break;

            if (item.IsTask)
            {
                var task = item.AsTask();

                if (!foundCurrent)
                {
                    if (ReferenceEquals(task, currentTask))
                        foundCurrent = true;
                    continue;
                }

                if (ReferenceEquals(task, targetTask))
                    return depth;

                if (task.Status == TaskStatus.Queued)
                    depth++;
            }
        }

        return null;
    }
}
