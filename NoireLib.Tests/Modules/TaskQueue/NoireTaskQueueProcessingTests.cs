using FluentAssertions;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Characterization tests for the task queue's two near-identical processing state machines, one over the unified
/// queue and one over a batch's own tasks.<br/>
/// Tests are paired QueueLevel_/BatchLevel_ wherever the same scenario can drive both, recording every status,
/// transition, and batch failure/cancellation mode, and stepping one pass at a time since the exact step ordering is
/// where the two machines sometimes agree and sometimes diverge.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueProcessingTests : IDisposable
{
    /// <summary>
    /// Duration used for post-completion delays and timeouts. Long enough to survive the tick-count granularity of
    /// the platform clock, short enough not to slow the suite down.
    /// </summary>
    private static readonly TimeSpan ShortDelay = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// Duration a completion condition may stay false before retry logic considers it stalled.
    /// </summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMilliseconds(40);

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

    /// <summary>
    /// Creates an activated queue that processes nothing on its own, so a test drives every pass explicitly.
    /// </summary>
    private NoireTaskQueue MakeQueue()
    {
        var queue = new NoireTaskQueue(moduleId: null, active: false, enableLogging: false);
        queuesToClean.Add(queue);
        queue.Activate();
        return queue;
    }

    /// <summary>
    /// Steps the queue until the predicate holds or the tick budget runs out, and reports the ticks used.
    /// </summary>
    private static int StepUntil(NoireTaskQueue queue, Func<bool> settled, int maxTicks = 50)
    {
        for (var tick = 1; tick <= maxTicks; tick++)
        {
            queue.TickOnce();

            if (settled())
                return tick;
        }

        return -1;
    }

    /// <summary>
    /// Steps the queue until the predicate holds or the wall-clock budget runs out, yielding between passes so a
    /// configured delay, timeout or stall window can elapse.<br/>
    /// The budget is generous on purpose: the queue only advances inside a pass, so a loaded machine makes a
    /// scenario take more passes rather than producing a different outcome.
    /// </summary>
    private static bool StepUntilElapsed(NoireTaskQueue queue, Func<bool> settled, int budgetMs = 10000)
    {
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < budgetMs)
        {
            queue.TickOnce();

            if (settled())
                return true;

            Thread.Sleep(2);
        }

        return false;
    }

    private static QueuedTask ImmediateTask(string customId, Action? action = null, bool isBlocking = true)
    {
        return new QueuedTask(customId, isBlocking)
        {
            ExecuteAction = action,
            CompletionCondition = TaskCompletionCondition.Immediate()
        };
    }

    private static QueuedTask GatedTask(string customId, Func<bool> gate, Action? action = null, bool isBlocking = true)
    {
        return new QueuedTask(customId, isBlocking)
        {
            ExecuteAction = action,
            CompletionCondition = TaskCompletionCondition.FromPredicate(gate)
        };
    }

    /// <summary>
    /// Builds a batch that owns its tasks, which is what wires up the parent batch policies.
    /// </summary>
    private static TaskBatch MakeBatch(string customId, params QueuedTask[] tasks)
    {
        var batch = new TaskBatch(customId, true);

        foreach (var task in tasks)
        {
            task.ParentBatch = batch;
            batch.AddTask(task);
        }

        return batch;
    }

    // Step ordering: how many passes a task costs at each level.

    [Fact]
    public void QueueLevel_ImmediateTask_ExecutesAndCompletesInOneTick()
    {
        var queue = MakeQueue();
        var ran = 0;
        var task = ImmediateTask("task", () => ran++);
        queue.EnqueueTask(task);
        queue.StartQueue();

        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.Completed, "the queue selects a queued task and executes it within the same pass");
        ran.Should().Be(1);
        queue.GetCurrentTask().Should().BeNull("completing a task clears the queue's current task reference");
    }

    [Fact]
    public void BatchLevel_ImmediateTask_NeedsAnExtraTickToEnterTheBatch()
    {
        var queue = MakeQueue();
        var ran = 0;
        var task = ImmediateTask("task", () => ran++);
        var batch = MakeBatch("batch", task);
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.TickOnce();

        batch.Status.Should().Be(BatchStatus.Processing, "starting a batch is all the queue-level pass does before returning");
        task.Status.Should().Be(TaskStatus.Queued, "the batch's first task only runs on the following pass, unlike a loose task which runs on the pass that selects it");
        ran.Should().Be(0);

        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.Completed);
        batch.Status.Should().Be(BatchStatus.Processing, "the batch needs a further pass to notice it has no work left");

        queue.TickOnce();

        batch.Status.Should().Be(BatchStatus.Completed);
        queue.GetCurrentBatch().Should().BeNull();
    }

    [Fact]
    public void QueueLevel_TwoImmediateTasks_TakeOneTickEachPlusACompletionTick()
    {
        var queue = MakeQueue();
        var first = ImmediateTask("first");
        var second = ImmediateTask("second");
        queue.EnqueueTask(first);
        queue.EnqueueTask(second);
        queue.StartQueue();

        queue.TickOnce();
        first.Status.Should().Be(TaskStatus.Completed);
        second.Status.Should().Be(TaskStatus.Queued, "only one task is selected per pass");

        queue.TickOnce();
        second.Status.Should().Be(TaskStatus.Completed);
        queue.QueueState.Should().Be(QueueState.Running, "the pass that completes the last task does not also check queue completion");

        queue.TickOnce();
        queue.QueueState.Should().Be(QueueState.Stopped, "a further pass finds nothing to do and stops the queue");
    }

    [Fact]
    public void BatchLevel_TwoImmediateTasks_TakeTwoExtraTicksForBatchEntryAndCompletion()
    {
        var queue = MakeQueue();
        var first = ImmediateTask("first");
        var second = ImmediateTask("second");
        var batch = MakeBatch("batch", first, second);
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.TickOnce();
        batch.Status.Should().Be(BatchStatus.Processing);

        queue.TickOnce();
        first.Status.Should().Be(TaskStatus.Completed);
        second.Status.Should().Be(TaskStatus.Queued, "the batch also selects a single task per pass");

        queue.TickOnce();
        second.Status.Should().Be(TaskStatus.Completed);
        batch.Status.Should().Be(BatchStatus.Processing);

        queue.TickOnce();
        batch.Status.Should().Be(BatchStatus.Completed, "the batch checks its own completion on the pass after its last task finishes");
        queue.QueueState.Should().Be(QueueState.Running);

        queue.TickOnce();
        queue.QueueState.Should().Be(QueueState.Stopped, "the queue-level completion check happens on yet another pass");
    }

    [Fact]
    public void QueueLevel_SelectsItemsInInsertionOrder_AcrossTasksAndBatches()
    {
        var queue = MakeQueue();
        var order = new List<string>();

        queue.EnqueueTask(ImmediateTask("loose-first", () => order.Add("loose-first")));
        queue.EnqueueBatch(MakeBatch(
            "batch",
            ImmediateTask("batched-first", () => order.Add("batched-first")),
            ImmediateTask("batched-second", () => order.Add("batched-second"))));
        var last = ImmediateTask("loose-last", () => order.Add("loose-last"));
        queue.EnqueueTask(last);
        queue.StartQueue();

        StepUntil(queue, () => last.Status == TaskStatus.Completed).Should().BePositive();

        order.Should().Equal(
            new[] { "loose-first", "batched-first", "batched-second", "loose-last" },
            "both machines select the first still-queued item in insertion order");
    }

    // Current item identity: the queue stores it, the batch recomputes it.

    [Fact]
    public void QueueLevel_CurrentTask_IsTheTaskBeingProcessed()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        queue.EnqueueTask(task);
        queue.StartQueue();

        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.WaitingForCompletion);
        queue.GetCurrentTask().Should().BeSameAs(task, "the queue-level machine stores the task it is working on");
        queue.GetCurrentQueueItem()!.IsTask.Should().BeTrue();
    }

    [Fact]
    public void BatchLevel_CurrentTask_StaysNullAndTheBatchIsTheCurrentItem()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        var batch = MakeBatch("batch", task);
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.TickOnce();
        queue.GetCurrentBatch().Should().BeSameAs(batch);
        queue.GetCurrentQueueItem()!.IsBatch.Should().BeTrue();

        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.WaitingForCompletion);
        queue.GetCurrentTask().Should().BeNull(
            "the batch machine never assigns the queue's current task field; it recomputes the task it is working on from task statuses each pass");
    }

    // Blocking gating.

    [Fact]
    public void QueueLevel_BlockingWaitingTask_GatesTheNextTask()
    {
        var queue = MakeQueue();
        var gate = false;
        var blocking = GatedTask("blocking", () => gate, isBlocking: true);
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(blocking);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        queue.TickOnce();
        blocking.Status.Should().Be(TaskStatus.WaitingForCompletion);

        for (var i = 0; i < 5; i++)
            queue.TickOnce();

        follower.Status.Should().Be(TaskStatus.Queued, "a blocking task that has not finished stops any further selection");

        gate = true;
        StepUntil(queue, () => follower.Status == TaskStatus.Completed).Should().BePositive();
        blocking.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void BatchLevel_BlockingWaitingTask_GatesTheNextTask()
    {
        var queue = MakeQueue();
        var gate = false;
        var blocking = GatedTask("blocking", () => gate, isBlocking: true);
        var follower = ImmediateTask("follower");
        queue.EnqueueBatch(MakeBatch("batch", blocking, follower));
        queue.StartQueue();

        StepUntil(queue, () => blocking.Status == TaskStatus.WaitingForCompletion).Should().BePositive();

        for (var i = 0; i < 5; i++)
            queue.TickOnce();

        follower.Status.Should().Be(TaskStatus.Queued, "the batch machine applies the same blocking gate as the queue machine");

        gate = true;
        StepUntil(queue, () => follower.Status == TaskStatus.Completed).Should().BePositive();
        blocking.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void QueueLevel_NonBlockingWaitingTask_LetsTheNextTaskStart()
    {
        var queue = MakeQueue();
        var nonBlocking = GatedTask("non-blocking", () => false, isBlocking: false);
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(nonBlocking);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        queue.TickOnce();
        nonBlocking.Status.Should().Be(TaskStatus.WaitingForCompletion);

        queue.TickOnce();

        follower.Status.Should().Be(TaskStatus.Completed, "a non-blocking task in flight does not gate the next selection");
        nonBlocking.Status.Should().Be(TaskStatus.WaitingForCompletion, "the non-blocking task keeps waiting while the next one runs");
    }

    [Fact]
    public void BatchLevel_NonBlockingWaitingTask_LetsTheNextTaskStart()
    {
        var queue = MakeQueue();
        var nonBlocking = GatedTask("non-blocking", () => false, isBlocking: false);
        var follower = ImmediateTask("follower");
        queue.EnqueueBatch(MakeBatch("batch", nonBlocking, follower));
        queue.StartQueue();

        StepUntil(queue, () => nonBlocking.Status == TaskStatus.WaitingForCompletion).Should().BePositive();

        queue.TickOnce();

        follower.Status.Should().Be(TaskStatus.Completed, "the batch machine agrees with the queue machine for a single non-blocking task");
        nonBlocking.Status.Should().Be(TaskStatus.WaitingForCompletion);
    }

    [Fact]
    public void QueueLevel_NonBlockingThenBlocking_TheBlockingTaskStillGatesTheRest()
    {
        var queue = MakeQueue();
        var nonBlocking = GatedTask("non-blocking", () => false, isBlocking: false);
        var blocking = GatedTask("blocking", () => false, isBlocking: true);
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(nonBlocking);
        queue.EnqueueTask(blocking);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        queue.TickOnce();
        nonBlocking.Status.Should().Be(TaskStatus.WaitingForCompletion);

        queue.TickOnce();
        blocking.Status.Should().Be(TaskStatus.WaitingForCompletion, "the non-blocking task let the blocking one start");
        queue.GetCurrentTask().Should().BeSameAs(blocking, "the current task is whichever task started most recently");

        for (var i = 0; i < 5; i++)
            queue.TickOnce();

        follower.Status.Should().Be(TaskStatus.Queued,
            "the most recently started task is the blocking one, so it gates everything behind it");
    }

    [Fact]
    public void BatchLevel_NonBlockingThenBlocking_DoesNotGate_BecauseTheFirstWaitingTaskIsTreatedAsCurrent()
    {
        var queue = MakeQueue();
        var nonBlocking = GatedTask("non-blocking", () => false, isBlocking: false);
        var blocking = GatedTask("blocking", () => false, isBlocking: true);
        var follower = ImmediateTask("follower");
        queue.EnqueueBatch(MakeBatch("batch", nonBlocking, blocking, follower));
        queue.StartQueue();

        StepUntil(queue, () => blocking.Status == TaskStatus.WaitingForCompletion).Should().BePositive();

        queue.TickOnce();

        // The batch machine picks the first task in Executing/Waiting status as its current task, which is still the
        // non-blocking one, so the blocking task behind it never gets to gate anything. Driven as loose tasks the
        // same three tasks leave the follower queued.
        follower.Status.Should().Be(TaskStatus.Completed,
            "the batch treats the earliest waiting task as current, so a later blocking task does not gate the batch");
        blocking.Status.Should().Be(TaskStatus.WaitingForCompletion);
    }

    // Recovery from a terminal status reached outside the machine.

    [Fact]
    public void QueueLevel_TerminalCurrentTask_IsAbandonedAndTheNextTaskStarts()
    {
        var queue = MakeQueue();
        var blocking = GatedTask("blocking", () => false, isBlocking: true);
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(blocking);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        queue.TickOnce();
        blocking.Status.Should().Be(TaskStatus.WaitingForCompletion);

        blocking.Status = TaskStatus.Completed;

        queue.TickOnce();

        follower.Status.Should().Be(TaskStatus.Completed,
            "the blocking gate skips terminal statuses, so the machine simply overwrites the stale current task");
    }

    [Fact]
    public void BatchLevel_TerminalBatchStatus_ClearsTheCurrentBatchAndResolvesItsTasks()
    {
        var queue = MakeQueue();
        var waiting = GatedTask("waiting", () => false);
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", waiting, follower);
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => waiting.Status == TaskStatus.WaitingForCompletion).Should().BePositive();

        batch.Status = BatchStatus.Cancelled;

        queue.TickOnce();

        queue.GetCurrentBatch().Should().BeNull("a batch that reached a terminal status is dropped by the guard at the top of batch processing");
        queue.GetCurrentQueueItem().Should().BeNull();

        // The status here was written by hand rather than produced by the queue, so the reconciliation at the
        // end of the pass owns the outcome: the guard still lets go of the batch, and the reconciliation resolves
        // the work it was holding instead of leaving it non-terminal for the rest of the queue's life.
        waiting.Status.Should().Be(TaskStatus.Cancelled, "a cancelled batch cannot run the task it had mid-wait");
        follower.Status.Should().Be(TaskStatus.Cancelled, "nor the one that had not started");
    }

    // Post-completion delay.

    [Fact]
    public void QueueLevel_ImmediateTaskWithPostCompletionDelay_PassesThroughWaitingForPostDelay()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task");
        task.PostCompletionDelay = ShortDelay;
        queue.EnqueueTask(task);
        queue.StartQueue();

        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.WaitingForPostDelay, "an immediate task with a post-completion delay parks instead of completing");

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Completed)
            .Should().BeTrue("the task completes once the delay has elapsed and a pass observes it");
    }

    [Fact]
    public void BatchLevel_ImmediateTaskWithPostCompletionDelay_PassesThroughWaitingForPostDelay()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task");
        task.PostCompletionDelay = ShortDelay;
        queue.EnqueueBatch(MakeBatch("batch", task));
        queue.StartQueue();

        queue.TickOnce();
        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.WaitingForPostDelay, "post-completion delay entry is identical at both levels");

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void QueueLevel_PredicateTaskWithPostCompletionDelay_PassesThroughWaitingForPostDelay()
    {
        var queue = MakeQueue();
        var gate = false;
        var task = GatedTask("task", () => gate);
        task.PostCompletionDelay = ShortDelay;
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(task);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        queue.TickOnce();
        task.Status.Should().Be(TaskStatus.WaitingForCompletion);

        gate = true;
        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.WaitingForPostDelay, "a met condition with a post-completion delay parks rather than completing");
        follower.Status.Should().Be(TaskStatus.Queued, "the blocking gate also holds while a task is in its post-completion delay");

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Completed).Should().BeTrue();
    }

    // Timeout.

    [Fact]
    public void QueueLevel_Timeout_FailsTheTaskAndTheQueueCarriesOn()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.Timeout = ShortDelay;
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(task);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed)
            .Should().BeTrue("a waiting task whose timeout elapses is failed by the pass that observes it");

        task.FailureException.Should().BeOfType<TimeoutException>();

        StepUntil(queue, () => follower.Status == TaskStatus.Completed)
            .Should().BePositive("a failed task does not stop the queue unless it is configured to");
    }

    [Fact]
    public void BatchLevel_Timeout_FailsTheTaskAndTheBatchCarriesOn()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.Timeout = ShortDelay;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskFailureMode = BatchTaskFailureMode.ContinueRemaining;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();
        task.FailureException.Should().BeOfType<TimeoutException>("the timeout exception is built by shared code at both levels");

        StepUntil(queue, () => batch.Status == BatchStatus.Completed)
            .Should().BePositive("with ContinueRemaining the batch reports completed even though one of its tasks failed");
        follower.Status.Should().Be(TaskStatus.Completed);
    }

    // Failure raised by the task action itself.

    [Fact]
    public void QueueLevel_ThrowingAction_FailsTheTaskDuringExecution()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task", () => throw new InvalidOperationException("boom"));
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(task);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.Failed, "an exception thrown by the action fails the task inside the same pass");
        task.FailureException.Should().BeOfType<InvalidOperationException>();

        queue.TickOnce();
        follower.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void BatchLevel_ThrowingAction_FailsTheTaskDuringExecution()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task", () => throw new InvalidOperationException("boom"));
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.TickOnce();
        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.Failed, "task execution is shared code, so a thrown action fails identically at both levels");
        task.FailureException.Should().BeOfType<InvalidOperationException>();
    }

    // Cancellation.

    [Fact]
    public void QueueLevel_CancelWhileQueued_SkipsTheTask()
    {
        var queue = MakeQueue();
        var ran = 0;
        var first = ImmediateTask("first");
        var cancelled = ImmediateTask("cancelled", () => ran++);
        queue.EnqueueTask(first);
        queue.EnqueueTask(cancelled);
        queue.CancelTask(cancelled.SystemId).Should().BeTrue();
        queue.StartQueue();

        StepUntil(queue, () => queue.QueueState == QueueState.Stopped).Should().BePositive();

        cancelled.Status.Should().Be(TaskStatus.Cancelled);
        ran.Should().Be(0, "processing only selects tasks still in Queued status");
        first.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void QueueLevel_CancelWhileWaiting_ReleasesTheBlockingGate()
    {
        var queue = MakeQueue();
        var blocking = GatedTask("blocking", () => false, isBlocking: true);
        var follower = ImmediateTask("follower");
        queue.EnqueueTask(blocking);
        queue.EnqueueTask(follower);
        queue.StartQueue();

        queue.TickOnce();
        blocking.Status.Should().Be(TaskStatus.WaitingForCompletion);

        queue.CancelTask(blocking.SystemId).Should().BeTrue();
        blocking.Status.Should().Be(TaskStatus.Cancelled);
        queue.GetCurrentTask().Should().BeNull("cancelling the current task clears the current task reference");

        queue.TickOnce();
        follower.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void QueueLevel_ApplyPostDelayOnFailure_PassesThroughWaitingForPostDelayThenFailed()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task", () => throw new InvalidOperationException("boom"));
        task.PostCompletionDelay = ShortDelay;
        task.ApplyPostDelayOnFailure = true;
        queue.EnqueueTask(task);
        queue.StartQueue();

        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.WaitingForPostDelay, "failure with a post-failure delay parks before it is finalized");
        task.FailureException.Should().BeOfType<InvalidOperationException>("the exception is recorded before the delay starts");

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void QueueLevel_ApplyPostDelayOnCancellation_PassesThroughWaitingForPostDelayThenCancelled()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.PostCompletionDelay = ShortDelay;
        task.ApplyPostDelayOnCancellation = true;
        queue.EnqueueTask(task);
        queue.StartQueue();

        queue.TickOnce();
        task.Status.Should().Be(TaskStatus.WaitingForCompletion);

        queue.CancelTask(task.SystemId).Should().BeTrue();
        task.Status.Should().Be(TaskStatus.WaitingForPostDelay, "cancellation with a post-cancellation delay parks before it is finalized");

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void QueueLevel_StopQueueOnFail_IsHonoredAfterThePostFailureDelay()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task", () => throw new InvalidOperationException("boom"));
        task.PostCompletionDelay = ShortDelay;
        task.ApplyPostDelayOnFailure = true;
        task.StopQueueOnFail = true;
        queue.EnqueueTask(task);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();

        queue.QueueState.Should().Be(QueueState.Stopped,
            "the queue-level post-delay finalizer honours StopQueueOnFail");
    }

    [Fact]
    public void BatchLevel_StopQueueOnFail_IsNotHonoredAfterThePostFailureDelay()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task", () => throw new InvalidOperationException("boom"));
        task.PostCompletionDelay = ShortDelay;
        task.ApplyPostDelayOnFailure = true;
        task.StopQueueOnFail = true;
        var batch = MakeBatch("batch", task);
        batch.TaskFailureMode = BatchTaskFailureMode.ContinueRemaining;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();

        // The batch post-delay finalizer applies the batch's failure mode instead of the task's own stop flag, so the
        // same task configuration stops the queue as a loose task but not inside a batch.
        queue.QueueState.Should().Be(QueueState.Running,
            "the batch-level post-delay finalizer never consults StopQueueOnFail");
    }

    // Retries.

    [Fact]
    public void QueueLevel_StalledCondition_RetriesTheActionThenCompletes()
    {
        var queue = MakeQueue();
        var attempts = 0;
        var gate = false;
        var task = GatedTask("task", () => gate, () =>
        {
            attempts++;
            if (attempts >= 2)
                gate = true;
        });
        task.RetryConfiguration = TaskRetryConfiguration.Unlimited(StallTimeout);
        queue.EnqueueTask(task);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Completed)
            .Should().BeTrue("a stalled condition re-runs the action and the retried run satisfies it");

        attempts.Should().Be(2, "the original run plus exactly one retry");
    }

    [Fact]
    public void BatchLevel_StalledCondition_RetriesTheActionThenCompletes()
    {
        var queue = MakeQueue();
        var attempts = 0;
        var gate = false;
        var task = GatedTask("task", () => gate, () =>
        {
            attempts++;
            if (attempts >= 2)
                gate = true;
        });
        task.RetryConfiguration = TaskRetryConfiguration.Unlimited(StallTimeout);
        queue.EnqueueBatch(MakeBatch("batch", task));
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Completed).Should().BeTrue();

        attempts.Should().Be(2, "stall detection and retry live in shared code, so both levels behave the same");
    }

    [Fact]
    public void QueueLevel_MaxRetriesExceeded_FailsWithMaxRetryAttemptsExceededException()
    {
        var queue = MakeQueue();
        var attempts = 0;
        var maxRetriesCallbacks = 0;
        var task = GatedTask("task", () => false, () => attempts++);
        task.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(1, StallTimeout);
        task.RetryConfiguration.OnMaxRetriesExceeded = _ => maxRetriesCallbacks++;
        queue.EnqueueTask(task);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();

        attempts.Should().Be(2, "the original run plus the one allowed retry");
        maxRetriesCallbacks.Should().Be(1);
        task.FailureException.Should().BeOfType<MaxRetryAttemptsExceededException>(
            "exhausted retries produce a retry exception rather than a timeout exception");
    }

    [Fact]
    public void BatchLevel_MaxRetriesExceeded_FailsWithMaxRetryAttemptsExceededException()
    {
        var queue = MakeQueue();
        var attempts = 0;
        var task = GatedTask("task", () => false, () => attempts++);
        task.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(1, StallTimeout);
        queue.EnqueueBatch(MakeBatch("batch", task));
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();

        attempts.Should().Be(2);
        task.FailureException.Should().BeOfType<MaxRetryAttemptsExceededException>();
    }

    [Fact]
    public void BatchLevel_FailParentBatchOnMaxRetries_FailsTheBatchItNames()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(1, StallTimeout);
        task.FailParentBatchOnMaxRetries = true;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskFailureMode = BatchTaskFailureMode.ContinueRemaining;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();

        // The batch machine consults the max-retry parent policies now, as the queue machine always did. The
        // policy takes precedence over the batch's own TaskFailureMode, so ContinueRemaining does not override it.
        batch.Status.Should().Be(BatchStatus.Failed,
            "FailParentBatchOnMaxRetries names this batch, and a task processed as part of it can reach it now");
        follower.Status.Should().NotBe(TaskStatus.Completed, "the batch failed before its follower could run");
    }

    [Fact]
    public void QueueLevel_FailParentBatchOnMaxRetries_AppliesToATaskThatCarriesAParentBatch()
    {
        var queue = MakeQueue();

        // The queue-level machine only ever sees top-level items, whose ParentBatch is normally null, so this is the
        // only arrangement in which its max-retry parent batch handling runs at all.
        var parent = new TaskBatch("parent", true);
        var task = GatedTask("task", () => false);
        task.ParentBatch = parent;
        task.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(1, StallTimeout);
        task.FailParentBatchOnMaxRetries = true;
        queue.EnqueueTask(task);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Failed).Should().BeTrue();

        parent.Status.Should().Be(BatchStatus.Failed,
            "the queue-level machine fails the parent batch when a task with this flag exhausts its retries");
    }

    [Fact]
    public void BatchLevel_FailParentBatchOnFail_FailsTheBatchAndStopsRemainingTasks()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task", () => throw new InvalidOperationException("boom"));
        task.FailParentBatchOnFail = true;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskFailureMode = BatchTaskFailureMode.ContinueRemaining;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.TickOnce();
        queue.TickOnce();

        task.Status.Should().Be(TaskStatus.Failed);
        batch.Status.Should().Be(BatchStatus.Failed, "the on-fail parent batch policy runs from the shared failure path, unlike the max-retry one");
        follower.Status.Should().Be(TaskStatus.Queued, "failing the batch leaves its remaining tasks queued rather than cancelling them");
    }

    [Fact]
    public void QueueLevel_RetryDelay_ParksTheTaskAsCurrentTaskWithoutRestartingItsClock()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.RetryConfiguration = TaskRetryConfiguration.Unlimited(StallTimeout, TimeSpan.FromSeconds(5));
        queue.EnqueueTask(task);
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Metadata is RetryDelayMetadata)
            .Should().BeTrue("a retry with a delay parks the task back in Queued status carrying retry metadata");

        task.Status.Should().Be(TaskStatus.Queued);
        queue.GetCurrentTask().Should().BeSameAs(task, "the parked task stays the current task and is picked up by the retry-delay branch");

        var startedAt = task.StartedAtTicks;

        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(30);
            queue.TickOnce();
        }

        task.Status.Should().Be(TaskStatus.Queued, "the task keeps parking itself until the retry delay elapses");
        task.StartedAtTicks.Should().Be(startedAt, "the retry-delay branch does not restamp the task's start time");
    }

    [Fact]
    public void BatchLevel_RetryDelay_ReselectsTheTaskEachTickAndRestartsItsClock()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.RetryConfiguration = TaskRetryConfiguration.Unlimited(StallTimeout, TimeSpan.FromSeconds(5));
        queue.EnqueueBatch(MakeBatch("batch", task));
        queue.StartQueue();

        StepUntilElapsed(queue, () => task.Metadata is RetryDelayMetadata).Should().BeTrue();

        task.Status.Should().Be(TaskStatus.Queued, "parking is identical: the task returns to Queued carrying retry metadata");
        queue.GetCurrentTask().Should().BeNull();

        var startedAt = task.StartedAtTicks;

        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(30);
            queue.TickOnce();
        }

        // The batch machine only recognises Executing and Waiting statuses as its current task, so a parked task is
        // invisible to its retry-delay branch and is instead re-selected as a fresh queued task on every pass, which
        // restamps its start time. The queue-level machine leaves that timestamp alone.
        task.Status.Should().Be(TaskStatus.Queued);
        task.StartedAtTicks.Should().NotBe(startedAt, "re-selecting the parked task as a queued task restamps its start time each pass");
    }

    // Batch statuses.

    [Fact]
    public void Batch_EmptyBatch_CompletesOnTheTickAfterItStarts()
    {
        var queue = MakeQueue();
        var batch = MakeBatch("batch");
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.TickOnce();
        batch.Status.Should().Be(BatchStatus.Processing);

        queue.TickOnce();
        batch.Status.Should().Be(BatchStatus.Completed, "an empty batch is short-circuited before any task selection runs");
    }

    [Fact]
    public void Batch_PostCompletionDelay_PassesThroughWaitingForPostDelay()
    {
        var queue = MakeQueue();
        var batch = MakeBatch("batch", ImmediateTask("task"));
        batch.PostCompletionDelay = ShortDelay;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => batch.Status == BatchStatus.WaitingForPostDelay)
            .Should().BePositive("a batch with a post-completion delay parks instead of completing");

        StepUntilElapsed(queue, () => batch.Status == BatchStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void Batch_PostCompletionDelay_ReportsCancelledWhenAnyTaskWasCancelled()
    {
        var queue = MakeQueue();
        var completions = 0;
        var cancelled = GatedTask("cancelled", () => false);
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", cancelled, follower);
        batch.PostCompletionDelay = ShortDelay;
        batch.OnCompleted = _ => completions++;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => cancelled.Status == TaskStatus.WaitingForCompletion).Should().BePositive();
        queue.CancelTask(cancelled.SystemId).Should().BeTrue();

        StepUntil(queue, () => batch.Status == BatchStatus.WaitingForPostDelay)
            .Should().BePositive("the batch still reaches completion and parks in its post-completion delay");

        StepUntilElapsed(queue, () => batch.Status != BatchStatus.WaitingForPostDelay).Should().BeTrue();

        // The post-delay finalizer re-derives the outcome by scanning for any cancelled task rather than remembering
        // why the delay was entered, so a batch that completed is reported as cancelled.
        batch.Status.Should().Be(BatchStatus.Cancelled,
            "any cancelled task turns a completing batch into a cancelled one once a post-completion delay is involved");
        completions.Should().Be(0, "the completion callback is skipped on that path");
        follower.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void Batch_WithoutPostCompletionDelay_ReportsCompletedEvenWhenATaskWasCancelled()
    {
        var queue = MakeQueue();
        var completions = 0;
        var cancelled = GatedTask("cancelled", () => false);
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", cancelled, follower);
        batch.OnCompleted = _ => completions++;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => cancelled.Status == TaskStatus.WaitingForCompletion).Should().BePositive();
        queue.CancelTask(cancelled.SystemId).Should().BeTrue();

        StepUntil(queue, () => batch.Status == BatchStatus.Completed)
            .Should().BePositive("without a post-completion delay the same batch reports completed and fires its callback");
        completions.Should().Be(1);
    }

    [Fact]
    public void Batch_FailedFromOutsideWithPostFailureDelay_PassesThroughWaitingForPostDelay()
    {
        var queue = MakeQueue();
        var waiting = GatedTask("waiting", () => false);
        var batch = MakeBatch("batch", waiting);
        batch.PostCompletionDelay = ShortDelay;
        batch.ApplyPostDelayOnFailure = true;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => waiting.Status == TaskStatus.WaitingForCompletion).Should().BePositive();

        batch.Fail(new InvalidOperationException("boom")).Should().BeTrue();
        batch.Status.Should().Be(BatchStatus.WaitingForPostDelay);

        StepUntilElapsed(queue, () => batch.Status == BatchStatus.Failed).Should().BeTrue();
        waiting.Status.Should().Be(TaskStatus.WaitingForCompletion, "failing a batch does not resolve the tasks it still holds");
    }

    [Fact]
    public void Batch_CancelledFromOutsideWithPostCancellationDelay_PassesThroughWaitingForPostDelay()
    {
        var queue = MakeQueue();
        var waiting = GatedTask("waiting", () => false);
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", waiting, follower);
        batch.PostCompletionDelay = ShortDelay;
        batch.ApplyPostDelayOnCancellation = true;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => waiting.Status == TaskStatus.WaitingForCompletion).Should().BePositive();

        batch.Cancel().Should().BeTrue();
        batch.Status.Should().Be(BatchStatus.WaitingForPostDelay);
        waiting.Status.Should().Be(TaskStatus.Cancelled, "cancelling a batch cancels every unfinished task up front");
        follower.Status.Should().Be(TaskStatus.Cancelled);

        StepUntilElapsed(queue, () => batch.Status == BatchStatus.Cancelled).Should().BeTrue();
    }

    // Batch task failure modes.

    [Fact]
    public void Batch_FailBatchMode_OnTimeout_LeavesRemainingTasksQueued()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.Timeout = ShortDelay;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskFailureMode = BatchTaskFailureMode.FailBatch;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntilElapsed(queue, () => batch.Status == BatchStatus.Failed).Should().BeTrue();

        task.Status.Should().Be(TaskStatus.Failed);
        follower.Status.Should().Be(TaskStatus.Queued, "failing the batch abandons its remaining tasks in place");
        queue.QueueState.Should().Be(QueueState.Running, "FailBatch does not stop the queue");
    }

    [Fact]
    public void Batch_FailBatchMode_OnThrownAction_StillRunsRemainingTasks()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task", () => throw new InvalidOperationException("boom"));
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskFailureMode = BatchTaskFailureMode.FailBatch;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => batch.Status == BatchStatus.Failed).Should().BePositive();

        // A failure raised inside task execution never reaches the batch's failure-mode handler; the batch only fails
        // later, from its completion check, by which time the remaining task has already run.
        task.Status.Should().Be(TaskStatus.Failed);
        follower.Status.Should().Be(TaskStatus.Completed,
            "FailBatch stops the remaining tasks for a timeout failure but not for a failure thrown by the action");
    }

    [Fact]
    public void Batch_FailBatchAndStopQueueMode_StopsTheQueue()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.Timeout = ShortDelay;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskFailureMode = BatchTaskFailureMode.FailBatchAndStopQueue;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntilElapsed(queue, () => queue.QueueState == QueueState.Stopped).Should().BeTrue();

        batch.Status.Should().Be(BatchStatus.Failed);
        follower.Status.Should().Be(TaskStatus.Queued);
    }

    [Fact]
    public void Batch_ContinueRemainingFailureMode_CompletesTheBatchDespiteAFailedTask()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.Timeout = ShortDelay;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskFailureMode = BatchTaskFailureMode.ContinueRemaining;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntilElapsed(queue, () => batch.Status == BatchStatus.Completed).Should().BeTrue();

        task.Status.Should().Be(TaskStatus.Failed);
        follower.Status.Should().Be(TaskStatus.Completed);
    }

    // Batch task cancellation modes.

    [Fact]
    public void Batch_TaskCancellationMode_AppliesWithoutAPostCancellationDelay()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskCancellationMode = BatchTaskCancellationMode.CancelBatch;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => task.Status == TaskStatus.WaitingForCompletion).Should().BePositive();
        queue.CancelTask(task.SystemId).Should().BeTrue();

        // The mode used to be reachable only from the post-cancellation-delay finalizer, so whether a batch
        // reacted to one of its tasks being cancelled depended on whether that task happened to carry a delay.
        // Both routes now consult it, which is what makes this test the delay-free twin of
        // Batch_CancelBatchMode_CancelsTheBatchThroughThePostCancellationDelay below.
        batch.Status.Should().Be(BatchStatus.Cancelled, "CancelBatch applies however the task was cancelled");
        follower.Status.Should().Be(TaskStatus.Cancelled, "cancelling the batch cancels its remaining tasks");
        queue.QueueState.Should().Be(QueueState.Running, "CancelBatch stops the batch, not the queue");
    }

    [Fact]
    public void Batch_CancelBatchMode_CancelsTheBatchThroughThePostCancellationDelay()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.PostCompletionDelay = ShortDelay;
        task.ApplyPostDelayOnCancellation = true;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskCancellationMode = BatchTaskCancellationMode.CancelBatch;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => task.Status == TaskStatus.WaitingForCompletion).Should().BePositive();
        queue.CancelTask(task.SystemId).Should().BeTrue();
        task.Status.Should().Be(TaskStatus.WaitingForPostDelay);

        StepUntilElapsed(queue, () => task.Status == TaskStatus.Cancelled).Should().BeTrue();

        batch.Status.Should().Be(BatchStatus.Cancelled);
        follower.Status.Should().Be(TaskStatus.Cancelled, "cancelling the batch cancels its remaining tasks");
        queue.QueueState.Should().Be(QueueState.Running);
    }

    [Fact]
    public void Batch_CancelBatchAndQueueMode_AlsoStopsTheQueue()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.PostCompletionDelay = ShortDelay;
        task.ApplyPostDelayOnCancellation = true;
        var batch = MakeBatch("batch", task, ImmediateTask("follower"));
        batch.TaskCancellationMode = BatchTaskCancellationMode.CancelBatchAndQueue;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => task.Status == TaskStatus.WaitingForCompletion).Should().BePositive();
        queue.CancelTask(task.SystemId).Should().BeTrue();

        StepUntilElapsed(queue, () => queue.QueueState == QueueState.Stopped).Should().BeTrue();

        batch.Status.Should().Be(BatchStatus.Cancelled);
    }

    [Fact]
    public void Batch_ContinueRemainingCancellationMode_RunsTheRemainingTasks()
    {
        var queue = MakeQueue();
        var task = GatedTask("task", () => false);
        task.PostCompletionDelay = ShortDelay;
        task.ApplyPostDelayOnCancellation = true;
        var follower = ImmediateTask("follower");
        var batch = MakeBatch("batch", task, follower);
        batch.TaskCancellationMode = BatchTaskCancellationMode.ContinueRemaining;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        StepUntil(queue, () => task.Status == TaskStatus.WaitingForCompletion).Should().BePositive();
        queue.CancelTask(task.SystemId).Should().BeTrue();

        StepUntilElapsed(queue, () => batch.Status == BatchStatus.Completed).Should().BeTrue();

        task.Status.Should().Be(TaskStatus.Cancelled);
        follower.Status.Should().Be(TaskStatus.Completed);
    }

    // Batch blocking.

    [Fact]
    public void Batch_WhenBlocking_HoldsBackTheItemsBehindIt()
    {
        var queue = MakeQueue();
        var held = GatedTask("held", () => false);
        var batch = MakeBatch("batch", held);
        var after = ImmediateTask("after");
        queue.EnqueueBatch(batch);
        queue.EnqueueTask(after);
        queue.StartQueue();

        StepUntil(queue, () => held.Status == TaskStatus.WaitingForCompletion).Should().BePositive();
        StepUntil(queue, () => after.Status == TaskStatus.Completed, maxTicks: 10)
            .Should().Be(-1, "a blocking batch owns the pass, which is the default");

        after.Status.Should().Be(TaskStatus.Queued);
    }

    [Fact]
    public void Batch_WhenNonBlocking_LetsTheItemsBehindItRun()
    {
        var queue = MakeQueue();
        var held = GatedTask("held", () => false);
        var batch = MakeBatch("batch", held);
        batch.IsBlocking = false;
        var after = ImmediateTask("after");
        queue.EnqueueBatch(batch);
        queue.EnqueueTask(after);
        queue.StartQueue();

        StepUntil(queue, () => after.Status == TaskStatus.Completed)
            .Should().BePositive("a non-blocking batch does not hold back the queue behind it");

        held.Status.Should().Be(TaskStatus.WaitingForCompletion, "and the batch is still working alongside it");
        batch.Status.Should().Be(BatchStatus.Processing);
    }

    // Batch outcome when its tasks are cancelled individually.

    [Fact]
    public void Batch_WhenEveryTaskWasCancelled_EndsCancelledRatherThanCompleted()
    {
        var queue = MakeQueue();
        var first = ImmediateTask("first");
        var second = ImmediateTask("second");
        var batch = MakeBatch("batch", first, second);
        batch.TaskCancellationMode = BatchTaskCancellationMode.ContinueRemaining;

        var completedCallbacks = 0;
        var cancelledCallbacks = 0;
        batch.OnCompleted = _ => completedCallbacks++;
        batch.OnCancelled = _ => cancelledCallbacks++;

        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.CancelTask(first.SystemId).Should().BeTrue();
        queue.CancelTask(second.SystemId).Should().BeTrue();

        StepUntil(queue, () => batch.Status != BatchStatus.Queued && batch.Status != BatchStatus.Processing)
            .Should().BePositive();

        batch.Status.Should().Be(BatchStatus.Cancelled, "no task in the batch ran to completion");
        cancelledCallbacks.Should().Be(1, "the batch reports the outcome it actually had");
        completedCallbacks.Should().Be(0, "and does not claim work it never did");
    }

    [Fact]
    public void Batch_WhenOnlySomeTasksWereCancelled_StillEndsCompleted()
    {
        var queue = MakeQueue();
        var cancelled = ImmediateTask("cancelled");
        var survivor = ImmediateTask("survivor");
        var batch = MakeBatch("batch", cancelled, survivor);
        batch.TaskCancellationMode = BatchTaskCancellationMode.ContinueRemaining;
        queue.EnqueueBatch(batch);
        queue.StartQueue();

        queue.CancelTask(cancelled.SystemId).Should().BeTrue();

        StepUntil(queue, () => batch.Status == BatchStatus.Completed)
            .Should().BePositive("a batch that reached its end with work done is complete, so only the all-cancelled case diverts");

        survivor.Status.Should().Be(TaskStatus.Completed);
    }

    // Queue completion.

    [Fact]
    public void Queue_StopsAndClearsItsItems_WhenEverythingFinishes()
    {
        var queue = MakeQueue();
        var task = ImmediateTask("task");
        queue.EnqueueTask(task);
        queue.StartQueue();

        StepUntil(queue, () => queue.QueueState == QueueState.Stopped).Should().BePositive();

        task.Status.Should().Be(TaskStatus.Completed);
        queue.GetAllTasks().Should().BeEmpty("stopping the queue clears it");
    }

    [Fact]
    public void Queue_GoesIdleAndKeepsItsItems_WhenStopOnCompleteIsDisabled()
    {
        var queue = MakeQueue();
        queue.SetAutoStopQueueOnComplete(false);
        var task = ImmediateTask("task");
        queue.EnqueueTask(task);
        queue.StartQueue();

        StepUntil(queue, () => queue.QueueState == QueueState.Idle).Should().BePositive();

        task.Status.Should().Be(TaskStatus.Completed);
        queue.GetAllTasks().Should().HaveCount(1, "going idle leaves the finished items in the queue");
    }
}
