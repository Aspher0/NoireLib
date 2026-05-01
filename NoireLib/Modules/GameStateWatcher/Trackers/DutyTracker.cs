using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using NoireLib.Events;
using NoireLib.TaskQueue;
using System;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks duty lifecycle events by wrapping <see cref="IDutyState"/> events with <see cref="EventWrapper"/> instances.
/// </summary>
public sealed class DutyTracker : GameStateSubTracker
{
    private readonly EventWrapper dutyStartedEvent;
    private readonly EventWrapper dutyWipedEvent;
    private readonly EventWrapper dutyRecommencedEvent;
    private readonly EventWrapper dutyCompletedEvent;

    private DateTimeOffset dutyStartTime;
    private bool lastIsInQueue;
    private bool lastIsWaitingForDuty;
    private DateTimeOffset queueStartTime;
    private DateTimeOffset lastQueueDurationSnapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="DutyTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    internal DutyTracker(NoireGameStateWatcher owner, bool active) : base(owner, active)
    {
        dutyStartedEvent = new(NoireService.DutyState, nameof(IDutyState.DutyStarted), name: $"{nameof(DutyTracker)}.DutyStarted");
        dutyWipedEvent = new(NoireService.DutyState, nameof(IDutyState.DutyWiped), name: $"{nameof(DutyTracker)}.DutyWiped");
        dutyRecommencedEvent = new(NoireService.DutyState, nameof(IDutyState.DutyRecommenced), name: $"{nameof(DutyTracker)}.DutyRecommenced");
        dutyCompletedEvent = new(NoireService.DutyState, nameof(IDutyState.DutyCompleted), name: $"{nameof(DutyTracker)}.DutyCompleted");

        dutyStartedEvent.AddCallback("handler", HandleDutyStarted);
        dutyWipedEvent.AddCallback("handler", HandleDutyWiped);
        dutyRecommencedEvent.AddCallback("handler", HandleDutyRecommenced);
        dutyCompletedEvent.AddCallback("handler", HandleDutyCompleted);
    }

    /// <summary>
    /// Gets a value indicating whether a duty is currently in progress.
    /// </summary>
    public bool IsDutyStarted => NoireService.DutyState.IsDutyStarted;

    /// <summary>
    /// Gets the territory identifier of the last duty that was started, or 0 if no duty has been observed.
    /// </summary>
    public ushort LastDutyTerritoryId { get; private set; }

    /// <summary>
    /// Gets the total number of wipes observed since the last duty started.
    /// </summary>
    public int WipeCount { get; private set; }

    /// <summary>
    /// Gets the time the current or last duty was started.
    /// </summary>
    public DateTimeOffset DutyStartTime => dutyStartTime;

    /// <summary>
    /// Gets the elapsed time since the current duty started, or <see cref="TimeSpan.Zero"/> if no duty has been observed.
    /// </summary>
    public TimeSpan ElapsedDutyTime => dutyStartTime == default ? TimeSpan.Zero : DateTimeOffset.UtcNow - dutyStartTime;

    /// <summary>
    /// Gets a value indicating whether this tracker has observed at least one duty start.
    /// </summary>
    public bool HasObservedDuty => dutyStartTime != default;

    /// <summary>
    /// Gets a value indicating whether at least one wipe has been observed in the current or last duty.
    /// </summary>
    public bool HasWiped => WipeCount > 0;

    /// <summary>
    /// Gets a value indicating whether the player is currently in the duty-finder queue.
    /// </summary>
    public bool IsInQueue => NoireService.Condition.Any(ConditionFlag.WaitingForDuty, ConditionFlag.WaitingForDutyFinder, ConditionFlag.InDutyQueue);

    /// <summary>
    /// Gets the time the current queue was entered, or <see cref="DateTimeOffset.MinValue"/> if not in queue.
    /// </summary>
    public DateTimeOffset QueueStartTime => queueStartTime;

    /// <summary>
    /// Gets the elapsed time spent in the current duty-finder queue, or <see cref="TimeSpan.Zero"/> if not in queue.
    /// </summary>
    public TimeSpan ElapsedQueueTime => lastIsInQueue && queueStartTime != default ? DateTimeOffset.UtcNow - queueStartTime : TimeSpan.Zero;

    /// <summary>
    /// Raised when a duty starts.
    /// </summary>
    public event Action<DutyStartedEvent>? OnDutyStarted;

    /// <summary>
    /// Raised when the party wipes in a duty.
    /// </summary>
    public event Action<DutyWipedEvent>? OnDutyWiped;

    /// <summary>
    /// Raised when a duty recommences after a wipe.
    /// </summary>
    public event Action<DutyRecommencedEvent>? OnDutyRecommenced;

    /// <summary>
    /// Raised when a duty is completed.
    /// </summary>
    public event Action<DutyCompletedEvent>? OnDutyCompleted;

    /// <summary>
    /// Raised when the player enters the duty-finder queue.
    /// </summary>
    public event Action<DutyQueueEnteredEvent>? OnDutyQueueEntered;

    /// <summary>
    /// Raised when the player leaves the duty-finder queue.
    /// </summary>
    public event Action<DutyQueueLeftEvent>? OnDutyQueueLeft;

    /// <summary>
    /// Raised when a duty-finder match is found (duty pop / commence prompt).
    /// </summary>
    public event Action<DutyCommenceEvent>? OnDutyCommence;

    /// <summary>
    /// Resets the <see cref="WipeCount"/> to zero.
    /// </summary>
    public void ResetWipeCount() => WipeCount = 0;

    /// <summary>
    /// Checks whether the current tracked duty matches the specified territory identifier.
    /// </summary>
    /// <param name="territoryId">The duty-territory identifier to compare.</param>
    /// <returns><see langword="true"/> if the current tracked duty matches; otherwise, <see langword="false"/>.</returns>
    public bool IsCurrentDuty(ushort territoryId) => IsDutyStarted && LastDutyTerritoryId == territoryId;

    /// <summary>
    /// Checks whether at least the specified number of wipes has been observed.
    /// </summary>
    /// <param name="minimumWipeCount">The minimum wipe count to compare against.</param>
    /// <returns><see langword="true"/> if the wipe threshold has been reached; otherwise, <see langword="false"/>.</returns>
    public bool HasWipedAtLeast(int minimumWipeCount) => WipeCount >= minimumWipeCount;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when a duty is in progress.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when a duty is active.</returns>
    public Func<bool> WaitForDutyStart() => () => IsDutyStarted;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when no duty is in progress.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when no duty is active.</returns>
    public Func<bool> WaitForDutyEnd() => () => !IsDutyStarted;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the player is in the duty-finder queue.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the player is queued.</returns>
    public Func<bool> WaitForQueue() => () => IsInQueue;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the player is not in the duty-finder queue.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the player is not queued.</returns>
    public Func<bool> WaitForNotInQueue() => () => !IsInQueue;

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        WipeCount = 0;
        LastDutyTerritoryId = 0;
        dutyStartTime = default;
        lastIsInQueue = NoireService.Condition[ConditionFlag.WaitingForDutyFinder];
        lastIsWaitingForDuty = NoireService.Condition[ConditionFlag.WaitingForDuty];
        queueStartTime = lastIsInQueue ? DateTimeOffset.UtcNow : default;
        lastQueueDurationSnapshot = default;

        dutyStartedEvent.Enable();
        dutyWipedEvent.Enable();
        dutyRecommencedEvent.Enable();
        dutyCompletedEvent.Enable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(DutyTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        dutyStartedEvent.Disable();
        dutyWipedEvent.Disable();
        dutyRecommencedEvent.Disable();
        dutyCompletedEvent.Disable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(DutyTracker)} deactivated.");
    }

    /// <inheritdoc/>
    internal override void Update()
    {
        var isInQueue = NoireService.Condition[ConditionFlag.WaitingForDutyFinder];
        if (isInQueue != lastIsInQueue)
        {
            lastIsInQueue = isInQueue;

            if (isInQueue)
            {
                queueStartTime = DateTimeOffset.UtcNow;

                if (Owner.EnableLogging)
                    NoireLogger.LogInfo(Owner, "Entered duty-finder queue.");

                PublishEvent(OnDutyQueueEntered, new DutyQueueEnteredEvent());
            }
            else
            {
                var duration = queueStartTime != default ? DateTimeOffset.UtcNow - queueStartTime : TimeSpan.Zero;
                lastQueueDurationSnapshot = queueStartTime;

                if (Owner.EnableLogging)
                    NoireLogger.LogInfo(Owner, $"Left duty-finder queue (Duration: {duration}).");

                PublishEvent(OnDutyQueueLeft, new DutyQueueLeftEvent(duration));
            }
        }

        var isWaitingForDuty = NoireService.Condition[ConditionFlag.WaitingForDuty];
        if (isWaitingForDuty && !lastIsWaitingForDuty)
        {
            var duration = lastQueueDurationSnapshot != default ? DateTimeOffset.UtcNow - lastQueueDurationSnapshot : TimeSpan.Zero;

            if (Owner.EnableLogging)
                NoireLogger.LogInfo(Owner, $"Duty commence (Queue duration: {duration}).");

            PublishEvent(OnDutyCommence, new DutyCommenceEvent(duration));
        }

        lastIsWaitingForDuty = isWaitingForDuty;
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        dutyStartedEvent.Dispose();
        dutyWipedEvent.Dispose();
        dutyRecommencedEvent.Dispose();
        dutyCompletedEvent.Dispose();
    }

    private void HandleDutyStarted(object? sender, ushort territoryId)
    {
        LastDutyTerritoryId = territoryId;
        WipeCount = 0;
        dutyStartTime = DateTimeOffset.UtcNow;

        var evt = new DutyStartedEvent(territoryId);

        if (Owner.EnableLogging)
            NoireLogger.LogInfo(Owner, $"Duty started (Territory: {territoryId}).");

        PublishEvent(OnDutyStarted, evt);
    }

    private void HandleDutyWiped(object? sender, ushort territoryId)
    {
        WipeCount++;

        var evt = new DutyWipedEvent(territoryId);

        if (Owner.EnableLogging)
            NoireLogger.LogInfo(Owner, $"Duty wiped (Territory: {territoryId}, Wipe #{WipeCount}).");

        PublishEvent(OnDutyWiped, evt);
    }

    private void HandleDutyRecommenced(object? sender, ushort territoryId)
    {
        var evt = new DutyRecommencedEvent(territoryId);

        if (Owner.EnableLogging)
            NoireLogger.LogInfo(Owner, $"Duty recommenced (Territory: {territoryId}).");

        PublishEvent(OnDutyRecommenced, evt);
    }

    private void HandleDutyCompleted(object? sender, ushort territoryId)
    {
        var evt = new DutyCompletedEvent(territoryId);

        if (Owner.EnableLogging)
            NoireLogger.LogInfo(Owner, $"Duty completed (Territory: {territoryId}).");

        PublishEvent(OnDutyCompleted, evt);
    }
}
