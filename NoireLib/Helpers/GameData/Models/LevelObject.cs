using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One placed level object, flattened from a level file entry. Kind-specific fields are zero or empty when they do not apply.</summary>
/// <param name="Kind">The object's kind.</param>
/// <param name="InstanceId">The object's instance id, unique within its level file. The game's layout is keyed by it.</param>
/// <param name="Position">The object's world position.</param>
/// <param name="DestTerritoryId">For an <see cref="LevelObjectKind.ExitRange"/>, the destination territory. Zero otherwise and for an <see cref="LevelExitKind.IntraZoneTeleport"/>.</param>
/// <param name="DestInstanceId">For an <see cref="LevelObjectKind.ExitRange"/>, the destination PopRange instance id, in the destination territory for a zone line and the source territory for an intra-zone teleport.</param>
/// <param name="BaseId">For an aetheryte, event NPC, or event object, its sheet row id. Zero otherwise.</param>
/// <param name="AssetPath">For a <see cref="LevelObjectKind.SharedGroup"/>, its SGB asset path. Empty otherwise.</param>
/// <param name="Yaw">The rotation about the up axis, in radians. Zero for kinds that carry none.</param>
/// <param name="Scale">The object's scale, an ExitRange's box half-extents. The default otherwise.</param>
/// <param name="FestivalId">The festival that must run for the layer to be placed, or zero when always placed.</param>
/// <param name="FestivalPhase">The festival phase the layer belongs to, or zero for every phase.</param>
/// <param name="ExitKind">For an <see cref="LevelObjectKind.ExitRange"/>, what the trigger does. <see cref="LevelExitKind.None"/> otherwise.</param>
/// <param name="ReturnInstanceId">For an <see cref="LevelObjectKind.ExitRange"/>, the PopRange the game returns the character to. Zero when none.</param>
/// <param name="LayerTerritories">The TerritoryType rows the layer belongs to, from <see cref="LayerSetHelper.ReadLayerTerritories"/>. Null or empty when the layer is unconditional.</param>
/// <param name="Layer">The name of the layer the object was read out of.</param>
public readonly record struct LevelObject(
    LevelObjectKind Kind,
    uint InstanceId,
    Vector3 Position,
    uint DestTerritoryId = 0,
    uint DestInstanceId = 0,
    uint BaseId = 0,
    string AssetPath = "",
    float Yaw = 0f,
    Vector3 Scale = default,
    ushort FestivalId = 0,
    ushort FestivalPhase = 0,
    LevelExitKind ExitKind = LevelExitKind.None,
    uint ReturnInstanceId = 0,
    IReadOnlyList<uint>? LayerTerritories = null,
    string Layer = "")
{
    /// <summary>Whether the object's layer is part of a territory.</summary>
    /// <param name="territoryId">The territory being read.</param>
    /// <returns>True when it is, and for any object whose layer is unconditional.</returns>
    public bool BelongsTo(uint territoryId) => LayerSetHelper.Belongs(LayerTerritories, territoryId);
}
