using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One placement of a world object in a territory's level files. <see cref="IsPlacedNow"/> asks the loaded layout.</summary>
/// <param name="Kind">What was placed. A single flag of <see cref="PlacementKinds"/>.</param>
/// <param name="BaseId">The EObj, ENpcBase or Aetheryte row id. Zero for a shared group.</param>
/// <param name="TerritoryId">The TerritoryType row it stands in, with quest and duty copies folded onto the place.</param>
/// <param name="TerritoryName">The territory's display name in the client's language.</param>
/// <param name="PlaceNameId">The territory's PlaceName row id.</param>
/// <param name="MapId">The Map row the flag coordinate is expressed on, the one the territory names. Zero when it has none.</param>
/// <param name="MapCoordinate">The flag coordinate on that map, the numbers a map link carries. Zero without a map.</param>
/// <param name="Position">The world position.</param>
/// <param name="Yaw">The rotation about the up axis, in radians.</param>
/// <param name="InstanceId">The placement's instance id, the key the loaded layout indexes it by.</param>
/// <param name="LevelFile">The level file that placed it, one of <see cref="LevelFileHelper.Files"/>.</param>
/// <param name="AssetPath">The SGB path for a shared group, empty otherwise.</param>
/// <param name="FestivalId">The festival that must run for the layer to be placed, zero when always placed.</param>
/// <param name="FestivalPhase">The festival phase the layer belongs to, zero for every phase.</param>
/// <param name="LayerTerritories">The TerritoryType rows the layer belongs to, empty when the layer is unconditional.</param>
public readonly record struct WorldObjectPlacement(
    PlacementKinds Kind,
    uint BaseId,
    uint TerritoryId,
    string TerritoryName,
    uint PlaceNameId,
    uint MapId,
    Vector2 MapCoordinate,
    Vector3 Position,
    float Yaw,
    uint InstanceId,
    string LevelFile,
    string AssetPath,
    ushort FestivalId,
    ushort FestivalPhase,
    IReadOnlyList<uint> LayerTerritories)
{
    /// <summary>Asks the loaded layout whether this placement is standing in the world right now.</summary>
    /// <returns>
    /// True when placed and active, false when the layout holds it inactive, and null when it cannot be answered: the
    /// placement is in another place than the loaded one, or the layout does not index it.
    /// </returns>
    public bool? IsPlacedNow()
    {
        var loaded = LayoutHelper.LoadedTerritory();

        if (loaded == 0 || TerritoryHelper.Bg(loaded) != TerritoryHelper.Bg(TerritoryId))
            return null;

        var kind = Kind switch
        {
            PlacementKinds.EventObject => LevelObjectKind.EventObject,
            PlacementKinds.EventNpc => LevelObjectKind.EventNpc,
            PlacementKinds.Aetheryte => LevelObjectKind.Aetheryte,
            PlacementKinds.SharedGroup => LevelObjectKind.SharedGroup,
            _ => LevelObjectKind.Other,
        };

        return LayoutHelper.IsInstancePlaced(new LevelObject(kind, InstanceId, Position, BaseId: BaseId, AssetPath: AssetPath));
    }
}
