namespace NoireLib.GameStateWatcher;

/// <summary>
/// An immutable snapshot of a party member captured from the party list.
/// </summary>
/// <param name="ContentId">The content identifier of the party member.</param>
/// <param name="ObjectId">The entity/object identifier of the party member.</param>
/// <param name="Name">The display name of the party member.</param>
/// <param name="WorldId">The home-world row identifier of the party member.</param>
/// <param name="ClassJobId">The class/job row identifier of the party member.</param>
/// <param name="Level">The current level of the party member.</param>
/// <param name="CurrentHp">The current hit points of the party member.</param>
/// <param name="MaxHp">The maximum hit points of the party member.</param>
/// <param name="TerritoryId">The territory row identifier the party member is in.</param>
public sealed record PartyMemberSnapshot(
    ulong ContentId,
    uint ObjectId,
    string Name,
    uint WorldId,
    uint ClassJobId,
    uint Level,
    uint CurrentHp,
    uint MaxHp,
    uint TerritoryId);
