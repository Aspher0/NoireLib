namespace NoireLib.Helpers;

/// <summary>One territory reduced to the three fields that identify the place it is.</summary>
/// <param name="TerritoryId">The TerritoryType row id.</param>
/// <param name="LevelPath">The territory's <c>Bg</c> string, which is the path its level files sit under.</param>
/// <param name="PlaceNameId">The territory's PlaceName row id, or zero when it has none.</param>
public readonly record struct TerritoryEntry(uint TerritoryId, string LevelPath, uint PlaceNameId);
