using System;

namespace NoireLib.GameWatcher;

// The internal base for the watcher's diff-producers. Sources own no subscription logic: they detect a fact change
// and hand a typed event record to the module core, which dispatches through the shared registry. Lifecycle is driven
// entirely by the owning module (demand refcounting + config overrides): sources are constructed cold (no game access
// in constructors) and only touch game state between Activate and Deactivate. Source isolation: any exception thrown
// from activation or a tick marks the source failed and shuts it down - every other source keeps working.
internal abstract class GameWatcherSource
{
    private TimeSpan pollCadence = TimeSpan.Zero;
    private DateTimeOffset nextPollDue = DateTimeOffset.MinValue;

    protected GameWatcherSource(NoireGameWatcher owner, SourceKind kind)
    {
        Owner = owner;
        Kind = kind;
    }

    protected NoireGameWatcher Owner { get; }

    public SourceKind Kind { get; }

    public bool IsRunning { get; private set; }

    // Set when initializing or ticking threw. The source then disabled itself.
    public bool HasFailed { get; private set; }

    public string? FailureMessage { get; private set; }

    public int RefCount { get; internal set; }

    public TimeSpan LastTickDuration { get; internal set; }

    // Zero ticks every frame.
    protected virtual TimeSpan DefaultPollCadence => TimeSpan.Zero;

    // Event-driven sources return false and are never ticked.
    public virtual bool IsPolling => true;

    // Seeding baselines never fires events: subscribers see changes from now on.
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

    // An exception marks the source failed and shuts it down.
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

    // Framework thread, while the module is active in game.
    protected abstract void OnActivate();

    protected abstract void OnDeactivate();

    protected virtual void OnTick(DateTimeOffset now) { }

    // Called once, when the module is disposed.
    public virtual void DisposeSource() { }

    // Marks the source failed and logs it. A failed source never restarts until the module is reactivated;
    // subscriptions to its events are reported in diagnostics.
    private protected void MarkFailed(Exception ex, string stage)
    {
        HasFailed = true;
        FailureMessage = $"{ex.GetType().Name} during {stage}: {ex.Message}";
        NoireLogger.LogError(Owner, ex, $"GameWatcher source {Kind} failed during {stage} and disabled itself. Every other source keeps working.");
    }

    // Clears a failure so the source can be retried, used when the module is reactivated.
    internal void ResetFailure()
    {
        HasFailed = false;
        FailureMessage = null;
    }
}
