namespace NoireLib.Helpers;

/// <summary>What the Aetheryte sheet says about a crystal beyond its name.</summary>
/// <param name="IsAetheryte">True for a teleport crystal, false for an aethernet shard.</param>
/// <param name="IsInvisible">The sheet's invisible flag.</param>
/// <param name="AethernetGroup">The aethernet group, zero outside any.</param>
/// <param name="AethernetName">The aethernet name in the client's language, empty when it has none.</param>
/// <param name="PlaceNameId">The PlaceName row the crystal is named by.</param>
/// <param name="TerritoryId">The TerritoryType row the sheet binds it to.</param>
/// <param name="MapId">The Map row the sheet binds it to.</param>
/// <param name="RequiredQuestId">The quest that attunes it, zero when none is needed.</param>
/// <param name="Order">The sheet's display order within its group.</param>
public sealed record AetheryteDetails(
    bool IsAetheryte,
    bool IsInvisible,
    byte AethernetGroup,
    string AethernetName,
    uint PlaceNameId,
    uint TerritoryId,
    uint MapId,
    uint RequiredQuestId,
    byte Order);
