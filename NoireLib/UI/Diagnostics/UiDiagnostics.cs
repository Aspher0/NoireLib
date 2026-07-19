using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// What NoireUI knows about itself: live counts, the faults it has hit, and the ladder that switches off the narrowest
/// broken thing rather than logging the same exception every frame forever.<br/>
/// Reached through <see cref="NoireUI.Diagnostics"/>. It answers the question a UI library otherwise cannot: why did
/// nothing draw.
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
    /// Invoked once for every fault, on the thread the fault happened on. The fault is already logged when this runs;
    /// this is the hook for surfacing it in your own UI.<br/>
    /// An exception thrown by the handler is swallowed, so a broken reporter cannot take the frame down with it.
    /// </summary>
    public Action<UiFault>? OnFault { get; set; }

    /// <summary>
    /// Whether NoireUI unwinds ImGui style stacks that were pushed and never popped.<br/>
    /// On by default. Turning it off leaves an unbalanced stack alone, which is what raw ImGui does on its own.
    /// </summary>
    public bool RepairStackLeaks { get; set; } = true;

    /// <summary>
    /// How many frames in a row a drawable may throw before the hub stops drawing it automatically.<br/>
    /// The ladder disables that one drawable and nothing else, so a single broken element cannot take the rest of the UI
    /// with it, and the log says so once instead of every frame. Set to 0 to never disable anything.
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
    /// <returns>A snapshot of the recent faults.</returns>
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
        DisabledDrawableCount);

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
            NoireLogger.LogError(exception, $"[{source}] {message}", nameof(NoireUI));
        else
            NoireLogger.LogWarning($"[{source}] {message}", nameof(NoireUI));

        var handler = OnFault;
        if (handler == null)
            return;

        try
        {
            handler(fault);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "The NoireUI fault handler threw. It is left attached; fix the handler.", nameof(NoireUI));
        }
    }

    /// <summary>
    /// Starts a new frame of counting. Called once per frame by the hub.
    /// </summary>
    /// <param name="frame">The frame being started.</param>
    internal void BeginFrame(int frame)
    {
        if (frame == currentFrame)
            return;

        currentFrame = frame;
        autoDrawnLastFrame = autoDrawnThisFrame;
        autoDrawnThisFrame = 0;
    }

    /// <summary>
    /// Records that a drawable drew itself, and clears its fault streak.
    /// </summary>
    /// <param name="drawable">The drawable that drew.</param>
    internal void NoteDrawn(NoireDrawable drawable)
    {
        autoDrawnThisFrame++;
        drawable.ConsecutiveDrawFaults = 0;
    }

    /// <summary>
    /// Records that a drawable threw while drawing, and switches it off once it has thrown
    /// <see cref="FaultTolerance"/> frames in a row.
    /// </summary>
    /// <param name="drawable">The drawable that threw.</param>
    /// <param name="exception">The exception it threw.</param>
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

    /// <summary>
    /// Records an ImGui style-stack leak that was unwound, logging the first one per container.
    /// </summary>
    /// <param name="containerName">The container whose body leaked.</param>
    /// <param name="entries">How many stack entries were unwound.</param>
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
