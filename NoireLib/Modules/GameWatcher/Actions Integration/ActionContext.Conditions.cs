using NoireLib.GameWatcher;
using System;
using System.Threading.Tasks;

namespace NoireLib.Actions;

/// <summary>
/// The game-watcher side of <see cref="ActionContext"/>: it lets an action wait on the same
/// <see cref="GameCondition"/> vocabulary the watcher already exposes, instead of on a bare predicate.<br/>
/// <see cref="GameConditions"/> holds the prebuilt conditions, combinable with
/// <see cref="GameCondition.And"/>, <see cref="GameCondition.Or"/> and <see cref="GameCondition.Not"/> into
/// a single value.<br/>
/// This is additive: the action layer works without it, and gains these waits because the watcher is present.
/// </summary>
public sealed partial class ActionContext
{
    /// <summary>
    /// Waits until a game condition holds. A dry run satisfies it at once.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="seconds">The most seconds to wait; 0 uses <see cref="ActionOptions.DefaultWaitTimeout"/>.</param>
    /// <returns>
    /// True when the condition held; false on timeout. Timing out is normal here, not an error, so
    /// <b>the result has to be used</b>: discarding it lets a wait that never held pass unnoticed and the action
    /// still report success. Use <see cref="Require(GameCondition, double, string)"/> when a timeout should end
    /// the action instead.
    /// </returns>
    public Task<bool> WaitUntil(GameCondition condition, double seconds = 0)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (IsDryRun)
            return Task.FromResult(true);

        if (!NoireService.IsInitialized())
            return Task.FromResult(condition.IsMet());

        var timeout = TimeSpan.FromSeconds(seconds > 0 ? seconds : Options.DefaultWaitTimeout);
        return condition.WaitAsync(timeout, CancellationToken);
    }

    /// <summary>
    /// Waits until a game condition holds and fails the action when it does not.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="seconds">The most seconds to wait.</param>
    /// <param name="message">What to report when the condition never held.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="ActionException">Thrown with <see cref="FailureReason.Timeout"/> when it does not hold in time.</exception>
    public async Task Require(GameCondition condition, double seconds, string message)
    {
        if (!await WaitUntil(condition, seconds))
            throw new ActionException(FailureReason.Timeout, message);
    }

    /// <summary>
    /// Waits for the next event of a given type - edge-triggered ("it just happened"), not level-triggered.
    /// A dry run returns null at once.
    /// </summary>
    /// <typeparam name="TEvent">The event type to wait for.</typeparam>
    /// <param name="watcher">The watcher whose events are awaited.</param>
    /// <param name="filter">An optional filter the event must satisfy.</param>
    /// <param name="seconds">The most seconds to wait; 0 uses <see cref="ActionOptions.DefaultWaitTimeout"/>.</param>
    /// <returns>The matching event, or null on timeout.</returns>
    public Task<TEvent?> WaitFor<TEvent>(NoireGameWatcher watcher, Func<TEvent, bool>? filter = null, double seconds = 0)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(watcher);

        if (IsDryRun)
            return Task.FromResult<TEvent?>(null);

        var timeout = TimeSpan.FromSeconds(seconds > 0 ? seconds : Options.DefaultWaitTimeout);
        return watcher.WaitFor(filter, timeout, CancellationToken);
    }
}
