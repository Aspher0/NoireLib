namespace NoireLib.UI;

/// <summary>
/// A consistent read of what NoireUI is doing right now, taken with <see cref="UiDiagnostics.Snapshot"/>.<br/>
/// Everything here is an exact count rather than an estimate, sampled on the frame the snapshot was taken.
/// </summary>
/// <param name="Frame">The frame the snapshot was taken on.</param>
/// <param name="Drawables">How many drawables are registered with the hub.</param>
/// <param name="AutoDrawn">How many of them drew themselves on the last completed frame.</param>
/// <param name="StateEntries">How many <see cref="UiFrameState"/> entries are held, across every value type.</param>
/// <param name="PendingDrawActions">How many actions are waiting in the <see cref="NoireUI.RunOnDraw"/> queue.</param>
/// <param name="DroppedDrawActions">How many queued actions have been dropped because the queue was full, since startup.</param>
/// <param name="StackRepairs">How many ImGui style-stack leaks have been repaired since startup.</param>
/// <param name="Faults">How many faults have been reported since startup.</param>
/// <param name="DisabledDrawables">How many drawables the fault ladder has switched off.</param>
/// <param name="TextFontSizes">How many distinct font sizes <see cref="NoireText"/> has built. Each one is a full glyph
/// atlas, so a number that keeps climbing is an interface asking for text by number instead of by
/// <see cref="TextSize"/>.</param>
/// <param name="AllocatedBytesPerFrame">How many bytes a frame of interface allocates, summed across every measured
/// scope's own allocation and averaged. Reads 0 unless <see cref="UiProfiler.Enabled"/> is on, since nothing is
/// measured while the profiler is off, and skips any scope marked through <see cref="UiProfiler.SetExcluded"/>. Unlike
/// the timings, this is the same number on every machine, which is what makes it the figure to compare a change
/// against.</param>
public readonly record struct UiDiagnosticsSnapshot(
    int Frame,
    int Drawables,
    int AutoDrawn,
    int StateEntries,
    int PendingDrawActions,
    int DroppedDrawActions,
    int StackRepairs,
    int Faults,
    int DisabledDrawables,
    int TextFontSizes,
    double AllocatedBytesPerFrame);
