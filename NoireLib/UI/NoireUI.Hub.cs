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

    /// <summary>
    /// The registry as it was when it last changed, so the per-frame pass can walk it without copying it.
    /// </summary>
    /// <remarks>
    /// The pass has to iterate something that a drawable registering or disposing itself mid-draw cannot invalidate,
    /// and it runs every frame. Copying the list each time is an allocation per frame for a collection that changes
    /// perhaps a dozen times over a plugin's life, so the copy is made when it changes instead.
    /// </remarks>
    private static NoireDrawable[] drawableSnapshot = Array.Empty<NoireDrawable>();

    /// <summary>
    /// Test seam replacing the ImGui frame counter when no ImGui context exists.
    /// </summary>
    internal static Func<int>? FrameOverride { get; set; }

    /// <summary>
    /// Test seam replacing the ImGui clock when no ImGui context exists.
    /// </summary>
    internal static Func<float>? TimeOverride { get; set; }

    /// <summary>
    /// The master default for automatic drawing, inherited by every drawable that does not decide for itself.<br/>
    /// Defaults to <see langword="false"/>: nothing draws itself out of the box and you own every <c>Draw()</c> call, so
    /// draw order is yours by construction. Setting it to <see langword="true"/> makes the whole library draw itself,
    /// with per-object opt-outs.<br/>
    /// This is a default, not a kill switch: a drawable that sets <see cref="NoireDrawable.AutoDraw"/> explicitly wins in
    /// either direction. Every one of those values was set by the same plugin, so a master that overrode an explicit
    /// request would only be lying to its author.
    /// </summary>
    public static bool AutoDraw { get; set; }

    /// <summary>
    /// Whether animations are reduced to their final state.<br/>
    /// When enabled, <see cref="NoireAnim"/> snaps to targets instead of easing, and decorative motion (pulse, shimmer,
    /// shake, flash) stops. Widgets stay fully functional; only the movement goes away.<br/>
    /// Follows <see cref="HostReducedMotion"/> until something assigns it. Assigning takes it over for good;
    /// <see cref="ClearReducedMotion"/> hands it back.
    /// </summary>
    /// <remarks>
    /// Reading the host's preference is the default because it is an accessibility setting the user has already stated
    /// once, to Dalamud, and a library that ignores it makes every plugin using it ask again. A plugin offering the
    /// choice itself should offer a way back to the host's answer as well, rather than turning a preference into a
    /// setting the user now owns in two places.
    /// </remarks>
    public static bool ReducedMotion
    {
        get => reducedMotion ?? HostReducedMotion;
        set => reducedMotion = value;
    }

    /// <summary>
    /// Whether Dalamud reports that the user has asked for reduced motion. False when there is no host to ask.
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
    /// An optional translation hook for every user-facing string NoireUI shows.<br/>
    /// Returning <see langword="null"/> for a key falls back to the shipped English default. NoireLib depends on no
    /// localization system and ships no locale files; wiring this to one is up to the plugin.
    /// </summary>
    /// <example><c>NoireUI.StringProvider = key =&gt; myLocalizer.GetOrNull(key);</c></example>
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
    /// The duration of the last frame in seconds, clamped to a sane range so a stalled frame cannot make an animation
    /// jump. Returns a nominal 60 FPS step when there is no ImGui context.
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
    /// How many actions <see cref="RunOnDraw"/> holds before the oldest are dropped. Bounded on purpose: an unbounded
    /// queue in front of a UI that has stopped drawing is a memory leak.
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
    /// Runs an action on the draw thread, at the start of the next frame.<br/>
    /// Safe to call from anywhere. Use it for anything that touches ImGui or a widget from a timer, a socket, a hotkey
    /// callback or a background task. When NoireLib is not initialized there is no draw thread to marshal onto, and the
    /// action runs inline on the calling thread.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static void RunOnDraw(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        EnsureFrameServices();
        DrawPump.Post(action);
    }

    /// <summary>
    /// Resolves a user-facing string through <see cref="StringProvider"/>, falling back to the shipped default.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="fallback">The shipped English default.</param>
    /// <returns>The translated string, or <paramref name="fallback"/>.</returns>
    public static string Text(string key, string fallback)
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

    /// <summary>
    /// Registers a drawable so the hub can draw it. Called by <see cref="NoireDrawable.Register"/>.
    /// </summary>
    /// <param name="drawable">The drawable to register.</param>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
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

    /// <summary>
    /// Unregisters a drawable so it stops being drawn. Called by <see cref="NoireDrawable.Dispose"/>.
    /// </summary>
    /// <param name="drawable">The drawable to unregister.</param>
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

    /// <summary>
    /// Makes sure the per-frame pass is running, so state pruning, the draw queue and diagnostics work for a plugin that
    /// only uses in-window helpers and never creates a drawable.<br/>
    /// Cheap enough to call from any helper entry point, and a no-op before NoireLib is initialized.
    /// </summary>
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

    /// <summary>
    /// The hub's per-frame pass: repairs any ImGui stack left unbalanced, drains the draw queue, prunes transient state
    /// and draws everything that draws itself.
    /// </summary>
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
