using FluentAssertions;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Characterizes every way a consumer can add work to the task queue from inside a completion condition, which is
/// consumer code the queue runs while holding its private queue lock and while it is part-way through a processing
/// pass.<br/>
/// Covered here: <c>EnqueueTask</c> in both overloads, <c>EnqueueBatch</c>, the queue-bound batch builder, and
/// <c>InsertTaskAfter</c> by system ID and by custom ID across all three <see cref="ContextDefinition"/> modes,
/// anchored on the calling task itself, on another task, and on a task already completed or cancelled. Each test
/// steps the queue one pass at a time and asserts the state between passes, so the thing being pinned is not only
/// whether an addition succeeds but exactly when its effect becomes visible: within the pass that made it, or only
/// on the next one.<br/>
/// These record what the code does today, not what it should do. In particular, enqueuing a batch from a standalone
/// task's condition hands the whole queue over to that batch, which stops the calling task's own condition from
/// being evaluated at all until the batch has finished.<br/>
/// A plain builder enqueue from a condition, and appending to one's own batch with <c>TaskBatch.AddTask</c>, are
/// covered in <see cref="NoireTaskQueueConditionTests"/> and are not repeated.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueReentrantAdditionTests : IDisposable
{
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
    /// Steps the queue a fixed number of passes. Every re-entrant callback in this file fires once, either latched
    /// by a bool or armed explicitly by the test, and stepping is always bounded, so nothing here can recurse or
    /// spin without end.
    /// </summary>
    private static void Step(NoireTaskQueue queue, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            queue.TickOnce();
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
    /// Builds a standalone task that runs an action and completes on the spot, for use as the thing being added.
    /// </summary>
    private static QueuedTask MakeImmediateTask(string customId, Action action)
        => new(customId, isBlocking: false)
        {
            ExecuteAction = action,
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

    /// <summary>
    /// Builds a batch task that waits on a predicate, for use as the task whose condition does the adding.
    /// </summary>
    private static QueuedTask MakeConditionTask(string customId, Func<bool> condition)
        => new(customId, isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.FromPredicate(condition)
        };

    // -- EnqueueTask from a condition -----------------------------------------------------------------------------

    [Fact]
    public void EnqueueTask_WithATaskObject_FromANonBlockingCurrentTasksCondition_RunsInTheSamePass()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var executions = 0;
        NoireTaskQueue? enqueueReturned = null;
        var spawned = MakeImmediateTask("spawned", () => executions++);

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    enqueueReturned = queue.EnqueueTask(spawned);
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 1);
        enqueued.Should().BeFalse("the first pass only starts the host task, so its condition has not run yet");

        Step(queue, 1);

        enqueueReturned.Should().BeSameAs(queue,
            "the task-object overload returns the queue for chaining, and it reaches that return normally from inside the lock it already holds");
        spawned.Status.Should().Be(TaskStatus.Completed,
            "the next queued item is chosen by reading the live queue after every condition has been evaluated, so a task added by a condition is picked up and run inside that very pass");
        executions.Should().Be(1, "the added task's action ran exactly once");
        host.Status.Should().Be(TaskStatus.WaitingForCompletion, "the calling task is untouched by the addition");
    }

    [Fact]
    public void EnqueueTask_Parameterised_FromACondition_IsQueuedImmediatelyAndRunsInTheSamePass()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var executions = 0;
        QueuedTask? spawned = null;
        TaskStatus statusInsideCondition = default;
        var tasksVisibleFromInside = 0;

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    spawned = queue.EnqueueTask("spawned", isBlocking: false, () => executions++, TaskCompletionCondition.Immediate());
                    statusInsideCondition = spawned.Status;
                    tasksVisibleFromInside = queue.GetAllTasks().Count;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        statusInsideCondition.Should().Be(TaskStatus.Queued,
            "the parameterised overload builds the task, enqueues it and hands it back before returning, so the condition already holds a queued task");
        tasksVisibleFromInside.Should().Be(2, "the addition is applied to the live queue rather than staged, so the caller can already see it");
        spawned!.Status.Should().Be(TaskStatus.Completed, "and the same pass goes on to run it");
        executions.Should().Be(1);
    }

    [Fact]
    public void EnqueueTask_FromABlockingCurrentTasksCondition_IsHeldBackUntilThatTaskFinishes()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var hostMayComplete = false;
        var executions = 0;
        QueuedTask? spawned = null;

        queue.CreateTask("host").AsBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    spawned = queue.EnqueueTask("spawned", isBlocking: false, () => executions++, TaskCompletionCondition.Immediate());
                }

                return hostMayComplete;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        enqueued.Should().BeTrue("the second pass is the one that evaluates the condition");
        spawned!.Status.Should().Be(TaskStatus.Queued,
            "a blocking current task sets the wait-for-blocking flag before the next item is chosen, so the addition lands in the queue but is not picked up in the pass that made it");

        Step(queue, 3);
        spawned.Status.Should().Be(TaskStatus.Queued, "and it stays unpicked for as long as the blocking task holds the queue");
        executions.Should().Be(0);

        hostMayComplete = true;
        StepUntil(queue, () => spawned.Status == TaskStatus.Completed).Should().Be(2,
            "one pass completes the blocking task and returns early, and the pass after it is the first free to pick the addition up");
        executions.Should().Be(1);
    }

    [Fact]
    public void EnqueueTask_FromAConditionThatThenReportsMet_LeavesThePickupToTheNextPass()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var executions = 0;
        QueuedTask? spawned = null;

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    spawned = queue.EnqueueTask("spawned", isBlocking: false, () => executions++, TaskCompletionCondition.Immediate());
                }

                return true;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        host.Status.Should().Be(TaskStatus.Completed, "the condition reported met, so the pass completed the task that owns it");
        spawned!.Status.Should().Be(TaskStatus.Queued,
            "completing the current task marks the pass for early return, and the early return is checked before the next item is chosen, so whether an addition is picked up in the same pass depends on what its own condition returned");
        executions.Should().Be(0);

        Step(queue, 1);
        spawned.Status.Should().Be(TaskStatus.Completed, "the following pass picks it up with nothing in the way");
        executions.Should().Be(1);
    }

    // -- EnqueueBatch and the batch builder from a condition ------------------------------------------------------

    [Fact]
    public void EnqueueBatch_FromANonBlockingCurrentTasksCondition_StartsInTheSamePassAndStarvesTheCallingTask()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var conditionCalls = 0;
        var innerExecutions = 0;
        TaskBatch? spawnedBatch = null;

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                conditionCalls++;

                if (!enqueued)
                {
                    enqueued = true;
                    spawnedBatch = queue.CreateBatch("spawned.batch").AddAction(() => innerExecutions++, "inner").Enqueue();
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        conditionCalls.Should().Be(1);
        spawnedBatch!.Status.Should().Be(BatchStatus.Processing,
            "a batch added by a condition is reached by the same live next-item read a task is, so the pass that added it also starts it");
        queue.GetCurrentBatch().Should().BeSameAs(spawnedBatch);
        spawnedBatch.Tasks[0].Status.Should().Be(TaskStatus.Queued,
            "starting a batch returns from the pass immediately, so no task inside it runs until the following one");

        Step(queue, 1);
        spawnedBatch.Tasks[0].Status.Should().Be(TaskStatus.Completed);
        conditionCalls.Should().Be(1,
            "processing delegates wholly to the current batch, so the standalone task whose condition created that batch stops being evaluated - this records today's behavior, which is that a condition can starve its own task by enqueuing a batch");

        Step(queue, 1);
        spawnedBatch.Status.Should().Be(BatchStatus.Completed);
        queue.GetCurrentBatch().Should().BeNull();
        conditionCalls.Should().Be(1, "the calling task is still frozen on the pass that finishes the batch");

        Step(queue, 1);
        conditionCalls.Should().Be(2, "and only once the batch is gone does the queue go back to evaluating it");
        host.Status.Should().Be(TaskStatus.WaitingForCompletion);
    }

    [Fact]
    public void CreateBatch_BuilderEnqueue_FromABlockingCurrentTasksCondition_IsHeldBackUntilThatTaskFinishes()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var hostMayComplete = false;
        var innerExecutions = 0;
        TaskBatch? spawnedBatch = null;

        queue.CreateTask("host").AsBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    spawnedBatch = queue.CreateBatch("spawned.batch").AddAction(() => innerExecutions++, "inner").Enqueue();
                }

                return hostMayComplete;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        spawnedBatch.Should().NotBeNull("the queue-bound batch builder terminates in EnqueueBatch, which re-enters the lock and returns the built batch");
        spawnedBatch!.Status.Should().Be(BatchStatus.Queued,
            "the blocking gate applies to batches exactly as it does to tasks, so the builder's batch is added but not started");
        innerExecutions.Should().Be(0);

        Step(queue, 3);
        spawnedBatch.Status.Should().Be(BatchStatus.Queued, "and it stays queued for as long as the blocking task holds the queue");

        hostMayComplete = true;
        StepUntil(queue, () => spawnedBatch.Status == BatchStatus.Completed).Should().BePositive();
        innerExecutions.Should().Be(1);
    }

    [Fact]
    public void EnqueueBatch_FromATaskConditionInsideAnotherBatch_StaysInvisibleUntilThatBatchFinishes()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var innerMayComplete = false;
        var spawnedExecutions = 0;
        TaskBatch? spawnedBatch = null;

        var outerBatch = queue.CreateBatch("outer.batch")
            .AddTask(MakeConditionTask("inner", () =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    spawnedBatch = queue.CreateBatch("spawned.batch").AddAction(() => spawnedExecutions++, "spawned.inner").Enqueue();
                }

                return innerMayComplete;
            }))
            .Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        enqueued.Should().BeTrue("the third pass is the one that evaluates the batch task's condition");
        spawnedBatch!.Status.Should().Be(BatchStatus.Queued);
        queue.GetAllBatches().Should().HaveCount(2, "the addition landed in the top-level queue");
        queue.GetCurrentBatch().Should().BeSameAs(outerBatch,
            "batch processing reads only the batch's own task list and never the queue behind it, so an addition made to the queue is not even visible to the pass that made it");

        Step(queue, 3);
        spawnedBatch.Status.Should().Be(BatchStatus.Queued, "and stays out of reach for as long as the owning batch is the current item");
        spawnedExecutions.Should().Be(0);

        innerMayComplete = true;
        StepUntil(queue, () => spawnedBatch.Status == BatchStatus.Completed).Should().BePositive(
            "once the owning batch finishes, the queue picks the added batch up as an ordinary queued item");
        spawnedExecutions.Should().Be(1);
    }

    [Fact]
    public void EnqueueTask_FromATaskConditionInsideABatch_StaysInvisibleUntilThatBatchFinishes()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var innerMayComplete = false;
        var spawnedExecutions = 0;
        QueuedTask? spawned = null;

        var outerBatch = queue.CreateBatch("outer.batch")
            .AddTask(MakeConditionTask("inner", () =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    spawned = queue.EnqueueTask("spawned", isBlocking: false, () => spawnedExecutions++, TaskCompletionCondition.Immediate());
                }

                return innerMayComplete;
            }))
            .Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        spawned!.Status.Should().Be(TaskStatus.Queued, "the enqueue succeeds and lands in the top-level queue");
        spawned.ParentBatch.Should().BeNull("EnqueueTask always adds a standalone item, whatever context the caller happened to be in");
        queue.GetCurrentBatch().Should().BeSameAs(outerBatch);

        Step(queue, 3);
        spawned.Status.Should().Be(TaskStatus.Queued, "the batch pass never looks at the queue, so the addition cannot be picked up while a batch is current");
        spawnedExecutions.Should().Be(0);

        innerMayComplete = true;
        StepUntil(queue, () => spawned.Status == TaskStatus.Completed).Should().BePositive();
        spawnedExecutions.Should().Be(1);
    }

    // -- InsertTaskAfter from a condition -------------------------------------------------------------------------

    [Fact]
    public void InsertTaskAfter_OwnTaskBySystemId_FromACondition_RunsAheadOfAlreadyQueuedWork()
    {
        var queue = MakeQueue();
        var inserted = false;
        var insertReturned = false;
        var order = new List<string>();
        var injected = MakeImmediateTask("injected", () => order.Add("injected"));

        QueuedTask? host = null;
        host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!inserted)
                {
                    inserted = true;
                    insertReturned = queue.InsertTaskAfter(injected, host!.SystemId, ContextDefinition.CrossContext);
                }

                return false;
            }).Enqueue();

        var tail = queue.CreateTask("tail").AsNonBlocking().WithAction(() => order.Add("tail"))
            .WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        insertReturned.Should().BeTrue(
            "the anchor is the currently executing task, and the guard only refuses anchors that sit before it, so a task may insert directly after itself");
        injected.Status.Should().Be(TaskStatus.Completed,
            "the insertion lands at the anchor's index plus one, which is ahead of everything already queued, and the same pass then reads the live queue and runs it");
        tail.Status.Should().Be(TaskStatus.Queued, "the item that was already queued is displaced to the following pass");

        Step(queue, 1);
        order.Should().Equal("injected", "tail");
    }

    [Fact]
    public void InsertTaskAfter_AnotherQueuedTaskByCustomId_FromACondition_LandsDirectlyAfterThatTask()
    {
        var queue = MakeQueue();
        var inserted = false;
        var insertReturned = false;
        var order = new List<string>();
        var injected = MakeImmediateTask("injected", () => order.Add("injected"));

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!inserted)
                {
                    inserted = true;
                    insertReturned = queue.InsertTaskAfter(injected, "target", ContextDefinition.CrossContext);
                }

                return false;
            }).Enqueue();

        var target = queue.CreateTask("target").AsNonBlocking().WithAction(() => order.Add("target"))
            .WithImmediateCompletion().Enqueue();
        queue.CreateTask("tail").AsNonBlocking().WithAction(() => order.Add("tail"))
            .WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        insertReturned.Should().BeTrue("the custom-ID overload resolves the anchor the same way and the anchor sits after the current task");
        target.Status.Should().Be(TaskStatus.Completed,
            "the anchor itself was the first queued item, so the pass that did the insertion went on to run the anchor rather than the insertion");
        injected.Status.Should().Be(TaskStatus.Queued, "the inserted task waits its turn behind the anchor it was placed after");

        Step(queue, 2);
        order.Should().Equal(new[] { "target", "injected", "tail" },
            "and it is placed ahead of what already followed the anchor");
    }

    [Fact]
    public void InsertTaskAfter_ATaskCompletedBeforeTheCurrentOne_IsRefusedSilently()
    {
        var queue = MakeQueue();
        var attempted = false;
        var insertReturned = true;
        var executions = 0;
        var injected = MakeImmediateTask("injected", () => executions++);

        var done = queue.CreateTask("done").AsNonBlocking().WithAction(() => { })
            .WithImmediateCompletion().Enqueue();

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!attempted)
                {
                    attempted = true;
                    insertReturned = queue.InsertTaskAfter(injected, "done", ContextDefinition.CrossContext);
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        attempted.Should().BeTrue();
        done.Status.Should().Be(TaskStatus.Completed, "a finished task keeps its place in the queue, so it is still found as an anchor");
        insertReturned.Should().BeFalse(
            "the anchor's index is compared against the current task's, and an anchor already behind the current task is refused - the call reports the refusal and raises nothing");
        queue.GetAllTasks().Should().HaveCount(2, "nothing was added");
        queue.GetTasksByCustomId("injected").Should().BeEmpty();
        injected.Status.Should().Be(TaskStatus.Queued, "the rejected task is left in its as-constructed state and never runs");
        executions.Should().Be(0);
    }

    [Fact]
    public void InsertTaskAfter_ATaskCancelledByTheSameCondition_StillSucceeds()
    {
        var queue = MakeQueue();
        var acted = false;
        var cancelReturned = false;
        var insertReturned = false;
        var victimExecutions = 0;
        var injectedExecutions = 0;
        var injected = MakeImmediateTask("injected", () => injectedExecutions++);

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!acted)
                {
                    acted = true;
                    cancelReturned = queue.CancelTask("victim");
                    insertReturned = queue.InsertTaskAfter(injected, "victim", ContextDefinition.CrossContext);
                }

                return false;
            }).Enqueue();

        var victim = queue.CreateTask("victim").AsNonBlocking().WithAction(() => victimExecutions++)
            .WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        cancelReturned.Should().BeTrue();
        victim.Status.Should().Be(TaskStatus.Cancelled);
        insertReturned.Should().BeTrue(
            "anchor resolution matches on identity alone and never looks at status, so a task cancelled a moment earlier in the same condition is still a usable anchor");
        injected.Status.Should().Be(TaskStatus.Completed,
            "the insertion sits after a cancelled item, which makes it the first queued item, so the same pass runs it");
        victimExecutions.Should().Be(0, "the anchor itself stays cancelled and never executes");
        injectedExecutions.Should().Be(1);
    }

    [Fact]
    public void InsertTaskAfter_AnUnknownAnchor_FromACondition_ReturnsFalseWithoutThrowing()
    {
        var queue = MakeQueue();
        var attempted = false;
        var insertReturned = true;
        Exception? raised = null;
        var injected = MakeImmediateTask("injected", () => { });

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!attempted)
                {
                    attempted = true;

                    try
                    {
                        insertReturned = queue.InsertTaskAfter(injected, "no.such.task", ContextDefinition.CrossContext);
                    }
                    catch (Exception ex)
                    {
                        raised = ex;
                    }
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        attempted.Should().BeTrue();
        raised.Should().BeNull("a missing anchor is reported through the return value rather than by raising");
        insertReturned.Should().BeFalse();
        queue.GetAllTasks().Should().HaveCount(1, "the queue is untouched");
    }

    [Fact]
    public void InsertTaskAfter_ATaskInsideABatch_FromAStandaloneCondition_SucceedsOnlyCrossContext()
    {
        var queue = MakeQueue();
        var attempted = false;
        var crossReturned = false;
        var sameReturned = true;
        var strictReturned = true;
        var crossExecutions = 0;
        var crossTask = MakeImmediateTask("insert.cross", () => crossExecutions++);
        var sameTask = MakeImmediateTask("insert.same", () => { });
        var strictTask = MakeImmediateTask("insert.strict", () => { });

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!attempted)
                {
                    attempted = true;
                    crossReturned = queue.InsertTaskAfter(crossTask, "inbatch", ContextDefinition.CrossContext);
                    sameReturned = queue.InsertTaskAfter(sameTask, "inbatch", ContextDefinition.SameContext);
                    strictReturned = queue.InsertTaskAfter(strictTask, "inbatch", ContextDefinition.SameContextStrict);
                }

                return false;
            }).Enqueue();

        var batch = queue.CreateBatch("target.batch")
            .AddTask(MakeImmediateTask("inbatch", () => { }))
            .Enqueue();

        queue.StartQueue();
        Step(queue, 2);

        crossReturned.Should().BeTrue(
            "cross-context anchor resolution descends into every queued batch, and an anchor found inside one makes the insertion land in that batch's task list rather than in the queue");
        crossTask.ParentBatch.Should().BeSameAs(batch, "the inserted task is adopted by the batch it was placed into");
        batch.Tasks.Select(t => t.CustomId).Should().Equal("inbatch", "insert.cross");

        sameReturned.Should().BeFalse(
            "same-context resolution from a standalone task looks only at standalone tasks, so a batch member is not an anchor it can see");
        strictReturned.Should().BeFalse(
            "strict resolution stops at the first batch past the current task, so it cannot see the anchor either");
        queue.GetAllTasks().Should().HaveCount(3, "only the cross-context insertion was applied - the other two calls added nothing and raised nothing");

        StepUntil(queue, () => crossTask.Status == TaskStatus.Completed).Should().BePositive(
            "the inserted task runs as an ordinary member of the batch it was placed into");
        crossExecutions.Should().Be(1);
    }

    [Fact]
    public void InsertTaskAfter_AStandaloneTask_FromABatchTasksCondition_SucceedsOnlyCrossContext()
    {
        var queue = MakeQueue();
        var attempted = false;
        var innerMayComplete = false;
        var crossReturned = false;
        var sameReturned = true;
        var strictReturned = true;
        var crossTask = MakeImmediateTask("insert.cross", () => { });
        var sameTask = MakeImmediateTask("insert.same", () => { });
        var strictTask = MakeImmediateTask("insert.strict", () => { });

        var batch = queue.CreateBatch("host.batch")
            .AddTask(MakeConditionTask("inner", () =>
            {
                if (!attempted)
                {
                    attempted = true;
                    crossReturned = queue.InsertTaskAfter(crossTask, "outer", ContextDefinition.CrossContext);
                    sameReturned = queue.InsertTaskAfter(sameTask, "outer", ContextDefinition.SameContext);
                    strictReturned = queue.InsertTaskAfter(strictTask, "outer", ContextDefinition.SameContextStrict);
                }

                return innerMayComplete;
            }))
            .Enqueue();

        queue.CreateTask("outer").AsNonBlocking().WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 3);

        attempted.Should().BeTrue();
        crossReturned.Should().BeTrue(
            "cross-context resolution reaches out of the batch into the queue, and the anchor being a standalone task makes the insertion a standalone queue item");
        crossTask.ParentBatch.Should().BeNull();
        sameReturned.Should().BeFalse(
            "with a batch current, both narrowed modes resolve anchors only within that batch, so a standalone anchor is invisible to them");
        strictReturned.Should().BeFalse();

        crossTask.Status.Should().Be(TaskStatus.Queued,
            "the insertion is in the queue behind the running batch, so nothing about the current pass changes");
        queue.GetCurrentBatch().Should().BeSameAs(batch);

        innerMayComplete = true;
        StepUntil(queue, () => crossTask.Status == TaskStatus.Completed).Should().BePositive(
            "it runs once the batch has finished and the queue resumes its own items");
    }

    [Fact]
    public void InsertTaskAfter_IntoOwnBatch_FromTheBatchsCurrentTasksCondition_RunsInTheSamePass()
    {
        var queue = MakeQueue();
        var inserted = false;
        var insertReturned = false;
        var order = new List<string>();
        QueuedTask? injected = null;

        var batch = queue.CreateBatch("own.batch")
            .AddTask(MakeConditionTask("first", () =>
            {
                if (!inserted)
                {
                    inserted = true;
                    insertReturned = queue.InsertTaskAfter(injected!, "first", ContextDefinition.SameContext);
                }

                return false;
            }))
            .AddTask(MakeImmediateTask("last", () => order.Add("last")))
            .Enqueue();

        injected = MakeImmediateTask("injected", () => order.Add("injected"));

        queue.StartQueue();
        Step(queue, 3);

        insertReturned.Should().BeTrue(
            "same-context resolution inside a batch finds the anchor in that batch's own task list, and the anchor being the batch's executing task is allowed");
        batch.Tasks.Select(t => t.CustomId).Should().Equal("first", "injected", "last");
        injected.Status.Should().Be(TaskStatus.Completed,
            "the batch chooses its next task by reading its own list after conditions have run, so an insertion made by the batch's current task is picked up in that same pass");
        batch.Tasks[2].Status.Should().Be(TaskStatus.Queued, "and it runs ahead of the task it was inserted in front of");

        Step(queue, 1);
        order.Should().Equal("injected", "last");
    }

    [Fact]
    public void InsertTaskAfter_IntoOwnBatch_FromANonCurrentBatchTasksCondition_LandsAndThePassSurvives()
    {
        // The batch walk is materialized before it runs, like the queue-level walk, so an addition made from a
        // non-current task's condition lands without costing the pass, even though it grows the list the pass is
        // iterating.
        var queue = MakeQueue();
        var armed = false;
        var inserted = false;
        var insertReturned = false;
        var readyIsMet = false;
        var injectedExecutions = 0;
        QueuedTask? injected = null;

        var readyTask = MakeConditionTask("ready", () => readyIsMet);

        var batch = queue.CreateBatch("own.batch")
            .AddTask(MakeConditionTask("anchor", () => false))
            .AddTask(readyTask)
            .AddTask(MakeConditionTask("adder", () =>
            {
                if (armed && !inserted)
                {
                    inserted = true;
                    insertReturned = queue.InsertTaskAfter(injected!, "anchor", ContextDefinition.SameContext);
                }

                return false;
            }))
            .Enqueue();

        injected = MakeImmediateTask("injected", () => injectedExecutions++);

        queue.StartQueue();
        Step(queue, 4);

        batch.Tasks.Should().OnlyContain(t => t.Status == TaskStatus.WaitingForCompletion,
            "four passes start the batch and then each of its three tasks in turn, so all three are waiting on their conditions");

        // The pass below evaluates "ready" first, deciding to complete it, and then "adder", which grows the list
        // being walked.
        readyIsMet = true;
        armed = true;
        Step(queue, 1);

        insertReturned.Should().BeTrue("the insertion itself is applied before the walk notices anything");
        batch.Tasks.Select(t => t.CustomId).Should().Equal(new[] { "anchor", "injected", "ready", "adder" },
            "so the batch really does carry the new task from this point on");
        readyTask.Status.Should().Be(TaskStatus.Completed,
            "the completion decided earlier in this pass is applied rather than discarded, because growing the batch no longer invalidates the walk");
        injected.Status.Should().Be(TaskStatus.Completed,
            "and the batch picks its next task by reading its own list after the conditions have run, so the inserted task runs in this same pass");
        injectedExecutions.Should().Be(1);
    }

    [Fact]
    public void EnqueueTask_FromANonCurrentWaitingTasksCondition_LeavesTheQueueLevelWalkIntact()
    {
        // The queue-level counterpart of the test above, arranged identically: a condition belonging to a waiting
        // task that is not the current one adds work while the same pass is walking the waiting tasks. Here the
        // walk is taken over a materialized snapshot, so the addition costs the pass nothing.
        var queue = MakeQueue();
        var armed = false;
        var enqueued = false;
        var readyIsMet = false;
        var spawnedExecutions = 0;
        QueuedTask? spawned = null;

        queue.CreateTask("anchor").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();
        var readyTask = queue.CreateTask("ready").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => readyIsMet).Enqueue();
        queue.CreateTask("adder").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (armed && !enqueued)
                {
                    enqueued = true;
                    spawned = queue.EnqueueTask("spawned", isBlocking: false, () => spawnedExecutions++, TaskCompletionCondition.Immediate());
                }

                return false;
            }).Enqueue();
        queue.CreateTask("tailwatch").AsNonBlocking().WithAction(() => { }).WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        queue.GetAllTasks().Should().OnlyContain(t => t.Status == TaskStatus.WaitingForCompletion,
            "four passes start each of the four tasks in turn, leaving the last of them as the current task");

        readyIsMet = true;
        armed = true;
        Step(queue, 1);

        enqueued.Should().BeTrue();
        readyTask.Status.Should().Be(TaskStatus.Completed,
            "the waiting tasks are snapshotted before their conditions run, so an addition made from one of them does not disturb the walk and the completions the pass decided on are still applied");
        spawned!.Status.Should().Be(TaskStatus.Completed,
            "and the same pass still goes on to pick the addition up and run it");
        spawnedExecutions.Should().Be(1);
    }
}
