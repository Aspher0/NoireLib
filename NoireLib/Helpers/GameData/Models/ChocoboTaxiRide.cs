namespace NoireLib.Helpers;

/// <summary>One chocobo taxi ride: a directed hop between two stands with a fixed fare and a fixed duration.</summary>
/// <param name="FromStandId">The departure stand's ChocoboTaxiStand row id.</param>
/// <param name="ToStandId">The arrival stand's ChocoboTaxiStand row id.</param>
/// <param name="Fare">The ride's fare in gil.</param>
/// <param name="TimeSeconds">The ride's fixed duration in seconds, converted from the sheet's minutes.</param>
/// <param name="DestinationName">The arrival stand's name in the client's own language, or empty when it did not resolve.</param>
public readonly record struct ChocoboTaxiRide(
    uint FromStandId,
    uint ToStandId,
    uint Fare,
    int TimeSeconds,
    string DestinationName);
