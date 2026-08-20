namespace NoireLib.Helpers;

/// <summary>One duty finder roulette.</summary>
/// <param name="RowId">The ContentRoulette row id.</param>
/// <param name="Name">Its name in the client language.</param>
/// <param name="Category">The category label the duty finder lists it under.</param>
/// <param name="RequiredLevel">The class level needed to queue.</param>
/// <param name="IsInDutyFinder">Whether the duty finder offers it.</param>
/// <param name="IsPvP">Whether it queues for PvP content.</param>
public sealed record RouletteInfo(
    uint RowId,
    string Name,
    string Category,
    byte RequiredLevel,
    bool IsInDutyFinder,
    bool IsPvP);
