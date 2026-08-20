using System;
using System.Diagnostics;
using System.Threading;

namespace NoireLib.Hooking;

/// <summary>
/// The state a generated detour guard reads at runtime. The fields are public because the emitted method loads them directly.
/// </summary>
/// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
internal sealed class HookGuardContext<TDelegate>
    where TDelegate : Delegate
{
    /// <summary>
    /// The detour the consumer supplied.
    /// </summary>
    public TDelegate Detour = null!;

    /// <summary>
    /// The original function, assigned once the underlying hook exists.
    /// </summary>
    public TDelegate? Original;

    /// <summary>
    /// The counters to update, shared with the hook.
    /// </summary>
    public HookStats Stats = null!;

    /// <summary>
    /// The hook name used in fault reports.
    /// </summary>
    public string Name = string.Empty;

    /// <summary>
    /// The number of consecutive faults after which the hook disables itself, or zero to never disable.
    /// </summary>
    public int FaultLimit;

    /// <summary>
    /// Whether call counts and timings are recorded.
    /// </summary>
    public bool CollectStats;

    /// <summary>
    /// The shortest interval between two fault log entries.
    /// </summary>
    public TimeSpan FaultLogInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Invoked when the fault limit is reached.
    /// </summary>
    public Action? OnFaultLimitReached;

    private long lastFaultLogTimestamp;

    /// <summary>
    /// Records a call that returned without throwing.
    /// </summary>
    /// <param name="startTimestamp">The timestamp taken before the detour ran, or zero when timing is off.</param>
    public void AfterCall(long startTimestamp)
    {
        // Timing needs a wrapper built to capture a timestamp, while counting can be turned on for an installed hook.
        if (CollectStats)
            Stats.RecordCall(startTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(startTimestamp).Ticks);

        Stats.RecordSuccess();
    }

    /// <summary>
    /// Records a detour that threw, logs it at most once per interval, and disables the hook when the fault limit is reached.
    /// </summary>
    /// <param name="exception">The exception the detour threw.</param>
    public void OnFault(Exception exception)
    {
        // Runs inside the catch that keeps a faulting detour away from the game: an escaping exception would
        // replace the detour's exception and skip the recovery below, so nothing here may throw or require init.
        try
        {
            var consecutive = Stats.RecordFault();

            if (ShouldLogFault())
                NoireLogger.LogError(exception, $"The detour for hook '{Name}' threw. It has now thrown {consecutive} time(s) in a row.", HookLog.Prefix);

            if (FaultLimit > 0 && consecutive >= FaultLimit)
            {
                NoireLogger.LogError($"Hook '{Name}' reached its fault limit of {FaultLimit} and has been disabled.", HookLog.Prefix);
                OnFaultLimitReached?.Invoke();
            }
        }
        catch
        {
            // Reporting a fault must never raise one.
        }
    }

    private bool ShouldLogFault()
    {
        if (FaultLogInterval <= TimeSpan.Zero)
            return true;

        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref lastFaultLogTimestamp);

        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < FaultLogInterval)
            return false;

        return Interlocked.CompareExchange(ref lastFaultLogTimestamp, now, last) == last;
    }
}
