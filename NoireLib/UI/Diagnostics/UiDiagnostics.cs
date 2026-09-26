using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// What NoireUI knows about itself: live counts, recent faults, and the fault ladder. Reached through
/// <see cref="NoireUI.Diagnostics"/>.
/// </summary>
public sealed class UiDiagnostics
{
    private const int RecentFaultCapacity = 32;

    private readonly object faultLock = new();
    private readonly Queue<UiFault> recentFaults = new();
    private readonly HashSet<string> repairedContainers = new(StringComparer.Ordinal);

    private int autoDrawnThisFrame;
    private int autoDrawnLastFrame;
    private int currentFrame = -1;

    /// <summary>
    /// Invoked once per fault, on the thread it happened on.
    /// </summary>
    public Action<UiFault>? OnFault { get; set; }

    /// <summary>
    /// Whether NoireUI unwinds ImGui style stacks that were pushed and never popped.
    /// </summary>
    public bool RepairStackLeaks { get; set; } = true;

    /// <summary>
    /// How many frames in a row a drawable may throw before the hub stops drawing it automatically.
    /// </summary>
    public int FaultTolerance { get; set; } = 10;

    /// <summary>
    /// How many faults have been reported since startup.
    /// </summary>
    public int FaultCount { get; private set; }

    /// <summary>
    /// How many ImGui style-stack leaks have been repaired since startup. See <see cref="RepairStackLeaks"/>.
    /// </summary>
    public int StackRepairCount { get; private set; }

    /// <summary>
    /// How many drawables the fault ladder has switched off. See <see cref="FaultTolerance"/>.
    /// </summary>
    public int DisabledDrawableCount { get; private set; }

    /// <summary>
    /// The most recent faults, oldest first, capped at 32.
    /// </summary>
    public IReadOnlyList<UiFault> RecentFaults
    {
        get
        {
            lock (faultLock)
                return recentFaults.ToArray();
        }
    }

    /// <summary>
    /// Takes a consistent read of what NoireUI is doing right now.
    /// </summary>
    /// <returns>The current counts.</returns>
    public UiDiagnosticsSnapshot Snapshot() => new(
        NoireUI.FrameCount,
        NoireUI.GetDrawables().Count,
        autoDrawnLastFrame,
        UiFrameState.Count,
        NoireUI.PendingDrawActions,
        NoireUI.DroppedDrawActions,
        StackRepairCount,
        FaultCount,
        DisabledDrawableCount,
        UiFontCache.BuiltSizeCount,
        NoireUI.Profiler.TotalAverageBytes);

    /// <summary>
    /// Reports a fault: logs it, records it, and hands it to <see cref="OnFault"/>.
    /// </summary>
    /// <param name="source">What produced the fault.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="exception">The exception behind it, when there was one.</param>
    public void ReportFault(string source, string message, Exception? exception)
    {
        var fault = new UiFault(source, message, exception, NoireUI.FrameCount, DateTimeOffset.UtcNow);

        lock (faultLock)
        {
            FaultCount++;
            recentFaults.Enqueue(fault);

            while (recentFaults.Count > RecentFaultCapacity)
                recentFaults.Dequeue();
        }

        if (exception != null)
            NoireLogger.LogError(exception, $"[{source}] {message}", "[NoireUI] ");
        else
            NoireLogger.LogWarning($"[{source}] {message}", "[NoireUI] ");

        var handler = OnFault;
        if (handler == null)
            return;

        try
        {
            handler(fault);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "The NoireUI fault handler threw. It is left attached; fix the handler.", "[NoireUI] ");
        }
    }

    // Called once per frame by the hub.
    internal void BeginFrame(int frame)
    {
        if (frame == currentFrame)
            return;

        currentFrame = frame;
        autoDrawnLastFrame = autoDrawnThisFrame;
        autoDrawnThisFrame = 0;
    }

    // Records that a drawable drew itself, and clears its fault streak.
    internal void NoteDrawn(NoireDrawable drawable)
    {
        autoDrawnThisFrame++;
        drawable.ConsecutiveDrawFaults = 0;
    }

    // Switches the drawable off once it has thrown FaultTolerance frames in a row.
    internal void NoteDrawFault(NoireDrawable drawable, Exception exception)
    {
        drawable.ConsecutiveDrawFaults++;

        var tolerance = FaultTolerance;
        if (tolerance > 0 && drawable.ConsecutiveDrawFaults >= tolerance)
        {
            drawable.AutoDraw = false;
            DisabledDrawableCount++;

            ReportFault(
                $"{drawable.Kind}:{drawable.Id}",
                $"Threw on {drawable.ConsecutiveDrawFaults} consecutive frames, so it has been switched off and nothing else has. " +
                $"Fix the cause, then set AutoDraw back on (or call Draw() yourself, which still works).",
                exception);

            return;
        }

        ReportFault($"{drawable.Kind}:{drawable.Id}", "Threw while drawing.", exception);
    }

    // Logs only the first unwound leak per container.
    internal void NoteStackRepair(string containerName, int entries)
    {
        StackRepairCount += entries;

        bool firstTime;
        lock (faultLock)
            firstTime = repairedContainers.Add(containerName);

        if (!firstTime)
            return;

        ReportFault(
            containerName,
            $"{entries} ImGui style stack {(entries == 1 ? "entry was" : "entries were")} pushed and never popped. " +
            "NoireUI unwound them so the rest of the frame draws correctly. Further leaks from here are repaired silently.",
            null);
    }
}
