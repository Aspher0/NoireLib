namespace NoireLib.UI;

/// <summary>
/// What one measured scope cost, taken from <see cref="UiProfiler.Snapshot()"/>.
/// </summary>
/// <remarks>
/// Times are in milliseconds of wall clock on the draw thread. Scopes nest: <b>total</b> includes everything measured
/// inside the scope, <b>self</b> does not; summing the total column double-counts nested scopes. Bytes are reported
/// the same way and are deterministic across hardware, unlike milliseconds.
/// </remarks>
/// <param name="Id">Identifies this call path. A scope called from two places is two entries, one per caller.</param>
/// <param name="ParentId">The <paramref name="Id"/> of the scope this one sits inside, or 0 when it is outermost.</param>
/// <param name="Name">The scope's name, as it was measured.</param>
/// <param name="Calls">How many times the scope ran on the last measured frame. Closed off on the next measurement
/// rather than on a clock, so a stopped scope reads zero one measured frame late.</param>
/// <param name="LastMs">What the scope cost in total on that frame, nested scopes included.</param>
/// <param name="AverageMs">A rolling average of <paramref name="LastMs"/>.</param>
/// <param name="PeakMs">The worst single frame of total time seen since the last <see cref="UiProfiler.Reset"/>.</param>
/// <param name="SelfLastMs">What the scope cost on that frame excluding everything measured inside it. The figure
/// that adds up across scopes.</param>
/// <param name="SelfAverageMs">A rolling average of <paramref name="SelfLastMs"/>.</param>
/// <param name="LastBytes">How many bytes the scope allocated on that frame, nested scopes included.</param>
/// <param name="AverageBytes">A rolling average of <paramref name="LastBytes"/>.</param>
/// <param name="PeakBytes">The worst single frame of total allocation seen since the last
/// <see cref="UiProfiler.Reset"/>. A peak far above the average is usually a cache filling rather than a per-frame
/// cost.</param>
/// <param name="SelfLastBytes">How many bytes the scope allocated on that frame excluding everything measured inside
/// it. The figure that adds up across scopes.</param>
/// <param name="SelfAverageBytes">A rolling average of <paramref name="SelfLastBytes"/>.</param>
/// <param name="Excluded">Whether the scope is left out of the profiler's totals through
/// <see cref="UiProfiler.SetExcluded"/>. Still measured and still reports its own figures; only the sums skip
/// it.</param>
public readonly record struct UiProfileEntry(
    int Id,
    int ParentId,
    string Name,
    int Calls,
    double LastMs,
    double AverageMs,
    double PeakMs,
    double SelfLastMs,
    double SelfAverageMs,
    long LastBytes,
    double AverageBytes,
    long PeakBytes,
    long SelfLastBytes,
    double SelfAverageBytes,
    bool Excluded = false);
