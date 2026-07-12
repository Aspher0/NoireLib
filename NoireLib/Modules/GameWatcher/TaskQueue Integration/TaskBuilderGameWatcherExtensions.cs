using NoireLib.TaskQueue;
using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// The bridge between the game watcher and <see cref="NoireTaskQueue"/> — additive extension methods only,
/// the queue's own API stays untouched.<br/>
/// <c>CompleteWhen</c> gates a task's completion on any <see cref="GameCondition"/>;
/// <c>CompleteOnGameEvent</c> completes it on the next matching watcher event (library or custom).
/// Neither requires an EventBus.
/// </summary>
public static class TaskBuilderGameWatcherExtensions
{
    /// <summary>
    /// Completes the task when a game condition holds — e.g.
    /// <c>.CompleteWhen(GameConditions.TerritoryIs(198).And(GameConditions.ScreenReady))</c>.
    /// The condition is evaluated by the queue on the framework thread.
    /// </summary>
    /// <typeparam name="TSelf">The concrete builder type (inferred).</typeparam>
    /// <param name="builder">The task builder.</param>
    /// <param name="condition">The condition to gate completion on.</param>
    /// <returns>The builder instance for chaining.</returns>
    public static TSelf CompleteWhen<TSelf>(this TaskBuilderBase<TSelf> builder, GameCondition condition)
        where TSelf : TaskBuilderBase<TSelf>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(condition);

        return builder.WithCondition(condition.IsMet);
    }

    /// <summary>
    /// Completes the task when the next matching watcher event fires — a fresh latch per call, so retried or
    /// re-enqueued tasks never complete against a stale match.<br/>
    /// By default the latch arms when the queue first evaluates the completion condition; pass
    /// <paramref name="armImmediately"/> to start capturing at enqueue time (the event may then precede the
    /// task's execution). Works with custom events published via
    /// <see cref="NoireGameWatcher.Publish{TEvent}"/> too — no EventBus involved.
    /// </summary>
    /// <typeparam name="TEvent">The watcher event type to wait for.</typeparam>
    /// <param name="builder">The task builder.</param>
    /// <param name="watcher">The watcher whose events complete the task.</param>
    /// <param name="filter">An optional filter the event must satisfy.</param>
    /// <param name="armImmediately">True to start capturing at enqueue time.</param>
    /// <returns>The builder instance for chaining.</returns>
    public static TaskBuilder CompleteOnGameEvent<TEvent>(
        this TaskBuilder builder,
        NoireGameWatcher watcher,
        Func<TEvent, bool>? filter = null,
        bool armImmediately = false)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(watcher);

        var latch = GameConditions.FromEvent(watcher, filter, armImmediately);
        return builder.WithCondition(latch.IsMet);
    }

    /// <inheritdoc cref="CompleteOnGameEvent{TEvent}(TaskBuilder, NoireGameWatcher, Func{TEvent, bool}?, bool)"/>
    /// <typeparam name="TModule">The queue module type of the typed builder.</typeparam>
    /// <typeparam name="TEvent">The watcher event type to wait for.</typeparam>
    public static TaskBuilder<TModule> CompleteOnGameEvent<TModule, TEvent>(
        this TaskBuilder<TModule> builder,
        NoireGameWatcher watcher,
        Func<TEvent, bool>? filter = null,
        bool armImmediately = false)
        where TModule : NoireTaskQueue
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(watcher);

        var latch = GameConditions.FromEvent(watcher, filter, armImmediately);
        return builder.WithCondition(latch.IsMet);
    }
}
