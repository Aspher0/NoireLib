using FluentAssertions;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Characterizes what happens when a consumer removes, skips or bulk-cancels queue items from inside a completion
/// condition, which is consumer code the queue evaluates while holding its private queue lock on the processing
/// thread.<br/>
/// Because a C# <c>lock</c> is a re-entrant monitor, every one of these calls acquires the lock again and mutates
/// the very collections the pass is walking. The tests step the queue one pass at a time so that both halves of the
/// question are recorded: what the mutation does, and whether the pass that was already in flight sees it.<br/>
/// The scope here is the removal side of that surface: <see cref="NoireTaskQueue.SkipCurrentTask"/>,
/// <see cref="NoireTaskQueue.SkipNextTasks"/>, <see cref="NoireTaskQueue.SkipCurrentBatch"/>,
/// <see cref="NoireTaskQueue.SkipNextBatches"/>, <see cref="NoireTaskQueue.CancelAllTasks"/>,
/// <see cref="NoireTaskQueue.CancelBatch(Guid)"/>, <see cref="NoireTaskQueue.CancelAllBatches"/>,
/// <see cref="NoireTaskQueue.ClearCompletedTasks"/> and <see cref="NoireTaskQueue.ClearCompletedBatches"/>.
/// The clear, stop and single-task cancel routes are characterized in NoireTaskQueueConditionTests and are not
/// repeated here.<br/>
/// Every expectation below records what the code does today, not what it ought to do. Several of them pin outcomes
/// that are plainly wrong: an operation that reports success while changing nothing, a batch that reports itself
/// completed after every one of its tasks was cancelled, a cancellation that turns into a completion, and per-task
/// cancellation callbacks that are skipped entirely on one of the two cancellation routes. Each of those carries a
/// comment saying so. They are the expectations a change that iterates the queue's own work over internal snapshots
/// is expected to revisit deliberately.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueReentrantRemovalTests : IDisposable
{
    /// <summary>
    /// Duration used for the one post-completion delay in this file. Long enough that a pass cannot outrun it,
    /// short enough not to slow the suite down noticeably.
    /// </summary>
    private static readonly TimeSpan PostDelay = TimeSpan.FromMilliseconds(400);

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
    /// Steps the queue a fixed number of passes. Every re-entrant callback in this file is latched so it fires
    /// once, and stepping is always bounded, so no scenario here can spin or recurse without end.
    /// </summary>
    private static void Step(NoireTaskQueue queue, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
            queue.TickOnce();
    }

    /// <summary>
    /// Builds a plain non-blocking task whose condition never holds, used as inert filler around the task under
    /// test.
    /// </summary>
    private static QueuedTask NeverReady(NoireTaskQueue queue, string customId)
        => queue.CreateTask(customId).AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

    // -- SkipCurrentTask ------------------------------------------------------------------------------------------

    [Fact]
    public void SkipCurrentTask_FromTheCurrentTasksOwnCondition_CancelsItAndLetsTheSamePassStartTheNextTask()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipReturned = false;
        var cancelCallbacks = 0;
        TaskStatus statusInsideCondition = default;
        QueuedTask? currentInsideCondition = null;
        QueueState stateInsideCondition = default;

        QueuedTask? host = null;
        host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipReturned = queue.SkipCurrentTask();
                    statusInsideCondition = host!.Status;
                    currentInsideCondition = queue.GetCurrentTask();
                    stateInsideCondition = queue.QueueState;
                }

                return false;
            }).Enqueue();

        var next = queue.CreateTask("next").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        var cancelledHost = host;
        cancelledHost.OnCancelled = _ => cancelCallbacks++;

        queue.StartQueue();
        Step(queue, 2);

        skipReturned.Should().BeTrue("the queue lock is re-entrant, so a skip issued from the condition the pass is evaluating acquires it again and runs to completion rather than deadlocking");
        statusInsideCondition.Should().Be(TaskStatus.Cancelled, "the skip is applied to live state, so the condition observes its own task finished before it has even returned");
        cancelCallbacks.Should().Be(1, "SkipCurrentTask routes through the single-task cancellation path, which does raise the task's OnCancelled callback");
        currentInsideCondition.Should().BeNull("cancelling the current task clears the queue's current-task reference immediately");
        stateInsideCondition.Should().Be(QueueState.Running, "SkipCurrentTask pauses and resumes around its work, and both transitions have happened by the time it returns");
        host!.Status.Should().Be(TaskStatus.Cancelled);
        next.Status.Should().Be(TaskStatus.Completed,
            "the pass goes on to pick the next queued item after evaluating conditions, so the task that follows the skipped one starts within that same pass rather than on the following one");
    }

    [Fact]
    public void SkipCurrentTask_FromAnotherTasksCondition_CancelsWhicheverTaskIsCurrentAtThatMoment()
    {
        var queue = MakeQueue();
        var armed = false;
        var skipReturned = false;
        var thirdCancelCallbacks = 0;

        var first = NeverReady(queue, "first");

        var second = queue.CreateTask("second").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed)
                {
                    armed = false;
                    skipReturned = queue.SkipCurrentTask();
                }

                return false;
            }).Enqueue();

        var third = NeverReady(queue, "third");
        third.OnCancelled = _ => thirdCancelCallbacks++;

        queue.StartQueue();
        Step(queue, 4);

        queue.GetCurrentTask().Should().BeSameAs(third,
            "the pass picks up one queued task per pass, so after four passes the last of the three is the current one and the other two are evaluated from the batched waiting loop");

        armed = true;
        Step(queue, 1);

        skipReturned.Should().BeTrue("the skip is not scoped to the task whose condition issued it - it targets whatever the queue considers current");
        third.Status.Should().Be(TaskStatus.Cancelled,
            "a condition belonging to one task cancels a completely different task, and the cancellation lands immediately even though that task's own condition had already been evaluated earlier in this same pass");
        thirdCancelCallbacks.Should().Be(1);
        second.Status.Should().Be(TaskStatus.WaitingForCompletion, "the task whose condition issued the skip is untouched by it");
        queue.GetCurrentTask().Should().BeSameAs(first,
            "the same pass then adopts the first still-waiting task as current, so the current-task reference moves twice within one pass");
    }

    [Fact]
    public void SkipCurrentTask_FromABatchTasksCondition_ConsultsTheBatchesCancellationMode()
    {
        // The batch is configured to cancel itself and stop the queue as soon as one of its tasks is cancelled.
        // A skip resolves its target by cancelling it, so it counts as a task cancellation and the batch reacts
        // to it. This is deliberate: BatchTaskCancellationMode describes what the batch does when one of its
        // tasks is cancelled, and a batch that opts into CancelBatchAndQueue is asking for exactly this.
        // Skipping without disturbing the batch means clearing the mode, not reaching for a different verb.
        var queue = MakeQueue();
        var entered = false;
        var skipReturned = false;
        var cancelCallbacks = 0;

        var skipped = new QueuedTask("batch.skipped", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipReturned = queue.SkipCurrentTask();
                }

                return false;
            })
        };
        skipped.OnCancelled = _ => cancelCallbacks++;

        var survivor = new QueuedTask("batch.survivor", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        var batch = queue.CreateBatch("batch")
            .WithTaskCancellationMode(BatchTaskCancellationMode.CancelBatchAndQueue)
            .AddTask(skipped)
            .AddTask(survivor)
            .Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        skipReturned.Should().BeTrue("inside a batch the skip targets the batch's own executing task rather than the queue-level current task");
        skipped.Status.Should().Be(TaskStatus.Cancelled);
        cancelCallbacks.Should().Be(1, "the per-task cancellation path raises OnCancelled");
        survivor.Status.Should().Be(TaskStatus.Cancelled, "CancelBatchAndQueue cancels the batch's remaining tasks");
        batch.Status.Should().Be(BatchStatus.Cancelled, "so skipping one task takes the batch down under this mode");
        queue.QueueState.Should().Be(QueueState.Stopped, "and CancelBatchAndQueue stops the queue as well");
    }

    // -- SkipNextTasks --------------------------------------------------------------------------------------------

    [Fact]
    public void SkipNextTasks_WithIncludeCurrentTask_CountsTheCurrentTaskTowardsTheRequestedCount()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipCount = -1;
        var cancelCallbacks = 0;

        QueuedTask? host = null;
        host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipCount = queue.SkipNextTasks(3, includeCurrentTask: true);
                }

                return false;
            }).Enqueue();

        var victimA = queue.CreateTask("victim.a").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        var victimB = queue.CreateTask("victim.b").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        var survivor = queue.CreateTask("survivor").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        host!.OnCancelled = _ => cancelCallbacks++;
        victimA.OnCancelled = _ => cancelCallbacks++;
        victimB.OnCancelled = _ => cancelCallbacks++;
        survivor.OnCancelled = _ => cancelCallbacks++;

        queue.StartQueue();
        Step(queue, 2);

        skipCount.Should().Be(3, "the current task consumes one of the three, so only two queued tasks are taken after it");
        host.Status.Should().Be(TaskStatus.Cancelled);
        victimA.Status.Should().Be(TaskStatus.Cancelled);
        victimB.Status.Should().Be(TaskStatus.Cancelled);
        cancelCallbacks.Should().Be(3, "each of the three goes through the single-task cancellation path, so each raises its own OnCancelled");
        survivor.Status.Should().Be(TaskStatus.Completed,
            "the count is respected exactly, and the first task past it is started and completed by the remainder of the same pass");
    }

    [Fact]
    public void SkipNextTasks_CrossContext_ReachesIntoQueuedBatchesAndPastThem()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipCount = -1;

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipCount = queue.SkipNextTasks(5, includeCurrentTask: false, ContextDefinition.CrossContext);
                }

                return false;
            }).Enqueue();

        var mid = queue.CreateTask("mid").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        var inBatchA = new QueuedTask("batch.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var inBatchB = new QueuedTask("batch.b", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = queue.CreateBatch("batch").AddTask(inBatchA).AddTask(inBatchB).Enqueue();

        var tail = queue.CreateTask("tail").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        skipCount.Should().Be(4, "CrossContext walks the whole queue, taking queued standalone tasks, the queued tasks held inside a batch, and the standalone tasks that follow that batch");
        mid.Status.Should().Be(TaskStatus.Cancelled);
        inBatchA.Status.Should().Be(TaskStatus.Cancelled);
        inBatchB.Status.Should().Be(TaskStatus.Cancelled);
        tail.Status.Should().Be(TaskStatus.Cancelled);
        host.Status.Should().Be(TaskStatus.WaitingForCompletion,
            "only tasks in Queued status are skipped, so the task whose condition issued the skip is never a candidate");
        batch.Status.Should().Be(BatchStatus.Processing,
            "cancelling every task in a batch leaves the batch itself Queued, so the same pass still picks it up and starts it");

        Step(queue, 1);

        batch.Status.Should().Be(BatchStatus.Cancelled,
            "a batch whose every task was cancelled ends cancelled rather than completed, so it does not raise OnCompleted for work none of which ran");
    }

    [Fact]
    public void SkipNextTasks_SameContext_SkipsPastABatchWithoutEnteringIt()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipCount = -1;
        TaskStatus batchTaskStatusInsideCondition = default;

        var inBatchA = new QueuedTask("batch.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var inBatchB = new QueuedTask("batch.b", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipCount = queue.SkipNextTasks(5, includeCurrentTask: false, ContextDefinition.SameContext);
                    batchTaskStatusInsideCondition = inBatchA.Status;
                }

                return false;
            }).Enqueue();

        var mid = queue.CreateTask("mid").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        queue.CreateBatch("batch").AddTask(inBatchA).AddTask(inBatchB).Enqueue();
        var tail = queue.CreateTask("tail").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        skipCount.Should().Be(2, "SameContext only considers standalone tasks when the caller is standalone, so the batch's own tasks are not candidates");
        batchTaskStatusInsideCondition.Should().Be(TaskStatus.Queued, "and that is already true at the moment the call returns");
        mid.Status.Should().Be(TaskStatus.Cancelled);
        tail.Status.Should().Be(TaskStatus.Cancelled,
            "a batch sitting between two standalone tasks does not stop the walk under SameContext, so the task after it is still skipped");

        Step(queue, 4);

        inBatchA.Status.Should().Be(TaskStatus.Completed, "the batch's tasks were never touched, so the batch runs to completion normally");
        inBatchB.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void SkipNextTasks_SameContextStrict_StopsAtTheFirstBatchBoundary()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipCount = -1;

        var inBatch = new QueuedTask("batch.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipCount = queue.SkipNextTasks(5, includeCurrentTask: false, ContextDefinition.SameContextStrict);
                }

                return false;
            }).Enqueue();

        var mid = queue.CreateTask("mid").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        queue.CreateBatch("batch").AddTask(inBatch).Enqueue();
        var tail = queue.CreateTask("tail").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        skipCount.Should().Be(1, "the walk stops dead at the first batch item, so only the standalone task before it is skipped");
        mid.Status.Should().Be(TaskStatus.Cancelled);
        inBatch.Status.Should().Be(TaskStatus.Queued);
        tail.Status.Should().Be(TaskStatus.Queued, "a task past the batch boundary is unreachable under SameContextStrict even though it is standalone");
    }

    [Fact]
    public void SkipNextTasks_ReachesWaitingTasksAndNotOnlyQueuedOnes()
    {
        // Skipping used to consider tasks in Queued status alone, which made non-blocking tasks that had already
        // started and were waiting on their conditions invisible to it: a consumer asking to skip everything got
        // back a number far smaller than requested and the waiting tasks stayed non-terminal for as long as the
        // queue lived. A skip resolves an unfinished task, so every non-terminal status is a candidate now.
        var queue = MakeQueue();
        var armed = false;
        var skipCount = -1;

        var first = NeverReady(queue, "first");

        var second = queue.CreateTask("second").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed)
                {
                    armed = false;
                    skipCount = queue.SkipNextTasks(10, includeCurrentTask: true);
                }

                return false;
            }).Enqueue();

        var third = NeverReady(queue, "third");

        queue.StartQueue();
        Step(queue, 4);

        armed = true;
        Step(queue, 1);

        skipCount.Should().Be(3, "ten were requested and the queue held three unfinished tasks, all of which qualify");
        third.Status.Should().Be(TaskStatus.Cancelled, "the current task is skipped through the includeCurrentTask branch");
        first.Status.Should().Be(TaskStatus.Cancelled, "a task waiting on its condition is resolvable by a skip");
        second.Status.Should().Be(TaskStatus.Cancelled, "including the one whose own condition issued the skip");

        Step(queue, 10);

        queue.GetPendingTaskCount().Should().Be(0,
            "the skip resolved every task the queue held, which is what asking to skip ten of three should do");
    }

    [Fact]
    public void SkipNextTasks_WithoutIncludeCurrentTask_LeavesTheCurrentTaskAlone()
    {
        // The walks resolve any unfinished task, so the current one has to be held back explicitly rather than
        // by relying on it not being Queued.
        var queue = MakeQueue();
        var first = NeverReady(queue, "first");
        var second = NeverReady(queue, "second");

        queue.StartQueue();
        Step(queue, 4);

        first.Status.Should().Be(TaskStatus.WaitingForCompletion);
        second.Status.Should().Be(TaskStatus.WaitingForCompletion);

        var skipCount = queue.SkipNextTasks(10, includeCurrentTask: false);

        skipCount.Should().Be(1, "the current task is held back, so only the other waiting task is resolved");
        second.Status.Should().Be(TaskStatus.WaitingForCompletion, "the most recently started task is the current one");
        first.Status.Should().Be(TaskStatus.Cancelled);
    }

    // -- SkipCurrentBatch and SkipNextBatches ---------------------------------------------------------------------

    [Fact]
    public void SkipCurrentBatch_FromAConditionInsideThatBatch_CancelsEveryTaskAndRaisesTheirCallbacks()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipReturned = false;
        var taskCancelCallbacks = 0;
        var batchCancelCallbacks = 0;
        BatchStatus batchStatusInsideCondition = default;
        TaskBatch? currentBatchInsideCondition = null;

        var host = new QueuedTask("batch.host", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipReturned = queue.SkipCurrentBatch();
                    currentBatchInsideCondition = queue.GetCurrentBatch();
                }

                return false;
            })
        };

        var pendingA = new QueuedTask("batch.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var pendingB = new QueuedTask("batch.b", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };

        foreach (var task in new[] { host, pendingA, pendingB })
            task.OnCancelled = _ => taskCancelCallbacks++;

        TaskBatch? batch = null;
        batch = queue.CreateBatch("batch")
            .OnCancelled(() =>
            {
                batchCancelCallbacks++;
                batchStatusInsideCondition = batch!.Status;
            })
            .AddTask(host).AddTask(pendingA).AddTask(pendingB)
            .Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        skipReturned.Should().BeTrue();
        batch!.Status.Should().Be(BatchStatus.Cancelled);
        batchStatusInsideCondition.Should().Be(BatchStatus.Processing, "the batch's own callback runs before its status is written, so it observes the batch mid-cancellation");
        currentBatchInsideCondition.Should().BeNull("the current-batch reference is cleared before the call returns, while the pass that is iterating that batch is still on the stack");
        host.Status.Should().Be(TaskStatus.Cancelled);
        pendingA.Status.Should().Be(TaskStatus.Cancelled);
        pendingB.Status.Should().Be(TaskStatus.Cancelled);
        batchCancelCallbacks.Should().Be(1);
        taskCancelCallbacks.Should().Be(3,
            "cancelling a batch raises each held task's own cancellation callback, so a task learns it was cancelled whichever route did the cancelling");

        Step(queue, 1);

        batch.Status.Should().Be(BatchStatus.Cancelled,
            "the pass that was interrupted goes on to run its batch completion check, and only the terminal-status guard inside the completion routine stops it from overwriting the cancellation");
    }

    [Fact]
    public void SkipCurrentBatch_WithNoCurrentBatch_IsASilentNoOp()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipReturned = true;

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipReturned = queue.SkipCurrentBatch();
                }

                return false;
            }).Enqueue();

        var inBatch = new QueuedTask("batch.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = queue.CreateBatch("batch").AddTask(inBatch).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        entered.Should().BeTrue();
        skipReturned.Should().BeFalse("a batch that is merely queued is not the current batch, so there is nothing for the call to target");
        batch.Status.Should().Be(BatchStatus.Processing,
            "the call reports failure and changes nothing, so the same pass goes on to start the batch it declined to skip");
        inBatch.Status.Should().Be(TaskStatus.Queued);
    }

    [Fact]
    public void SkipNextBatches_WithIncludeCurrentBatch_CancelsTheOwningBatchAndTheNextQueuedOne()
    {
        var queue = MakeQueue();
        var entered = false;
        var skipCount = -1;

        var host = new QueuedTask("a.host", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!entered)
                {
                    entered = true;
                    skipCount = queue.SkipNextBatches(2, includeCurrentBatch: true);
                }

                return false;
            })
        };

        var batchA = queue.CreateBatch("batch.a").AddTask(host).Enqueue();

        var inB = new QueuedTask("b.task", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batchB = queue.CreateBatch("batch.b").AddTask(inB).Enqueue();

        var inC = new QueuedTask("c.task", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batchC = queue.CreateBatch("batch.c").AddTask(inC).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        skipCount.Should().Be(2, "the batch that owns the calling condition counts as one of the two, leaving room for exactly one queued batch after it");
        batchA.Status.Should().Be(BatchStatus.Cancelled);
        host.Status.Should().Be(TaskStatus.Cancelled);
        batchB.Status.Should().Be(BatchStatus.Cancelled);
        inB.Status.Should().Be(TaskStatus.Cancelled, "cancelling a batch cancels every task it holds, even one that had not started");
        batchC.Status.Should().Be(BatchStatus.Queued, "the count is respected exactly");

        Step(queue, 3);

        inC.Status.Should().Be(TaskStatus.Completed, "the queue moves on to the surviving batch on a later pass and runs it normally");
        batchC.Status.Should().Be(BatchStatus.Completed);
    }

    // -- CancelAllTasks -------------------------------------------------------------------------------------------

    [Fact]
    public void CancelAllTasks_CrossContext_CancelsEverySharedIdIncludingTheCallersOwnTask()
    {
        var queue = MakeQueue();
        var entered = false;
        var cancelledCount = -1;
        var cancelCallbacks = 0;
        TaskStatus hostStatusInsideCondition = default;

        QueuedTask? host = null;
        host = queue.CreateTask("dup").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    cancelledCount = queue.CancelAllTasks("dup", ContextDefinition.CrossContext);
                    hostStatusInsideCondition = host!.Status;
                }

                return false;
            }).Enqueue();

        var mid = queue.CreateTask("dup").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        var inBatch = new QueuedTask("dup", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = queue.CreateBatch("batch").AddTask(inBatch).Enqueue();

        var tail = queue.CreateTask("dup").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        foreach (var task in new[] { host!, mid, inBatch, tail })
            task.OnCancelled = _ => cancelCallbacks++;

        queue.StartQueue();
        Step(queue, 2);

        cancelledCount.Should().Be(4, "CrossContext collects matching tasks from the top level and from inside every batch, and the calling condition's own task is one of them");
        hostStatusInsideCondition.Should().Be(TaskStatus.Cancelled, "the caller's own task is finished underneath it while its condition is still running");
        host!.Status.Should().Be(TaskStatus.Cancelled);
        mid.Status.Should().Be(TaskStatus.Cancelled);
        inBatch.Status.Should().Be(TaskStatus.Cancelled);
        tail.Status.Should().Be(TaskStatus.Cancelled);
        cancelCallbacks.Should().Be(4, "this route goes through the per-task cancellation path, so unlike batch cancellation every task does get its OnCancelled");
        batch.Status.Should().Be(BatchStatus.Processing, "the batch itself is untouched, so the same pass still starts it");

        Step(queue, 1);

        batch.Status.Should().Be(BatchStatus.Cancelled,
            "a batch whose only task was cancelled ends cancelled, matching the outcome its contents actually had");
    }

    [Fact]
    public void CancelAllTasks_SameContext_LeavesTasksHeldInsideBatchesAlone()
    {
        var queue = MakeQueue();
        var entered = false;
        var cancelledCount = -1;

        var inBatch = new QueuedTask("dup", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };

        queue.CreateTask("dup").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    cancelledCount = queue.CancelAllTasks("dup", ContextDefinition.SameContext);
                }

                return false;
            }).Enqueue();

        var mid = queue.CreateTask("dup").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        queue.CreateBatch("batch").AddTask(inBatch).Enqueue();
        var tail = queue.CreateTask("dup").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        cancelledCount.Should().Be(3, "a standalone caller only reaches standalone tasks, so the identically named task inside the batch is not a candidate");
        mid.Status.Should().Be(TaskStatus.Cancelled);
        tail.Status.Should().Be(TaskStatus.Cancelled, "the batch does not stop the walk under SameContext");
        inBatch.Status.Should().Be(TaskStatus.Queued);

        Step(queue, 3);

        inBatch.Status.Should().Be(TaskStatus.Completed, "and it goes on to run normally");
    }

    [Fact]
    public void CancelAllTasks_SameContextStrict_StopsAtTheFirstBatchBoundary()
    {
        var queue = MakeQueue();
        var entered = false;
        var cancelledCount = -1;

        var inBatch = new QueuedTask("dup", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };

        queue.CreateTask("dup").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    cancelledCount = queue.CancelAllTasks("dup", ContextDefinition.SameContextStrict);
                }

                return false;
            }).Enqueue();

        var mid = queue.CreateTask("dup").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        queue.CreateBatch("batch").AddTask(inBatch).Enqueue();
        var tail = queue.CreateTask("dup").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        cancelledCount.Should().Be(2, "the collection walk breaks at the first batch item that sits after the current task, so only the caller and the standalone task before the batch are taken");
        mid.Status.Should().Be(TaskStatus.Cancelled);
        inBatch.Status.Should().Be(TaskStatus.Queued);
        tail.Status.Should().Be(TaskStatus.Queued, "a standalone task past a batch boundary is unreachable under SameContextStrict");
    }

    [Fact]
    public void CancelAllTasks_TargetingATaskInItsPostCompletionDelay_CancelsIt()
    {
        // The cancellation path re-reads the status after raising OnCancelled and compares it against the status
        // captured beforehand, so only a status the callback itself moved stands the cancellation down. A task
        // already waiting on its post completion delay when the cancellation arrives is cancelled rather than
        // left to complete via the elapsing delay.
        var queue = MakeQueue();
        var entered = false;
        var cancelledCount = -1;
        var cancelCallbacks = 0;
        var completedCallbacks = 0;
        TaskStatus statusInsideCondition = default;

        var delayed = queue.CreateTask("delayed").AsNonBlocking().WithAction(() => { })
            .WithImmediateCompletion()
            .WithDelay(PostDelay)
            .OnCancelled(() => cancelCallbacks++)
            .OnCompleted(() => completedCallbacks++)
            .Enqueue();

        queue.CreateTask("watcher").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    cancelledCount = queue.CancelAllTasks("delayed");
                    statusInsideCondition = delayed.Status;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        entered.Should().BeTrue();
        cancelledCount.Should().Be(1, "the call reports that it cancelled the task");
        cancelCallbacks.Should().Be(1, "and the task's cancellation callback really did run");
        statusInsideCondition.Should().Be(TaskStatus.Cancelled,
            "the status the task already had no longer stands the cancellation down, so a task cancelled while waiting on its post delay is written Cancelled like any other");

        Thread.Sleep((int)PostDelay.TotalMilliseconds + 150);
        Step(queue, 3);

        delayed.Status.Should().Be(TaskStatus.Cancelled,
            "and it stays cancelled: the elapsed delay no longer finds a task in WaitingForPostDelay to complete");
        completedCallbacks.Should().Be(0, "so no completion callback runs on a task the consumer was told had been cancelled");
        queue.GetStatistics().CancelledTasks.Should().Be(1, "and the statistic agrees with what the caller was told");
    }

    // -- CancelBatch and CancelAllBatches -------------------------------------------------------------------------

    [Fact]
    public void CancelBatch_BySystemId_FromAStandaloneCondition_CancelsTheBatchAndAllItsTasksImmediately()
    {
        var queue = MakeQueue();
        var entered = false;
        var cancelReturned = false;
        var taskCancelCallbacks = 0;
        var batchCancelCallbacks = 0;
        BatchStatus statusInsideCondition = default;

        var inBatchA = new QueuedTask("batch.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var inBatchB = new QueuedTask("batch.b", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };

        foreach (var task in new[] { inBatchA, inBatchB })
            task.OnCancelled = _ => taskCancelCallbacks++;

        TaskBatch? batch = null;

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    cancelReturned = queue.CancelBatch(batch!.SystemId);
                    statusInsideCondition = batch.Status;
                }

                return false;
            }).Enqueue();

        batch = queue.CreateBatch("batch")
            .OnCancelled(() => batchCancelCallbacks++)
            .AddTask(inBatchA).AddTask(inBatchB)
            .Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        cancelReturned.Should().BeTrue("cancelling a batch by id from inside a condition takes the queue lock re-entrantly and runs to completion");
        statusInsideCondition.Should().Be(BatchStatus.Cancelled, "and the effect is visible before the call returns");
        inBatchA.Status.Should().Be(TaskStatus.Cancelled);
        inBatchB.Status.Should().Be(TaskStatus.Cancelled);
        batchCancelCallbacks.Should().Be(1);
        taskCancelCallbacks.Should().Be(2,
            "the batch route raises each held task's own cancellation callback, matching what the per-task routes have always done");
        batch.Status.Should().Be(BatchStatus.Cancelled);
    }

    [Fact]
    public void CancelBatch_ByCustomId_TargetingTheBatchThatOwnsTheCallingCondition_Succeeds()
    {
        var queue = MakeQueue();
        var entered = false;
        var cancelReturned = false;
        QueueState stateInsideCondition = default;

        var host = new QueuedTask("batch.host", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!entered)
                {
                    entered = true;
                    cancelReturned = queue.CancelBatch("batch");
                    stateInsideCondition = queue.QueueState;
                }

                return false;
            })
        };

        var sibling = new QueuedTask("batch.sibling", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = queue.CreateBatch("batch").AddTask(host).AddTask(sibling).Enqueue();

        var tail = queue.CreateTask("tail").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        cancelReturned.Should().BeTrue("a task can cancel the batch it belongs to from its own completion condition, while the pass is iterating that batch's task list");
        stateInsideCondition.Should().Be(QueueState.Running,
            "unlike the skip routes, cancelling by id does not pause and resume the queue, so the pass never sees a state change at all");
        batch.Status.Should().Be(BatchStatus.Cancelled);
        host.Status.Should().Be(TaskStatus.Cancelled);
        sibling.Status.Should().Be(TaskStatus.Cancelled, "the sibling never runs, because it was cancelled before the pass reached it");

        Step(queue, 2);

        batch.Status.Should().Be(BatchStatus.Cancelled, "the interrupted pass's own batch completion check does not overwrite the cancellation");
        tail.Status.Should().Be(TaskStatus.Completed, "and the queue carries on with the item after the cancelled batch");
    }

    [Fact]
    public void CancelAllBatches_CancelsEveryBatchCarryingTheId()
    {
        // The shared collection helper offers the batch itself to the predicate before descending into its tasks,
        // which is the only way a predicate testing for a batch can ever match.
        var queue = MakeQueue();
        var entered = false;
        var cancelledCount = -1;
        var batchCancelCallbacks = 0;

        var inA = new QueuedTask("a.task", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var inB = new QueuedTask("b.task", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    cancelledCount = queue.CancelAllBatches("grp");
                }

                return false;
            }).Enqueue();

        var batchA = queue.CreateBatch("grp").OnCancelled(() => batchCancelCallbacks++).AddTask(inA).Enqueue();
        var batchB = queue.CreateBatch("grp").OnCancelled(() => batchCancelCallbacks++).AddTask(inB).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        entered.Should().BeTrue();
        cancelledCount.Should().Be(2, "both batches carry the id, so both are candidates and both are cancelled");
        batchCancelCallbacks.Should().Be(2, "and each cancelled batch raises its own cancellation callback");

        Step(queue, 6);

        batchA.Status.Should().Be(BatchStatus.Cancelled, "a cancelled batch does not go on to run");
        batchB.Status.Should().Be(BatchStatus.Cancelled);
        inA.Status.Should().Be(TaskStatus.Cancelled, "and cancelling a batch resolves the tasks it holds");
        inB.Status.Should().Be(TaskStatus.Cancelled);
    }

    // -- ClearCompletedTasks and ClearCompletedBatches ------------------------------------------------------------

    [Fact]
    public void ClearCompletedTasks_FromACondition_RemovesFinishedTasksAndTheirStatisticsImmediately()
    {
        var queue = MakeQueue();
        var entered = false;
        var removed = -1;
        var tasksVisibleFromInside = -1;
        var completedFromInside = -1;

        var done = queue.CreateTask("done").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    removed = queue.ClearCompletedTasks();
                    tasksVisibleFromInside = queue.GetAllTasks().Count;
                    completedFromInside = queue.GetStatistics().CompletedTasks;
                }

                return false;
            }).Enqueue();

        var later = queue.CreateTask("later").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        removed.Should().Be(1, "clearing from inside a condition runs to completion instead of blocking on the lock the caller already holds");
        tasksVisibleFromInside.Should().Be(2, "the queue the pass is walking loses an element underneath it, and the condition can already see the shorter queue");
        completedFromInside.Should().Be(0,
            "the completed-task statistic counts tasks whose status is Completed rather than keeping a tally, so removing a completed task erases it from the statistics as well");
        done.Status.Should().Be(TaskStatus.Completed, "the removed task keeps its status - it is simply no longer reachable through the queue");
        queue.GetAllTasks().Should().NotContain(done);
        later.Status.Should().Be(TaskStatus.Completed, "and the same pass goes on to start and finish the next queued task");
    }

    [Fact]
    public void ClearCompletedTasks_RemovingATaskTheSamePassAlreadyDecidedToComplete_KeepsTheCancellation()
    {
        var queue = MakeQueue();
        var firstIsReady = false;
        var armed = false;
        var firstCompletedCallbacks = 0;
        var firstCancelCallbacks = 0;
        var removed = -1;

        var first = queue.CreateTask("first").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => firstIsReady)
            .OnCompleted(() => firstCompletedCallbacks++)
            .OnCancelled(() => firstCancelCallbacks++)
            .Enqueue();

        queue.CreateTask("second").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed)
                {
                    armed = false;
                    queue.CancelTask("first");
                    removed = queue.ClearCompletedTasks();
                }

                return false;
            }).Enqueue();

        NeverReady(queue, "third");

        queue.StartQueue();
        Step(queue, 4);

        first.Status.Should().Be(TaskStatus.WaitingForCompletion);

        // The pass decides to complete the first task, then a later task's condition cancels it and removes it
        // from the queue before the deferred completion runs.
        firstIsReady = true;
        armed = true;
        Step(queue, 1);

        removed.Should().Be(1, "the cancellation made the task eligible for clearing, so it is removed from the queue in the middle of the pass that was going to complete it");
        first.Status.Should().Be(TaskStatus.Cancelled,
            "the deferred completion re-validates the status before acting, so a task finished as cancelled keeps that outcome even though the pass had already collected it for completion");
        firstCancelCallbacks.Should().Be(1);
        firstCompletedCallbacks.Should().Be(0, "and the completion callback is never raised");
        queue.GetAllTasks().Should().NotContain(first);
        queue.GetStatistics().CompletedTasks.Should().Be(0);
    }

    [Fact]
    public void ClearCompletedTasks_RemovingATaskWrittenCompletedInTheSamePass_StillRunsItsCompletion()
    {
        // Writing a status directly is the one way a consumer can resolve a task as completed, and the deferred
        // completion deliberately does not treat that as a reason to stand down. Combined with a removal in the
        // same breath, the completion callback fires for a task that is no longer in the queue and the statistics
        // never show it. Recorded as today's behaviour.
        var queue = MakeQueue();
        var firstIsReady = false;
        var armed = false;
        var firstCompletedCallbacks = 0;
        var removed = -1;

        var first = queue.CreateTask("first").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => firstIsReady)
            .OnCompleted(() => firstCompletedCallbacks++)
            .Enqueue();

        queue.CreateTask("second").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed)
                {
                    armed = false;
                    first!.Status = TaskStatus.Completed;
                    removed = queue.ClearCompletedTasks();
                }

                return false;
            }).Enqueue();

        NeverReady(queue, "third");

        queue.StartQueue();
        Step(queue, 4);

        firstIsReady = true;
        armed = true;
        Step(queue, 1);

        removed.Should().Be(1, "the directly written status made the task eligible for clearing");
        first.Status.Should().Be(TaskStatus.Completed);
        firstCompletedCallbacks.Should().Be(1,
            "the deferred completion only stands down for a task finished as cancelled or failed, so it still runs here and raises the callback on a task that has already left the queue");
        queue.GetAllTasks().Should().NotContain(first);
        queue.GetStatistics().CompletedTasks.Should().Be(0,
            "and because the statistic is derived from the queue's contents, the completion it just raised is invisible in it");
    }

    [Fact]
    public void ClearCompletedTasks_RemovingATaskTheSamePassAlreadyDecidedToFail_KeepsTheCancellation()
    {
        var queue = MakeQueue();
        var firstShouldThrow = false;
        var armed = false;
        var firstFailedCallbacks = 0;
        var firstCancelCallbacks = 0;
        var removed = -1;

        var first = queue.CreateTask("first").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!firstShouldThrow)
                    return false;

                firstShouldThrow = false;
                throw new InvalidOperationException("condition failed");
            })
            .OnFailed(() => firstFailedCallbacks++)
            .OnCancelled(() => firstCancelCallbacks++)
            .Enqueue();

        queue.CreateTask("second").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed)
                {
                    armed = false;
                    queue.CancelTask("first");
                    removed = queue.ClearCompletedTasks();
                }

                return false;
            }).Enqueue();

        NeverReady(queue, "third");

        queue.StartQueue();
        Step(queue, 4);

        // The pass collects the first task for failure when its condition throws, then a later task's condition
        // cancels and removes it before the deferred failure runs.
        firstShouldThrow = true;
        armed = true;
        Step(queue, 1);

        removed.Should().Be(1);
        first.Status.Should().Be(TaskStatus.Cancelled,
            "the deferred failure re-validates against every terminal status, so a task cancelled after the throw stays cancelled rather than being failed a second time");
        firstCancelCallbacks.Should().Be(1);
        firstFailedCallbacks.Should().Be(0, "the failure callback is never raised, so the exception the condition threw surfaces to nobody");
        first.FailureException.Should().BeOfType<InvalidOperationException>(
            "recorded as today's behaviour rather than endorsed: the task carries the exception that faulted it while reporting a status of Cancelled");
        queue.GetAllTasks().Should().NotContain(first);
        queue.GetStatistics().FailedTasks.Should().Be(0);
    }

    [Fact]
    public void ClearCompletedBatches_FromACondition_RemovesTheBatchButKeepsItsTallies()
    {
        var queue = MakeQueue();
        var entered = false;
        var removed = -1;
        var batchesVisibleFromInside = -1;
        var tasksVisibleFromInside = -1;

        var inBatch = new QueuedTask("batch.task", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = queue.CreateBatch("batch").AddTask(inBatch).Enqueue();

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    removed = queue.ClearCompletedBatches();
                    batchesVisibleFromInside = queue.GetAllBatches().Count;
                    tasksVisibleFromInside = queue.GetAllTasks().Count;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 5);

        entered.Should().BeTrue();
        batch.Status.Should().Be(BatchStatus.Completed, "the batch had already finished by the time the host task's condition first ran");
        removed.Should().Be(1);
        batchesVisibleFromInside.Should().Be(0, "the removal is applied to the live queue, so the condition sees it before returning");
        tasksVisibleFromInside.Should().Be(1, "removing the batch takes the tasks it held out of the queue with it, leaving only the host task");

        var stats = queue.GetStatistics();
        stats.BatchesCompleted.Should().Be(1,
            "the batch counters are running tallies rather than derived from the queue, so unlike the task counters they survive the removal");
        stats.CompletedTasks.Should().Be(0, "while the task counter, being derived, forgets the batch's completed task entirely");
    }

    [Fact]
    public void ClearCompletedBatches_AfterCancellingTheOwningBatchInTheSamePass_RemovesItMidPass()
    {
        var queue = MakeQueue();
        var entered = false;
        var removed = -1;
        var batchesVisibleFromInside = -1;

        TaskBatch? batch = null;

        var host = new QueuedTask("batch.host", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!entered)
                {
                    entered = true;
                    queue.CancelBatch(batch!.SystemId);
                    removed = queue.ClearCompletedBatches();
                    batchesVisibleFromInside = queue.GetAllBatches().Count;
                }

                return false;
            })
        };

        batch = queue.CreateBatch("batch").AddTask(host).Enqueue();
        var tail = queue.CreateTask("tail").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        removed.Should().Be(1, "a cancelled batch counts as finished, so it is eligible for clearing the moment it is cancelled");
        batchesVisibleFromInside.Should().Be(0,
            "the batch is removed from the queue while the pass that is iterating its task list is still on the stack, and that pass carries on against the detached batch without faulting");
        batch!.Status.Should().Be(BatchStatus.Cancelled);
        host.Status.Should().Be(TaskStatus.Cancelled);

        Step(queue, 1);

        tail.Status.Should().Be(TaskStatus.Completed, "the queue picks up the item after the removed batch on the following pass");
        queue.QueueState.Should().Be(QueueState.Running);
    }

    // -- Strict boundary when a standalone task is followed by a batch --------------------------------------------

    [Fact]
    public void SkipNextTasks_Strict_WithAStandaloneCurrentTaskFollowedByABatch_SkipsOnlyThatTask()
    {
        // A standalone task is executing and the next item in the queue is a batch. SameContextStrict must stop at
        // that batch, so asking to skip the current task plus far more than the queue holds resolves exactly one
        // task and leaves the batch and its contents entirely alone.
        var queue = MakeQueue();
        var gate = false;

        var standalone = queue.CreateTask("standalone").WithAction(() => { })
            .WithCondition(() => gate).Enqueue();

        var inA = new QueuedTask("b.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var inB = new QueuedTask("b.b", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = queue.CreateBatch("batch").AddTask(inA).AddTask(inB).Enqueue();

        queue.StartQueue();
        Step(queue, 3);
        queue.GetCurrentTask().Should().BeSameAs(standalone, "the standalone task is the executing one when the skip is issued");

        var skipped = queue.SkipNextTasks(9, includeCurrentTask: true, ContextDefinition.SameContextStrict);

        skipped.Should().Be(1, "the batch is a strict boundary, so only the current standalone task is in scope");
        standalone.Status.Should().Be(TaskStatus.Cancelled);
        batch.Status.Should().NotBe(BatchStatus.Cancelled, "the batch is past the strict boundary and must not be touched");
        inA.Status.Should().NotBe(TaskStatus.Cancelled, "nor may anything inside it be");
        inB.Status.Should().NotBe(TaskStatus.Cancelled);
    }

    [Fact]
    public void SkipNextTasks_WhenSkippingATaskThatStopsTheQueue_DoesNotThrowWhileWalkingTheQueue()
    {
        // Cancelling a task can structurally change the queue: StopQueueOnCancel stops the queue, and stopping it
        // clears it. The skip helpers walked the live list while cancelling, so that surfaced to the caller as
        // "Collection was modified; enumeration operation may not execute" out of SkipNextTasks. They walk a
        // materialized copy now.
        var queue = MakeQueue();

        var head = queue.CreateTask("head").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        var stopper = queue.CreateTask("stopper").WithAction(() => { }).WithImmediateCompletion().Enqueue();
        stopper.StopQueueOnCancel = true;

        queue.CreateTask("trailing").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 1);
        head.Status.Should().Be(TaskStatus.WaitingForCompletion, "the head task holds the queue open while the two behind it stay queued");

        var act = () => queue.SkipNextTasks(9, includeCurrentTask: true, ContextDefinition.CrossContext);

        act.Should().NotThrow("a skip must survive a cancellation that clears the queue underneath it");
    }

    [Fact]
    public void SkipNextTasks_Strict_WithANonBlockingCurrentTaskWhileABatchProcesses_DoesNotReachIntoTheBatch()
    {
        // A non blocking standalone task waits on its condition, so the queue starts the batch behind it and both
        // a current task and a current batch exist at once. The current context is still the standalone task, so a
        // strict skip must stop at the batch and leave its contents alone.
        var queue = MakeQueue();
        var gate = false;

        var standalone = queue.CreateTask("standalone").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => gate).Enqueue();

        var inA = new QueuedTask("b.a", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var inB = new QueuedTask("b.b", isBlocking: false) { ExecuteAction = () => { }, CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = queue.CreateBatch("batch").AddTask(inA).AddTask(inB).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        queue.GetCurrentTask().Should().BeSameAs(standalone, "the standalone task is the current one");
        queue.GetCurrentBatch().Should().NotBeNull("and the batch behind it is processing at the same time");

        var skipped = queue.SkipNextTasks(9, includeCurrentTask: true, ContextDefinition.SameContextStrict);

        skipped.Should().Be(1, "only the current standalone task is inside the strict context");
        standalone.Status.Should().Be(TaskStatus.Cancelled);
        inB.Status.Should().NotBe(TaskStatus.Cancelled, "a task inside the batch is past the strict boundary and must not be skipped");
        batch.Status.Should().NotBe(BatchStatus.Cancelled, "nor may the batch itself be cancelled");
    }
}
