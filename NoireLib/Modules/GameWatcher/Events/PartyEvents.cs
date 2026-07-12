using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when a member joins the party.
/// </summary>
/// <param name="Member">The joining member.</param>
public sealed record PartyMemberJoinedEvent(PartyMemberSnapshot Member);

/// <summary>
/// Fired when a member leaves the party.
/// </summary>
/// <param name="Member">The member's last known snapshot.</param>
public sealed record PartyMemberLeftEvent(PartyMemberSnapshot Member);

/// <summary>
/// Fired when an observed property of a party member changes (level, job, HP as known to the party list).
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record PartyMemberChangedEvent(PartyMemberSnapshot Previous, PartyMemberSnapshot Current);

/// <summary>
/// Fired when a party member's territory changes — remote presence for party members, even when they are
/// not in the local object table. Territory data is server-synchronized and seconds-grained.
/// </summary>
/// <param name="Member">The member's current snapshot.</param>
/// <param name="PreviousTerritoryId">The previous territory row id.</param>
/// <param name="TerritoryId">The new territory row id.</param>
public sealed record PartyMemberTerritoryChangedEvent(PartyMemberSnapshot Member, uint PreviousTerritoryId, uint TerritoryId);

/// <summary>
/// Fired when the party leader changes.
/// </summary>
/// <param name="PreviousLeader">The previous leader, or null when unknown.</param>
/// <param name="Leader">The new leader, or null when unknown.</param>
public sealed record PartyLeaderChangedEvent(PartyMemberSnapshot? PreviousLeader, PartyMemberSnapshot? Leader);

/// <summary>
/// Fired when the party size changes.
/// </summary>
/// <param name="PreviousSize">The previous party size.</param>
/// <param name="Size">The new party size.</param>
public sealed record PartySizeChangedEvent(int PreviousSize, int Size);

/// <summary>
/// Fired when the party's role composition changes (tank/healer/DPS counts).
/// </summary>
/// <param name="Tanks">The number of tanks.</param>
/// <param name="Healers">The number of healers.</param>
/// <param name="Dps">The number of DPS.</param>
public sealed record PartyCompositionChangedEvent(int Tanks, int Healers, int Dps);

/// <summary>
/// Fired when the alliance member list changes.
/// </summary>
/// <param name="Joined">The members that joined the alliance since the last tick.</param>
/// <param name="Left">The members that left the alliance since the last tick.</param>
public sealed record AllianceChangedEvent(IReadOnlyList<PartyMemberSnapshot> Joined, IReadOnlyList<PartyMemberSnapshot> Left);
