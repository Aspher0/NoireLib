using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// One aetheryte or aethernet shard, flattened from the sheet. The Aetheryte sheet carries no coordinates of its own,
/// so <see cref="Position"/> arrives empty from <see cref="AetheryteHelper.ReadAll"/> and is filled by
/// <see cref="AetheryteHelper.ApplyLevelPositions"/> from the crystals placed in the level files.
/// </summary>
/// <param name="Id">The Aetheryte row id.</param>
/// <param name="IsCityAetheryte">True for a map-teleport target, false for an aethernet shard.</param>
/// <param name="AethernetGroup">The aethernet group, or zero when the node is not part of one.</param>
/// <param name="TerritoryId">The territory the crystal stands in.</param>
/// <param name="Position">The crystal's world position, or the origin when nothing has placed it yet.</param>
/// <param name="Name">The display name in the client's own language.</param>
/// <param name="RequiredQuest">The quest that attunes it, or zero when none is needed.</param>
/// <param name="AetherstreamX">The aetherstream X coordinate: a fare-region coordinate, not a world position.</param>
/// <param name="AetherstreamY">The aetherstream Y coordinate: a fare-region coordinate, not a world position.</param>
/// <param name="ArrivalOnly">True for a hidden aetheryte such as an airship landing, which can be arrived at but never departed from and has no crystal.</param>
/// <param name="Ward">The one-based residential ward the point stands in, or zero when it stands in no particular ward; only an owned estate's teleport target carries it.</param>
public readonly record struct AetheryteEntry(
    uint Id,
    bool IsCityAetheryte,
    byte AethernetGroup,
    uint TerritoryId,
    Vector3 Position,
    string Name,
    uint RequiredQuest,
    int AetherstreamX,
    int AetherstreamY,
    bool ArrivalOnly = false,
    int Ward = 0);
