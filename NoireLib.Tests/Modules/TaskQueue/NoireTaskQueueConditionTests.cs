using FluentAssertions;
using Newtonsoft.Json;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Characterizes the two places where consumer-supplied code runs while the queue holds its private lock: the
/// completion condition, evaluated on every waiting task on every processing pass, and
/// <see cref="NoireTaskQueue.GetTasksByPredicate"/>, the only public query that invokes a consumer predicate inside
/// the lock.<br/>
/// The lock is a re-entrant monitor, so a callback that calls back into the queue on the processing thread succeeds
/// instead of deadlocking, and its mutation lands mid-pass, visible to decisions the pass has already made. These
/// tests pin that behavior as an invariant a future change must alter deliberately rather than by accident.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueConditionTests : IDisposable
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
    /// Steps the queue a fixed number of passes. Every re-entrant callback in this file is latched so it fires once,
    /// and stepping is always bounded, so no scenario here can spin or recurse without end.
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
    /// A serializer that resolves every setting from the object below it, so these tests are unaffected by whatever
    /// any other code in the process assigned to <see cref="JsonConvert.DefaultSettings"/>.
    /// </summary>
    private static JsonSerializer MakeSerializer() => JsonSerializer.Create(new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None
    });

    private static string Serialize(JsonSerializer serializer, object value)
    {
        var writer = new System.IO.StringWriter();
        serializer.Serialize(writer, value);
        return writer.ToString();
    }

    private static T? Deserialize<T>(JsonSerializer serializer, string json)
    {
        using var stringReader = new System.IO.StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);
        return serializer.Deserialize<T>(jsonReader);
    }

    // -- Completion-condition route: user code under the queue lock, every pass ------------------------------------

    [Fact]
    public void Condition_ThatCancelsItsOwnTask_SeesTheCancellationTakeEffectImmediately()
    {
        var queue = MakeQueue();
        var entered = false;
        var cancelReturned = false;
        TaskStatus statusInsideCondition = default;

        QueuedTask? task = null;
        task = queue.CreateTask("self.cancel").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (entered)
                    return false;

                entered = true;
                cancelReturned = queue.CancelTask("self.cancel");
                statusInsideCondition = task!.Status;
                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 5);

        entered.Should().BeTrue("the condition is evaluated on a processing pass, which is the whole point of this file");
        cancelReturned.Should().BeTrue("the queue lock is a re-entrant monitor, so a cancel issued from inside the condition acquires it again and succeeds rather than deadlocking");
        statusInsideCondition.Should().Be(TaskStatus.Cancelled, "the mutation is applied to live state, so the condition observes its own effect before it has even returned");
        task!.Status.Should().Be(TaskStatus.Cancelled);
    }

    [Fact]
    public void Condition_ThatCancelsItsOwnTaskAndReturnsTrue_DoesNotAlsoComplete()
    {
        var queue = MakeQueue();
        var entered = false;

        var task = queue.CreateTask("cancel.then.true").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!entered)
                {
                    entered = true;
                    queue.CancelTask("cancel.then.true");
                }

                return true;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 5);

        task.Status.Should().Be(TaskStatus.Cancelled,
            "ProcessWaitingTaskStatus re-reads the status after evaluating the condition and abandons the pass when it moved");
    }

    [Fact]
    public void Condition_ThatCompletesAnotherTaskByWritingItsStatus_StillRunsTheQueuesCompletion()
    {
        // QueuedTask.Status is publicly settable, so a consumer can complete another task with a direct write
        // instead of a queue method. The deferred finalization still runs the completion callback when the write
        // agrees with what the pass had already decided; only a task written to cancelled or failed is left alone.
        var queue = MakeQueue();
        var firstIsReady = false;
        var secondShouldCompleteFirst = false;
        var firstCompletedCallbacks = 0;

        var first = queue.CreateTask("first").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => firstIsReady)
            .OnCompleted(_ => firstCompletedCallbacks++)
            .Enqueue();

        var second = queue.CreateTask("second").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (secondShouldCompleteFirst)
                {
                    secondShouldCompleteFirst = false;
                    first!.Status = TaskStatus.Completed;
                }

                return false;
            }).Enqueue();

        var third = queue.CreateTask("third").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        first.Status.Should().Be(TaskStatus.WaitingForCompletion);
        third.Status.Should().Be(TaskStatus.WaitingForCompletion);

        firstIsReady = true;
        secondShouldCompleteFirst = true;
        Step(queue, 1);

        first.Status.Should().Be(TaskStatus.Completed, "the direct write stands");
        firstCompletedCallbacks.Should().Be(1,
            "the pending completion still runs for a task the consumer resolved the same way, so writing the status does not cost the completion callback");
        queue.GetStatistics().CompletedTasks.Should().Be(1,
            "the statistic counts tasks whose status is Completed rather than tracking an internal tally, so a directly written status counts either way");
    }

    [Fact]
    public void Condition_ThatCancelsATaskAlreadyMarkedForCompletion_HasTheCancellationHonored()
    {
        // A pass collects the tasks to complete before the later tasks' conditions have run, so a cancellation
        // issued from one of those conditions arrives after the decision to complete was already made. The
        // deferred finalization re-validates the status, so the cancellation wins.
        var queue = MakeQueue();
        var firstIsReady = false;
        var secondShouldCancelFirst = false;
        var cancelReturned = false;
        TaskStatus statusInsideCondition = default;

        var first = queue.CreateTask("first").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => firstIsReady).Enqueue();

        var second = queue.CreateTask("second").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (secondShouldCancelFirst)
                {
                    secondShouldCancelFirst = false;
                    cancelReturned = queue.CancelTask("first");
                    statusInsideCondition = first!.Status;
                }

                return false;
            }).Enqueue();

        var third = queue.CreateTask("third").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        first.Status.Should().Be(TaskStatus.WaitingForCompletion);
        second.Status.Should().Be(TaskStatus.WaitingForCompletion);
        third.Status.Should().Be(TaskStatus.WaitingForCompletion);
        queue.GetCurrentTask().Should().BeSameAs(third,
            "the third task is the current one, so the other two are both evaluated from the same batched loop in a single pass");

        // In the next pass the first task's condition is met and the second task's condition, evaluated after it,
        // cancels the first.
        firstIsReady = true;
        secondShouldCancelFirst = true;
        Step(queue, 1);

        cancelReturned.Should().BeTrue("the cancellation really was applied");
        statusInsideCondition.Should().Be(TaskStatus.Cancelled, "and it really did take effect at the time it was issued");
        first.Status.Should().Be(TaskStatus.Cancelled,
            "the deferred finalization re-validates the status before acting, so a task finished by a consumer callback during the pass keeps the outcome that callback gave it");
        queue.GetStatistics().CompletedTasks.Should().Be(0,
            "and a cancelled task is not counted as completed");
    }

    [Fact]
    public void Condition_ThatEnqueuesATask_HasItPickedUpInTheSamePass()
    {
        var queue = MakeQueue();
        var enqueued = false;
        var executions = 0;
        var tasksVisibleFromInside = 0;
        QueuedTask? spawned = null;

        queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!enqueued)
                {
                    enqueued = true;
                    spawned = queue.CreateTask("spawned").WithAction(() => executions++)
                        .WithImmediateCompletion().Enqueue();
                    tasksVisibleFromInside = queue.GetAllTasks().Count;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        queue.TickOnce();
        enqueued.Should().BeFalse("the first pass only starts the task, so its condition has not been evaluated yet");

        queue.TickOnce();

        tasksVisibleFromInside.Should().Be(2, "the enqueue is applied to the live queue, so the condition can already see the task it just added");
        spawned!.Status.Should().Be(TaskStatus.Completed,
            "the same pass goes on to pick the next queued item after evaluating conditions, so a task enqueued from a condition runs within that very pass rather than on the following one");
        executions.Should().Be(1);
    }

    [Fact]
    public void Condition_ThatQueriesTheQueue_ObservesMidPassState()
    {
        var queue = MakeQueue();
        var probed = false;
        var allTasks = 0;
        var waitingTasks = 0;
        string? currentTaskId = null;
        QueueState stateInsideCondition = default;

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!probed)
                {
                    probed = true;
                    allTasks = queue.GetAllTasks().Count;
                    waitingTasks = queue.GetTasksByPredicate(t => t.Status == TaskStatus.WaitingForCompletion).Count;
                    currentTaskId = queue.GetCurrentTask()?.CustomId;
                    stateInsideCondition = queue.QueueState;
                }

                return false;
            }).Enqueue();

        var other = queue.CreateTask("other").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        probed.Should().BeTrue();
        allTasks.Should().Be(2, "a read-only query from a condition re-enters the lock on the same thread and returns normally");
        waitingTasks.Should().Be(1, "only the calling task is waiting at that point in the pass, so the query reports half-processed state rather than a settled snapshot");
        currentTaskId.Should().Be("host");
        stateInsideCondition.Should().Be(QueueState.Running);
        host.Status.Should().Be(TaskStatus.WaitingForCompletion);
        other.Status.Should().Be(TaskStatus.WaitingForCompletion);
    }

    [Fact]
    public void Condition_ThatClearsTheQueue_EmptiesItAndCancelsTheCallingTask()
    {
        var queue = MakeQueue();
        var cleared = false;
        var removed = -1;
        var tasksVisibleFromInside = -1;
        QueueState stateInsideCondition = default;

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!cleared)
                {
                    cleared = true;
                    removed = queue.ClearQueue();
                    tasksVisibleFromInside = queue.GetAllTasks().Count;
                    stateInsideCondition = queue.QueueState;
                }

                return false;
            }).Enqueue();

        var other = queue.CreateTask("other").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => false).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        removed.Should().Be(2, "clearing from inside a condition runs to completion instead of blocking on the lock the caller already holds");
        tasksVisibleFromInside.Should().Be(0, "the queue the pass is iterating has been emptied underneath it");
        stateInsideCondition.Should().Be(QueueState.Running, "ClearQueue restores whatever state it found, so the pass carries on as if nothing happened");
        host.Status.Should().Be(TaskStatus.Cancelled, "ClearQueue cancels the current task, which here is the task whose condition is running");
        other.Status.Should().Be(TaskStatus.Queued,
            "only the current task and current batch are cancelled - every other item is dropped from the queue still carrying its old status and never receives a cancellation callback");
        queue.GetAllTasks().Should().BeEmpty();
    }

    [Fact]
    public void Condition_ThatStopsTheQueue_StopsAndEmptiesItFromInside()
    {
        var queue = MakeQueue();
        var stopped = false;
        var tasksVisibleFromInside = -1;
        QueueState stateInsideCondition = default;

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!stopped)
                {
                    stopped = true;
                    queue.StopQueue();
                    tasksVisibleFromInside = queue.GetAllTasks().Count;
                    stateInsideCondition = queue.QueueState;
                }

                return false;
            }).Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        stateInsideCondition.Should().Be(QueueState.Stopped, "StopQueue clears the queue and moves it to Stopped while the pass that called it is still running");
        tasksVisibleFromInside.Should().Be(0);
        host.Status.Should().Be(TaskStatus.Cancelled);
        queue.QueueState.Should().Be(QueueState.Stopped);
    }

    [Fact]
    public void Condition_ThatSkipsQueuedTasks_CancelsThemAndLeavesTheQueueRunning()
    {
        var queue = MakeQueue();
        var skipped = false;
        var skipCount = -1;
        QueueState stateInsideCondition = default;

        var host = queue.CreateTask("host").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                if (!skipped)
                {
                    skipped = true;
                    skipCount = queue.SkipNextTasks(1);
                    stateInsideCondition = queue.QueueState;
                }

                return false;
            }).Enqueue();

        var victim = queue.CreateTask("victim").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        Step(queue, 4);

        skipCount.Should().Be(1, "SkipNextTasks pauses and resumes the queue around its work, and both transitions happen while a processing pass is on the stack");
        stateInsideCondition.Should().Be(QueueState.Running, "the pause is undone before SkipNextTasks returns, so the pass does not notice it happened");
        victim.Status.Should().Be(TaskStatus.Cancelled);
        host.Status.Should().Be(TaskStatus.WaitingForCompletion);
        queue.QueueState.Should().Be(QueueState.Running);
    }

    [Fact]
    public void Condition_ThatThrows_FailsOnlyItsOwnTaskAndLetsTheQueueContinue()
    {
        var queue = MakeQueue();
        var calls = 0;

        var thrower = queue.CreateTask("throws").AsNonBlocking().WithAction(() => { })
            .WithCondition(() =>
            {
                calls++;
                throw new InvalidOperationException("condition failed");
            }).Enqueue();

        var later = queue.CreateTask("later").WithAction(() => { }).WithImmediateCompletion().Enqueue();

        queue.StartQueue();
        StepUntil(queue, () => later.Status == TaskStatus.Completed).Should().BePositive(
            "a faulted condition must not stop the rest of the queue from being processed");

        calls.Should().Be(1, "the task is failed by the first throw rather than being asked again on every later pass");
        thrower.Status.Should().Be(TaskStatus.Failed,
            "the exception is contained to the task whose condition raised it, so the fault surfaces as that task failing instead of unwinding the pass");
        thrower.FailureException.Should().BeOfType<InvalidOperationException>("the consumer's own exception is what the task carries, not a synthesized timeout");
        queue.QueueState.Should().Be(QueueState.Running);
    }

    [Fact]
    public void PostCompletionDelayProvider_ThatCancelsItsTask_IsCaughtByTheGuardAfterTheProvider()
    {
        var queue = MakeQueue();
        var cancelReturned = false;
        TaskStatus statusInsideProvider = default;

        var task = queue.CreateTask("delayed").AsNonBlocking().WithAction(() => { })
            .WithCondition(() => true)
            .WithDelay(t =>
            {
                cancelReturned = queue.CancelTask("delayed");
                statusInsideProvider = t.Status;
                return TimeSpan.FromMilliseconds(1);
            })
            .Enqueue();

        queue.StartQueue();
        Step(queue, 5);

        cancelReturned.Should().BeTrue("the delay provider is the other consumer callback invoked under the lock during a pass");
        statusInsideProvider.Should().Be(TaskStatus.Cancelled);
        task.Status.Should().Be(TaskStatus.Cancelled,
            "the status is re-read after the delay provider returns, so the task is not pushed into a post-completion delay it was cancelled out of");
    }

    [Fact]
    public void BatchCondition_ThatAppendsToItsOwnBatch_TakesEffectWithoutThrowing()
    {
        var queue = MakeQueue();
        var appended = false;
        var appendedTask = new QueuedTask("third", isBlocking: false)
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        TaskBatch? batch = null;
        batch = queue.CreateBatch("batch")
            .AddTask(new QueuedTask("first", isBlocking: false)
            {
                ExecuteAction = () => { },
                CompletionCondition = TaskCompletionCondition.FromPredicate(() => false)
            })
            .AddTask(new QueuedTask("second", isBlocking: false)
            {
                ExecuteAction = () => { },
                CompletionCondition = TaskCompletionCondition.FromPredicate(() =>
                {
                    if (!appended)
                    {
                        appended = true;
                        batch!.AddTask(appendedTask);
                    }

                    return false;
                })
            })
            .Enqueue();

        queue.StartQueue();
        StepUntil(queue, () => appendedTask.Status == TaskStatus.Completed).Should().BePositive(
            "the batch pass evaluates the non-current waiting task's condition while iterating the batch's own task list, and the task that condition appends is picked up and executed on a later pass");

        appended.Should().BeTrue();
        batch.Tasks.Should().HaveCount(3, "the list the pass was iterating grew by one while it was iterating it");
        batch.Tasks[0].Status.Should().Be(TaskStatus.WaitingForCompletion);
        batch.Tasks[1].Status.Should().Be(TaskStatus.WaitingForCompletion);
        batch.Status.Should().Be(BatchStatus.Processing,
            "growing the batch mid-iteration raises nothing today, so the pass completes and the batch simply has more work than it started with");
    }

    // -- Query route: GetTasksByPredicate is the only public query taking a consumer predicate ---------------------

    [Fact]
    public void GetTasksByPredicate_WithAPredicateThatQueriesTheQueueAgain_Returns()
    {
        var queue = MakeQueue();
        queue.CreateTask("a").WithImmediateCompletion().Enqueue();
        queue.CreateTask("b").WithImmediateCompletion().Enqueue();

        var matches = queue.GetTasksByPredicate(t => queue.GetAllTasks().Count > 0 && t.CustomId == "a");

        matches.Should().HaveCount(1, "a nested read-only query re-enters the lock on the same thread, so it returns instead of deadlocking");
    }

    [Fact]
    public void GetTasksByPredicate_WithAPredicateThatCancelsWhatItSees_CancelsAndStillReturnsThem()
    {
        var queue = MakeQueue();
        var first = queue.CreateTask("a").WithCondition(() => false).Enqueue();
        var second = queue.CreateTask("b").WithCondition(() => false).Enqueue();

        var matches = queue.GetTasksByPredicate(t =>
        {
            queue.CancelTask(t.SystemId);
            return true;
        });

        first.Status.Should().Be(TaskStatus.Cancelled, "cancelling only rewrites a task's status, so it does not disturb the enumeration in progress");
        second.Status.Should().Be(TaskStatus.Cancelled);
        matches.Should().HaveCount(2, "the result still contains tasks the predicate itself cancelled while deciding whether they matched");
    }

    [Fact]
    public void GetTasksByPredicate_WithAPredicateThatEnqueues_ReadsTheQueueAsItWasWhenAsked()
    {
        var queue = MakeQueue();
        queue.CreateTask("a").WithCondition(() => false).Enqueue();
        var enqueuedOnce = false;

        // The candidate list is taken before any predicate runs, so a predicate that enqueues no longer
        // structurally modifies the list being walked. The query answers about the queue as it stood on entry.
        var matches = queue.GetTasksByPredicate(t =>
        {
            if (!enqueuedOnce)
            {
                enqueuedOnce = true;
                queue.CreateTask("spawned").WithImmediateCompletion().Enqueue();
            }

            return true;
        });

        matches.Should().HaveCount(1, "only the task present when the query was issued is a candidate");
        matches[0].CustomId.Should().Be("a");
        queue.GetAllTasks().Should().HaveCount(2, "and the task the predicate added is really in the queue");
    }

    [Fact]
    public void GetTasksByPredicate_WithAPredicateThatClearsTheQueue_ReadsTheQueueAsItWasWhenAsked()
    {
        var queue = MakeQueue();
        queue.CreateTask("a").WithCondition(() => false).Enqueue();
        queue.CreateTask("b").WithCondition(() => false).Enqueue();
        var clearedOnce = false;

        var matches = queue.GetTasksByPredicate(t =>
        {
            if (!clearedOnce)
            {
                clearedOnce = true;
                queue.ClearQueue();
            }

            return true;
        });

        matches.Should().HaveCount(2, "both tasks were candidates when the query was issued, and clearing does not retract them");
        queue.GetAllTasks().Should().BeEmpty("the clear itself still took effect");
    }

    // -- Serialization of the persisted shape ---------------------------------------------------------------------

    [Fact]
    public void QueuedTask_CarryingAnyDelegate_CannotBeSerialized()
    {
        var serializer = MakeSerializer();
        var task = new QueuedTask("has.action")
        {
            ExecuteAction = () => { },
            CompletionCondition = TaskCompletionCondition.Immediate()
        };

        var act = () => Serialize(serializer, task);

        act.Should().Throw<JsonSerializationException>(
                "a delegate is serialized as an ordinary object, so the writer walks into the reflection graph behind it")
            .WithMessage("Self referencing loop detected*");
    }

    [Fact]
    public void TaskCompletionCondition_FromPredicate_CannotBeSerialized()
    {
        var serializer = MakeSerializer();
        var condition = TaskCompletionCondition.FromPredicate(() => true);

        var act = () => Serialize(serializer, condition);

        act.Should().Throw<JsonSerializationException>("the predicate is reached through the condition exactly as any other delegate would be")
            .WithMessage("Self referencing loop detected*");
    }

    [Fact]
    public void QueuedTask_WithoutDelegates_SerializesConfigurationAndDropsInternalTiming()
    {
        var serializer = MakeSerializer();
        var task = new QueuedTask("plain", isBlocking: false)
        {
            CompletionCondition = TaskCompletionCondition.Immediate(),
            Timeout = TimeSpan.FromSeconds(5),
            Metadata = "meta",
            Status = TaskStatus.WaitingForCompletion,
            StopQueueOnFail = true
        };

        var json = Serialize(serializer, task);

        json.Should().Contain("\"CustomId\":\"plain\"")
            .And.Contain("\"Status\":2", "the status enum is written as its numeric value")
            .And.Contain("\"Timeout\":\"00:00:05\"")
            .And.Contain("\"StopQueueOnFail\":true")
            .And.Contain("\"SystemId\":");
        json.Should().Contain("\"ExecuteAction\":null").And.Contain("\"OnCompleted\":null")
            .And.Contain("\"PostCompletionDelayProvider\":null",
                "delegate-valued members are written as null when unset, which is the only case in which a task can be written at all");
        json.Should().NotContain("QueuedAtTicks", "the timing and retry bookkeeping is internal, so none of it is part of the persisted shape");
        json.Should().NotContain("CurrentRetryAttempt").And.NotContain("PostDelayStartTicks").And.NotContain("EventConditionMet");
    }

    [Fact]
    public void QueuedTask_CannotBeDeserialized_AtAll()
    {
        var serializer = MakeSerializer();
        var json = Serialize(serializer, new QueuedTask("plain") { CompletionCondition = TaskCompletionCondition.Immediate() });

        var act = () => Deserialize<QueuedTask>(serializer, json);

        act.Should().Throw<JsonSerializationException>(
                "QueuedTask declares two public constructors and no parameterless one, so the serializer cannot pick a creator - a task can be written out but never read back, which means there is no task round-trip today in either direction")
            .WithMessage("Unable to find a constructor to use for type*");
    }

    [Fact]
    public void QueuedTask_BelongingToABatch_IsAReferenceLoop()
    {
        var serializer = MakeSerializer();
        var task = new QueuedTask("inner") { CompletionCondition = TaskCompletionCondition.Immediate() };
        var batch = new TaskBatch("owner");
        batch.AddTask(task);
        task.ParentBatch = batch;

        var act = () => Serialize(serializer, task);

        act.Should().Throw<JsonSerializationException>("a task points at its batch and the batch points back at the task")
            .WithMessage("Self referencing loop detected*ParentBatch.Tasks*");
    }

    [Fact]
    public void QueuedTask_EnqueuedThroughTheBuilder_SerializesTheOwningModulesState()
    {
        var serializer = MakeSerializer();
        var queue = MakeQueue();
        var task = queue.CreateTask("owned").WithImmediateCompletion().Enqueue();

        var json = Serialize(serializer, task);

        json.Should().Contain("\"OwningQueue\":{",
            "the builder sets OwningQueue, and that back-reference is a public property, so writing one task drags the module's own public state into the document");
        json.Should().Contain("\"ShouldStopQueueOnComplete\":").And.Contain("\"IsActive\":");
    }

    [Fact]
    public void TaskBatch_HoldingTasks_CannotBeDeserialized()
    {
        var serializer = MakeSerializer();
        var batch = new TaskBatch("batch.with.tasks");
        batch.AddTask(new QueuedTask("inner") { CompletionCondition = TaskCompletionCondition.Immediate() });
        var json = Serialize(serializer, batch);

        json.Should().Contain("\"TaskCount\":1", "computed counters are written out even though nothing reads them back");

        var act = () => Deserialize<TaskBatch>(serializer, json);

        act.Should().Throw<JsonSerializationException>("reading the batch fails as soon as it reaches the first task, for the same missing-constructor reason")
            .WithMessage("Unable to find a constructor to use for type*");
    }

    [Fact]
    public void TaskBatch_WithoutTasks_RoundTripsExceptItsSystemId()
    {
        var serializer = MakeSerializer();
        var batch = new TaskBatch("empty.batch", isBlocking: false)
        {
            Status = BatchStatus.Processing,
            StartedAtTicks = 123,
            StopQueueOnFail = true,
            TaskFailureMode = BatchTaskFailureMode.FailBatch
        };

        var restored = Deserialize<TaskBatch>(serializer, Serialize(serializer, batch));

        restored.Should().NotBeNull("TaskBatch has a single public constructor, so the serializer can use it");
        restored!.CustomId.Should().Be("empty.batch");
        restored.IsBlocking.Should().BeFalse();
        restored.Status.Should().Be(BatchStatus.Processing);
        restored.StartedAtTicks.Should().Be(123);
        restored.QueuedAtTicks.Should().Be(batch.QueuedAtTicks);
        restored.StopQueueOnFail.Should().BeTrue();
        restored.TaskFailureMode.Should().Be(BatchTaskFailureMode.FailBatch);
        restored.SystemId.Should().NotBe(batch.SystemId,
            "SystemId is get-only and is not a constructor parameter, so the written value is ignored and the restored batch gets a freshly generated identity");
    }

    [Fact]
    public void TaskCompletionCondition_Immediate_RoundTrips()
    {
        var serializer = MakeSerializer();

        var restored = Deserialize<TaskCompletionCondition>(serializer, Serialize(serializer, TaskCompletionCondition.Immediate()));

        restored!.Type.Should().Be(CompletionConditionType.Immediate, "a condition carrying no delegate is the one part of a task that survives a full round-trip");
        restored.IsMet().Should().BeTrue();
    }

    [Fact]
    public void TaskCompletionCondition_FromEvent_WritesAnAssemblyQualifiedTypeName()
    {
        var serializer = MakeSerializer();

        var json = Serialize(serializer, TaskCompletionCondition.FromEvent<string>());

        json.Should().Contain("\"EventType\":\"System.String, System.Private.CoreLib",
            "the event type is a Type-valued property that serializes to an assembly-qualified name of its own accord, independently of the TypeNameHandling setting");
        json.Should().Contain("\"EventFilter\":null", "an event condition without a filter is the only event condition that can be written at all");
    }
}
