using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>The CustomTalk services the cosmic travel NPCs run, found by their script names.</summary>
/// <param name="EntranceTalkIds">The boarding service, the planet select at the cosmoport and at Bestways Burrow.</param>
/// <param name="ExitTalkIds">The leave service, the trip back to Etheirys.</param>
public readonly record struct CosmicTravelTalks(IReadOnlySet<uint> EntranceTalkIds, IReadOnlySet<uint> ExitTalkIds);
