namespace NoireLib.Helpers;

/// <summary>One Cosmic Exploration planet as the WKS sheets describe it.</summary>
/// <param name="TerritoryId">The planet's TerritoryType row id.</param>
/// <param name="Order">The planet's release order, zero-based, from the WKSTerritoryInfo row order.</param>
public readonly record struct CosmicPlanet(uint TerritoryId, int Order);
