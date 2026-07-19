using FluentAssertions;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Characterization tests for the <see cref="ContextDefinition"/> query families of <see cref="NoireTaskQueue"/>.<br/>
/// Four families (<c>GetTask</c>, <c>GetTasksByPredicate</c>, <c>GetTasksByCustomId</c>, <c>GetAllTasks</c>) are each
/// evaluated across <see cref="ContextDefinition.CrossContext"/>, <see cref="ContextDefinition.SameContext"/> and
/// <see cref="ContextDefinition.SameContextStrict"/>, pinning what each returns for the same queue in three execution
/// states: nothing executing, a standalone task executing, and a batch processing. The batch boundary is where the
/// three modes genuinely disagree.<br/>
/// A final group of five look-alike methods answers "are these two in the same context" rather than "iterate the
/// ones that are"; they share the enum but compute their strict boundary from a different starting point, as the
/// tests here show.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueQueryTests : IDisposable
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

    #region Harness

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
    /// The standard mixed queue every context test is built on: loose tasks and batches alternating, so every mode has
    /// a batch boundary to react to.<br/>
    /// Queue order is loose.first, batch.one, loose.middle, batch.two, loose.last, which flattens to seven tasks.
    /// </summary>
    private sealed class MixedQueue
    {
        public NoireTaskQueue Queue = null!;

        public QueuedTask LooseFirst = null!;
        public QueuedTask BatchOneA = null!;
        public QueuedTask BatchOneB = null!;
        public QueuedTask LooseMiddle = null!;
        public QueuedTask BatchTwoA = null!;
        public QueuedTask BatchTwoB = null!;
        public QueuedTask LooseLast = null!;

        public TaskBatch BatchOne = null!;
        public TaskBatch BatchTwo = null!;

        /// <summary>Opens the completion condition of loose.middle, releasing the standalone hold point.</summary>
        public bool MiddleGate;

        /// <summary>Opens the completion condition of batch.two.a, releasing the in-batch hold point.</summary>
        public bool BatchGate;
    }

    /// <summary>
    /// Builds the mixed queue without running it, so every task is still Queued and nothing is current.
    /// </summary>
    private MixedQueue BuildMixedQueue()
    {
        var mixed = new MixedQueue { Queue = MakeQueue() };

        mixed.LooseFirst = mixed.Queue.EnqueueTask("loose.first", true, null, TaskCompletionCondition.Immediate());

        mixed.BatchOneA = new QueuedTask("batch.one.a") { CompletionCondition = TaskCompletionCondition.Immediate() };
        mixed.BatchOneB = new QueuedTask("batch.one.b") { CompletionCondition = TaskCompletionCondition.Immediate() };
        mixed.BatchOne = mixed.Queue.CreateBatch("batch.one").AddTasks(mixed.BatchOneA, mixed.BatchOneB).Enqueue();

        mixed.LooseMiddle = mixed.Queue.EnqueueTask(
            "loose.middle",
            true,
            null,
            TaskCompletionCondition.FromPredicate(() => mixed.MiddleGate));

        mixed.BatchTwoA = new QueuedTask("batch.two.a")
        {
            CompletionCondition = TaskCompletionCondition.FromPredicate(() => mixed.BatchGate)
        };
        mixed.BatchTwoB = new QueuedTask("batch.two.b") { CompletionCondition = TaskCompletionCondition.Immediate() };
        mixed.BatchTwo = mixed.Queue.CreateBatch("batch.two").AddTasks(mixed.BatchTwoA, mixed.BatchTwoB).Enqueue();

        mixed.LooseLast = mixed.Queue.EnqueueTask("loose.last", true, null, TaskCompletionCondition.Immediate());

        return mixed;
    }

    /// <summary>
    /// Drives the mixed queue until the standalone task loose.middle is the current task, with batch.one behind it and
    /// batch.two ahead of it.
    /// </summary>
    private static MixedQueue RunToStandaloneHold(MixedQueue mixed)
    {
        mixed.Queue.StartQueue();

        StepUntil(mixed.Queue, () => ReferenceEquals(mixed.Queue.GetCurrentTask(), mixed.LooseMiddle))
            .Should().BePositive("the standalone hold point is the state every SameContextStrict expectation is written against");

        mixed.Queue.GetCurrentBatch().Should().BeNull("loose.middle is a standalone task, so no batch is current");

        return mixed;
    }

    /// <summary>
    /// Drives the mixed queue until batch.two is the processing batch, held open by its first task.
    /// </summary>
    private static MixedQueue RunToBatchHold(MixedQueue mixed)
    {
        RunToStandaloneHold(mixed);
        mixed.MiddleGate = true;

        StepUntil(mixed.Queue, () => ReferenceEquals(mixed.Queue.GetCurrentBatch(), mixed.BatchTwo)
            && mixed.BatchTwoA.Status == TaskStatus.WaitingForCompletion)
            .Should().BePositive("the in-batch hold point is the state every in-batch expectation is written against");

        mixed.Queue.GetCurrentTask().Should().BeNull(
            "completing loose.middle clears the current task, and entering a batch never sets it again");

        return mixed;
    }

    private static string?[] IdsOf(IEnumerable<QueuedTask> tasks)
        => tasks.Select(task => task.CustomId).ToArray();

    /// <summary>
    /// Invokes the private context predicate. It is unreachable from the public surface without an EventBus, and it is
    /// the one method in the final region that has no public entry point at all.
    /// </summary>
    private static bool InvokeAreTasksInSameContext(NoireTaskQueue queue, QueuedTask target, ContextDefinition mode)
    {
        var method = typeof(NoireTaskQueue).GetMethod(
            "AreTasksInSameContext",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull("the context predicate is the method this region exists to pin");

        return (bool)method!.Invoke(queue, new object[] { target, mode })!;
    }

    #endregion

    #region GetTask family

    [Fact]
    public void GetTaskByCustomId_CrossContext_ReachesIntoEveryBatch()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetTaskByCustomId("loose.first", ContextDefinition.CrossContext)
            .Should().BeSameAs(mixed.LooseFirst, "a standalone task is reachable in every mode");
        mixed.Queue.GetTaskByCustomId("batch.one.a", ContextDefinition.CrossContext)
            .Should().BeSameAs(mixed.BatchOneA, "CrossContext descends into batches");
        mixed.Queue.GetTaskByCustomId("batch.two.b", ContextDefinition.CrossContext)
            .Should().BeSameAs(mixed.BatchTwoB, "no batch boundary stops a CrossContext lookup");
        mixed.Queue.GetTaskByCustomId("loose.last", ContextDefinition.CrossContext)
            .Should().BeSameAs(mixed.LooseLast, "the scan runs to the end of the queue");
    }

    [Fact]
    public void GetTaskByCustomId_SameContext_SeesStandaloneTasksOnlyButAcrossEveryBatch()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetTaskByCustomId("loose.last", ContextDefinition.SameContext)
            .Should().BeSameAs(mixed.LooseLast, "SameContext allows any number of batches between two standalone tasks");
        mixed.Queue.GetTaskByCustomId("batch.one.a", ContextDefinition.SameContext)
            .Should().BeNull("a batch task is a different context from a standalone one");
        mixed.Queue.GetTaskByCustomId("batch.two.a", ContextDefinition.SameContext)
            .Should().BeNull("batch membership, not distance, is what excludes a task in SameContext");
    }

    [Fact]
    public void GetTaskByCustomId_SameContextStrict_StopsAtTheFirstBatchWhenNothingIsExecuting()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetTaskByCustomId("loose.first", ContextDefinition.SameContextStrict)
            .Should().BeSameAs(mixed.LooseFirst, "loose.first sits before any batch boundary");
        mixed.Queue.GetTaskByCustomId("loose.middle", ContextDefinition.SameContextStrict)
            .Should().BeNull("batch.one separates loose.middle from the start of the scan, which ends the strict context");
        mixed.Queue.GetTaskByCustomId("loose.last", ContextDefinition.SameContextStrict)
            .Should().BeNull("the strict scan never gets past the first batch when there is no current task");
        mixed.Queue.GetTaskByCustomId("batch.one.a", ContextDefinition.SameContextStrict)
            .Should().BeNull("the strict scan breaks on the batch rather than descending into it");
    }

    [Fact]
    public void GetTaskByCustomId_WithCurrentStandaloneTask_StrictSpansBatchesBeforeTheCurrentTask()
    {
        var mixed = RunToStandaloneHold(BuildMixedQueue());

        mixed.Queue.GetTaskByCustomId("loose.first", ContextDefinition.SameContextStrict)
            .Should().BeSameAs(mixed.LooseFirst,
                "the strict scan always starts at index 0, so it returns tasks that are behind the current one and already finished");
        mixed.Queue.GetTaskByCustomId("loose.middle", ContextDefinition.SameContextStrict)
            .Should().BeSameAs(mixed.LooseMiddle, "the current task itself is in its own strict context");
        mixed.Queue.GetTaskByCustomId("loose.last", ContextDefinition.SameContextStrict)
            .Should().BeNull("batch.two sits after the current task, and that is the boundary strict mode honours");

        // batch.one precedes the current task, so the strict scan walks past it without breaking. Only a batch after
        // the current task ends the context.
        mixed.Queue.GetTaskByCustomId("batch.one.a", ContextDefinition.SameContextStrict)
            .Should().BeNull("walking past a batch never means looking inside it");
    }

    [Fact]
    public void GetTaskByCustomId_WhileInsideABatch_SameContextAndStrictSeeOnlyThatBatch()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        foreach (var mode in new[] { ContextDefinition.SameContext, ContextDefinition.SameContextStrict })
        {
            mixed.Queue.GetTaskByCustomId("batch.two.b", mode)
                .Should().BeSameAs(mixed.BatchTwoB, $"{mode} inside a batch searches that batch's task list");
            mixed.Queue.GetTaskByCustomId("loose.first", mode)
                .Should().BeNull($"{mode} inside a batch cannot see standalone tasks at all");
            mixed.Queue.GetTaskByCustomId("loose.last", mode)
                .Should().BeNull($"{mode} inside a batch cannot see standalone tasks at all");
            mixed.Queue.GetTaskByCustomId("batch.one.a", mode)
                .Should().BeNull($"{mode} inside a batch cannot see another batch");
        }

        mixed.Queue.GetTaskByCustomId("loose.first", ContextDefinition.CrossContext)
            .Should().BeSameAs(mixed.LooseFirst, "CrossContext is unaffected by which batch is processing");
    }

    [Fact]
    public void GetTaskBySystemId_MatchesTheCustomIdLookupInEveryMode()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetTaskBySystemId(mixed.BatchOneA.SystemId, ContextDefinition.CrossContext)
            .Should().BeSameAs(mixed.BatchOneA, "both entry points share one predicate-driven scan");
        mixed.Queue.GetTaskBySystemId(mixed.BatchOneA.SystemId, ContextDefinition.SameContext)
            .Should().BeNull("the context rules are applied to the scan, not to the kind of identifier");
        mixed.Queue.GetTaskBySystemId(mixed.LooseLast.SystemId, ContextDefinition.SameContextStrict)
            .Should().BeNull("the strict boundary applies to system id lookups identically");
    }

    [Fact]
    public void GetTaskByCustomId_UnknownContextDefinition_ReturnsNull()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetTaskByCustomId("loose.first", (ContextDefinition)99)
            .Should().BeNull("an unmapped enum value falls through to the default arm rather than to CrossContext");
    }

    [Fact]
    public void GetTaskByCustomId_UnknownId_ReturnsNullInEveryMode()
    {
        var mixed = BuildMixedQueue();

        foreach (var mode in new[] { ContextDefinition.CrossContext, ContextDefinition.SameContext, ContextDefinition.SameContextStrict })
        {
            mixed.Queue.GetTaskByCustomId("not.a.task", mode)
                .Should().BeNull($"{mode} reports a miss as null rather than throwing");
        }
    }

    #endregion

    #region GetTasksByPredicate family

    [Fact]
    public void GetTasksByPredicate_CrossContext_FlattensBatchTasksInPlace()
    {
        var mixed = BuildMixedQueue();

        var tasks = mixed.Queue.GetTasksByPredicate(_ => true, ContextDefinition.CrossContext);

        IdsOf(tasks).Should().Equal(
            new[] { "loose.first", "batch.one.a", "batch.one.b", "loose.middle", "batch.two.a", "batch.two.b", "loose.last" },
            "CrossContext walks the unified queue in order and splices each batch's tasks in at the batch's position");
    }

    [Fact]
    public void GetTasksByPredicate_SameContext_DropsEveryBatchTask()
    {
        var mixed = BuildMixedQueue();

        var tasks = mixed.Queue.GetTasksByPredicate(_ => true, ContextDefinition.SameContext);

        IdsOf(tasks).Should().Equal(
            new[] { "loose.first", "loose.middle", "loose.last" },
            "SameContext keeps every standalone task no matter how many batches sit between them");
    }

    [Fact]
    public void GetTasksByPredicate_SameContextStrict_StopsAtTheFirstBatch()
    {
        var mixed = BuildMixedQueue();

        var tasks = mixed.Queue.GetTasksByPredicate(_ => true, ContextDefinition.SameContextStrict);

        IdsOf(tasks).Should().Equal(
            new[] { "loose.first" },
            "with no current task the strict scan breaks on the first batch, so only the tasks before it survive");
    }

    [Fact]
    public void GetTasksByPredicate_WithCurrentStandaloneTask_StrictExtendsToTheBatchAfterIt()
    {
        var mixed = RunToStandaloneHold(BuildMixedQueue());

        var tasks = mixed.Queue.GetTasksByPredicate(_ => true, ContextDefinition.SameContextStrict);

        IdsOf(tasks).Should().Equal(
            new[] { "loose.first", "loose.middle" },
            "a batch only breaks the strict scan once it sits after the current task, and the scan still starts from index 0");
    }

    [Fact]
    public void GetTasksByPredicate_WhileInsideABatch_SameContextAndStrictReturnThatBatchInFull()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        IdsOf(mixed.Queue.GetTasksByPredicate(_ => true, ContextDefinition.SameContext))
            .Should().Equal(new[] { "batch.two.a", "batch.two.b" }, "SameContext inside a batch is exactly that batch's task list");
        IdsOf(mixed.Queue.GetTasksByPredicate(_ => true, ContextDefinition.SameContextStrict))
            .Should().Equal(new[] { "batch.two.a", "batch.two.b" }, "the two same-context modes are indistinguishable inside a batch");
        mixed.Queue.GetTasksByPredicate(_ => true, ContextDefinition.CrossContext)
            .Should().HaveCount(7, "CrossContext still returns the whole queue while a batch is processing");
    }

    [Fact]
    public void GetTasksByPredicate_NarrowingPredicate_IsAppliedWithinTheContextNotAcrossIt()
    {
        var mixed = BuildMixedQueue();

        bool StartsWithBatch(QueuedTask task) => task.CustomId?.StartsWith("batch") == true;

        mixed.Queue.GetTasksByPredicate(StartsWithBatch, ContextDefinition.CrossContext)
            .Should().HaveCount(4, "CrossContext sees every batch task across both batches");
        mixed.Queue.GetTasksByPredicate(StartsWithBatch, ContextDefinition.SameContext)
            .Should().BeEmpty("the context narrows the candidate set first, and it contains no batch tasks");
        mixed.Queue.GetTasksByPredicate(StartsWithBatch, ContextDefinition.SameContextStrict)
            .Should().BeEmpty("the strict candidate set is even smaller, so the predicate matches nothing");
    }

    [Fact]
    public void GetTasksByPredicate_UnknownContextDefinition_ReturnsEmpty()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetTasksByPredicate(_ => true, (ContextDefinition)99)
            .Should().BeEmpty("an unmapped enum value returns an empty list rather than falling back to CrossContext");
    }

    #endregion

    #region GetTasksByCustomId family

    /// <summary>
    /// A queue where one custom id repeats on both sides of a batch and inside it, so each mode returns a different
    /// subset of the same three matches.
    /// </summary>
    private sealed class DuplicateIdQueue
    {
        public NoireTaskQueue Queue = null!;
        public QueuedTask BeforeBatch = null!;
        public QueuedTask InsideBatch = null!;
        public QueuedTask AfterBatch = null!;
        public QueuedTask UnrelatedInBatch = null!;
        public TaskBatch Batch = null!;
    }

    private DuplicateIdQueue BuildDuplicateIdQueue()
    {
        var layout = new DuplicateIdQueue { Queue = MakeQueue() };

        layout.BeforeBatch = layout.Queue.EnqueueTask("dup", true, null, TaskCompletionCondition.Immediate());

        layout.InsideBatch = new QueuedTask("dup") { CompletionCondition = TaskCompletionCondition.Immediate() };
        layout.UnrelatedInBatch = new QueuedTask("other") { CompletionCondition = TaskCompletionCondition.Immediate() };
        layout.Batch = layout.Queue.CreateBatch("dup.batch")
            .AddTasks(layout.InsideBatch, layout.UnrelatedInBatch)
            .Enqueue();

        layout.AfterBatch = layout.Queue.EnqueueTask("dup", true, null, TaskCompletionCondition.Immediate());

        return layout;
    }

    [Fact]
    public void GetTasksByCustomId_CrossContext_ReturnsEveryMatchInQueueOrder()
    {
        var layout = BuildDuplicateIdQueue();

        layout.Queue.GetTasksByCustomId("dup", ContextDefinition.CrossContext)
            .Should().Equal(new[] { layout.BeforeBatch, layout.InsideBatch, layout.AfterBatch },
                "CrossContext collects standalone and batch matches alike, in unified queue order");
    }

    [Fact]
    public void GetTasksByCustomId_SameContext_SkipsTheInBatchMatchButKeepsBothStandalone()
    {
        var layout = BuildDuplicateIdQueue();

        layout.Queue.GetTasksByCustomId("dup", ContextDefinition.SameContext)
            .Should().Equal(new[] { layout.BeforeBatch, layout.AfterBatch },
                "SameContext keeps standalone matches on both sides of a batch and drops only the one inside it");
    }

    [Fact]
    public void GetTasksByCustomId_SameContextStrict_KeepsOnlyTheMatchBeforeTheBatch()
    {
        var layout = BuildDuplicateIdQueue();

        layout.Queue.GetTasksByCustomId("dup", ContextDefinition.SameContextStrict)
            .Should().Equal(new[] { layout.BeforeBatch },
                "the batch ends the strict scan, so the standalone match after it is unreachable");
    }

    [Fact]
    public void GetTasksByCustomId_WhileInsideABatch_ReturnsOnlyThatBatchesMatches()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        foreach (var mode in new[] { ContextDefinition.SameContext, ContextDefinition.SameContextStrict })
        {
            mixed.Queue.GetTasksByCustomId("batch.two.a", mode)
                .Should().Equal(new[] { mixed.BatchTwoA }, $"{mode} inside a batch matches only within that batch");
            mixed.Queue.GetTasksByCustomId("loose.first", mode)
                .Should().BeEmpty($"{mode} inside a batch cannot reach a standalone task");
        }
    }

    [Fact]
    public void GetTasksByCustomId_UnknownIdOrUnknownContext_ReturnsEmpty()
    {
        var layout = BuildDuplicateIdQueue();

        layout.Queue.GetTasksByCustomId("nothing", ContextDefinition.CrossContext)
            .Should().BeEmpty("a miss is an empty list rather than null");
        layout.Queue.GetTasksByCustomId("dup", (ContextDefinition)99)
            .Should().BeEmpty("an unmapped enum value returns empty rather than falling back to CrossContext");
    }

    #endregion

    #region GetAllTasks family

    [Fact]
    public void GetAllTasks_CrossContext_ReturnsTheWholeQueueFlattened()
    {
        var mixed = BuildMixedQueue();

        IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.CrossContext)).Should().Equal(
            new[] { "loose.first", "batch.one.a", "batch.one.b", "loose.middle", "batch.two.a", "batch.two.b", "loose.last" },
            "CrossContext is the only mode that reports the queue's full task population");
    }

    [Fact]
    public void GetAllTasks_SameContext_ReturnsStandaloneTasksAcrossBatchBoundaries()
    {
        var mixed = BuildMixedQueue();

        IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.SameContext)).Should().Equal(
            new[] { "loose.first", "loose.middle", "loose.last" },
            "batches in between are permitted, so all three standalone tasks stay in one context");
    }

    [Fact]
    public void GetAllTasks_SameContextStrict_StopsAtTheFirstBatch()
    {
        var mixed = BuildMixedQueue();

        IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.SameContextStrict)).Should().Equal(
            new[] { "loose.first" },
            "any batch boundary breaks the strict standalone context, and here the first one is at queue index 1");
    }

    [Fact]
    public void GetAllTasks_WithCurrentStandaloneTask_StrictReachesTheBatchAfterTheCurrentTask()
    {
        var mixed = RunToStandaloneHold(BuildMixedQueue());

        IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.SameContextStrict)).Should().Equal(
            new[] { "loose.first", "loose.middle" },
            "the current task moves the strict boundary forward to the next batch, and completed tasks before it are still reported");
        IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.SameContext)).Should().Equal(
            new[] { "loose.first", "loose.middle", "loose.last" },
            "SameContext is unaffected by where the current task sits");
    }

    [Fact]
    public void GetAllTasks_WhileInsideABatch_ReturnsThatBatchInBothSameContextModes()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.SameContext))
            .Should().Equal(new[] { "batch.two.a", "batch.two.b" }, "the processing batch replaces the standalone context entirely");
        IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.SameContextStrict))
            .Should().Equal(new[] { "batch.two.a", "batch.two.b" }, "strict and flexible collapse to the same answer inside a batch");
        mixed.Queue.GetAllTasks(ContextDefinition.CrossContext)
            .Should().HaveCount(7, "CrossContext ignores the processing batch");
    }

    [Fact]
    public void GetAllTasks_NoArgumentOverload_MatchesCrossContext()
    {
        var mixed = BuildMixedQueue();

        IdsOf(mixed.Queue.GetAllTasks())
            .Should().Equal(IdsOf(mixed.Queue.GetAllTasks(ContextDefinition.CrossContext)),
                "the parameterless overload delegates to CrossContext");
    }

    [Fact]
    public void GetAllTasks_UnknownContextDefinition_ReturnsEmpty()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetAllTasks((ContextDefinition)99)
            .Should().BeEmpty("an unmapped enum value returns empty rather than the whole queue");
    }

    #endregion

    #region Batch-boundary divergence across all four families

    [Fact]
    public void AllFourFamilies_AgreeOnATaskBeforeTheFirstBatch()
    {
        var mixed = BuildMixedQueue();

        foreach (var mode in new[] { ContextDefinition.CrossContext, ContextDefinition.SameContext, ContextDefinition.SameContextStrict })
        {
            mixed.Queue.GetTaskByCustomId("loose.first", mode)
                .Should().BeSameAs(mixed.LooseFirst, $"GetTask in {mode} reaches a task with no batch before it");
            mixed.Queue.GetTasksByPredicate(t => t.CustomId == "loose.first", mode)
                .Should().Equal(new[] { mixed.LooseFirst }, $"GetTasksByPredicate in {mode} reaches it too");
            mixed.Queue.GetTasksByCustomId("loose.first", mode)
                .Should().Equal(new[] { mixed.LooseFirst }, $"GetTasksByCustomId in {mode} reaches it too");
            mixed.Queue.GetAllTasks(mode)
                .Should().Contain(mixed.LooseFirst, $"GetAllTasks in {mode} includes it");
        }
    }

    [Fact]
    public void AllFourFamilies_DivergeOnAStandaloneTaskBehindABatch()
    {
        var mixed = BuildMixedQueue();

        // loose.last sits behind two batches. CrossContext and SameContext both reach it; SameContextStrict does not.
        mixed.Queue.GetTaskByCustomId("loose.last", ContextDefinition.CrossContext).Should().BeSameAs(mixed.LooseLast, "CrossContext ignores boundaries");
        mixed.Queue.GetTaskByCustomId("loose.last", ContextDefinition.SameContext).Should().BeSameAs(mixed.LooseLast, "SameContext permits batches in between");
        mixed.Queue.GetTaskByCustomId("loose.last", ContextDefinition.SameContextStrict).Should().BeNull("SameContextStrict permits none");

        mixed.Queue.GetTasksByPredicate(t => t.CustomId == "loose.last", ContextDefinition.SameContext).Should().Equal(new[] { mixed.LooseLast }, "SameContext permits batches in between");
        mixed.Queue.GetTasksByPredicate(t => t.CustomId == "loose.last", ContextDefinition.SameContextStrict).Should().BeEmpty("SameContextStrict permits none");

        mixed.Queue.GetTasksByCustomId("loose.last", ContextDefinition.SameContext).Should().Equal(new[] { mixed.LooseLast }, "SameContext permits batches in between");
        mixed.Queue.GetTasksByCustomId("loose.last", ContextDefinition.SameContextStrict).Should().BeEmpty("SameContextStrict permits none");

        mixed.Queue.GetAllTasks(ContextDefinition.SameContext).Should().Contain(mixed.LooseLast, "SameContext permits batches in between");
        mixed.Queue.GetAllTasks(ContextDefinition.SameContextStrict).Should().NotContain(mixed.LooseLast, "SameContextStrict permits none");
    }

    [Fact]
    public void AllFourFamilies_DivergeOnABatchTask()
    {
        var mixed = BuildMixedQueue();

        // Only CrossContext ever descends into a batch while the standalone context is the current one.
        mixed.Queue.GetTaskByCustomId("batch.one.b", ContextDefinition.CrossContext).Should().BeSameAs(mixed.BatchOneB, "CrossContext descends into batches");
        mixed.Queue.GetTaskByCustomId("batch.one.b", ContextDefinition.SameContext).Should().BeNull("SameContext never descends into a batch from a standalone context");
        mixed.Queue.GetTaskByCustomId("batch.one.b", ContextDefinition.SameContextStrict).Should().BeNull("SameContextStrict breaks on the batch instead");

        mixed.Queue.GetTasksByPredicate(t => t.CustomId == "batch.one.b", ContextDefinition.CrossContext).Should().Equal(new[] { mixed.BatchOneB }, "CrossContext descends into batches");
        mixed.Queue.GetTasksByPredicate(t => t.CustomId == "batch.one.b", ContextDefinition.SameContext).Should().BeEmpty("SameContext never descends into a batch from a standalone context");

        mixed.Queue.GetTasksByCustomId("batch.one.b", ContextDefinition.CrossContext).Should().Equal(new[] { mixed.BatchOneB }, "CrossContext descends into batches");
        mixed.Queue.GetTasksByCustomId("batch.one.b", ContextDefinition.SameContextStrict).Should().BeEmpty("SameContextStrict breaks on the batch instead");

        mixed.Queue.GetAllTasks(ContextDefinition.CrossContext).Should().Contain(mixed.BatchOneB, "CrossContext descends into batches");
        mixed.Queue.GetAllTasks(ContextDefinition.SameContext).Should().NotContain(mixed.BatchOneB, "SameContext never descends into a batch from a standalone context");
    }

    [Fact]
    public void AllFourFamilies_QueueThatStartsWithABatch_StrictReturnsNothing()
    {
        var queue = MakeQueue();
        var inBatch = new QueuedTask("first.batch.task") { CompletionCondition = TaskCompletionCondition.Immediate() };
        queue.CreateBatch("leading.batch").AddTasks(inBatch).Enqueue();
        var loose = queue.EnqueueTask("trailing.loose", true, null, TaskCompletionCondition.Immediate());

        queue.GetAllTasks(ContextDefinition.SameContextStrict)
            .Should().BeEmpty("the scan breaks on the batch at index 0, so the strict context is empty before it starts");
        queue.GetTaskByCustomId("trailing.loose", ContextDefinition.SameContextStrict)
            .Should().BeNull("nothing is reachable strictly when the queue leads with a batch");
        queue.GetTasksByCustomId("first.batch.task", ContextDefinition.SameContextStrict)
            .Should().BeEmpty("breaking on the batch is not the same as searching it");

        queue.GetAllTasks(ContextDefinition.SameContext)
            .Should().Equal(new[] { loose }, "SameContext still reports the standalone task behind the leading batch");
        queue.GetAllTasks(ContextDefinition.CrossContext)
            .Should().Equal(new[] { inBatch, loose }, "CrossContext reports both");
    }

    #endregion

    #region Same-context predicates (separate from the query families)

    // The five methods below answer "is this task in the same context as the current one" or "how many/how far",
    // rather than "iterate the tasks that are". They share the ContextDefinition enum and nothing else: their strict
    // boundary is computed from a different starting point than the query families', as the tests here show.

    [Fact]
    public void AreTasksInSameContext_CrossContext_IsAlwaysTrue()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseFirst, ContextDefinition.CrossContext)
            .Should().BeTrue("CrossContext short circuits to true without inspecting the queue at all");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.BatchOneA, ContextDefinition.CrossContext)
            .Should().BeTrue("not even a foreign batch makes CrossContext say no");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseLast, (ContextDefinition)99)
            .Should().BeTrue("the default arm of this switch returns true, unlike the query families which return empty");
    }

    [Fact]
    public void AreTasksInSameContext_WithNothingExecuting_SameContextAcceptsEvenBatchTasks()
    {
        var mixed = BuildMixedQueue();

        InvokeAreTasksInSameContext(mixed.Queue, mixed.BatchOneA, ContextDefinition.SameContext)
            .Should().BeTrue("with neither a current task nor a current batch, the flexible check returns true unconditionally");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseLast, ContextDefinition.SameContext)
            .Should().BeTrue("the same unconditional true covers standalone tasks");

        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseFirst, ContextDefinition.SameContextStrict)
            .Should().BeTrue("strict scans from the queue head and finds loose.first before any batch");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseMiddle, ContextDefinition.SameContextStrict)
            .Should().BeFalse("batch.one is reached first, which ends the strict context");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.BatchOneA, ContextDefinition.SameContextStrict)
            .Should().BeFalse("a task with a parent batch is rejected outright");
    }

    [Fact]
    public void AreTasksInSameContext_WithCurrentStandaloneTask_StrictRejectsEvenTheCurrentTaskItself()
    {
        var mixed = RunToStandaloneHold(BuildMixedQueue());

        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseFirst, ContextDefinition.SameContext)
            .Should().BeTrue("the flexible check only asks whether the target is standalone, not where it sits");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.BatchTwoA, ContextDefinition.SameContext)
            .Should().BeFalse("a batch task is not standalone");

        // The strict scan starts looking only after it has passed the current task, and batch.two is the very next
        // item, so nothing at all qualifies here, including loose.middle which is the current task.
        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseMiddle, ContextDefinition.SameContextStrict)
            .Should().BeFalse("the current task is consumed by the scan rather than matched, so it is not in its own strict context");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseFirst, ContextDefinition.SameContextStrict)
            .Should().BeFalse("a task before the current one is skipped, unlike in the query families which return it");
        InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseLast, ContextDefinition.SameContextStrict)
            .Should().BeFalse("batch.two sits between the current task and loose.last");
    }

    [Fact]
    public void AreTasksInSameContext_WhileInsideABatch_RequiresTheSameBatch()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        foreach (var mode in new[] { ContextDefinition.SameContext, ContextDefinition.SameContextStrict })
        {
            InvokeAreTasksInSameContext(mixed.Queue, mixed.BatchTwoB, mode)
                .Should().BeTrue($"{mode} inside a batch accepts a sibling task");
            InvokeAreTasksInSameContext(mixed.Queue, mixed.BatchOneA, mode)
                .Should().BeFalse($"{mode} inside a batch rejects another batch's task");
            InvokeAreTasksInSameContext(mixed.Queue, mixed.LooseLast, mode)
                .Should().BeFalse($"{mode} inside a batch rejects a standalone task");
        }
    }

    [Fact]
    public void GetPendingTaskCount_WithNothingExecuting_CountsPerMode()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetPendingTaskCount(ContextDefinition.CrossContext)
            .Should().Be(7, "every task in the queue, batch tasks included, is still Queued");
        mixed.Queue.GetPendingTaskCount(ContextDefinition.SameContext)
            .Should().Be(3, "only the standalone tasks count, across every batch boundary");
        mixed.Queue.GetPendingTaskCount(ContextDefinition.SameContextStrict)
            .Should().Be(1, "the count stops at the first batch");
    }

    [Fact]
    public void GetPendingTaskCount_WithCurrentStandaloneTask_CountsWithinTheStrictContext()
    {
        var mixed = RunToStandaloneHold(BuildMixedQueue());

        mixed.Queue.GetPendingTaskCount(ContextDefinition.CrossContext)
            .Should().Be(3, "batch.two's two tasks and loose.last are the only ones still Queued");
        mixed.Queue.GetPendingTaskCount(ContextDefinition.SameContext)
            .Should().Be(1, "loose.last is the only standalone task still Queued");

        // batch.one sits behind the current task, so it no longer closes the context; the walk reaches loose.middle
        // and then stops at batch.two. Nothing in that span is still Queued, so this is zero either way. The shape
        // that actually distinguishes the boundary is covered by
        // StrictBoundary_IsTheSameForQueriesAndCounters_WhenABatchSitsBehindTheCurrentTask.
        mixed.Queue.GetPendingTaskCount(ContextDefinition.SameContextStrict)
            .Should().Be(0, "loose.first is Completed and loose.middle is executing, so the strict span holds no Queued task");
    }

    [Fact]
    public void StrictBoundary_IsTheSameForQueriesAndCounters_WhenABatchSitsBehindTheCurrentTask()
    {
        // A batch behind the executing task does not close the strict context; the first batch ahead of it does. This
        // queue is shaped to expose that: a still-Queued standalone task sits between the current task and the batch
        // ahead, so a scan that stopped at the batch behind would miss it and report a smaller context. The queries
        // and the boundary-check helpers agree on this boundary.
        var queue = MakeQueue();
        var gate = false;

        queue.EnqueueTask("before", true, null, TaskCompletionCondition.Immediate());
        queue.CreateBatch("batch.behind")
            .AddTasks(new QueuedTask("behind.a") { CompletionCondition = TaskCompletionCondition.Immediate() })
            .Enqueue();
        var current = queue.EnqueueTask("current", true, null, TaskCompletionCondition.FromPredicate(() => gate));
        var after = queue.EnqueueTask("after", true, null, TaskCompletionCondition.Immediate());
        queue.CreateBatch("batch.ahead")
            .AddTasks(new QueuedTask("ahead.a") { CompletionCondition = TaskCompletionCondition.Immediate() })
            .Enqueue();
        queue.EnqueueTask("beyond", true, null, TaskCompletionCondition.Immediate());

        queue.StartQueue();
        StepUntil(queue, () => ReferenceEquals(queue.GetCurrentTask(), current))
            .Should().BePositive("the assertions below describe the state with 'current' executing");

        IdsOf(queue.GetAllTasks(ContextDefinition.SameContextStrict))
            .Should().Equal(new[] { "before", "current", "after" },
                "the strict context runs from the head of the queue to the first batch ahead of the current task, so the batch behind it is not a boundary");

        queue.GetPendingTaskCount(ContextDefinition.SameContextStrict)
            .Should().Be(1, "'after' is the only still-Queued task inside that same span, and the counter uses the boundary the query uses");

        queue.GetTaskDepth(after, ContextDefinition.SameContextStrict)
            .Should().Be(0, "depth is measured in that same span, so the task immediately after the current one is at depth zero");
    }

    [Fact]
    public void GetPendingTaskCount_WhileInsideABatch_CountsOnlyThatBatch()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        mixed.Queue.GetPendingTaskCount(ContextDefinition.SameContext)
            .Should().Be(1, "batch.two.b is the only Queued task left in the processing batch");
        mixed.Queue.GetPendingTaskCount(ContextDefinition.SameContextStrict)
            .Should().Be(1, "both same-context modes count the processing batch identically");
        mixed.Queue.GetPendingTaskCount(ContextDefinition.CrossContext)
            .Should().Be(2, "batch.two.b and loose.last are Queued queue wide");
    }

    [Fact]
    public void GetTaskDepth_WithNothingExecuting_CountsPerMode()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.GetTaskDepth(mixed.LooseFirst, ContextDefinition.CrossContext).Should().Be(0, "the head of the queue is at depth zero in every mode");
        mixed.Queue.GetTaskDepth(mixed.BatchOneB, ContextDefinition.CrossContext).Should().Be(2, "CrossContext counts batch tasks as it walks");
        mixed.Queue.GetTaskDepth(mixed.LooseMiddle, ContextDefinition.CrossContext).Should().Be(3, "the two batch.one tasks sit between loose.first and loose.middle");
        mixed.Queue.GetTaskDepth(mixed.LooseLast, ContextDefinition.CrossContext).Should().Be(6, "six queued tasks precede loose.last");

        mixed.Queue.GetTaskDepth(mixed.LooseFirst, ContextDefinition.SameContext).Should().Be(0, "the standalone walk skips batches without counting them");
        mixed.Queue.GetTaskDepth(mixed.LooseMiddle, ContextDefinition.SameContext).Should().Be(1, "only loose.first precedes it in the standalone context");
        mixed.Queue.GetTaskDepth(mixed.LooseLast, ContextDefinition.SameContext).Should().Be(2, "batches in between are not counted and not a boundary");
        mixed.Queue.GetTaskDepth(mixed.BatchOneA, ContextDefinition.SameContext).Should().BeNull("a task with a parent batch has no standalone depth");

        mixed.Queue.GetTaskDepth(mixed.LooseFirst, ContextDefinition.SameContextStrict).Should().Be(0, "loose.first is before the first batch");
        mixed.Queue.GetTaskDepth(mixed.LooseMiddle, ContextDefinition.SameContextStrict).Should().BeNull("the strict walk breaks on batch.one");
        mixed.Queue.GetTaskDepth(mixed.LooseLast, ContextDefinition.SameContextStrict).Should().BeNull("the strict walk never reaches loose.last");
    }

    [Fact]
    public void GetTaskDepth_WhileInsideABatch_IsMeasuredWithinThatBatch()
    {
        var mixed = RunToBatchHold(BuildMixedQueue());

        foreach (var mode in new[] { ContextDefinition.SameContext, ContextDefinition.SameContextStrict })
        {
            mixed.Queue.GetTaskDepth(mixed.BatchTwoB, mode)
                .Should().Be(0, $"{mode} measures from the executing task inside the batch, and nothing sits between them");
            mixed.Queue.GetTaskDepth(mixed.BatchTwoA, mode)
                .Should().BeNull($"{mode} returns null for the executing task itself rather than zero");
            mixed.Queue.GetTaskDepth(mixed.LooseLast, mode)
                .Should().BeNull($"{mode} rejects a target outside the processing batch");
            mixed.Queue.GetTaskDepth(mixed.BatchOneA, mode)
                .Should().BeNull($"{mode} rejects a target in another batch");
        }
    }

    [Fact]
    public void SkipNextTasks_CrossContext_SkipsIntoEveryBatch()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.SkipNextTasks(100, false, ContextDefinition.CrossContext)
            .Should().Be(7, "CrossContext cancels every Queued task in the queue, batch tasks included");
        mixed.Queue.GetAllTasks(ContextDefinition.CrossContext)
            .Should().AllSatisfy(task => task.Status.Should().Be(TaskStatus.Cancelled), "each skipped task ends up Cancelled");
    }

    [Fact]
    public void SkipNextTasks_SameContext_SkipsStandaloneTasksAcrossBatchBoundaries()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.SkipNextTasks(100, false, ContextDefinition.SameContext)
            .Should().Be(3, "the three standalone tasks are skipped and the batches are stepped over");
        mixed.BatchOneA.Status.Should().Be(TaskStatus.Queued, "a batch task is a different context and is left alone");
        mixed.LooseLast.Status.Should().Be(TaskStatus.Cancelled, "a standalone task behind two batches is still in context");
    }

    [Fact]
    public void SkipNextTasks_SameContextStrict_StopsAtTheFirstBatch()
    {
        var mixed = BuildMixedQueue();

        mixed.Queue.SkipNextTasks(100, false, ContextDefinition.SameContextStrict)
            .Should().Be(1, "the walk breaks on batch.one, so only loose.first is skipped");
        mixed.LooseFirst.Status.Should().Be(TaskStatus.Cancelled);
        mixed.LooseMiddle.Status.Should().Be(TaskStatus.Queued, "the strict boundary protects everything behind the first batch");
    }

    [Fact]
    public void SkipNextTasks_WhileInsideABatch_SameContextStaysInTheBatchAndCrossContextDoesNot()
    {
        var sameContext = RunToBatchHold(BuildMixedQueue());

        sameContext.Queue.SkipNextTasks(100, false, ContextDefinition.SameContext)
            .Should().Be(1, "only batch.two.b is still Queued inside the processing batch");
        sameContext.LooseLast.Status.Should().Be(TaskStatus.Queued, "the same-context skip never leaves the batch");

        var crossContext = RunToBatchHold(BuildMixedQueue());

        crossContext.Queue.SkipNextTasks(100, false, ContextDefinition.CrossContext)
            .Should().Be(2, "CrossContext skips the rest of the batch and then continues through the queue");
        crossContext.LooseLast.Status.Should().Be(TaskStatus.Cancelled, "the standalone task behind the batch is skipped too");
    }

    #endregion
}
