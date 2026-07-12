using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// The internal base for the watcher's diff-producers. Sources own no subscription logic: they detect a fact
/// change and hand a typed event record to the module core, which dispatches through the shared registry.<br/>
/// Lifecycle is driven entirely by the owning module (demand refcounting + config overrides): sources are
/// constructed cold (no game access in constructors) and only touch game state between
/// <see cref="Activate"/> and <see cref="Deactivate"/>.<br/>
/// Source isolation: any exception thrown from activation or a tick marks the source failed and shuts it
/// down — every other source keeps working.
/// </summary>
internal abstract class GameWatcherSource
{
    private TimeSpan pollCadence = TimeSpan.Zero;
    private DateTimeOffset nextPollDue = DateTimeOffset.MinValue;

    protected GameWatcherSource(NoireGameWatcher owner, SourceKind kind)
    {
        Owner = owner;
        Kind = kind;
    }

    /// <summary>The owning module.</summary>
    protected NoireGameWatcher Owner { get; }

    /// <summary>The source's identity in configuration and diagnostics.</summary>
    public SourceKind Kind { get; }

    /// <summary>Whether the source is currently running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Whether the source failed to initialize or tick and disabled itself (source isolation).</summary>
    public bool HasFailed { get; private set; }

    /// <summary>The failure description when <see cref="HasFailed"/> is true.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>The number of live registrations holding this source active (managed by the module).</summary>
    public int RefCount { get; internal set; }

    /// <summary>The duration of the last tick, for diagnostics.</summary>
    public TimeSpan LastTickDuration { get; internal set; }

    /// <summary>The default poll cadence when no override is configured. Zero = every tick.</summary>
    protected virtual TimeSpan DefaultPollCadence => TimeSpan.Zero;

    /// <summary>Whether this source does per-tick work. Event-driven sources return false and are never ticked.</summary>
    public virtual bool IsPolling => true;

    /// <summary>
    /// Starts the source: install hooks, attach native events, seed baselines. Baseline seeding never fires
    /// events — subscribers observe changes from now on, not a replay of the present.
    /// </summary>
    /// <returns>True when the source started; false when it failed (already logged and marked).</returns>
    public bool Activate()
    {
        if (IsRunning || HasFailed)
            return IsRunning;

        try
        {
            pollCadence = Owner.ResolvePollCadence(Kind, DefaultPollCadence);
            nextPollDue = DateTimeOffset.MinValue;
            OnActivate();
            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            MarkFailed(ex, "activation");
            return false;
        }
    }

    /// <summary>
    /// Stops the source: uninstall hooks, detach events, clear cached state.
    /// </summary>
    public void Deactivate()
    {
        if (!IsRunning)
            return;

        IsRunning = false;

        try
        {
            OnDeactivate();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, $"GameWatcher source {Kind} threw during deactivation.");
        }
    }

    /// <summary>
    /// Runs one tick when the source is running, polling, and due per its cadence.
    /// Exceptions mark the source failed and shut it down (source isolation).
    /// </summary>
    /// <param name="now">The current UTC timestamp, shared by all sources this tick.</param>
    public void Tick(DateTimeOffset now)
    {
        if (!IsRunning || !IsPolling)
            return;

        if (pollCadence > TimeSpan.Zero)
        {
            if (now < nextPollDue)
                return;

            nextPollDue = now + pollCadence;
        }

        var start = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            OnTick(now);
        }
        catch (Exception ex)
        {
            MarkFailed(ex, "tick");
            Deactivate();
        }
        finally
        {
            LastTickDuration = System.Diagnostics.Stopwatch.GetElapsedTime(start);
        }
    }

    /// <summary>Source-specific activation. Runs on the framework thread when the module is active in game.</summary>
    protected abstract void OnActivate();

    /// <summary>Source-specific deactivation.</summary>
    protected abstract void OnDeactivate();

    /// <summary>Source-specific per-tick work. Only called for polling sources.</summary>
    protected virtual void OnTick(DateTimeOffset now) { }

    /// <summary>Releases unmanaged resources (hooks). Called once when the module is disposed.</summary>
    public virtual void DisposeSource() { }

    /// <summary>
    /// Marks the source failed and logs it. A failed source never restarts until the module is reactivated;
    /// subscriptions to its events are reported in diagnostics.
    /// </summary>
    private protected void MarkFailed(Exception ex, string stage)
    {
        HasFailed = true;
        FailureMessage = $"{ex.GetType().Name} during {stage}: {ex.Message}";
        NoireLogger.LogError(Owner, ex, $"GameWatcher source {Kind} failed during {stage} and disabled itself. Every other source keeps working.");
    }

    /// <summary>
    /// Clears a failure so the source can be retried, used when the module is reactivated.
    /// </summary>
    internal void ResetFailure()
    {
        HasFailed = false;
        FailureMessage = null;
    }
}
