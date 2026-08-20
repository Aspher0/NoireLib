using NoireLib.Helpers.ObjectExtensions;
using NoireLib.Internal.Helpers;
using NoireLib.Models;
using System;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Executes an action after a delay, unless cancelled first. Useful for a loading indicator or timeout handler
/// that should be skipped if the primary operation finishes quickly.<br/>
/// Each trigger is independent, with its own delay.<br/>
/// Prefer <see cref="DelayerHelper"/> unless you need this directly; if you use it, remember to call <see cref="Dispose"/>.
/// </summary>
public class Delayer : DelayerBase<DelayedTrigger>
{
    /// <summary>
    /// Creates a new delayed trigger instance.
    /// </summary>
    public Delayer() { }

    /// <inheritdoc/>
    protected override long CurrentTick => Environment.TickCount64;

    /// <summary>
    /// Starts a delayed trigger that will execute the action after the specified delay unless cancelled.
    /// </summary>
    /// <param name="delay">The delay before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger StartAsync(TimeSpan delay, Action action)
    {
        action.ThrowIfNull(nameof(action));
        ThrowIfInvalidDelay(delay);

        return Schedule(Ticks(delay), action, null, null, null, false);
    }

    /// <summary>
    /// Starts a delayed trigger that will execute the asynchronous action after the specified delay unless cancelled.
    /// </summary>
    /// <param name="delay">The delay before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger StartAsync(TimeSpan delay, Func<Task> action)
    {
        action.ThrowIfNull(nameof(action));
        ThrowIfInvalidDelay(delay);

        return Schedule(Ticks(delay), null, action, null, null, false);
    }

    /// <summary>
    /// Starts a delayed trigger with a condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// </summary>
    /// <param name="delay">The delay before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="cancelCondition">A callback that determines if the action should cancel.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger? StartAsync(TimeSpan delay, Action action, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));
        ThrowIfInvalidDelay(delay);

        if (immediatelyCancelOnConditionMet && cancelCondition())
            return null;

        return Schedule(Ticks(delay), action, null, cancelCondition, null, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Starts a delayed trigger with an asynchronous condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// </summary>
    /// <param name="delay">The delay before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="cancelCondition">An asynchronous function that determines if the action should execute. Called after the delay period.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public async Task<DelayedTrigger?> StartAsync(TimeSpan delay, Func<Task> action, Func<Task<bool>> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));
        ThrowIfInvalidDelay(delay);

        if (immediatelyCancelOnConditionMet && await cancelCondition())
            return null;

        return Schedule(Ticks(delay), null, action, null, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Starts a delayed trigger without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// </summary>
    /// <param name="delay">The delay before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger Start(TimeSpan delay, Action action)
    {
        return StartAsync(delay, action);
    }

    /// <summary>
    /// Starts a delayed trigger with a condition without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// </summary>
    /// <param name="delay">The delay before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="cancelCondition">A function that determines if the action should execute.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger? Start(TimeSpan delay, Action action, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        return StartAsync(delay, action, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before a specific trigger will execute.
    /// </summary>
    /// <param name="trigger">The DelayedTrigger instance.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if the trigger is not found or has no time remaining (when allowNegative is false).</returns>
    public double GetRemainingTime(DelayedTrigger? trigger, bool allowNegative = false)
    {
        if (trigger == null)
            return 0;

        return GetRemaining(trigger.UniqueId, allowNegative);
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the next trigger will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if no trigger is pending (when allowNegative is false).</returns>
    public double GetNextRemainingTime(bool allowNegative = false)
    {
        return GetNextRemaining(allowNegative);
    }

    private static long Ticks(TimeSpan delay) => (long)delay.TotalMilliseconds;

    private static void ThrowIfInvalidDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delay));
    }
}
