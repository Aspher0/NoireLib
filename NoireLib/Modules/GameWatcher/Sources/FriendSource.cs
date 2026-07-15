using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// Observes the friend list through the game's social data (info proxy) - remote presence beyond the object
/// table: online state and location changes for friends anywhere.<br/>
/// The proxy is refreshed in the background (<c>RequestData</c>) so friend facts stay current without the
/// friend list being open - but the refresh is <b>skipped while the friend-list window is open</b>, because
/// refreshing then re-sorts and scrolls the addon. While the window is open the game keeps the list live
/// anyway, so the passive reads still see updates. Values are seconds-grained and can lag reality.
/// </summary>
internal sealed class FriendSource : GameWatcherSource
{
    private const string FriendListAddonName = "FriendList";

    private readonly Dictionary<ulong, FriendSnapshot> friends = new();
    private readonly Random rng = new();
    private bool seeded;
    private bool hasSignature;
    private ulong lastSignature;
    private DateTimeOffset nextRefreshRequest = DateTimeOffset.MinValue;

    public FriendSource(NoireGameWatcher owner) : base(owner, SourceKind.Friends) { }

    /// <inheritdoc/>
    protected override TimeSpan DefaultPollCadence => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        friends.Clear();
        seeded = false;
        hasSignature = false;
        lastSignature = 0;
        nextRefreshRequest = DateTimeOffset.MinValue;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        friends.Clear();
        seeded = false;
        hasSignature = false;
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        MaybeRequestRefresh(now);

        var current = ReadFriends(now);

        if (current == null || current.Count == 0)
        {
            // Proxy not loaded (friend list never opened, or cleared): nothing reliable to diff. Keep the last
            // baseline so a genuine change is caught when the game next loads the list, and reset the stability
            // tracker so a partial reload is not mistaken for a settled snapshot.
            hasSignature = false;
            return;
        }

        // The game repopulates the proxy over several frames (paged), so a single read can be a half-loaded
        // list. Diffing those partial reads is what produced the add/remove storm on friend-list open. Guard
        // against it: only act once the set has settled - a read whose order-independent signature matches the
        // previous read is treated as a complete, stable snapshot.
        var signature = ComputeSignature(current);

        if (!hasSignature || signature != lastSignature)
        {
            lastSignature = signature;
            hasSignature = true;
            return;
        }

        if (!seeded)
        {
            // First stable snapshot seeds the baseline silently - subscribers observe changes from now on.
            seeded = true;
            Replace(current);
            return;
        }

        // Diff against the last stable baseline (never a silent reseed): a change that happened while the list
        // was closed/stale - a friend going offline, moving world - surfaces on the next settled load.
        DiffAndReplace(current);
    }

    private void DiffAndReplace(Dictionary<ulong, FriendSnapshot> current)
    {
        foreach (var (contentId, friend) in current)
        {
            if (!friends.TryGetValue(contentId, out var previous))
            {
                Owner.DispatchEvent(new FriendAddedEvent(friend));
                continue;
            }

            if (!previous.IsOnline && friend.IsOnline)
                Owner.DispatchEvent(new FriendOnlineEvent(friend));
            else if (previous.IsOnline && !friend.IsOnline)
                Owner.DispatchEvent(new FriendOfflineEvent(friend));

            if (previous.TerritoryId != friend.TerritoryId)
                Owner.DispatchEvent(new FriendTerritoryChangedEvent(friend, previous.TerritoryId, friend.TerritoryId));
        }

        List<FriendSnapshot>? removed = null;

        foreach (var (contentId, previous) in friends)
        {
            if (!current.ContainsKey(contentId))
                (removed ??= new List<FriendSnapshot>()).Add(previous);
        }

        if (removed != null)
        {
            foreach (var friend in removed)
                Owner.DispatchEvent(new FriendRemovedEvent(friend));
        }

        Replace(current);
    }

    private void Replace(Dictionary<ulong, FriendSnapshot> current)
    {
        friends.Clear();

        foreach (var (contentId, friend) in current)
            friends[contentId] = friend;
    }

    /// <summary>
    /// Refreshes the social proxy on the configured interval - but only while the friend-list window is closed,
    /// so a background refresh never re-sorts or scrolls the addon the player is looking at. While the window is
    /// open the timer is held (not advanced), so the first refresh fires as soon as it closes.
    /// </summary>
    private void MaybeRequestRefresh(DateTimeOffset now)
    {
        if (now < nextRefreshRequest)
            return;

        if (AddonSource.ReadIsVisible(FriendListAddonName))
            return;

        nextRefreshRequest = now + Owner.ActiveOptions.FriendsRefreshCadence.Next(rng);
        RequestRefresh();
    }

    private static unsafe void RequestRefresh()
    {
        var proxy = InfoProxyFriendList.Instance();

        if (proxy != null)
            proxy->RequestData();
    }

    /// <summary>
    /// An order-independent signature of the friend set: a reordered-but-identical list hashes the same, and
    /// any membership / online / territory / world change alters it. Used only to detect a settled snapshot.
    /// </summary>
    private static ulong ComputeSignature(Dictionary<ulong, FriendSnapshot> current)
    {
        ulong signature = (ulong)current.Count * 0x9E3779B97F4A7C15UL;

        foreach (var (contentId, friend) in current)
        {
            var entry = contentId;
            entry = entry * 31 + (friend.IsOnline ? 1UL : 0UL);
            entry = entry * 31 + friend.TerritoryId;
            entry = entry * 31 + friend.CurrentWorldId;

            unchecked
            {
                signature += entry;
            }
        }

        return signature;
    }

    /// <summary>The current friend snapshots, for facade queries.</summary>
    internal IReadOnlyCollection<FriendSnapshot> CurrentFriends => friends.Values;

    private static unsafe Dictionary<ulong, FriendSnapshot>? ReadFriends(DateTimeOffset now)
    {
        var proxy = InfoProxyFriendList.Instance();

        if (proxy == null)
            return null;

        var result = new Dictionary<ulong, FriendSnapshot>();
        var span = proxy->CharDataSpan;

        foreach (ref readonly var data in span)
        {
            if (data.ContentId == 0)
                continue;

            var isOnline = (data.State & InfoProxyCommonList.CharacterData.OnlineStatus.Online) != 0;

            result[data.ContentId] = new FriendSnapshot
            {
                ContentId = data.ContentId,
                Name = data.NameString,
                HomeWorldId = data.HomeWorld,
                CurrentWorldId = isOnline ? data.CurrentWorld : 0u,
                TerritoryId = isOnline ? data.Location : 0u,
                ClassJobId = isOnline ? data.Job : 0u,
                IsOnline = isOnline,
                CapturedAt = now,
            };
        }

        return result;
    }
}
