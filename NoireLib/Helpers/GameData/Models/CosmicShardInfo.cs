using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One cosmic aethernet shard: the free intra-planet teleport network's stop.</summary>
/// <param name="WksAetheryteId">The WKSAetheryte row id.</param>
/// <param name="PlaceNameId">The PlaceName row the shard is named by.</param>
/// <param name="ObjectIds">The EObj rows placed for this shard, more than one when the stop has several placements.</param>
public readonly record struct CosmicShardInfo(uint WksAetheryteId, uint PlaceNameId, IReadOnlyList<uint> ObjectIds);
