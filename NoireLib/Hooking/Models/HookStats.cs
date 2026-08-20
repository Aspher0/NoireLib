using System;
using System.Threading;

namespace NoireLib.Hooking;

/// <summary>Counts what a hook's detour has done since it was installed, only while <see cref="HookOptions.CollectStats"/> is set.</summary>
public sealed class HookStats
{
    private long callCount;
    private long faultCount;
    private long totalDetourTicks;
    private long peakDetourTicks;
    private int consecutiveFaults;
    private long lastCallTicks;

    /// <summary>How many times the detour has been entered.</summary>
    public long CallCount => Interlocked.Read(ref callCount);

    /// <summary>How many times the detour has thrown.</summary>
    public long FaultCount => Interlocked.Read(ref faultCount);

    /// <summary>How many faults there have been since the last call that did not throw.</summary>
    public int ConsecutiveFaults => Volatile.Read(ref consecutiveFaults);

    /// <summary>The total time spent inside the detour.</summary>
    public TimeSpan TotalDetourTime => TimeSpan.FromTicks(Interlocked.Read(ref totalDetourTicks));

    /// <summary>The longest single time spent inside the detour.</summary>
    public TimeSpan PeakDetourTime => TimeSpan.FromTicks(Interlocked.Read(ref peakDetourTicks));

    /// <summary>The average time spent inside the detour.</summary>
    public TimeSpan AverageDetourTime
    {
        get
        {
            var calls = CallCount;
            return calls == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Interlocked.Read(ref totalDetourTicks) / calls);
        }
    }

    /// <summary>The UTC time of the most recent call, or null when the detour has never run.</summary>
    public DateTime? LastCallUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref lastCallTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Clears every counter.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref callCount, 0);
        Interlocked.Exchange(ref faultCount, 0);
        Interlocked.Exchange(ref totalDetourTicks, 0);
        Interlocked.Exchange(ref peakDetourTicks, 0);
        Interlocked.Exchange(ref lastCallTicks, 0);
        Volatile.Write(ref consecutiveFaults, 0);
    }

    /// <summary>Records one detour entry and its duration.</summary>
    /// <param name="elapsedTicks">Ticks spent inside the detour.</param>
    internal void RecordCall(long elapsedTicks)
    {
        Interlocked.Increment(ref callCount);
        Interlocked.Add(ref totalDetourTicks, elapsedTicks);
        Interlocked.Exchange(ref lastCallTicks, DateTime.UtcNow.Ticks);

        var peak = Interlocked.Read(ref peakDetourTicks);
        while (elapsedTicks > peak)
        {
            var seen = Interlocked.CompareExchange(ref peakDetourTicks, elapsedTicks, peak);
            if (seen == peak)
                break;

            peak = seen;
        }
    }

    /// <summary>Clears the consecutive-fault run after a call that did not throw.</summary>
    internal void RecordSuccess() => Volatile.Write(ref consecutiveFaults, 0);

    /// <summary>Records one detour fault.</summary>
    /// <returns>The new consecutive-fault count.</returns>
    internal int RecordFault()
    {
        Interlocked.Increment(ref faultCount);
        return Interlocked.Increment(ref consecutiveFaults);
    }
}
