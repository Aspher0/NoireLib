using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One chocobo taxi stand and the rides that leave it.</summary>
/// <param name="StandId">The ChocoboTaxiStand row id.</param>
/// <param name="Name">The stand's name in the client's own language.</param>
/// <param name="Rides">The rides leaving this stand.</param>
public readonly record struct ChocoboTaxiStandInfo(uint StandId, string Name, IReadOnlyList<ChocoboTaxiRide> Rides);
