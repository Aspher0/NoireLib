namespace NoireLib.Helpers;

/// <summary>The current Diadem season as the sheets describe it.</summary>
/// <param name="TerritoryId">The TerritoryType row the season runs in.</param>
/// <param name="ContentFinderConditionId">The season's ContentFinderCondition row.</param>
/// <param name="JobCategoryId">The ClassJobCategory a class must belong to for entry.</param>
/// <param name="JobLevel">The class level required for entry.</param>
public readonly record struct DiademEntry(uint TerritoryId, uint ContentFinderConditionId, uint JobCategoryId, int JobLevel);
