using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.DutyState;
using Lumina.Excel.Sheets;
using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// Wraps the native duty-state events (started/wiped/recommenced/completed) and derives duty-queue tracking
/// (entered/left/pop with measured queue duration) from condition flags and content-finder pops.
/// </summary>
internal sealed class DutySource : GameWatcherSource
{
    private bool lastInQueue;
    private DateTimeOffset queueEnteredAt;

    public DutySource(NoireGameWatcher owner) : base(owner, SourceKind.Duty) { }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        lastInQueue = ReadInQueue();
        queueEnteredAt = DateTimeOffset.UtcNow;

        NoireService.DutyState.DutyStarted += OnDutyStarted;
        NoireService.DutyState.DutyWiped += OnDutyWiped;
        NoireService.DutyState.DutyRecommenced += OnDutyRecommenced;
        NoireService.DutyState.DutyCompleted += OnDutyCompleted;
        NoireService.ClientState.CfPop += OnCfPop;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        NoireService.DutyState.DutyStarted -= OnDutyStarted;
        NoireService.DutyState.DutyWiped -= OnDutyWiped;
        NoireService.DutyState.DutyRecommenced -= OnDutyRecommenced;
        NoireService.DutyState.DutyCompleted -= OnDutyCompleted;
        NoireService.ClientState.CfPop -= OnCfPop;
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        var inQueue = ReadInQueue();

        if (inQueue == lastInQueue)
            return;

        lastInQueue = inQueue;

        if (inQueue)
        {
            queueEnteredAt = now;
            Owner.DispatchEvent(new DutyQueueEnteredEvent());
        }
        else
        {
            Owner.DispatchEvent(new DutyQueueLeftEvent(now - queueEnteredAt));
        }
    }

    private static bool ReadInQueue()
        => NoireService.Condition.Any(ConditionFlag.WaitingForDuty, ConditionFlag.WaitingForDutyFinder, ConditionFlag.InDutyQueue);

    private void OnDutyStarted(IDutyStateEventArgs args)
        => Owner.DispatchEvent(new DutyStartedEvent(args.TerritoryType.RowId));

    private void OnDutyWiped(IDutyStateEventArgs args)
        => Owner.DispatchEvent(new DutyWipedEvent(args.TerritoryType.RowId));

    private void OnDutyRecommenced(IDutyStateEventArgs args)
        => Owner.DispatchEvent(new DutyRecommencedEvent(args.TerritoryType.RowId));

    private void OnDutyCompleted(IDutyStateEventArgs args)
        => Owner.DispatchEvent(new DutyCompletedEvent(args.TerritoryType.RowId));

    private void OnCfPop(ContentFinderCondition content)
    {
        TimeSpan? queueDuration = lastInQueue ? DateTimeOffset.UtcNow - queueEnteredAt : null;
        Owner.DispatchEvent(new DutyPopEvent(content.RowId, content.Name.ExtractText(), queueDuration));
    }
}
