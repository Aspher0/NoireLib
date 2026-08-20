using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// One placed level object, flattened from a Lumina LGB instance into the fields a consumer reads. Kind-specific
/// fields are zero or empty when they do not apply, which keeps a hand-built fixture to only the fields the code
/// under test cares about.
/// </summary>
/// <param name="Kind">The object's kind.</param>
/// <param name="InstanceId">The object's instance id, unique within its level file and the key the game's own layout is keyed by.</param>
/// <param name="Position">The object's world position, from its transform translation.</param>
/// <param name="DestTerritoryId">
/// For an <see cref="LevelObjectKind.ExitRange"/>, the destination territory; zero otherwise, and also zero for an
/// <see cref="LevelExitKind.IntraZoneTeleport"/>, which stays in the territory it starts in and so names none.
/// </param>
/// <param name="DestInstanceId">
/// For an <see cref="LevelObjectKind.ExitRange"/>, the destination PopRange instance id. It is resolved against the
/// destination territory for a zone line and against the source territory for an intra-zone teleport.
/// </param>
/// <param name="BaseId">For an aetheryte, event NPC, or event object, its sheet row id; zero otherwise.</param>
/// <param name="AssetPath">For a <see cref="LevelObjectKind.SharedGroup"/>, its SGB asset path; empty otherwise.</param>
/// <param name="Yaw">The object's rotation about the up axis, in radians, which reconstructs an ExitRange's box; zero otherwise.</param>
/// <param name="Scale">The object's scale, an ExitRange's box half-extents; the default otherwise.</param>
/// <param name="FestivalId">
/// The festival that must be running for the layer to be placed, or zero when always placed. Ignoring this shows
/// every past year's seasonal decorations as permanently present.
/// </param>
/// <param name="FestivalPhase">The festival phase the layer belongs to, or zero when it belongs to every phase.</param>
/// <param name="ExitKind">
/// For an <see cref="LevelObjectKind.ExitRange"/>, what the trigger does; <see cref="LevelExitKind.None"/> otherwise.
/// </param>
/// <param name="ReturnInstanceId">
/// For an <see cref="LevelObjectKind.ExitRange"/>, the PopRange the game returns the character to, which an
/// intra-zone teleport pair uses to name each other's landing spot; zero when the object names none.
/// </param>
/// <param name="LayerTerritories">
/// The TerritoryType rows the object's layer belongs to, from <see cref="LayerSetHelper.ReadLayerTerritories"/>.
/// Null or empty when the layer is unconditional, which is most of them. Several territories share one level
/// directory, so an object whose layer names only the others is not standing in this territory at all.
/// </param>
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
    IReadOnlyList<uint>? LayerTerritories = null)
{
    /// <summary>Whether the object's layer is part of a territory.</summary>
    /// <param name="territoryId">The territory being read.</param>
    /// <returns>True when it is, and for any object whose layer is unconditional.</returns>
    public bool BelongsTo(uint territoryId) => LayerSetHelper.Belongs(LayerTerritories, territoryId);
}
