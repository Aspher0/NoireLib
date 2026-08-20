using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One warp read out of the <c>Warp</c> sheet and the interactable that triggers it.
/// </summary>
/// <param name="TriggerBaseId">The ENpcBase or EObj row id of the interactable that triggers this warp.</param>
/// <param name="TriggerKind">Whether the trigger is an event NPC or an event object.</param>
/// <param name="WarpRowId">The Warp sheet row id, unique per warp.</param>
/// <param name="DestTerritoryId">The territory the warp lands in, which is the departure territory for an in-place lift.</param>
/// <param name="ArrivalInstanceId">
/// The destination PopRange instance id, resolved to a position against the destination territory's own level file
/// through <see cref="LevelFileHelper.BuildPopRangeIndex"/>.
/// </param>
/// <param name="GilCost">
/// The warp's gil fare from its <c>WarpCondition</c>, or zero when it is free, which is a price paid and not a sum
/// the character must already be carrying.
/// </param>
/// <param name="ClassLevel">The class or job level the warp's condition requires, or zero when it needs none.</param>
/// <param name="RequiredQuests">The up to four quests the warp's condition names, empty when it names none.</param>
/// <param name="QuestThreshold">
/// How many of <see cref="RequiredQuests"/> must be complete, which the condition states as a mode and so is only
/// ever one or the full count.
/// </param>
/// <param name="LogicId">
/// The warp's <c>WarpLogic</c> row id, zero when it names none, read with <see cref="WarpHelper.ReadLogic"/>.
/// </param>
/// <param name="LogicParams">
/// The logic row's named arguments, empty for the generic rows. These can be the warp's only gate: three warps carry
/// quest arguments here while their <c>WarpCondition</c> names no quest at all. See
/// <see cref="WarpHelper.NamesContentGate(WarpLogicInfo)"/>.
/// </param>
public readonly record struct WarpDefinition(
    uint TriggerBaseId,
    WarpTriggerKind TriggerKind,
    uint WarpRowId,
    uint DestTerritoryId,
    uint ArrivalInstanceId,
    int GilCost,
    int ClassLevel,
    IReadOnlyList<uint> RequiredQuests,
    int QuestThreshold,
    uint LogicId = 0,
    IReadOnlyList<WarpLogicParam>? LogicParams = null);
