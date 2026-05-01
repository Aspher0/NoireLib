using Dalamud.Game.ClientState.Objects.SubKinds;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks party composition changes by polling the party list each framework tick.<br/>
/// Provides snapshot-based query methods for filtering and inspecting party members.
/// </summary>
public sealed class PartyTracker : GameStateSubTracker
{
    private readonly object snapshotLock = new();
    private List<PartyMemberSnapshot> previousMembers = new();
    private long previousPartyId;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    internal PartyTracker(NoireGameStateWatcher owner, bool active) : base(owner, active)
    {
    }

    /// <summary>
    /// Gets a snapshot of all current party members.
    /// </summary>
    public IReadOnlyList<PartyMemberSnapshot> Members
    {
        get
        {
            lock (snapshotLock)
                return previousMembers.ToArray();
        }
    }

    /// <summary>
    /// Gets the current party size.
    /// </summary>
    public int CurrentSize
    {
        get
        {
            lock (snapshotLock)
                return previousMembers.Count;
        }
    }

    /// <summary>
    /// Gets the current party identifier.
    /// </summary>
    public long CurrentPartyId
    {
        get
        {
            lock (snapshotLock)
                return previousPartyId;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the player is alone (party size &lt;= 1).
    /// </summary>
    public bool IsAlone => CurrentSize <= 1;

    /// <summary>
    /// Gets a value indicating whether the party is at full capacity (8 members).
    /// </summary>
    public bool IsFull => CurrentSize >= 8;

    /// <summary>
    /// Gets a value indicating whether the player is currently in a party with at least one other member.
    /// </summary>
    public bool HasParty => CurrentSize > 1;

    /// <summary>
    /// Gets the combined current hit points of all tracked party members.
    /// </summary>
    public uint TotalCurrentHp
    {
        get
        {
            lock (snapshotLock)
                return previousMembers.Aggregate<PartyMemberSnapshot, uint>(0, (current, member) => current + member.CurrentHp);
        }
    }

    /// <summary>
    /// Gets the combined maximum hit points of all tracked party members.
    /// </summary>
    public uint TotalMaxHp
    {
        get
        {
            lock (snapshotLock)
                return previousMembers.Aggregate<PartyMemberSnapshot, uint>(0, (current, member) => current + member.MaxHp);
        }
    }

    /// <summary>
    /// Gets the average level of all tracked party members.
    /// </summary>
    public double AverageLevel
    {
        get
        {
            lock (snapshotLock)
                return previousMembers.Count == 0 ? 0d : previousMembers.Average(member => member.Level);
        }
    }

    /// <summary>
    /// Raised when the party composition changes.
    /// </summary>
    public event Action<PartyChangedEvent>? OnPartyChanged;

    /// <summary>
    /// Raised when an existing party member changes while remaining in the party.
    /// </summary>
    public event Action<PartyMemberChangedEvent>? OnPartyMemberChanged;

    /// <summary>
    /// Checks whether a player with the specified content identifier is currently in the party.
    /// </summary>
    /// <param name="contentId">The content identifier to look up.</param>
    /// <returns><see langword="true"/> if the player is in the party; otherwise, <see langword="false"/>.</returns>
    public bool IsInParty(ulong contentId)
    {
        lock (snapshotLock)
            return previousMembers.Any(m => m.ContentId == contentId);
    }

    /// <summary>
    /// Finds the first party member matching the provided predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>The matching party member snapshot, or <see langword="null"/> if not found.</returns>
    public PartyMemberSnapshot? FindMember(Func<PartyMemberSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (snapshotLock)
            return previousMembers.FirstOrDefault(predicate);
    }

    /// <summary>
    /// Finds a party member by display name (case-insensitive substring match).
    /// </summary>
    /// <param name="name">The name substring to search for.</param>
    /// <returns>The matching party member snapshot, or <see langword="null"/> if not found.</returns>
    public PartyMemberSnapshot? GetMemberByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (snapshotLock)
            return previousMembers.FirstOrDefault(m => m.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Retrieves a party member by content identifier.
    /// </summary>
    /// <param name="contentId">The content identifier to look up.</param>
    /// <returns>The matching party member snapshot, or <see langword="null"/> if not found.</returns>
    public PartyMemberSnapshot? GetMemberByContentId(ulong contentId)
    {
        lock (snapshotLock)
            return previousMembers.FirstOrDefault(member => member.ContentId == contentId);
    }

    /// <summary>
    /// Retrieves a party member by object identifier.
    /// </summary>
    /// <param name="objectId">The object identifier to look up.</param>
    /// <returns>The matching party member snapshot, or <see langword="null"/> if not found.</returns>
    public PartyMemberSnapshot? GetMemberByObjectId(uint objectId)
    {
        lock (snapshotLock)
            return previousMembers.FirstOrDefault(member => member.ObjectId == objectId);
    }

    /// <summary>
    /// Retrieves all party members whose names match the specified text.
    /// </summary>
    /// <param name="name">The text to match against party-member names.</param>
    /// <param name="exactMatch">Whether the name must match exactly instead of using a substring comparison.</param>
    /// <returns>An array of matching party member snapshots.</returns>
    public PartyMemberSnapshot[] GetMembersByName(string name, bool exactMatch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (snapshotLock)
        {
            return previousMembers
                .Where(member => exactMatch
                    ? member.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    : member.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    /// <summary>
    /// Retrieves all party members from the specified world.
    /// </summary>
    /// <param name="worldId">The world row identifier to match.</param>
    /// <returns>An array of matching party member snapshots.</returns>
    public PartyMemberSnapshot[] GetMembersByWorldId(uint worldId)
    {
        lock (snapshotLock)
            return previousMembers.Where(member => member.WorldId == worldId).ToArray();
    }

    /// <summary>
    /// Retrieves all party members using the specified class or job.
    /// </summary>
    /// <param name="classJobId">The class/job row identifier to match.</param>
    /// <returns>An array of matching party member snapshots.</returns>
    public PartyMemberSnapshot[] GetMembersByClassJobId(uint classJobId)
    {
        lock (snapshotLock)
            return previousMembers.Where(member => member.ClassJobId == classJobId).ToArray();
    }

    /// <summary>
    /// Retrieves all live player-character objects that correspond to the tracked party members.
    /// </summary>
    /// <returns>An array of matching live player characters.</returns>
    public IPlayerCharacter[] GetLiveMembers()
    {
        lock (snapshotLock)
        {
            return previousMembers
                .Where(member => member.ContentId > 0)
                .Select(member => Owner.Objects.GetPlayerCharacterByContentId(member.ContentId))
                .OfType<IPlayerCharacter>()
                .ToArray();
        }
    }

    /// <summary>
    /// Retrieves the live player-character object for the specified party member, if available.
    /// </summary>
    /// <param name="contentId">The content identifier to look up.</param>
    /// <returns>The matching live player character, or <see langword="null"/> if not found.</returns>
    public IPlayerCharacter? GetMemberCharacter(ulong contentId) => contentId > 0 ? Owner.Objects.GetPlayerCharacterByContentId(contentId) : null;

    /// <summary>
    /// Retrieves the first live player-character object whose party-member name matches the specified text.
    /// </summary>
    /// <param name="name">The text to match against party-member names.</param>
    /// <param name="exactMatch">Whether the name must match exactly instead of using a substring comparison.</param>
    /// <returns>The matching live player character, or <see langword="null"/> if not found.</returns>
    public IPlayerCharacter? GetMemberCharacter(string name, bool exactMatch = false)
    {
        var member = GetMembersByName(name, exactMatch).FirstOrDefault();
        return member == null ? null : GetMemberCharacter(member.ContentId);
    }

    /// <summary>
    /// Returns all party members matching the provided predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>An array of matching party member snapshots.</returns>
    public PartyMemberSnapshot[] FindAllMembers(Func<PartyMemberSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (snapshotLock)
            return previousMembers.Where(predicate).ToArray();
    }

    /// <summary>
    /// Checks whether the party contains a member whose name matches the specified text.
    /// </summary>
    /// <param name="name">The text to match against party-member names.</param>
    /// <param name="exactMatch">Whether the name must match exactly instead of using a substring comparison.</param>
    /// <returns><see langword="true"/> if a matching party member exists; otherwise, <see langword="false"/>.</returns>
    public bool HasMember(string name, bool exactMatch = false) => GetMembersByName(name, exactMatch).Length > 0;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the party reaches the specified minimum size.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="minimumSize">The minimum party size to wait for.</param>
    /// <returns>A predicate returning <see langword="true"/> when the party is at least the specified size.</returns>
    public Func<bool> WaitForPartySize(int minimumSize) => () => CurrentSize >= minimumSize;

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        CaptureInitialSnapshot();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(PartyTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        lock (snapshotLock)
        {
            previousMembers.Clear();
            previousPartyId = 0;
        }

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(PartyTracker)} deactivated.");
    }

    /// <inheritdoc/>
    internal override void Update()
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        var partyList = NoireService.PartyList;
        var currentMembers = new List<PartyMemberSnapshot>(partyList.Length);

        for (var i = 0; i < partyList.Length; i++)
        {
            var member = partyList[i];
            if (member == null)
                continue;

            currentMembers.Add(new PartyMemberSnapshot(
                ContentId: member.ContentId,
                ObjectId: member.EntityId,
                Name: member.Name.TextValue,
                WorldId: member.World.RowId,
                ClassJobId: member.ClassJob.RowId,
                Level: member.Level,
                CurrentHp: member.CurrentHP,
                MaxHp: member.MaxHP,
                TerritoryId: member.Territory.RowId));
        }

        lock (snapshotLock)
        {
            var currentPartyId = partyList.PartyId;
            var changed = false;
            List<PartyMemberChangedEvent> memberChangedEvents = new();

            if (currentPartyId != previousPartyId)
                changed = true;
            else if (currentMembers.Count != previousMembers.Count)
                changed = true;
            else
            {
                var prevIds = new HashSet<ulong>(previousMembers.Select(m => m.ContentId));
                var currIds = new HashSet<ulong>(currentMembers.Select(m => m.ContentId));

                if (!prevIds.SetEquals(currIds))
                    changed = true;
            }

            var previousMembersByObjectId = previousMembers.ToDictionary(member => member.ObjectId);
            foreach (var currentMember in currentMembers)
            {
                if (!previousMembersByObjectId.TryGetValue(currentMember.ObjectId, out var previousMember))
                    continue;

                if (previousMember != currentMember)
                    memberChangedEvents.Add(new PartyMemberChangedEvent(previousMember, currentMember));
            }

            if (!changed && memberChangedEvents.Count == 0)
                return;

            var prevContentIds = new HashSet<ulong>(previousMembers.Select(m => m.ContentId));
            var currContentIds = new HashSet<ulong>(currentMembers.Select(m => m.ContentId));

            var joined = currentMembers.Where(m => !prevContentIds.Contains(m.ContentId)).ToArray();
            var left = previousMembers.Where(m => !currContentIds.Contains(m.ContentId)).ToArray();

            var previousSnapshot = previousMembers.ToArray();
            previousMembers = currentMembers;
            previousPartyId = currentPartyId;

            var evt = new PartyChangedEvent(
                PreviousMembers: previousSnapshot,
                CurrentMembers: currentMembers.ToArray(),
                Joined: joined,
                Left: left);

            if (changed && Owner.EnableLogging)
                NoireLogger.LogDebug(Owner, $"Party changed: {previousSnapshot.Length} -> {currentMembers.Count} members ({joined.Length} joined, {left.Length} left).");

            if (changed)
                PublishEvent(OnPartyChanged, evt);

            foreach (var memberChangedEvent in memberChangedEvents)
                PublishEvent(OnPartyMemberChanged, memberChangedEvent);
        }
    }

    private void CaptureInitialSnapshot()
    {
        lock (snapshotLock)
        {
            previousMembers.Clear();

            if (!NoireService.ClientState.IsLoggedIn)
                return;

            var partyList = NoireService.PartyList;
            previousPartyId = partyList.PartyId;

            for (var i = 0; i < partyList.Length; i++)
            {
                var member = partyList[i];
                if (member == null)
                    continue;

                previousMembers.Add(new PartyMemberSnapshot(
                    ContentId: member.ContentId,
                    ObjectId: member.EntityId,
                    Name: member.Name.TextValue,
                    WorldId: member.World.RowId,
                    ClassJobId: member.ClassJob.RowId,
                    Level: member.Level,
                    CurrentHp: member.CurrentHP,
                    MaxHp: member.MaxHP,
                    TerritoryId: member.Territory.RowId));
            }
        }
    }
}
