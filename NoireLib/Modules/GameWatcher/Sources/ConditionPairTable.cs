using Dalamud.Game.ClientState.Conditions;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// The declarative flag→event table behind the Condition source: one row per derived enter/leave pair,
/// not twenty hand-written blocks. A derived state is "any of the row's flags is set"; transitions fire the
/// row's enter/leave events.<br/>
/// Pure data — unit-testable without the game.
/// </summary>
internal static class ConditionPairTable
{
    /// <summary>One derived enter/leave pair.</summary>
    internal sealed record Row(
        string Name,
        ConditionFlag[] Flags,
        Type EnterEventType,
        Type LeaveEventType,
        Func<object> CreateEnterEvent,
        Func<object> CreateLeaveEvent);

    private static Row Make<TEnter, TLeave>(string name, params ConditionFlag[] flags)
        where TEnter : new()
        where TLeave : new()
        => new(name, flags, typeof(TEnter), typeof(TLeave), () => new TEnter(), () => new TLeave());

    /// <summary>Every derived pair the Condition source produces.</summary>
    public static readonly IReadOnlyList<Row> Rows = new[]
    {
        Make<CombatEnteredEvent, CombatLeftEvent>("combat", ConditionFlag.InCombat),
        Make<MountedEvent, DismountedEvent>("mounted", ConditionFlag.Mounted, ConditionFlag.RidingPillion),
        Make<FlightStartedEvent, FlightEndedEvent>("flying", ConditionFlag.InFlight),
        Make<SwimmingStartedEvent, SwimmingEndedEvent>("swimming", ConditionFlag.Swimming),
        Make<DivingStartedEvent, DivingEndedEvent>("diving", ConditionFlag.Diving),
        Make<CraftingStartedEvent, CraftingEndedEvent>("crafting", ConditionFlag.Crafting, ConditionFlag.ExecutingCraftingAction, ConditionFlag.PreparingToCraft),
        Make<GatheringStartedEvent, GatheringEndedEvent>("gathering", ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction),
        Make<FishingStartedEvent, FishingEndedEvent>("fishing", ConditionFlag.Fishing),
        Make<PerformanceStartedEvent, PerformanceEndedEvent>("performing", ConditionFlag.Performing),
        Make<OccupiedStartedEvent, OccupiedEndedEvent>(
            "occupied",
            ConditionFlag.Occupied, ConditionFlag.Occupied30, ConditionFlag.Occupied33,
            ConditionFlag.Occupied38, ConditionFlag.Occupied39, ConditionFlag.OccupiedInEvent,
            ConditionFlag.OccupiedInQuestEvent, ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.OccupiedSummoningBell),
        Make<CutsceneStartedEvent, CutsceneEndedEvent>(
            "cutscene",
            ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78),
        Make<LoadingStartedEvent, LoadingEndedEvent>("loading", ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51),
        Make<DutyEnteredEvent, DutyLeftEvent>("duty", ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95),
    };

    /// <summary>
    /// Computes a derived state from a flag reader: true when any of the row's flags is set.
    /// </summary>
    /// <param name="row">The table row.</param>
    /// <param name="isFlagSet">Reads a single condition flag.</param>
    /// <returns>The derived state.</returns>
    public static bool ComputeState(Row row, Func<ConditionFlag, bool> isFlagSet)
    {
        foreach (var flag in row.Flags)
        {
            if (isFlagSet(flag))
                return true;
        }

        return false;
    }
}
