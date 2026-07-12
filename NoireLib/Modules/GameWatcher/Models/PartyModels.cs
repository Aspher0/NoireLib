using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// An immutable snapshot of a party or alliance member.<br/>
/// Party data covers members anywhere — <see cref="TerritoryId"/> is server-synchronized and works even when
/// the member is not in the local object table (remote presence for party members, seconds-grained).
/// </summary>
public sealed record PartyMemberSnapshot
{
    /// <summary>The member's content id.</summary>
    public required ulong ContentId { get; init; }

    /// <summary>The member's entity id, or 0 when the member is not in the local object table.</summary>
    public required uint EntityId { get; init; }

    /// <summary>The member's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The member's world row id.</summary>
    public required uint WorldId { get; init; }

    /// <summary>The member's class/job row id.</summary>
    public required uint ClassJobId { get; init; }

    /// <summary>The member's level.</summary>
    public required uint Level { get; init; }

    /// <summary>The territory row id the member is currently in (server-synchronized, seconds-grained).</summary>
    public required uint TerritoryId { get; init; }

    /// <summary>The member's current HP as known to the party list.</summary>
    public required uint CurrentHp { get; init; }

    /// <summary>The member's maximum HP as known to the party list.</summary>
    public required uint MaxHp { get; init; }

    /// <summary>Whether this member is the party leader.</summary>
    public required bool IsLeader { get; init; }

    /// <summary>Whether this member is an alliance member (rather than a member of your own party).</summary>
    public required bool IsAllianceMember { get; init; }
}

/// <summary>
/// The current party state returned by <c>watcher.Party.State</c>.
/// </summary>
public sealed record PartyState
{
    /// <summary>The party members (excluding alliance members). Empty when solo.</summary>
    public required IReadOnlyList<PartyMemberSnapshot> Members { get; init; }

    /// <summary>The alliance members, or empty when not in an alliance.</summary>
    public required IReadOnlyList<PartyMemberSnapshot> AllianceMembers { get; init; }

    /// <summary>The content id of the party leader, or null when solo or unknown.</summary>
    public required ulong? LeaderContentId { get; init; }

    /// <summary>Whether the party is part of an alliance.</summary>
    public required bool IsAlliance { get; init; }

    /// <summary>The party size (party members only; 0 when solo).</summary>
    public int Size => Members.Count;

    /// <summary>The UTC timestamp when the state was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
