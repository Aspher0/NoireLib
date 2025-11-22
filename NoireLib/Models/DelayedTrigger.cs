using NoireLib.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Models;

/// <summary>
/// Represents a single trigger execution with methods to cancel, check status, and get remaining time.
/// </summary>
public class DelayedTrigger
{
    internal Guid Id { get; set; } = Guid.NewGuid();
    internal Action? Action { get; set; }
    internal Func<Task>? AsyncAction { get; set; }
    internal Func<bool>? Condition { get; set; }
    internal Func<Task<bool>>? AsyncCondition { get; set; }
    internal bool CheckConditionImmediately { get; set; }
    internal long ScheduledExecutionMs { get; set; }
    internal CancellationTokenSource Cts { get; set; } = new();
    internal Delayer? ParentTrigger { get; set; }

    /// <summary>
    /// Gets whether this trigger has been cancelled.
    /// </summary>
    public bool IsCancelled => Cts.IsCancellationRequested;

    /// <summary>
    /// Gets whether this trigger is still pending execution.
    /// </summary>
    public bool IsRunning => ParentTrigger?.IsRunning(this) ?? false;

    /// <summary>
    /// Gets the remaining time in milliseconds before this trigger will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds.</returns>
    public double GetRemainingTime(bool allowNegative = false)
    {
        if (IsCancelled || !IsRunning)
            return 0;

        var remaining = ScheduledExecutionMs - Environment.TickCount64;
        return allowNegative ? remaining : Math.Max(0, remaining);
    }

    /// <summary>
    /// Cancels this trigger execution.
    /// </summary>
    /// <returns>True if the trigger was successfully cancelled, false if it was already cancelled or completed.</returns>
    public bool Cancel()
    {
        return ParentTrigger?.Cancel(this) ?? false;
    }

    /// <summary>
    /// Gets the unique identifier for this trigger.
    /// </summary>
    public Guid GetId() => Id;
}
