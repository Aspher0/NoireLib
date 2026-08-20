using NoireLib.Helpers.ObjectExtensions;
using NoireLib.Internal.Helpers;
using NoireLib.Models;
using System;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Executes an action after a number of game frames, unless cancelled first. Useful when the wait is about the
/// game having drawn or updated a given number of times rather than about wall-clock time, so a stutter or a
/// loading screen does not fire it early.<br/>
/// Each trigger is independent, with its own delay.<br/>
/// Prefer <see cref="FrameDelayerHelper"/> unless you need this directly; if you use it, remember to call
/// <see cref="DelayerBase{TTrigger}.Dispose"/>. The frame twin of <see cref="Delayer"/>.
/// </summary>
public class FrameDelayer : DelayerBase<FrameDelayedTrigger>
{
    /// <summary>
    /// Creates a new frame delayer instance.
    /// </summary>
    public FrameDelayer() { }

    /// <inheritdoc/>
    protected override long CurrentTick => FrameClock.Current;

    /// <summary>
    /// Starts a delayed trigger that will execute the action after the specified number of frames unless cancelled.
    /// </summary>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    public FrameDelayedTrigger StartAsync(long frames, Action action)
    {
        action.ThrowIfNull(nameof(action));
        ThrowIfInvalidFrames(frames);

        return Schedule(frames, action, null, null, null, false);
    }

    /// <summary>
    /// Starts a delayed trigger that will execute the asynchronous action after the specified number of frames unless cancelled.
    /// </summary>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    public FrameDelayedTrigger StartAsync(long frames, Func<Task> action)
    {
        action.ThrowIfNull(nameof(action));
        ThrowIfInvalidFrames(frames);

        return Schedule(frames, null, action, null, null, false);
    }

    /// <summary>
    /// Starts a delayed trigger with a condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// </summary>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="cancelCondition">A callback that determines if the action should cancel.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    public FrameDelayedTrigger? StartAsync(long frames, Action action, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));
        ThrowIfInvalidFrames(frames);

        if (immediatelyCancelOnConditionMet && cancelCondition())
            return null;

        return Schedule(frames, action, null, cancelCondition, null, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Starts a delayed trigger with an asynchronous condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// </summary>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="cancelCondition">An asynchronous function that determines if the action should execute. Called after the delay period.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    public async Task<FrameDelayedTrigger?> StartAsync(long frames, Func<Task> action, Func<Task<bool>> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));
        ThrowIfInvalidFrames(frames);

        if (immediatelyCancelOnConditionMet && await cancelCondition())
            return null;

        return Schedule(frames, null, action, null, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Starts a delayed trigger without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// </summary>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    public FrameDelayedTrigger Start(long frames, Action action)
    {
        return StartAsync(frames, action);
    }

    /// <summary>
    /// Starts a delayed trigger with a condition without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// </summary>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="cancelCondition">A function that determines if the action should execute.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    public FrameDelayedTrigger? Start(long frames, Action action, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        return StartAsync(frames, action, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Gets how many game frames are left before a specific trigger will execute.
    /// </summary>
    /// <param name="trigger">The FrameDelayedTrigger instance.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled frame has passed; otherwise returns 0.</param>
    /// <returns>The remaining frames, or 0 if the trigger is not found or has none remaining (when allowNegative is false).</returns>
    public double GetRemainingFrames(FrameDelayedTrigger? trigger, bool allowNegative = false)
    {
        if (trigger == null)
            return 0;

        return GetRemaining(trigger.UniqueId, allowNegative);
    }

    /// <summary>
    /// Gets how many game frames are left before the next trigger will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled frame has passed; otherwise returns 0.</param>
    /// <returns>The remaining frames, or 0 if no trigger is pending (when allowNegative is false).</returns>
    public double GetNextRemainingFrames(bool allowNegative = false)
    {
        return GetNextRemaining(allowNegative);
    }

    private static void ThrowIfInvalidFrames(long frames)
    {
        if (frames < 1)
            throw new ArgumentException("Frame count must be at least one.", nameof(frames));
    }
}
