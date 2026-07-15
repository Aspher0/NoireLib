namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when a friend comes online. Friend data follows the social-list refresh cadence - seconds-grained.
/// </summary>
/// <param name="Friend">The friend's current snapshot.</param>
public sealed record FriendOnlineEvent(FriendSnapshot Friend);

/// <summary>
/// Fired when a friend goes offline.
/// </summary>
/// <param name="Friend">The friend's current snapshot.</param>
public sealed record FriendOfflineEvent(FriendSnapshot Friend);

/// <summary>
/// Fired when a friend's location (territory) changes - remote presence beyond the object table.
/// </summary>
/// <param name="Friend">The friend's current snapshot.</param>
/// <param name="PreviousTerritoryId">The previous territory row id.</param>
/// <param name="NewTerritoryId">The new territory row id.</param>
public sealed record FriendTerritoryChangedEvent(FriendSnapshot Friend, uint PreviousTerritoryId, uint NewTerritoryId);

/// <summary>
/// Fired when an entry is added to the friend list.
/// </summary>
/// <param name="Friend">The added friend.</param>
public sealed record FriendAddedEvent(FriendSnapshot Friend);

/// <summary>
/// Fired when an entry is removed from the friend list.
/// </summary>
/// <param name="Friend">The removed friend's last snapshot.</param>
public sealed record FriendRemovedEvent(FriendSnapshot Friend);
