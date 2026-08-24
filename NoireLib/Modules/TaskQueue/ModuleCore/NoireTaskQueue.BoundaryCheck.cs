using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TaskQueue;

public partial class NoireTaskQueue
{
    // Checks if a target task is in the same context as the currently executing task based on the boundary type.
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

    // Checks if tasks are in the same context with flexible batch boundaries (SameContext).
    private bool AreTasksInSameContextFlexible(QueuedTask targetTask)
    {
        if (IsCurrentContextBatch)
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

    // Checks if tasks are in the same context with strict boundary checking (SameContextStrict).
    private bool AreTasksInSameContextStrict(QueuedTask targetTask)
    {
        if (IsCurrentContextBatch)
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

    private int CountTasksSameContext()
    {
        return CountTasksInCurrentContext(stopAtBatch: false);
    }

    private int CountTasksStrictBoundary()
    {
        return CountTasksInCurrentContext(stopAtBatch: true);
    }

    // Whether the queue's current context is a batch rather than a standalone task.
    private bool IsCurrentContextBatch => currentBatch != null && currentTask == null;

    // The position of the executing standalone task in the unified queue, or -1 when none is executing.
    private int GetCurrentStandaloneTaskIndex()
    {
        return currentTask != null
            ? unifiedQueue.FindIndex(item => item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
            : -1;
    }

    // Reports whether a batch at the given position ends the strict standalone context.
    private static bool IsStrictBoundaryBatch(int index, int currentTaskIndex)
    {
        return currentTaskIndex == -1 || index > currentTaskIndex;
    }

    // Lists the tasks a context-scoped query reaches, in queue order.
    private List<QueuedTask> ListTasksInContext(ContextDefinition contextDefinition)
    {
        var tasks = new List<QueuedTask>();

        if (contextDefinition == ContextDefinition.CrossContext)
        {
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                    tasks.Add(item.AsTask());
                else if (item.IsBatch)
                    tasks.AddRange(item.AsBatch().Tasks);
            }

            return tasks;
        }

        if (contextDefinition is not (ContextDefinition.SameContext or ContextDefinition.SameContextStrict))
            return tasks;

        if (IsCurrentContextBatch && currentBatch != null)
        {
            tasks.AddRange(currentBatch.Tasks);
            return tasks;
        }

        var stopAtBatch = contextDefinition == ContextDefinition.SameContextStrict;
        var currentIndex = GetCurrentStandaloneTaskIndex();

        for (int i = 0; i < unifiedQueue.Count; i++)
        {
            var item = unifiedQueue[i];

            if (stopAtBatch && item.IsBatch && IsStrictBoundaryBatch(i, currentIndex))
                break;

            if (item.IsTask)
                tasks.Add(item.AsTask());
        }

        return tasks;
    }

    // Counts pending tasks in the current context (batch or standalone).
    private int CountTasksInCurrentContext(bool stopAtBatch)
    {
        if (IsCurrentContextBatch)
        {
            return currentBatch!.Tasks.Count(t => t.Status == TaskStatus.Queued);
        }
        else
        {
            var currentIndex = GetCurrentStandaloneTaskIndex();
            int count = 0;

            for (int i = 0; i < unifiedQueue.Count; i++)
            {
                var item = unifiedQueue[i];

                if (stopAtBatch && item.IsBatch && IsStrictBoundaryBatch(i, currentIndex))
                    break;

                if (item.IsTask && item.AsTask().Status == TaskStatus.Queued)
                    count++;
            }

            return count;
        }
    }

    // Calculates the depth (distance in tasks) of a given task from the current executing task.
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

    // Calculates task depth with SameContext boundary (same batch or both standalone with batches allowed in
    // between).
    private int? GetTaskDepthSameContext(QueuedTask targetTask)
    {
        if (IsCurrentContextBatch)
        {
            return GetTaskDepthInBatch(targetTask, currentBatch!);
        }
        else
        {
            return GetTaskDepthStandalone(targetTask, stopAtBatch: false);
        }
    }

    // Calculates task depth with SameContextStrict (same batch or standalone with no batch separation).
    private int? GetTaskDepthStrictBoundary(QueuedTask targetTask)
    {
        if (IsCurrentContextBatch)
        {
            return GetTaskDepthInBatch(targetTask, currentBatch!);
        }
        else
        {
            return GetTaskDepthStandalone(targetTask, stopAtBatch: true);
        }
    }

    // Calculates task depth when the current context is inside a batch.
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

    // Calculates task depth when the current context is standalone (not in a batch).
    private int? GetTaskDepthStandalone(QueuedTask targetTask, bool stopAtBatch)
    {
        if (targetTask.ParentBatch != null)
            return null;

        var currentIndex = GetCurrentStandaloneTaskIndex();
        int depth = 0;
        bool foundCurrent = currentTask == null;

        for (int i = 0; i < unifiedQueue.Count; i++)
        {
            var item = unifiedQueue[i];

            if (stopAtBatch && item.IsBatch && IsStrictBoundaryBatch(i, currentIndex))
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
