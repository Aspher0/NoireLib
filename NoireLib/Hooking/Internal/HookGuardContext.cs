using System;
using System.Diagnostics;
using System.Threading;

namespace NoireLib.Hooking;

// The state a generated detour guard reads at runtime. The fields are public because the emitted method loads them
// directly.
internal sealed class HookGuardContext<TDelegate>
    where TDelegate : Delegate
{
    public TDelegate Detour = null!;

    public TDelegate? Original;

    public HookStats Stats = null!;

    public string Name = string.Empty;

    // The number of consecutive faults after which the hook disables itself, or zero to never disable.
    public int FaultLimit;

    public bool CollectStats;

    public TimeSpan FaultLogInterval = TimeSpan.FromSeconds(5);

    public Action? OnFaultLimitReached;

    private long lastFaultLogTimestamp;

    public void AfterCall(long startTimestamp)
    {
        // Timing needs a wrapper built to capture a timestamp, while counting can be turned on for an installed hook.
        if (CollectStats)
            Stats.RecordCall(startTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(startTimestamp).Ticks);
    // The number of consecutive faults after which the hook disables itself, or zero to never disable.
        Stats.RecordSuccess();
    }

    // Records a detour that threw, logs it at most once per interval, and disables the hook when the fault limit is
    // reached.
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
