using Dalamud.Game.ClientState.Conditions;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

// Wraps ConditionChange: dispatches the raw ConditionChangedEvent and the derived enter/leave pairs generated from
// the declarative ConditionPairTable. Event-driven - zero tick cost.
internal sealed class ConditionSource : GameWatcherSource
{
    private readonly Dictionary<string, bool> derivedStates = new();

    public ConditionSource(NoireGameWatcher owner) : base(owner, SourceKind.Condition) { }

    /// <inheritdoc/>
    public override bool IsPolling => false;

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        // Baseline seeding: derived states are computed without firing events.
        derivedStates.Clear();

        foreach (var row in ConditionPairTable.Rows)
            derivedStates[row.Name] = ConditionPairTable.ComputeState(row, flag => NoireService.Condition[flag]);

        NoireService.Condition.ConditionChange += OnConditionChange;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        NoireService.Condition.ConditionChange -= OnConditionChange;
        derivedStates.Clear();
    }

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        Owner.DispatchEvent(new ConditionChangedEvent(flag, value));

        foreach (var row in ConditionPairTable.Rows)
        {
            var involvesFlag = false;

            foreach (var rowFlag in row.Flags)
            {
                if (rowFlag == flag)
                {
                    involvesFlag = true;
                    break;
                }
            }

            if (!involvesFlag)
                continue;

            var newState = ConditionPairTable.ComputeState(row, f => NoireService.Condition[f]);

            if (derivedStates.TryGetValue(row.Name, out var oldState) && oldState == newState)
                continue;

            derivedStates[row.Name] = newState;
            Owner.DispatchUntyped(newState ? row.CreateEnterEvent() : row.CreateLeaveEvent());
        }
    }
}
