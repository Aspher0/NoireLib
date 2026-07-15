using Lumina.Excel.Sheets;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Diffs the party list (and the alliance through the native group manager): joins/leaves/changes,
/// leader changes, size and role-composition changes, and member territory changes - the latter working
/// even for members outside the local object table (remote presence, server-synchronized).
/// </summary>
internal sealed class PartySource : GameWatcherSource
{
    private readonly Dictionary<ulong, PartyMemberSnapshot> members = new();
    private readonly Dictionary<ulong, PartyMemberSnapshot> allianceMembers = new();
    private ulong? lastLeaderContentId;
    private (int Tanks, int Healers, int Dps) lastComposition;
    private bool seeded;

    public PartySource(NoireGameWatcher owner) : base(owner, SourceKind.Party) { }

    public PartyState CaptureState(DateTimeOffset now) => new()
    {
        Members = ReadPartyMembers(),
        AllianceMembers = ReadAllianceMembers(),
        LeaderContentId = ReadLeaderContentId(),
        IsAlliance = NoireService.PartyList.IsAlliance,
        CapturedAt = now,
    };

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        members.Clear();
        allianceMembers.Clear();
        seeded = false;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        members.Clear();
        allianceMembers.Clear();
        seeded = false;
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        var current = ReadPartyMembers().ToDictionary(m => m.ContentId);
        var currentAlliance = ReadAllianceMembers().ToDictionary(m => m.ContentId);
        var leaderContentId = ReadLeaderContentId();
        var composition = ComputeComposition(current.Values);

        if (!seeded)
        {
            // Baseline seeding without events.
            seeded = true;
            Replace(members, current);
            Replace(allianceMembers, currentAlliance);
            lastLeaderContentId = leaderContentId;
            lastComposition = composition;
            return;
        }

        var previousSize = members.Count;

        foreach (var (contentId, member) in current)
        {
            if (!members.TryGetValue(contentId, out var previous))
            {
                Owner.DispatchEvent(new PartyMemberJoinedEvent(member));
                continue;
            }

            if (previous.TerritoryId != member.TerritoryId)
                Owner.DispatchEvent(new PartyMemberTerritoryChangedEvent(member, previous.TerritoryId, member.TerritoryId));

            if (previous.Level != member.Level
                || previous.ClassJobId != member.ClassJobId
                || previous.CurrentHp != member.CurrentHp
                || previous.MaxHp != member.MaxHp
                || previous.EntityId != member.EntityId)
            {
                Owner.DispatchEvent(new PartyMemberChangedEvent(previous, member));
            }
        }

        foreach (var (contentId, previous) in members)
        {
            if (!current.ContainsKey(contentId))
                Owner.DispatchEvent(new PartyMemberLeftEvent(previous));
        }

        if (current.Count != previousSize)
            Owner.DispatchEvent(new PartySizeChangedEvent(previousSize, current.Count));

        if (leaderContentId != lastLeaderContentId)
        {
            var previousLeader = lastLeaderContentId is { } prevId && members.TryGetValue(prevId, out var prevLeader) ? prevLeader : null;
            var newLeader = leaderContentId is { } newId && current.TryGetValue(newId, out var curLeader) ? curLeader : null;

            Owner.DispatchEvent(new PartyLeaderChangedEvent(previousLeader, newLeader));
        }

        if (composition != lastComposition)
            Owner.DispatchEvent(new PartyCompositionChangedEvent(composition.Tanks, composition.Healers, composition.Dps));

        // Alliance diff (joined/left as batches).
        List<PartyMemberSnapshot>? allianceJoined = null;
        List<PartyMemberSnapshot>? allianceLeft = null;

        foreach (var (contentId, member) in currentAlliance)
        {
            if (!allianceMembers.ContainsKey(contentId))
                (allianceJoined ??= new List<PartyMemberSnapshot>()).Add(member);
        }

        foreach (var (contentId, previous) in allianceMembers)
        {
            if (!currentAlliance.ContainsKey(contentId))
                (allianceLeft ??= new List<PartyMemberSnapshot>()).Add(previous);
        }

        if (allianceJoined != null || allianceLeft != null)
        {
            Owner.DispatchEvent(new AllianceChangedEvent(
                (IReadOnlyList<PartyMemberSnapshot>?)allianceJoined ?? Array.Empty<PartyMemberSnapshot>(),
                (IReadOnlyList<PartyMemberSnapshot>?)allianceLeft ?? Array.Empty<PartyMemberSnapshot>()));
        }

        Replace(members, current);
        Replace(allianceMembers, currentAlliance);
        lastLeaderContentId = leaderContentId;
        lastComposition = composition;
    }

    private static void Replace(Dictionary<ulong, PartyMemberSnapshot> target, Dictionary<ulong, PartyMemberSnapshot> source)
    {
        target.Clear();

        foreach (var (key, value) in source)
            target[key] = value;
    }

    private static List<PartyMemberSnapshot> ReadPartyMembers()
    {
        var result = new List<PartyMemberSnapshot>();
        var partyList = NoireService.PartyList;
        var leaderIndex = partyList.PartyLeaderIndex;

        for (var i = 0; i < partyList.Length; i++)
        {
            var member = partyList[i];

            if (member == null || member.ContentId == 0)
                continue;

            result.Add(new PartyMemberSnapshot
            {
                ContentId = member.ContentId,
                EntityId = member.EntityId,
                Name = member.Name.TextValue,
                WorldId = member.World.RowId,
                ClassJobId = member.ClassJob.RowId,
                Level = member.Level,
                TerritoryId = member.Territory.RowId,
                CurrentHp = member.CurrentHP,
                MaxHp = member.MaxHP,
                IsLeader = i == leaderIndex,
                IsAllianceMember = false,
            });
        }

        return result;
    }

    private static unsafe List<PartyMemberSnapshot> ReadAllianceMembers()
    {
        var result = new List<PartyMemberSnapshot>();
        var groupManager = FFXIVClientStructs.FFXIV.Client.Game.Group.GroupManager.Instance();

        if (groupManager == null)
            return result;

        var group = groupManager->GetGroup(false);

        if (group == null || !group->IsAlliance)
            return result;

        foreach (ref readonly var member in group->AllianceMembers)
        {
            if (member.ContentId == 0)
                continue;

            result.Add(new PartyMemberSnapshot
            {
                ContentId = member.ContentId,
                EntityId = member.EntityId,
                Name = member.NameString,
                WorldId = member.HomeWorld,
                ClassJobId = member.ClassJob,
                Level = member.Level,
                TerritoryId = member.TerritoryType,
                CurrentHp = member.CurrentHP,
                MaxHp = member.MaxHP,
                IsLeader = false,
                IsAllianceMember = true,
            });
        }

        return result;
    }

    private static ulong? ReadLeaderContentId()
    {
        var partyList = NoireService.PartyList;

        if (partyList.Length == 0)
            return null;

        var leaderIndex = (int)partyList.PartyLeaderIndex;

        if (leaderIndex < 0 || leaderIndex >= partyList.Length)
            return null;

        var leader = partyList[leaderIndex];
        return leader == null || leader.ContentId == 0 ? null : leader.ContentId;
    }

    private static (int Tanks, int Healers, int Dps) ComputeComposition(IEnumerable<PartyMemberSnapshot> current)
    {
        int tanks = 0, healers = 0, dps = 0;

        foreach (var member in current)
        {
            switch (GetRole(member.ClassJobId))
            {
                case 1: tanks++; break;
                case 4: healers++; break;
                case 2:
                case 3: dps++; break;
            }
        }

        return (tanks, healers, dps);
    }

    /// <summary>The ClassJob sheet role: 1 = tank, 2/3 = DPS, 4 = healer, 0 = crafter/gatherer.</summary>
    internal static byte GetRole(uint classJobId)
        => ExcelSheetHelper.TryGetRow<ClassJob>(classJobId, out var classJob) ? classJob?.Role ?? 0 : (byte)0;
}
