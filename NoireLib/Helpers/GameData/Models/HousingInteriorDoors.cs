namespace NoireLib.Helpers;

/// <summary>An interior's two doors: the one back out, and the one further in when the interior has one.</summary>
/// <param name="TerritoryId">The interior territory.</param>
/// <param name="Outward">The door leading back the way the character came.</param>
/// <param name="Inward">The door leading deeper in, unfound for an interior with only one door.</param>
public readonly record struct HousingInteriorDoors(uint TerritoryId, HousingDoor Outward, HousingDoor Inward);
