using FluentAssertions;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Characterizes what happens when a completion condition writes directly to the public properties of a
/// <see cref="QueuedTask"/> or a <see cref="TaskBatch"/> while the queue is in the middle of a processing pass.<br/>
/// Almost every part of a task's configuration and its status is a public settable property, so "complete this
/// task", "give that one a timeout" or "swap this callback out" is expressible as a plain property write with no
/// queue method involved. A condition runs inside the queue lock on the processing thread, so such a write lands
/// in the middle of a pass that has already made, or is about to make, decisions based on the value being
/// overwritten.<br/>
/// Each test pins three things: whether the write takes effect, when the queue next reads the property, and
/// whether a callback is gained or lost as a result. Several of the pinned outcomes are plainly undesirable: a
/// completion callback that never fires, a task or a batch left permanently unfinished, a statistic that
/// disagrees with what actually happened. Those are recorded here because they are today's behavior, not because
/// they are correct. They are the expectations that a change making consumer mutation safe is meant to alter
/// deliberately.<br/>
/// Every re-entrant callback in this file is latched so it fires once, and every scenario is stepped a bounded
/// number of passes, so nothing here can recurse or spin.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueReentrantMutationTests : IDisposable
{
    private readonly List<NoireTaskQueue> queuesToClean = new();

    /// <summary>
    /// A timeout that is already over the moment it is assigned, so a timeout test never depends on wall clock
    /// progress between two passes that may share the same tick value.
    /// </summary>
    private static readonly TimeSpan AlreadyElapsed = TimeSpan.FromMilliseconds(-1);

    /// <summary>
    /// A stall threshold that is already exceeded as soon as stall tracking has a starting point, for the same
    /// determinism reason as <see cref="AlreadyElapsed"/>.
    /// </summary>
    private static readonly TimeSpan StallsImmediately = TimeSpan.FromMilliseconds(-1);

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
    /// Steps the queue a fixed number of passes, so a test can assert the state between two passes and pin the
    /// pass on which a written property is next read.
    /// </summary>
    private static void Step(NoireTaskQueue queue, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            queue.TickOnce();
    }

    /// <summary>
    /// Steps the queue until the predicate holds or the tick budget runs out, and reports the ticks used.<br/>
    /// A budget rather than a loop-until-true, so a scenario that never settles fails as an assertion instead of
    /// hanging the suite.
    /// </summary>
    private static int StepUntil(NoireTaskQueue queue, Func<bool> settled, int maxTicks = 30)
    {
        for (var tick = 1; tick <= maxTicks; tick++)
        {
            queue.TickOnce();

            if (settled())
                return tick;
        }

        return -1;
    }

    // --- Status written on the condition's own task ---------------------------------------------------------

    [Fact]
    public void Condition_WritingCompletedOnItsOwnTask_IsReconciledIntoARealCompletion()
    {
        var queue = MakeQueue();
        var written = false;
        var completedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.complete").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => completedCallbacks++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Status = TaskStatus.Completed;
                }

                return false;
            }).Enqueue();

        // A task that never settles, so the queue keeps its contents and the statistics stay observable. An
        // emptied queue stops and clears itself by default, which would take the subject with it.
        queue.CreateTask("pin").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Completed, "the write lands on live state and nothing puts it back");
        completedCallbacks.Should().Be(1,
            "the pass ends by reconciling statuses it did not write itself, so resolving a task by assigning to Status raises the same callback the queue's own completion path would have");
        queue.GetStatistics().CompletedTasks.Should().Be(1,
            "and the statistic agrees with the callback rather than counting a completion nothing observed");
    }

    [Fact]
    public void Condition_WritingCompletedOnItsOwnTask_IsReconciledExactlyOnce()
    {
        var queue = MakeQueue();
        var written = false;
        var completedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.complete.once").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => completedCallbacks++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Status = TaskStatus.Completed;
                }

                return false;
            }).Enqueue();

        queue.CreateTask("pin").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 6);

        completedCallbacks.Should().Be(1,
            "the reconciliation marks what it finishes, so later passes over the same still-queued task leave it alone");
    }

    [Fact]
    public void Condition_WritingFailedOnItsOwnTask_DoesNotApplyStopQueueOnFail()
    {
        var queue = MakeQueue();
        var written = false;

        QueuedTask? task = null;
        task = queue.CreateTask("self.fail.nopolicy").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Status = TaskStatus.Failed;
                }

                return false;
            }).Enqueue();

        task.StopQueueOnFail = true;
        queue.CreateTask("pin").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        task.Status.Should().Be(TaskStatus.Failed, "the write stands and is reconciled");

        // The boundary the reconciliation draws: assigning a status states an outcome, it does not ask the queue
        // to run the policies attached to that outcome. FailTask is what expresses that intent, and it stays
        // available. Without this line a consumer could not mark a task failed without risking the whole queue.
        queue.QueueState.Should().Be(QueueState.Running,
            "a status written by hand raises the callback and the statistics, but does not apply StopQueueOnFail");
    }

    [Fact]
    public void Condition_WritingCancelledOnItsOwnTask_IsReconciledIntoARealCancellation()
    {
        var queue = MakeQueue();
        var written = false;
        var cancelledCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.cancel.write").AsNonBlocking().WithAction(() => { })
            .OnCancelled(_ => cancelledCallbacks++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Status = TaskStatus.Cancelled;
                }

                return false;
            }).Enqueue();

        queue.CreateTask("pin").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Cancelled, "the write stands");
        cancelledCallbacks.Should().Be(1,
            "the reconciliation raises the cancellation callback for a status the consumer wrote, so writing Cancelled and calling CancelTask are observationally the same");
        queue.GetStatistics().CancelledTasks.Should().Be(1);
    }

    [Fact]
    public void Condition_WritingFailedOnItsOwnTask_IsReconciledIntoARealFailureWithACause()
    {
        var queue = MakeQueue();
        var written = false;
        var failedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.fail.write").AsNonBlocking().WithAction(() => { })
            .OnFailed((_, _) => failedCallbacks++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Status = TaskStatus.Failed;
                }

                return false;
            }).Enqueue();

        queue.CreateTask("pin").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Failed, "the write stands");
        failedCallbacks.Should().Be(1, "the reconciliation raises the failure callback for a status the consumer wrote");
        task.FailureException.Should().NotBeNull(
            "and supplies a cause, so a task failed by a direct write does not report a failure with nothing behind it");
        queue.GetStatistics().FailedTasks.Should().Be(1);
    }

    [Fact]
    public void Condition_WritingQueuedOnItsOwnTask_ReExecutesTheActionInTheSamePass()
    {
        var queue = MakeQueue();
        var written = false;
        var executions = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.requeue").AsNonBlocking().WithAction(() => executions++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Status = TaskStatus.Queued;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 1);
        executions.Should().Be(1, "the first pass starts the task, which is the only execution the queue intended");

        Step(queue, 1);

        executions.Should().Be(2,
            "after the condition returns, the same pass goes on to look for the next queued item and finds the task the condition just put back into Queued, so re-animating a task in flight re-runs its action within that very pass rather than on the next one");
        task!.Status.Should().Be(TaskStatus.WaitingForCompletion,
            "the re-execution takes the ordinary route, so the task is waiting on its condition again as if it had just started");

        Step(queue, 3);
        executions.Should().Be(2, "the write is latched, so nothing here loops");
    }

    [Fact]
    public void Condition_WritingExecutingOnItsOwnTask_StrandsIt()
    {
        var queue = MakeQueue();
        var written = false;
        var conditionCalls = 0;
        var executions = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.executing").AsNonBlocking().WithAction(() => executions++)
            .WithCondition(() =>
            {
                conditionCalls++;

                if (!written)
                {
                    written = true;
                    task!.Status = TaskStatus.Executing;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 10);

        task!.Status.Should().Be(TaskStatus.Executing, "the write stands");
        conditionCalls.Should().Be(1,
            "a pass only evaluates conditions for tasks that are waiting, so a task written back to Executing is never asked again");
        executions.Should().Be(1,
            "nor is it re-executed: only a task in Queued is picked up as the next item, so Executing is the one status a task cannot be driven out of");
        queue.QueueState.Should().Be(QueueState.Running,
            "the completion check counts Executing as unfinished, so the queue never reports itself complete either");
        queue.GetRemainingTaskCount().Should().Be(1, "the task is stranded for the lifetime of the queue");
        queue.GetStatistics().ExecutingTasks.Should().Be(1);
    }

    // --- Status written on another task ---------------------------------------------------------------------

    [Fact]
    public void Condition_WritingCompletedOnAnotherWaitingTask_StillRaisesItsCompletionCallback()
    {
        // The existing condition suite covers writing Completed onto a task the same pass had already decided to
        // complete, where the pending completion still runs. This is the other case: the target was not going to
        // be completed on this pass, so nothing runs at all.
        var queue = MakeQueue();
        var written = false;
        var targetCompletedCallbacks = 0;

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => targetCompletedCallbacks++)
            .WithCondition(() => false).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    target.Status = TaskStatus.Completed;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        target.Status.Should().Be(TaskStatus.Completed, "the write stands");
        targetCompletedCallbacks.Should().Be(1,
            "a task the condition marked Completed is no longer waiting, so the pass leaves it out of its own walk, but the reconciliation at the end of the pass picks it up and finishes it properly");
        queue.GetStatistics().CompletedTasks.Should().Be(1);
    }

    [Fact]
    public void Condition_WritingQueuedOnATaskThatAlreadyCompleted_RunsItAgainAndRaisesASecondCompletion()
    {
        var queue = MakeQueue();
        var written = false;
        var executions = 0;
        var completedCallbacks = 0;

        var finished = queue.CreateTask("finished").WithAction(() => executions++)
            .WithImmediateCompletion()
            .OnCompleted(_ => completedCallbacks++).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    finished.Status = TaskStatus.Queued;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);
        executions.Should().Be(1, "the task ran and completed before the writer's condition was ever evaluated");
        completedCallbacks.Should().Be(1);

        Step(queue, 1);

        executions.Should().Be(2,
            "a finished task written back to Queued is indistinguishable from one that never ran, so the same pass picks it up and executes it a second time");
        completedCallbacks.Should().Be(2,
            "and it completes a second time as well, so the consumer's completion callback is raised twice for a single enqueue");
        finished.Status.Should().Be(TaskStatus.Completed);
        queue.GetStatistics().CompletedTasks.Should().Be(1,
            "the statistic counts task instances rather than completions, so it reports one completion where two were raised");
    }

    [Fact]
    public void Condition_WritingFailedOnATaskAlreadyMarkedForCompletion_KeepsTheFailureAndRaisesIt()
    {
        var queue = MakeQueue();
        var targetReady = false;
        var armed = false;
        var written = false;
        var completedCallbacks = 0;
        var failedCallbacks = 0;

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => completedCallbacks++)
            .OnFailed((_, _) => failedCallbacks++)
            .WithCondition(() => targetReady).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed && !written)
                {
                    written = true;
                    target.Status = TaskStatus.Failed;
                }

                return false;
            }).Enqueue();

        queue.CreateTask("pin").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 3);
        target.Status.Should().Be(TaskStatus.WaitingForCompletion, "nothing has resolved the target yet");

        // On the next pass the target's condition is met and is collected for completion, and the writer's
        // condition, which runs after it in the same pass, fails it before that collection is acted on.
        targetReady = true;
        armed = true;
        Step(queue, 1);

        written.Should().BeTrue("the writer really did run after the target was collected");
        target.Status.Should().Be(TaskStatus.Failed,
            "the deferred completion re-validates the status and steps aside for a task finished as failed, so the consumer's outcome wins over the one the pass had already chosen");
        completedCallbacks.Should().Be(0, "the completion it was collected for never happens");
        failedCallbacks.Should().Be(1,
            "and the failure the consumer wrote is reconciled at the end of the pass, so the write costs the task neither its outcome nor its callback");
        queue.GetStatistics().FailedTasks.Should().Be(1);
        queue.GetStatistics().CompletedTasks.Should().Be(0);
    }

    // --- CompletionCondition replaced -----------------------------------------------------------------------

    [Fact]
    public void Condition_ReplacingItsOwnCondition_IsNotReReadUntilTheNextPass()
    {
        var queue = MakeQueue();
        var replaced = false;
        var replacementCalls = 0;
        var completedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.swap").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => completedCallbacks++)
            .WithCondition(() =>
            {
                if (!replaced)
                {
                    replaced = true;
                    task!.CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
                    {
                        replacementCalls++;
                        return true;
                    });
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        replaced.Should().BeTrue("the original condition ran and swapped itself out");
        replacementCalls.Should().Be(0,
            "the pass already holds the result of the invocation it made, so replacing the condition object mid-evaluation cannot influence the pass that replaced it");
        task!.Status.Should().Be(TaskStatus.WaitingForCompletion);

        Step(queue, 1);

        replacementCalls.Should().Be(1, "the replacement is read on the next pass, which is the first time the queue asks the task again");
        task.Status.Should().Be(TaskStatus.Completed);
        completedCallbacks.Should().Be(1, "the ordinary completion path runs, so nothing is lost by swapping the condition");
    }

    [Fact]
    public void Condition_ReplacingItselfAndReturningTrue_CompletesOnTheReturnValueItAlreadyGave()
    {
        var queue = MakeQueue();
        var replaced = false;
        var replacementCalls = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.swap.true").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!replaced)
                {
                    replaced = true;
                    task!.CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
                    {
                        replacementCalls++;
                        return false;
                    });
                }

                return true;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Completed,
            "the return value of the invocation the pass made is what decides the outcome, so installing a condition that would never be met does not stop the task completing on the answer already given");
        replacementCalls.Should().Be(0, "the replacement is never asked, because the task is finished before anything reads it again");
    }

    [Fact]
    public void Condition_ReplacingAnotherWaitingTasksCondition_TakesEffectInTheSamePass()
    {
        var queue = MakeQueue();
        var replaced = false;
        var completedCallbacks = 0;

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => completedCallbacks++)
            .WithCondition(() => false).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!replaced)
                {
                    replaced = true;
                    target.CompletionCondition = TaskCompletionCondition.Immediate();
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);
        target.Status.Should().Be(TaskStatus.WaitingForCompletion, "the writer has not been asked yet");

        Step(queue, 1);

        replaced.Should().BeTrue();
        target.Status.Should().Be(TaskStatus.Completed,
            "the current task's condition runs before the other waiting tasks are evaluated, so a condition installed on a task that has not been reached yet is read within that same pass");
        completedCallbacks.Should().Be(1, "the task takes the ordinary completion route, callback included");
    }

    [Fact]
    public void Condition_ReplacingTheCurrentTasksCondition_TakesEffectOnTheNextPass()
    {
        var queue = MakeQueue();
        var armed = false;
        var replaced = false;

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed && !replaced)
                {
                    replaced = true;
                    // The target is the pass's current task, whose condition has already been evaluated above.
                    queue.GetCurrentTask()!.CompletionCondition = TaskCompletionCondition.Immediate();
                }

                return false;
            }).Enqueue();

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 3);
        queue.GetCurrentTask().Should().BeSameAs(target, "the later task is the current one, so the writer is evaluated after it");

        armed = true;
        Step(queue, 1);

        replaced.Should().BeTrue();
        target.Status.Should().Be(TaskStatus.WaitingForCompletion,
            "the current task's condition is evaluated first, so a replacement installed by a task evaluated later in the same pass arrives too late for it");

        Step(queue, 1);
        target.Status.Should().Be(TaskStatus.Completed, "it is read on the following pass instead, one pass later than the same write onto a not-yet-evaluated task");
    }

    // --- Timeout --------------------------------------------------------------------------------------------

    [Fact]
    public void Condition_SettingAnElapsedTimeoutOnItsOwnTask_FailsItOnTheSamePass()
    {
        var queue = MakeQueue();
        var written = false;
        var failedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.timeout").AsNonBlocking().WithAction(() => { })
            .OnFailed((_, _) => failedCallbacks++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Timeout = AlreadyElapsed;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Failed,
            "the timeout is tested immediately after the condition returns false, on the same pass, so a timeout a task did not have when the pass began is enforced against it straight away");
        task.FailureException.Should().BeOfType<TimeoutException>();
        failedCallbacks.Should().Be(1, "this route goes through FailTask, so the failure callback is raised normally");
        queue.GetStatistics().FailedTasks.Should().Be(1);
    }

    [Fact]
    public void Condition_ClearingAnElapsedTimeoutOnItsOwnTask_RescuesItFromThatPass()
    {
        var queue = MakeQueue();
        var cleared = false;
        var ready = false;
        var failedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.untimeout").AsNonBlocking().WithAction(() => { })
            .WithTimeout(AlreadyElapsed)
            .OnFailed((_, _) => failedCallbacks++)
            .WithCondition(() =>
            {
                if (!cleared)
                {
                    cleared = true;
                    task!.Timeout = null;
                }

                return ready;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 5);

        cleared.Should().BeTrue();
        task!.Status.Should().Be(TaskStatus.WaitingForCompletion,
            "the condition runs before the timeout is tested, so clearing the timeout from inside it rescues the task from a failure that was otherwise certain on that pass");
        failedCallbacks.Should().Be(0);

        ready = true;
        StepUntil(queue, () => task.Status == TaskStatus.Completed).Should().BePositive(
            "and the task goes on to complete normally afterwards");
    }

    [Fact]
    public void Condition_SettingAnElapsedTimeoutOnAnotherTask_FailsItOnTheSamePass()
    {
        var queue = MakeQueue();
        var written = false;
        var failedCallbacks = 0;

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .OnFailed((_, _) => failedCallbacks++)
            .WithCondition(() => false).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    target.Timeout = AlreadyElapsed;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);
        target.Status.Should().Be(TaskStatus.WaitingForCompletion);

        Step(queue, 1);

        target.Status.Should().Be(TaskStatus.Failed,
            "the target is evaluated after the writer within the same pass, so it is failed on the timeout it was given moments earlier");
        target.FailureException.Should().BeOfType<TimeoutException>();
        failedCallbacks.Should().Be(1, "the deferred failure loop calls FailTask, so the callback is raised");
    }

    // --- RetryConfiguration ---------------------------------------------------------------------------------

    [Fact]
    public void Condition_SettingARetryConfigurationOnItsOwnTask_TakesEffectOnlyAfterStallTrackingStarts()
    {
        var queue = MakeQueue();
        var written = false;
        var executions = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.retry").AsNonBlocking().WithAction(() => executions++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(1, StallsImmediately);
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        executions.Should().Be(1,
            "stall tracking is started when a task begins waiting and only if it already has a retry configuration, so the pass that installs one has no starting point to measure a stall against and cannot retry yet");
        task!.Status.Should().Be(TaskStatus.WaitingForCompletion,
            "that same pass starts the tracking instead, which is what makes the configuration live from here on");

        Step(queue, 1);
        executions.Should().Be(2, "the next pass sees the stall and re-runs the action, one pass later than the write");

        Step(queue, 1);
        task.Status.Should().Be(TaskStatus.Failed, "and the pass after that exhausts the single attempt allowed");
        task.FailureException.Should().BeOfType<MaxRetryAttemptsExceededException>();
        executions.Should().Be(2, "no further execution happens once the attempts are spent");
    }

    [Fact]
    public void Condition_ReplacingARetryConfigurationOnItsOwnTask_IsReadOnTheSamePass()
    {
        var queue = MakeQueue();
        var replaced = false;
        var executions = 0;
        var failedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.retry.swap").AsNonBlocking().WithAction(() => executions++)
            .WithRetries(5, TimeSpan.FromMinutes(10))
            .OnFailed((_, _) => failedCallbacks++)
            .WithCondition(() =>
            {
                if (!replaced)
                {
                    replaced = true;
                    task!.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(0, StallsImmediately);
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Failed,
            "the task already had a configuration, so stall tracking was running before the swap and the new thresholds are measured against it on the very pass that installed them");
        task.FailureException.Should().BeOfType<MaxRetryAttemptsExceededException>(
            "the replacement allows no attempts, so the first stall it detects is also the last");
        executions.Should().Be(1, "no retry is ever run");
        failedCallbacks.Should().Be(1);
    }

    // --- PostCompletionDelay and its provider ---------------------------------------------------------------

    [Fact]
    public void Condition_SettingAPostCompletionDelayAndReturningTrue_DivertsItsOwnTaskIntoTheDelay()
    {
        var queue = MakeQueue();
        var written = false;
        var completedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.delay").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => completedCallbacks++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    // Zero rather than a real duration, so the delay elapses on the next pass whatever the clock does.
                    task!.PostCompletionDelay = TimeSpan.Zero;
                }

                return true;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.WaitingForPostDelay,
            "the delay is read straight after the condition reports itself met, so a delay installed during that same call diverts the task away from the completion it was about to get");
        completedCallbacks.Should().Be(0, "the completion is deferred, not cancelled");

        Step(queue, 1);
        task.Status.Should().Be(TaskStatus.Completed, "the delay costs one extra pass and nothing else");
        completedCallbacks.Should().Be(1, "no callback is lost");
    }

    [Fact]
    public void Condition_ClearingAPostCompletionDelay_CompletesWithoutTheDelayPass()
    {
        var queue = MakeQueue();
        var cleared = false;
        var completedCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.undelay").AsNonBlocking().WithAction(() => { })
            .WithDelay(TimeSpan.FromSeconds(30))
            .OnCompleted(_ => completedCallbacks++)
            .WithCondition(() =>
            {
                if (!cleared)
                {
                    cleared = true;
                    task!.PostCompletionDelay = null;
                }

                return true;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        cleared.Should().BeTrue();
        task!.Status.Should().Be(TaskStatus.Completed,
            "clearing the delay from the same call that reports the condition met is read in time, so the task completes at once instead of sitting out a thirty second delay");
        completedCallbacks.Should().Be(1);
    }

    [Fact]
    public void Condition_InstallingAPostCompletionDelayProvider_HasItInvokedLaterInTheSamePass()
    {
        var queue = MakeQueue();
        var installed = false;
        var providerCalls = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.provider").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!installed)
                {
                    installed = true;
                    task!.PostCompletionDelayProvider = _ =>
                    {
                        providerCalls++;
                        return TimeSpan.Zero;
                    };
                }

                return true;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        providerCalls.Should().Be(1,
            "the provider is consulted immediately after a condition reports itself met, so one installed by that very condition is invoked before the call stack unwinds");
        task!.Status.Should().Be(TaskStatus.WaitingForPostDelay);

        Step(queue, 1);
        task.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void Condition_ClearingThePostCompletionDelayProvider_NeverInvokesIt()
    {
        var queue = MakeQueue();
        var cleared = false;
        var providerCalls = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.unprovider").AsNonBlocking().WithAction(() => { })
            .WithDelay(_ =>
            {
                providerCalls++;
                return TimeSpan.FromSeconds(30);
            })
            .WithCondition(() =>
            {
                if (!cleared)
                {
                    cleared = true;
                    task!.PostCompletionDelayProvider = null;
                }

                return true;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        providerCalls.Should().Be(0, "the provider is read only at the moment the delay would start, which is after the condition has had its chance to remove it");
        task!.Status.Should().Be(TaskStatus.Completed, "with no provider and no delay value the task simply completes");
    }

    [Fact]
    public void Condition_SettingAPostCompletionDelayOnAnotherTaskAboutToComplete_DivertsIt()
    {
        var queue = MakeQueue();
        var targetReady = false;
        var armed = false;
        var written = false;
        var completedCallbacks = 0;

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => completedCallbacks++)
            .WithCondition(() => targetReady).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed && !written)
                {
                    written = true;
                    target.PostCompletionDelay = TimeSpan.Zero;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        targetReady = true;
        armed = true;
        Step(queue, 1);

        written.Should().BeTrue();
        target.Status.Should().Be(TaskStatus.WaitingForPostDelay,
            "the writer runs before the target is evaluated, so the target meets its condition already carrying a delay it did not have when the pass started");
        completedCallbacks.Should().Be(0);

        Step(queue, 1);
        target.Status.Should().Be(TaskStatus.Completed, "the delay elapses and the deferred completion runs, callback included");
        completedCallbacks.Should().Be(1);
    }

    // --- IsBlocking -----------------------------------------------------------------------------------------

    [Fact]
    public void Condition_MakingItsOwnCurrentTaskBlocking_HoldsTheNextTaskFromThatPassOn()
    {
        var queue = MakeQueue();
        var written = false;
        var gateReady = false;

        QueuedTask? gate = null;
        gate = queue.CreateTask("gate").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    gate!.IsBlocking = true;
                }

                return gateReady;
            }).Enqueue();

        var next = queue.CreateTask("next").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        gate!.IsBlocking.Should().BeTrue("the write stands");
        next.Status.Should().Be(TaskStatus.Queued,
            "the blocking gate is consulted right after the condition returns, so a task that makes itself blocking mid-wait holds the queue from that pass on, where it would otherwise have let the next task start on that very pass");

        gateReady = true;
        StepUntil(queue, () => next.Status == TaskStatus.Completed).Should().BePositive(
            "and the hold is released as soon as the gating task finishes");
    }

    [Fact]
    public void Condition_MakingItsOwnCurrentTaskNonBlocking_LetsTheNextTaskStartInTheSamePass()
    {
        var queue = MakeQueue();
        var written = false;

        QueuedTask? gate = null;
        gate = queue.CreateTask("gate").AsBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    gate!.IsBlocking = false;
                }

                return false;
            }).Enqueue();

        var next = queue.CreateTask("next").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        gate!.IsBlocking.Should().BeFalse("the write stands");
        next.Status.Should().Be(TaskStatus.Completed,
            "the gate is read after the condition returns, so dropping the flag mid-wait releases the queue on that same pass and the next task both starts and finishes within it");
        gate.Status.Should().Be(TaskStatus.WaitingForCompletion, "the task that opened the gate carries on waiting");
    }

    [Fact]
    public void Condition_MakingANonCurrentWaitingTaskBlocking_ChangesNothing()
    {
        var queue = MakeQueue();
        var written = false;

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    target.IsBlocking = true;
                }

                return false;
            }).Enqueue();

        var next = queue.CreateTask("next").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        target.IsBlocking.Should().BeTrue("the write stands");
        next.Status.Should().Be(TaskStatus.Completed,
            "only the pass's current task is consulted for blocking, so making some other waiting task blocking gates nothing; the flag only becomes live if that task later becomes the current one");
    }

    // --- ExecuteAction --------------------------------------------------------------------------------------

    [Fact]
    public void Condition_ReplacingAQueuedTasksAction_RunsTheReplacementInTheSamePass()
    {
        var queue = MakeQueue();
        var written = false;
        var originalRuns = 0;
        var replacementRuns = 0;

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    queue.GetTaskByCustomId("target")!.ExecuteAction = () => replacementRuns++;
                }

                return false;
            }).Enqueue();

        var target = queue.CreateTask("target").WithAction(() => originalRuns++).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        replacementRuns.Should().Be(1,
            "the next queued item is picked and executed at the end of the same pass the condition ran in, so an action swapped onto a task that has not started yet is the one that runs");
        originalRuns.Should().Be(0, "the action the task was built with never runs at all");
        target.Status.Should().Be(TaskStatus.Completed);
    }

    [Fact]
    public void Condition_ReplacingItsOwnActionAfterItHasRun_IsNeverRead()
    {
        var queue = MakeQueue();
        var written = false;
        var originalRuns = 0;
        var replacementRuns = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.action").AsNonBlocking().WithAction(() => originalRuns++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.ExecuteAction = () => replacementRuns++;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 8);

        originalRuns.Should().Be(1, "the task had already run before its condition was ever evaluated");
        replacementRuns.Should().Be(0,
            "nothing reads the action again for a task that is merely waiting, so the replacement is inert unless the task is retried or put back into Queued");
        task!.Status.Should().Be(TaskStatus.WaitingForCompletion);
    }

    // --- Outcome callbacks replaced or nulled ---------------------------------------------------------------

    [Fact]
    public void Condition_NullingAndReplacingOnCompletedOnTasksAboutToComplete_LosesOneAndGainsTheOther()
    {
        var queue = MakeQueue();
        var ready = false;
        var armed = false;
        var written = false;
        var nulledOriginal = 0;
        var swappedOriginal = 0;
        var swappedReplacement = 0;

        var nulled = queue.CreateTask("nulled").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => nulledOriginal++)
            .WithCondition(() => ready).Enqueue();

        var swapped = queue.CreateTask("swapped").AsNonBlocking().WithAction(() => { })
            .OnCompleted(_ => swappedOriginal++)
            .WithCondition(() => ready).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed && !written)
                {
                    written = true;
                    nulled.OnCompleted = null;
                    swapped.OnCompleted = _ => swappedReplacement++;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        ready = true;
        armed = true;
        Step(queue, 1);

        nulled.Status.Should().Be(TaskStatus.Completed);
        swapped.Status.Should().Be(TaskStatus.Completed);
        nulledOriginal.Should().Be(0,
            "the callback is read only at the moment the queue raises it, which is after the condition that removed it, so the queue completes the task and quietly raises nothing");
        swappedOriginal.Should().Be(0, "the delegate the task was built with is gone by the time the queue looks");
        swappedReplacement.Should().Be(1, "and the one installed mid-pass is what gets invoked instead");
        queue.GetStatistics().CompletedTasks.Should().Be(2,
            "both tasks count as completed, so the statistic cannot distinguish the one whose callback was dropped");
    }

    [Fact]
    public void Condition_NullingOnCancelledBeforeCancellingItsOwnTask_LosesTheCallback()
    {
        var queue = MakeQueue();
        var written = false;
        var cancelledCallbacks = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.uncallback").AsNonBlocking().WithAction(() => { })
            .OnCancelled(_ => cancelledCallbacks++)
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.OnCancelled = null;
                    queue.CancelTask("self.uncallback");
                }

                return false;
            }).Enqueue();

        queue.CreateTask("pin").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Cancelled, "the cancellation itself goes through the queue and succeeds");
        cancelledCallbacks.Should().Be(0,
            "CancelTaskInternal reads the callback when it runs, so a task can be cancelled through the queue's own method and still raise nothing if the delegate was removed first");
        queue.GetStatistics().CancelledTasks.Should().Be(1);
    }

    [Fact]
    public void Condition_NullingAndReplacingOnFailedOnTasksAboutToTimeOut_LosesOneAndGainsTheOther()
    {
        var queue = MakeQueue();
        var armed = false;
        var written = false;
        var nulledOriginal = 0;
        var swappedOriginal = 0;
        var swappedReplacement = 0;

        var nulled = queue.CreateTask("nulled").AsNonBlocking().WithAction(() => { })
            .OnFailed((_, _) => nulledOriginal++)
            .WithCondition(() => false).Enqueue();

        var swapped = queue.CreateTask("swapped").AsNonBlocking().WithAction(() => { })
            .OnFailed((_, _) => swappedOriginal++)
            .WithCondition(() => false).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed && !written)
                {
                    written = true;
                    nulled.OnFailed = null;
                    swapped.OnFailed = (_, _) => swappedReplacement++;
                    nulled.Timeout = AlreadyElapsed;
                    swapped.Timeout = AlreadyElapsed;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        armed = true;
        Step(queue, 1);

        nulled.Status.Should().Be(TaskStatus.Failed);
        swapped.Status.Should().Be(TaskStatus.Failed);
        nulledOriginal.Should().Be(0,
            "FailTask reads the callback at the moment it raises it, so a task driven into failure by the same condition that removed its handler fails silently");
        swappedOriginal.Should().Be(0);
        swappedReplacement.Should().Be(1, "the replacement installed in the same pass is the one that receives the failure");
        queue.GetStatistics().FailedTasks.Should().Be(2);
    }

    // --- Metadata -------------------------------------------------------------------------------------------

    [Fact]
    public void Condition_ReplacingMetadata_StandsAndChangesNothing()
    {
        var queue = MakeQueue();
        var written = false;
        var ready = false;

        QueuedTask? writer = null;
        QueuedTask? target = null;

        writer = queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithMetadata("writer.original")
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    writer!.Metadata = "writer.replaced";
                    target!.Metadata = "target.replaced";
                }

                return ready;
            }).Enqueue();

        target = queue.CreateTask("target").AsNonBlocking().WithAction(() => { })
            .WithMetadata("target.original")
            .WithCondition(() => ready).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        writer!.Metadata.Should().Be("writer.replaced", "the write stands");
        target!.Metadata.Should().Be("target.replaced", "including on a task that had not started when it was written");
        writer.Status.Should().Be(TaskStatus.WaitingForCompletion,
            "processing only ever inspects metadata to recognize an internal retry marker, so an ordinary value is inert whichever task it is written to");
        target.Status.Should().Be(TaskStatus.WaitingForCompletion);

        ready = true;
        StepUntil(queue, () => writer.Status == TaskStatus.Completed && target.Status == TaskStatus.Completed)
            .Should().BePositive("and both tasks complete exactly as they would have without the write");
    }

    [Fact]
    public void Condition_WritingRetryDelayMetadataAndQueuedStatus_RoutesTheTaskThroughTheRetryPath()
    {
        // Metadata is not entirely inert: the queue reads it to recognize a task waiting out a retry delay, and
        // that marker type is reachable from any consumer that can name it. Paired with a status write it puts a
        // task back through the retry machinery even though it has no retry configuration at all.
        var queue = MakeQueue();
        var written = false;
        var executions = 0;

        QueuedTask? task = null;
        task = queue.CreateTask("self.retrymeta").AsNonBlocking().WithAction(() => executions++)
            .WithMetadata("original")
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.Metadata = new RetryDelayMetadata { DelayUntilTicks = 0, OriginalMetadata = "original" };
                    task.Status = TaskStatus.Queued;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        executions.Should().Be(2, "the same pass picks the task back up and re-runs its action through the retry route");
        task!.Metadata.Should().Be("original",
            "the retry route restores the metadata it found behind the marker, so the marker consumes itself");
        task.Status.Should().Be(TaskStatus.WaitingForCompletion);
        task.RetryConfiguration.Should().BeNull("no retry was ever configured for this task");
    }

    // --- StopQueueOnFail and StopQueueOnCancel --------------------------------------------------------------

    [Fact]
    public void Condition_SettingStopQueueOnFailBeforeItsOwnFailure_StopsAndClearsTheQueueMidPass()
    {
        var queue = MakeQueue();
        var written = false;

        QueuedTask? task = null;
        task = queue.CreateTask("self.stoponfail").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    task!.StopQueueOnFail = true;
                    task.Timeout = AlreadyElapsed;
                }

                return false;
            }).Enqueue();

        queue.CreateTask("bystander").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        task!.Status.Should().Be(TaskStatus.Failed, "the timeout written alongside the flag fails the task on that pass");
        queue.QueueState.Should().Be(QueueState.Stopped,
            "the flag is read at the end of the failure, so a task can stop the whole queue from inside its own condition");
        queue.GetAllTasks().Should().BeEmpty(
            "stopping clears the queue, so the bystander is discarded still carrying its Queued status and never receives a cancellation callback");
        queue.GetStatistics().FailedTasks.Should().Be(0,
            "the statistic reads the queue's contents, and the failed task was cleared out of them by the stop it triggered, so the failure it just recorded is no longer visible anywhere in the statistics");
    }

    [Fact]
    public void Condition_SettingStopQueueOnCancelOnAnotherTaskBeforeCancellingIt_TakesTheWriterDownWithIt()
    {
        var queue = MakeQueue();
        var written = false;

        var victim = queue.CreateTask("victim").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        var writer = queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    victim.StopQueueOnCancel = true;
                    queue.CancelTask("victim");
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        victim.Status.Should().Be(TaskStatus.Cancelled);
        writer.Status.Should().Be(TaskStatus.Cancelled,
            "the flag is read at the end of the cancellation and stops the queue, stopping clears it, and clearing cancels whatever task is current, which here is the task whose own condition asked for the cancellation");
        queue.GetAllTasks().Should().BeEmpty("everything else is discarded still carrying its old status");
        queue.QueueState.Should().Be(QueueState.Stopped);
    }

    [Fact]
    public void Condition_SettingStopQueueOnCancel_WithAutoStopDisabled_HasTheStopUndoneByTheSamePass()
    {
        // The stop above survives only because an emptied queue stops itself as well. With that turned off, the
        // pass the stop happened inside carries on, finds the queue it just emptied, and reports it complete.
        var queue = MakeQueue();
        queue.SetAutoStopQueueOnComplete(false);
        var written = false;

        var victim = queue.CreateTask("victim").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.CreateTask("writer").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!written)
                {
                    written = true;
                    victim.StopQueueOnCancel = true;
                    queue.CancelTask("victim");
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        written.Should().BeTrue();
        queue.QueueState.Should().Be(QueueState.Idle,
            "a stop requested from inside a condition is applied at once but is not final: the pass it interrupted runs to its end and overwrites the state it set");
    }

    // --- Parent batch policy flags --------------------------------------------------------------------------

    [Fact]
    public void BatchTaskCondition_SettingCancelParentBatchOnCancel_CancelsTheWholeBatch()
    {
        var queue = MakeQueue();
        var written = false;
        var batchCancelledCallbacks = 0;

        QueuedTask? first = null;
        first = new QueuedTask("first", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    first!.CancelParentBatchOnCancel = true;
                    queue.CancelTask("first");
                }

                return false;
            })
        };

        var second = new QueuedTask("second", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        var batch = queue.CreateBatch("batch").AddTask(first).AddTask(second).Enqueue();
        batch.OnCancelled = _ => batchCancelledCallbacks++;

        queue.StartQueue();
        Step(queue, 3);

        first.Status.Should().Be(TaskStatus.Cancelled);
        batch.Status.Should().Be(BatchStatus.Cancelled,
            "the flag is read at the end of the cancellation, so a task can take its whole batch down from inside its own condition");
        second.Status.Should().Be(TaskStatus.Cancelled, "cancelling a batch cancels every task in it that had not finished");
        batchCancelledCallbacks.Should().Be(1, "the batch cancellation callback is raised normally");
    }

    [Fact]
    public void BatchTaskCondition_SettingFailParentBatchOnCancel_FailsTheBatchAndStrandsItsSiblings()
    {
        var queue = MakeQueue();
        var written = false;
        var batchFailedCallbacks = 0;
        var secondExecutions = 0;

        QueuedTask? first = null;
        first = new QueuedTask("first", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    first!.FailParentBatchOnCancel = true;
                    queue.CancelTask("first");
                }

                return false;
            })
        };

        var second = new QueuedTask("second", isBlocking: false)
        {
            ExecuteAction = () => secondExecutions++,
            CompletionCondition = TaskCompletionCondition.FromPredicate(() => false)
        };

        var batch = queue.CreateBatch("batch").AddTask(first).AddTask(second).Enqueue();
        batch.OnFailed = (_, _) => batchFailedCallbacks++;

        queue.StartQueue();
        Step(queue, 3);

        first.Status.Should().Be(TaskStatus.Cancelled);
        batch.Status.Should().Be(BatchStatus.Failed, "the flag is read during the cancellation and fails the batch");
        batchFailedCallbacks.Should().Be(1);
        secondExecutions.Should().Be(1,
            "failing a batch does not touch its remaining tasks, and the pass that failed it carries on to start the next queued task in it, so a task is launched into a batch that has already failed");

        Step(queue, 2);

        second.Status.Should().Be(TaskStatus.WaitingForCompletion,
            "that task is then stranded: the failed batch is no longer the current one, so nothing ever evaluates its condition again or resolves it");
        queue.QueueState.Should().Be(QueueState.Stopped,
            "and the queue reports itself finished regardless, because it judges the batch by its own status and not by its tasks");
    }

    [Fact]
    public void BatchTaskCondition_SettingFailParentBatchOnFail_FailsTheBatchAndLeavesItsSiblingUnstarted()
    {
        var queue = MakeQueue();
        var written = false;
        var batchFailedCallbacks = 0;

        QueuedTask? first = null;
        first = new QueuedTask("first", isBlocking: false)
        {
            ExecuteAction = () => { },
            Timeout = AlreadyElapsed,
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    first!.FailParentBatchOnFail = true;
                }

                return false;
            })
        };

        var second = new QueuedTask("second", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        var batch = queue.CreateBatch("batch").AddTask(first).AddTask(second).Enqueue();
        batch.OnFailed = (_, _) => batchFailedCallbacks++;

        queue.StartQueue();
        Step(queue, 3);

        first.Status.Should().Be(TaskStatus.Failed, "the elapsed timeout is tested on the pass the flag was written");
        batch.Status.Should().Be(BatchStatus.Failed,
            "the parent batch policy is consulted while the task failure is being finalized, so a flag set moments earlier in the condition decides the batch's fate");
        batchFailedCallbacks.Should().Be(1);

        Step(queue, 2);
        second.Status.Should().Be(TaskStatus.Queued,
            "the sibling is left exactly as it was: never started, never cancelled, and with no callback of any kind");
        queue.QueueState.Should().Be(QueueState.Stopped, "the queue judges the batch by its status and treats the run as finished");
    }

    [Fact]
    public void BatchTaskCondition_SettingCancelParentBatchOnFail_CancelsTheBatchAndItsSibling()
    {
        var queue = MakeQueue();
        var written = false;

        QueuedTask? first = null;
        first = new QueuedTask("first", isBlocking: false)
        {
            ExecuteAction = () => { },
            Timeout = AlreadyElapsed,
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    first!.CancelParentBatchOnFail = true;
                }

                return false;
            })
        };

        var second = new QueuedTask("second", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        var batch = queue.CreateBatch("batch").AddTask(first).AddTask(second).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        first.Status.Should().Be(TaskStatus.Failed);
        batch.Status.Should().Be(BatchStatus.Cancelled, "the cancel flavor of the policy cancels the batch instead of failing it");
        second.Status.Should().Be(TaskStatus.Cancelled,
            "and unlike the fail flavor it also resolves the siblings, so which of the two flags a condition writes decides whether the rest of the batch is cleaned up or abandoned");
    }

    [Fact]
    public void BatchTaskCondition_SettingFailParentBatchOnMaxRetries_FailsTheParentBatch()
    {
        var queue = MakeQueue();
        var written = false;
        var batchCompletedCallbacks = 0;

        QueuedTask? first = null;
        first = new QueuedTask("first", isBlocking: false)
        {
            ExecuteAction = () => { },
            RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(0, StallsImmediately),
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    first!.FailParentBatchOnMaxRetries = true;
                    first.CancelParentBatchOnMaxRetries = true;
                }

                return false;
            })
        };

        var second = new QueuedTask("second", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        var batch = queue.CreateBatch("batch").AddTask(first).AddTask(second).Enqueue();
        batch.OnCompleted = _ => batchCompletedCallbacks++;

        queue.StartQueue();
        Step(queue, 5);

        first.Status.Should().Be(TaskStatus.Failed);
        first.FailureException.Should().BeOfType<MaxRetryAttemptsExceededException>("the single stall exhausts the zero attempts allowed");
        first.FailParentBatchOnMaxRetries.Should().BeTrue("the write stands");
        batch.Status.Should().Be(BatchStatus.Failed,
            "the batch level consults the max retry parent policies now, so a flag written from inside a batch task reaches the batch it names rather than being dead weight");
        batchCompletedCallbacks.Should().Be(0, "the batch did not complete, so it does not claim to have");
    }

    // --- TaskBatch state written from one of its own tasks --------------------------------------------------

    [Fact]
    public void BatchTaskCondition_WritingCompletedOnItsOwnBatch_ResolvesEveryTaskInIt()
    {
        var queue = MakeQueue();
        var written = false;
        var batchCompletedCallbacks = 0;

        TaskBatch? batch = null;
        var first = new QueuedTask("first")
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    batch!.Status = BatchStatus.Completed;
                }

                return false;
            })
        };

        var second = new QueuedTask("second")
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        batch = queue.CreateBatch("batch").AddTask(first).AddTask(second).Enqueue();
        batch.OnCompleted = _ => batchCompletedCallbacks++;

        queue.StartQueue();
        Step(queue, 4);

        batch.Status.Should().Be(BatchStatus.Completed, "the write stands");
        batchCompletedCallbacks.Should().Be(1,
            "the reconciliation at the end of the pass finishes a batch the consumer resolved by hand, so its callback runs");
        queue.GetStatistics().BatchesCompleted.Should().Be(1,
            "and the tally agrees with the batch's own status instead of contradicting it");

        first.Status.Should().Be(TaskStatus.Cancelled,
            "a batch written complete is over, so the work it still held is resolved rather than left waiting forever");
        second.Status.Should().Be(TaskStatus.Cancelled, "including the task that had not started");
    }

    [Fact]
    public void BatchTaskCondition_ChangingTaskFailureMode_IsReadOnTheSamePass()
    {
        var queue = MakeQueue();
        var written = false;
        var batchFailedCallbacks = 0;

        TaskBatch? batch = null;
        var first = new QueuedTask("first", isBlocking: false)
        {
            ExecuteAction = () => { },
            Timeout = AlreadyElapsed,
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    batch!.TaskFailureMode = BatchTaskFailureMode.FailBatch;
                }

                return false;
            })
        };

        var second = new QueuedTask("second", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        batch = queue.CreateBatch("batch").AddTask(first).AddTask(second).Enqueue();
        batch.OnFailed = (_, _) => batchFailedCallbacks++;

        queue.StartQueue();
        Step(queue, 3);

        first.Status.Should().Be(TaskStatus.Failed);
        batch.Status.Should().Be(BatchStatus.Failed,
            "the failure mode is read while the task failure is being handled, on the same pass, so a batch that would have shrugged the failure off fails instead");
        batchFailedCallbacks.Should().Be(1);
        second.Status.Should().Be(TaskStatus.Queued, "the remaining task is left unstarted and unresolved");
    }

    [Fact]
    public void BatchTaskCondition_ReplacingTheBatchOnCompleted_LosesTheOriginal()
    {
        var queue = MakeQueue();
        var written = false;
        var originalCallbacks = 0;
        var replacementCallbacks = 0;

        TaskBatch? batch = null;
        var only = new QueuedTask("only", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    batch!.OnCompleted = _ => replacementCallbacks++;
                }

                return true;
            })
        };

        batch = queue.CreateBatch("batch").AddTask(only).Enqueue();
        batch.OnCompleted = _ => originalCallbacks++;

        queue.StartQueue();
        Step(queue, 4);

        batch.Status.Should().Be(BatchStatus.Completed);
        originalCallbacks.Should().Be(0, "the delegate the batch was configured with is gone before the queue reaches the point where it raises it");
        replacementCallbacks.Should().Be(1, "the one a task's condition installed is raised in its place");
    }

    [Fact]
    public void BatchTaskCondition_SettingTheBatchPostCompletionDelay_AddsAPass()
    {
        var queue = MakeQueue();
        var written = false;
        var batchCompletedCallbacks = 0;

        TaskBatch? batch = null;
        var only = new QueuedTask("only", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    batch!.PostCompletionDelay = TimeSpan.Zero;
                }

                return true;
            })
        };

        batch = queue.CreateBatch("batch").AddTask(only).Enqueue();
        batch.OnCompleted = _ => batchCompletedCallbacks++;

        queue.StartQueue();
        Step(queue, 4);

        batch.Status.Should().Be(BatchStatus.WaitingForPostDelay,
            "the batch delay is read when the batch is about to complete, which is after the task condition that installed it, so the batch is diverted into a delay it was not configured with");
        batchCompletedCallbacks.Should().Be(0);

        Step(queue, 1);
        batch.Status.Should().Be(BatchStatus.Completed, "the delay costs one extra pass");
        batchCompletedCallbacks.Should().Be(1, "and no callback is lost");
    }

    [Fact]
    public void BatchTaskCondition_TogglingBatchIsBlocking_LetsTheRestOfTheQueueRun()
    {
        var queue = MakeQueue();
        var written = false;

        TaskBatch? batch = null;
        var only = new QueuedTask("only", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
            {
                if (!written)
                {
                    written = true;
                    batch!.IsBlocking = false;
                }

                return false;
            })
        };

        batch = queue.CreateBatch("batch").AsBlocking().AddTask(only).Enqueue();
        var after = queue.CreateTask("after").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 8);

        batch.IsBlocking.Should().BeFalse("the write stands");
        after.Status.Should().Be(TaskStatus.Completed,
            "clearing the flag mid-flight takes effect on the next pass: a non-blocking batch no longer owns the whole pass, so the queue moves on to the item behind it while the batch is still running");
    }
}
