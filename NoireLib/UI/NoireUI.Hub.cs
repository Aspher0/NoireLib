using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// The hub's frame-wide services: the automatic-drawing policy, the drawable registry, the draw-thread queue, the frame
/// clock and the reduced-motion switch.
/// </summary>
public static partial class NoireUI
{
    private static readonly object SyncRoot = new();
    private static readonly List<NoireDrawable> Drawables = new();
    private static readonly UiDrawPump DrawPump = new();

    private static bool frameServicesReady;

    // The registry as it was when it last changed, so the per-frame pass can walk it without copying it.
    private static NoireDrawable[] drawableSnapshot = Array.Empty<NoireDrawable>();

    // Test seam replacing the ImGui frame counter when no ImGui context exists.
    internal static Func<int>? FrameOverride { get; set; }

    // Test seam replacing the ImGui clock when no ImGui context exists.
    internal static Func<float>? TimeOverride { get; set; }

    /// <summary>
    /// The master default for automatic drawing, inherited by every drawable that does not decide for itself.
    /// </summary>
    public static bool AutoDraw { get; set; }

    /// <summary>
    /// Whether animations are reduced to their final state.
    /// </summary>
    public static bool ReducedMotion
    {
        get => reducedMotion ?? HostReducedMotion;
        set => reducedMotion = value;
    }

    /// <summary>
    /// Whether Dalamud reports that the user has asked for reduced motion, false when there is no host to ask.
    /// </summary>
    public static bool HostReducedMotion
        => NoireService.IsInitialized() && NoireService.PluginInterface.UiBuilder.ShouldUseReducedMotion;

    /// <summary>
    /// Whether <see cref="ReducedMotion"/> is currently a plugin's own answer rather than the host's.
    /// </summary>
    public static bool HasReducedMotionOverride => reducedMotion.HasValue;

    /// <summary>
    /// Drops the plugin's own answer, so <see cref="ReducedMotion"/> follows <see cref="HostReducedMotion"/> again.
    /// </summary>
    public static void ClearReducedMotion() => reducedMotion = null;

    private static bool? reducedMotion;

    /// <summary>
    /// An optional translation hook for every user-facing string NoireUI shows.
    /// </summary>
    public static Func<string, string?>? StringProvider { get; set; }

    /// <summary>
    /// The current ImGui frame number, or 0 when there is no ImGui context (unit tests).
    /// </summary>
    public static int FrameCount => FrameOverride?.Invoke() ?? (NoireService.IsInitialized() ? ImGui.GetFrameCount() : 0);

    /// <summary>
    /// The ImGui clock in seconds since startup, or 0 when there is no ImGui context (unit tests).
    /// </summary>
    public static float Time => TimeOverride?.Invoke() ?? (NoireService.IsInitialized() ? (float)ImGui.GetTime() : 0f);

    /// <summary>
    /// The duration of the last frame in seconds, clamped to a sane range.
    /// </summary>
    public static float DeltaTime
    {
        get
        {
            if (!NoireService.IsInitialized() || TimeOverride != null)
                return 1f / 60f;

            return Math.Clamp(ImGui.GetIO().DeltaTime, 1f / 1000f, 1f / 10f);
        }
    }

    /// <summary>
    /// How many actions <see cref="RunOnDraw"/> holds before the oldest are dropped.
    /// </summary>
    public static int RunOnDrawCapacity
    {
        get => DrawPump.Capacity;
        set => DrawPump.Capacity = value;
    }

    /// <summary>
    /// How many actions are waiting for the next frame.
    /// </summary>
    public static int PendingDrawActions => DrawPump.Count;

    /// <summary>
    /// Runs an action on the draw thread, at the start of the next frame.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static void RunOnDraw(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        EnsureFrameServices();
        DrawPump.Post(action);
    }

    // Falls back to the shipped default when the string provider answers nothing or throws.
    internal static string Localize(string key, string fallback)
    {
        var provider = StringProvider;
        if (provider == null)
            return fallback;

        try
        {
            var translated = provider(key);
            return string.IsNullOrEmpty(translated) ? fallback : translated;
        }
        catch (Exception ex)
        {
            Diagnostics.ReportFault(nameof(StringProvider), $"The string provider threw while resolving '{key}'.", ex);
            return fallback;
        }
    }

    /// <summary>
    /// Gets a snapshot of every registered drawable.
    /// </summary>
    /// <returns>A snapshot list of the registered drawables.</returns>
    public static IReadOnlyList<NoireDrawable> GetDrawables()
    {
        // Copied rather than handing back the array the frame pass walks, which a caller could cast and write through.
        // This is asked for on demand, not per frame, so the copy costs nothing that matters.
        lock (SyncRoot)
            return Drawables.ToArray();
    }

    // Called by NoireDrawable.Register. Throws when NoireLib has not been initialized yet.
    internal static void RegisterDrawable(NoireDrawable drawable)
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib must be initialized before using NoireLib.UI drawables.");

        lock (SyncRoot)
        {
            if (!Drawables.Contains(drawable))
            {
                Drawables.Add(drawable);
                drawableSnapshot = Drawables.ToArray();
            }

            EnsureFrameHook();
        }

        if (drawable is NoireOverlayButton)
            RefreshUiHideOverrides();
    }

    // Called by NoireDrawable.Dispose.
    internal static void UnregisterDrawable(NoireDrawable drawable)
    {
        lock (SyncRoot)
        {
            if (Drawables.Remove(drawable))
                drawableSnapshot = Drawables.ToArray();
        }

        if (drawable is NoireOverlayButton)
            RefreshUiHideOverrides();
    }

    // Cheap enough to call from any helper entry point, and a no-op before NoireLib is initialized.
    internal static void EnsureFrameServices()
    {
        if (frameServicesReady || !NoireService.IsInitialized())
            return;

        lock (SyncRoot)
        {
            if (frameServicesReady)
                return;

            EnsureFrameHook();
            frameServicesReady = true;
        }
    }

    // The hub's per-frame pass: repairs any ImGui stack left unbalanced, drains the draw queue, prunes transient state
    // and draws everything that draws itself.
    private static void OnFrame()
    {
        if (!NoireService.IsInitialized())
            return;

        var frame = FrameCount;

        UiFrameState.Tick(frame);
        Diagnostics.BeginFrame(frame);

        DrawPump.Drain();

        // Read once, so a drawable that registers or disposes itself from inside its own draw does not disturb the pass
        // it is running in. The next frame picks the change up.
        var snapshot = drawableSnapshot;

        foreach (var drawable in snapshot)
        {
            try
            {
                // The name is built only while the profiler is on, since composing it is the same dictionary lookup the
                // measurement itself would cost.
                using var scope = Profiler.Measure(
                    Profiler.Enabled ? UiIds.Join(drawable.Kind, ":", drawable.Id) : string.Empty);

                if (drawable.TryAutoDraw())
                    Diagnostics.NoteDrawn(drawable);
            }
            catch (Exception ex)
            {
                Diagnostics.NoteDrawFault(drawable, ex);
            }
        }
    }
}
