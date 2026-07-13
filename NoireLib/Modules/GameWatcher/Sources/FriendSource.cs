using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// Snapshots the friend list through the game's social data (info proxy) — remote presence beyond the object
/// table: online state and location changes for friends anywhere.<br/>
/// The list is <b>not guaranteed fresh</b>: the source requests a refresh on activation and re-requests at
/// <see cref="GameWatcherOptions.FriendsRefreshInterval"/>. Between refreshes, values can lag reality.
/// </summary>
internal sealed class FriendSource : GameWatcherSource
{
    private readonly Dictionary<ulong, FriendSnapshot> friends = new();
    private DateTimeOffset nextRefreshRequest = DateTimeOffset.MinValue;
    private bool seeded;
    private bool resyncPending;

    public FriendSource(NoireGameWatcher owner) : base(owner, SourceKind.Friends) { }

    /// <inheritdoc/>
    protected override TimeSpan DefaultPollCadence => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        friends.Clear();
        seeded = false;
        resyncPending = false;
        nextRefreshRequest = DateTimeOffset.MinValue;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        friends.Clear();
        seeded = false;
        resyncPending = false;
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        if (now >= nextRefreshRequest)
        {
            nextRefreshRequest = now + Owner.ActiveOptions.FriendsRefreshInterval;
            RequestRefresh();
        }

        var current = ReadFriends(now);

        if (current == null)
            return;

        // The game only keeps the friend info-proxy fully populated while the friend list is open (or briefly
        // after a RequestData). Between those windows CharDataSpan reads back empty, then repopulates wholesale.
        // Diffing across those transitions would fire a remove-everyone storm on clear and an add-everyone storm
        // on repopulate, so an empty read is treated as "data not live right now": keep the last-known baseline
        // and mark a resync, and the next populated read re-seeds silently instead of emitting a storm. Real
        // online/offline/territory changes still diff correctly while the data stays live (list open).
        if (current.Count == 0)
        {
            if (friends.Count > 0)
                resyncPending = true;

            return;
        }

        if (!seeded || resyncPending)
        {
            // Silent (re)seed — baseline refresh after activation or an empty/stale window, no events.
            seeded = true;
            resyncPending = false;

            friends.Clear();

            foreach (var (contentId, friend) in current)
                friends[contentId] = friend;

            return;
        }

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

        List<ulong>? removed = null;

        foreach (var contentId in friends.Keys)
        {
            if (!current.ContainsKey(contentId))
                (removed ??= new List<ulong>()).Add(contentId);
        }

        if (removed != null)
        {
            foreach (var contentId in removed)
            {
                Owner.DispatchEvent(new FriendRemovedEvent(friends[contentId]));
                friends.Remove(contentId);
            }
        }

        foreach (var (contentId, friend) in current)
            friends[contentId] = friend;
    }

    /// <summary>The current friend snapshots, for facade queries.</summary>
    internal IReadOnlyCollection<FriendSnapshot> CurrentFriends => friends.Values;

    private static unsafe void RequestRefresh()
    {
        var proxy = InfoProxyFriendList.Instance();
        proxy->RequestData();
    }

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
