namespace NoireLib.UI;

/// <summary>
/// What one measured scope cost, taken from <see cref="UiProfiler.Snapshot()"/>. Times are in milliseconds of wall
/// clock on the draw thread. <b>Total</b> includes everything measured inside the scope and <b>self</b> does not, so
/// summing the total column double-counts nested scopes.
/// </summary>
/// <param name="Id">Identifies this call path.</param>
/// <param name="ParentId">The <paramref name="Id"/> of the scope this one sits inside, or 0 when it is outermost.</param>
/// <param name="Name">The scope's name, as it was measured.</param>
/// <param name="Calls">How many times the scope ran on the last measured frame.</param>
/// <param name="LastMs">What the scope cost in total on that frame, nested scopes included.</param>
/// <param name="AverageMs">A rolling average of <paramref name="LastMs"/>.</param>
/// <param name="PeakMs">The worst single frame of total time seen since the last <see cref="UiProfiler.Reset"/>.</param>
/// <param name="SelfLastMs">What the scope cost on that frame excluding everything measured inside it.</param>
/// <param name="SelfAverageMs">A rolling average of <paramref name="SelfLastMs"/>.</param>
/// <param name="LastBytes">How many bytes the scope allocated on that frame, nested scopes included.</param>
/// <param name="AverageBytes">A rolling average of <paramref name="LastBytes"/>.</param>
/// <param name="PeakBytes">The worst single frame of total allocation seen since the last
/// <see cref="UiProfiler.Reset"/>.</param>
/// <param name="SelfLastBytes">How many bytes the scope allocated on that frame excluding everything measured inside
/// it.</param>
/// <param name="SelfAverageBytes">A rolling average of <paramref name="SelfLastBytes"/>.</param>
/// <param name="Excluded">Whether the scope is left out of the profiler's totals through
/// <see cref="UiProfiler.SetExcluded"/>.</param>
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
