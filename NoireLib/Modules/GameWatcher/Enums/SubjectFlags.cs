using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// Precomputed relationship flags carried by every <see cref="CharacterSnapshot"/>, captured once per tick
/// per subject so scope filters are flag checks — never a re-query of game state inside a handler filter.
/// </summary>
[Flags]
public enum SubjectFlags
{
    /// <summary>No relationship.</summary>
    None = 0,

    /// <summary>The subject is the local player.</summary>
    IsLocalPlayer = 1 << 0,

    /// <summary>The subject is a member of the local player's party.</summary>
    IsPartyMember = 1 << 1,

    /// <summary>The subject is a member of the local player's alliance.</summary>
    IsAllianceMember = 1 << 2,

    /// <summary>
    /// The subject is on the local player's friend list, as known to the client at capture time.<br/>
    /// Friend data freshness depends on the game's social list refresh; a just-added friend may not match yet.
    /// </summary>
    IsFriend = 1 << 3,
}
