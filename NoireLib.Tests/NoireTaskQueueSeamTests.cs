using FluentAssertions;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Tests for the seam that makes the task queue drivable without a running game.<br/>
/// Processing is otherwise reachable only from the framework update, so these pin the two things the rest of the
/// TaskQueue suite depends on: that a queue can be activated while NoireLib is not initialized, and that
/// <see cref="NoireTaskQueue.TickOnce"/> runs the same processing pass a frame runs.<br/>
/// This is also the harness pattern the other TaskQueue test classes follow: build inactive and unlogged,
/// activate, enqueue, start, then step the queue one tick at a time and assert what each step did.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireTaskQueueSeamTests : IDisposable
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
    /// Steps the queue until the predicate holds or the tick budget runs out, and reports the ticks used.<br/>
    /// A budget rather than a loop-until-true, so a characterization test that never settles fails as an
    /// assertion instead of hanging the suite.
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

    [Fact]
    public void Activate_WithoutNoireLibInitialized_DoesNotThrow()
    {
        var queue = new NoireTaskQueue(moduleId: null, active: false, enableLogging: false);
        queuesToClean.Add(queue);

        var act = () => queue.Activate();

        act.Should().NotThrow("processing is driven by the framework update, which simply cannot be wired up before NoireLib is initialized");
        queue.IsActive.Should().BeTrue("the active state is recorded even though nothing is wired");
    }

    [Fact]
    public void Deactivate_WithoutNoireLibInitialized_DoesNotThrow()
    {
        var queue = MakeQueue();

        var act = () => queue.Deactivate();

        act.Should().NotThrow("there is no framework to detach a handler from that was never attached");
        queue.IsActive.Should().BeFalse();
    }

    [Fact]
    public void StartQueue_RequiresAnActiveModule()
    {
        var queue = new NoireTaskQueue(moduleId: null, active: false, enableLogging: false);
        queuesToClean.Add(queue);
        queue.EnqueueTask("inactive.task", false, () => { }, TaskCompletionCondition.Immediate());

        queue.StartQueue();

        queue.QueueState.Should().NotBe(QueueState.Running, "an inactive module refuses to start, which is why the harness activates first");
    }

    [Fact]
    public void TickOnce_WhileNotRunning_DoesNothing()
    {
        var queue = MakeQueue();
        var executed = 0;
        queue.EnqueueTask("idle.task", false, () => executed++, TaskCompletionCondition.Immediate());

        // The queue was never started, so it is Idle rather than Running.
        queue.TickOnce();

        executed.Should().Be(0, "the queue state gate belongs to processing, so a tick obeys it exactly as a frame does");
    }

    [Fact]
    public void TickOnce_DrivesAnEnqueuedTaskToCompletion()
    {
        var queue = MakeQueue();
        var executed = 0;
        var task = queue.EnqueueTask("smoke.task", false, () => executed++, TaskCompletionCondition.Immediate());

        queue.StartQueue();
        queue.QueueState.Should().Be(QueueState.Running);

        var ticks = StepUntil(queue, () => task.Status == TaskStatus.Completed);

        ticks.Should().BePositive("stepping the queue must drive a task to completion without a running game");
        executed.Should().Be(1, "the task's action runs exactly once");
    }

    [Fact]
    public void TickOnce_DrivesTasksInOrder()
    {
        var queue = MakeQueue();
        var order = new List<string>();

        queue.EnqueueTask("first", false, () => order.Add("first"), TaskCompletionCondition.Immediate());
        var last = queue.EnqueueTask("second", false, () => order.Add("second"), TaskCompletionCondition.Immediate());

        queue.StartQueue();
        StepUntil(queue, () => last.Status == TaskStatus.Completed).Should().BePositive();

        order.Should().Equal("first", "second");
    }

    [Fact]
    public void TickOnce_HonorsAConditionThatIsNotYetMet()
    {
        var queue = MakeQueue();
        var ready = false;
        var task = queue.EnqueueTask("gated.task", false, () => { }, TaskCompletionCondition.FromPredicate(() => ready));

        queue.StartQueue();

        // The condition is false, so no number of ticks completes it.
        for (var i = 0; i < 5; i++)
            queue.TickOnce();

        task.Status.Should().NotBe(TaskStatus.Completed, "a task waits for its completion condition rather than completing on the next tick");

        ready = true;
        StepUntil(queue, () => task.Status == TaskStatus.Completed).Should().BePositive("once the condition holds, a further tick completes the task");
    }
}
