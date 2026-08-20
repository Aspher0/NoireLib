using NoireLib.Internal.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Models;

/// <summary>
/// One scheduled execution, with the state its delayer keeps for it and the questions it can answer about itself.
/// The unit the delay is counted in belongs to the delayer, so the remaining amount is exposed by the derived
/// trigger rather than here.
/// </summary>
public abstract class DelayedTriggerBase
{
    internal Guid UniqueId { get; set; } = Guid.NewGuid();
    internal Action? Action { get; set; }
    internal Func<Task>? AsyncAction { get; set; }
    internal Func<bool>? Condition { get; set; }
    internal Func<Task<bool>>? AsyncCondition { get; set; }
    internal bool CheckConditionImmediately { get; set; }
    internal long ScheduledTick { get; set; }
    internal CancellationTokenSource Cts { get; set; } = new();
    internal IDelayerHost? Host { get; set; }

    /// <summary>
    /// Gets whether this trigger has been cancelled.
    /// </summary>
    public bool IsCancelled => Cts.IsCancellationRequested;

    /// <summary>
    /// Gets whether this trigger is still pending execution.
    /// </summary>
    public bool IsRunning => Host?.IsRunning(UniqueId) ?? false;

    /// <summary>
    /// Cancels this trigger execution.
    /// </summary>
    /// <returns>True if the trigger was successfully cancelled, false if it was already cancelled or completed.</returns>
    public bool Cancel()
    {
        return Host?.Cancel(UniqueId) ?? false;
    }

    /// <summary>
    /// Gets the unique identifier for this trigger.
    /// </summary>
    /// <returns>The trigger's id.</returns>
    public Guid GetId() => UniqueId;

    /// <summary>
    /// Gets how much of the delay is left, in the delayer's own unit.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled tick has passed; otherwise returns 0.</param>
    /// <returns>The remaining amount, or 0 when the trigger is no longer pending.</returns>
    protected double GetRemainingCore(bool allowNegative)
    {
        if (IsCancelled)
            return 0;

        return Host?.GetRemaining(UniqueId, allowNegative) ?? 0;
    }
}
