using System;

namespace NoireLib.UI;

/// <summary>
/// Something NoireUI could not do, reported once at the moment it happened.<br/>
/// Faults are surfaced through <see cref="UiDiagnostics.OnFault"/> and kept in
/// <see cref="UiDiagnostics.RecentFaults"/>; they are already logged by the time they reach you.
/// </summary>
/// <param name="Source">What produced the fault: a drawable id, or the name of the hub member that failed.</param>
/// <param name="Message">A description of what went wrong, in plain terms.</param>
/// <param name="Exception">The exception behind it, when there was one.</param>
/// <param name="Frame">The frame the fault happened on.</param>
/// <param name="TimeUtc">When it happened.</param>
public sealed record UiFault(string Source, string Message, Exception? Exception, int Frame, DateTimeOffset TimeUtc);
