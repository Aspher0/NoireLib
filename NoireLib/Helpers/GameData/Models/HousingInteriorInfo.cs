namespace NoireLib.Helpers;

/// <summary>One housing interior territory paired with what the game's own sheet says it is.</summary>
/// <param name="TerritoryId">The interior's TerritoryType row id.</param>
/// <param name="Kind">What kind of interior it is.</param>
public readonly record struct HousingInteriorInfo(uint TerritoryId, HousingInteriorKind Kind);
