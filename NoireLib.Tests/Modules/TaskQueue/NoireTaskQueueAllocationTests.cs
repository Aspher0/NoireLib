using FluentAssertions;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>A processing pass over waiting tasks whose conditions stay false allocates nothing.</summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueAllocationTests : IDisposable
{
    private const int MeasuredTicks = 1000;

    private readonly List<NoireTaskQueue> queuesToClean = new();

    public void Dispose()
    {
        foreach (var queue in queuesToClean)
        {
            try
            {
                queue.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private NoireTaskQueue MakeQueue()
    {
        var queue = new NoireTaskQueue(moduleId: null, active: false, enableLogging: false);
        queuesToClean.Add(queue);
        queue.Activate();
        return queue;
    }

    private static QueuedTask NeverDoneTask(string customId, bool isBlocking)
    {
        return new QueuedTask(customId, isBlocking)
        {
            CompletionCondition = TaskCompletionCondition.FromPredicate(static () => false)
        };
    }

    private static TaskBatch MakeBatch(string customId, bool isBlocking, params QueuedTask[] tasks)
    {
        var batch = new TaskBatch(customId, isBlocking);

        foreach (var task in tasks)
        {
            task.ParentBatch = batch;
            batch.AddTask(task);
        }

        return batch;
    }

    private static long MeasureWarmTicks(NoireTaskQueue queue)
    {
        for (var i = 0; i < 5; i++)
            queue.TickOnce();

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < MeasuredTicks; i++)
            queue.TickOnce();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void QueueLevel_BlockingTaskWaiting_AllocatesNothing()
    {
        var queue = MakeQueue();
        var waiting = NeverDoneTask("waiting", isBlocking: true);
        var behind = NeverDoneTask("behind", isBlocking: true);

        queue.EnqueueTask(waiting);
        queue.EnqueueTask(behind);
        queue.StartQueue();

        var allocated = MeasureWarmTicks(queue);

        waiting.Status.Should().Be(TaskStatus.WaitingForCompletion);
        behind.Status.Should().Be(TaskStatus.Queued);
        allocated.Should().Be(0);
    }

    [Fact]
    public void QueueLevel_NonBlockingTasksWaiting_AllocatesNothing()
    {
        var queue = MakeQueue();
        var first = NeverDoneTask("first", isBlocking: false);
        var second = NeverDoneTask("second", isBlocking: false);
        var third = NeverDoneTask("third", isBlocking: false);

        queue.EnqueueTask(first);
        queue.EnqueueTask(second);
        queue.EnqueueTask(third);
        queue.StartQueue();

        var allocated = MeasureWarmTicks(queue);

        first.Status.Should().Be(TaskStatus.WaitingForCompletion);
        second.Status.Should().Be(TaskStatus.WaitingForCompletion);
        third.Status.Should().Be(TaskStatus.WaitingForCompletion);
        queue.QueueState.Should().Be(QueueState.Running);
        allocated.Should().Be(0);
    }

    [Fact]
    public void BatchLevel_BlockingTaskWaiting_AllocatesNothing()
    {
        var queue = MakeQueue();
        var waiting = NeverDoneTask("waiting", isBlocking: true);
        var behind = NeverDoneTask("behind", isBlocking: true);
        var batch = MakeBatch("batch", true, waiting, behind);

        queue.EnqueueBatch(batch);
        queue.StartQueue();

        var allocated = MeasureWarmTicks(queue);

        batch.Status.Should().Be(BatchStatus.Processing);
        waiting.Status.Should().Be(TaskStatus.WaitingForCompletion);
        behind.Status.Should().Be(TaskStatus.Queued);
        allocated.Should().Be(0);
    }

    [Fact]
    public void BatchLevel_NonBlockingBatchWithWaitingTasks_AllocatesNothing()
    {
        var queue = MakeQueue();
        var first = NeverDoneTask("first", isBlocking: false);
        var second = NeverDoneTask("second", isBlocking: false);
        var batch = MakeBatch("batch", false, first, second);
        var alongside = NeverDoneTask("alongside", isBlocking: false);

        queue.EnqueueBatch(batch);
        queue.EnqueueTask(alongside);
        queue.StartQueue();

        var allocated = MeasureWarmTicks(queue);

        batch.Status.Should().Be(BatchStatus.Processing);
        first.Status.Should().Be(TaskStatus.WaitingForCompletion);
        second.Status.Should().Be(TaskStatus.WaitingForCompletion);
        alongside.Status.Should().Be(TaskStatus.WaitingForCompletion);
        allocated.Should().Be(0);
    }

    [Fact]
    public void NotRunning_AllocatesNothing()
    {
        var queue = MakeQueue();
        queue.EnqueueTask(NeverDoneTask("parked", isBlocking: true));

        var allocated = MeasureWarmTicks(queue);

        queue.QueueState.Should().NotBe(QueueState.Running);
        allocated.Should().Be(0);
    }
}
