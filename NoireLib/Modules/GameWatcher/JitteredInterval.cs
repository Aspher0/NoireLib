using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// A randomized refresh interval: each cycle waits a uniformly-random duration in <c>[Min, Max]</c>.<br/>
/// Used for the background social-proxy refreshes (friends, player search) whose requests hit the server —
/// a fixed beat is a detectable pattern, so the wait is jittered. An absolute floor
/// (<see cref="FloorSeconds"/>) is enforced on every effective wait regardless of configuration, so the
/// request rate can never be dialed into a hammer.
/// </summary>
public readonly struct JitteredInterval
{
    /// <summary>The absolute minimum (in seconds) any effective wait is clamped to, for anti-detection.</summary>
    public const double FloorSeconds = 30d;

    /// <summary>Creates a jittered interval spanning <paramref name="min"/> to <paramref name="max"/>.</summary>
    /// <param name="min">The lower bound (raised to <see cref="FloorSeconds"/> when smaller).</param>
    /// <param name="max">The upper bound (raised to the effective lower bound when smaller).</param>
    public JitteredInterval(TimeSpan min, TimeSpan max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>The lower bound of the wait. Effective value is floored at <see cref="FloorSeconds"/>.</summary>
    public TimeSpan Min { get; init; }

    /// <summary>The upper bound of the wait. Effective value is never below the effective lower bound.</summary>
    public TimeSpan Max { get; init; }

    /// <summary>The default cadence: a uniform random wait between 30 and 40 seconds.</summary>
    public static JitteredInterval Default => new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40));

    /// <summary>
    /// Picks the next wait: a uniform random duration in the effective <c>[Min, Max]</c>, with both bounds
    /// floored at <see cref="FloorSeconds"/>. <c>default(JitteredInterval)</c> yields a flat 30 seconds.
    /// </summary>
    /// <param name="random">The RNG to draw from (a per-source instance; framework-thread only).</param>
    /// <returns>The duration to wait before the next refresh.</returns>
    public TimeSpan Next(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var minSeconds = Math.Max(FloorSeconds, Min.TotalSeconds);
        var maxSeconds = Math.Max(minSeconds, Max.TotalSeconds);
        var picked = minSeconds + ((maxSeconds - minSeconds) * random.NextDouble());

        return TimeSpan.FromSeconds(picked);
    }
}
