using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One placed housing door. <see cref="Found"/> is false when the level file held none.</summary>
/// <param name="Position">The door's world position.</param>
/// <param name="InteractObjectId">The EObj row id of the object to interact with.</param>
/// <param name="Found">Whether a door was resolved at all.</param>
public readonly record struct HousingDoor(Vector3 Position, uint InteractObjectId, bool Found = true);
