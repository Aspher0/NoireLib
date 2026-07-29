using System;

namespace NoireLib.UI;

/// <summary>
/// Something NoireUI could not do, reported once at the moment it happened. Surfaced through
/// <see cref="UiDiagnostics.OnFault"/> and kept in <see cref="UiDiagnostics.RecentFaults"/>; already logged by the
/// time it reaches you.
/// </summary>
/// <param name="Source">What produced the fault: a drawable id, or the name of the hub member that failed.</param>
/// <param name="Message">A description of what went wrong, in plain terms.</param>
/// <param name="Exception">The exception behind it, when there was one.</param>
/// <param name="Frame">The frame the fault happened on.</param>
/// <param name="TimeUtc">When it happened.</param>
public sealed record UiFault(string Source, string Message, Exception? Exception, int Frame, DateTimeOffset TimeUtc);
