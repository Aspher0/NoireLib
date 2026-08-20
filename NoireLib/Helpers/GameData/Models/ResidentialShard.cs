using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// One residential aethernet shard, which unlike a city shard has no Aetheryte row of its own and exists only as a
/// crystal placed in the district's level file.
/// </summary>
/// <param name="TerritoryId">The residential district the crystal stands in.</param>
/// <param name="Position">The crystal's world position.</param>
/// <param name="PlaceNameId">The PlaceName row id of the ward the crystal serves.</param>
/// <param name="Order">The crystal's index within its district's level file, a stable per-district key.</param>
public readonly record struct ResidentialShard(uint TerritoryId, Vector3 Position, uint PlaceNameId, int Order);
