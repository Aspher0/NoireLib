using ContentType = FFXIVClientStructs.FFXIV.Client.Game.Event.ContentType;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>One duty as the duty finder describes it.</summary>
/// <param name="ConditionId">The ContentFinderCondition row id.</param>
/// <param name="Name">The duty's name.</param>
/// <param name="ShortCode">The duty's internal short code, which is stable across languages.</param>
/// <param name="TerritoryId">The TerritoryType the duty takes place in.</param>
/// <param name="ContentTypeId">The ContentType row id: dungeon, trial, raid and so on.</param>
/// <param name="ContentId">The row the duty's content is defined by, which for an instanced duty is an InstanceContent row.</param>
/// <param name="ContentLinkType">Which sheet <paramref name="ContentId"/> points into.</param>
/// <param name="LevelRequired">The class level needed to enter.</param>
/// <param name="LevelSync">The level the duty syncs down to, or zero when it does not sync.</param>
/// <param name="ItemLevelRequired">The average item level needed to enter.</param>
/// <param name="ItemLevelSync">The item level the duty syncs down to, or zero when it does not sync.</param>
/// <param name="PartySize">How many players the duty queues for.</param>
/// <param name="AcceptClassJobCategoryId">The ClassJobCategory of jobs allowed to queue.</param>
/// <param name="RouletteIds">The ContentRoulette rows that can draw the duty.</param>
/// <param name="IsInDutyFinder">Whether the duty is listed in the duty finder at all.</param>
/// <param name="IsHighEnd">Whether the duty is high-end content.</param>
/// <param name="IsPvP">Whether the duty is player versus player content.</param>
/// <param name="AllowsUndersized">Whether the duty can be entered with fewer players than it queues for.</param>
public sealed record DutyInfo(
    uint ConditionId,
    string Name,
    string ShortCode,
    uint TerritoryId,
    uint ContentTypeId,
    uint ContentId,
    ContentType ContentLinkType,
    byte LevelRequired,
    byte LevelSync,
    ushort ItemLevelRequired,
    ushort ItemLevelSync,
    byte PartySize,
    uint AcceptClassJobCategoryId,
    IReadOnlyList<uint> RouletteIds,
    bool IsInDutyFinder,
    bool IsHighEnd,
    bool IsPvP,
    bool AllowsUndersized)
{
    /// <summary>Whether the duty is drawn by at least one roulette.</summary>
    public bool IsInAnyRoulette => RouletteIds.Count > 0;

    /// <summary>Whether a given roulette draws this duty.</summary>
    /// <param name="contentRouletteId">The ContentRoulette row id.</param>
    /// <returns>True when the roulette can draw it.</returns>
    public bool IsInRoulette(uint contentRouletteId) => RouletteIds.Contains(contentRouletteId);

    /// <summary>Whether the duty is instanced content, and so has readable unlock and completion state.</summary>
    public bool IsInstanceContent => ContentLinkType == ContentType.Instance && ContentId != 0;
}
