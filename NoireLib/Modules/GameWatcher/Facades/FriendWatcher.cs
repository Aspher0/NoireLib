using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Friend-list facts through the game's social data - remote presence beyond the object table: a friend
/// coming online, going offline, or arriving somewhere, without them ever being near you.<br/>
/// The proxy is refreshed in the background on <see cref="GameWatcherOptions.FriendsRefreshCadence"/> (a
/// jittered cadence, floored at 30s) so facts stay current without the friend list being open - except while
/// the friend-list window <i>is</i> open,
/// when the refresh is held so it never re-sorts or scrolls the addon. Values are seconds-grained and can lag
/// reality between refreshes.
/// </summary>
public sealed class FriendWatcher : GameWatcherFacade
{
    internal FriendWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to friends coming online.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnOnline(Action<FriendOnlineEvent> handler, NoireSubscriptionOptions<FriendOnlineEvent>? options = null)
        => On(handler, null, options, nameof(OnOnline));

    /// <inheritdoc cref="OnOnline(Action{FriendOnlineEvent}, NoireSubscriptionOptions{FriendOnlineEvent}?)"/>
    public NoireSubscriptionToken OnOnlineAsync(Func<FriendOnlineEvent, Task> handler, NoireSubscriptionOptions<FriendOnlineEvent>? options = null)
        => On(null, handler, options, nameof(OnOnline));

    /// <summary>
    /// Subscribes to friends going offline.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnOffline(Action<FriendOfflineEvent> handler, NoireSubscriptionOptions<FriendOfflineEvent>? options = null)
        => On(handler, null, options, nameof(OnOffline));

    /// <inheritdoc cref="OnOffline(Action{FriendOfflineEvent}, NoireSubscriptionOptions{FriendOfflineEvent}?)"/>
    public NoireSubscriptionToken OnOfflineAsync(Func<FriendOfflineEvent, Task> handler, NoireSubscriptionOptions<FriendOfflineEvent>? options = null)
        => On(null, handler, options, nameof(OnOffline));

    /// <summary>
    /// Subscribes to friend location (territory) changes - a friend arrives somewhere, without them being in
    /// your object table.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnTerritoryChanged(Action<FriendTerritoryChangedEvent> handler, NoireSubscriptionOptions<FriendTerritoryChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnTerritoryChanged));

    /// <inheritdoc cref="OnTerritoryChanged(Action{FriendTerritoryChangedEvent}, NoireSubscriptionOptions{FriendTerritoryChangedEvent}?)"/>
    public NoireSubscriptionToken OnTerritoryChangedAsync(Func<FriendTerritoryChangedEvent, Task> handler, NoireSubscriptionOptions<FriendTerritoryChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnTerritoryChanged));

    /// <summary>
    /// Subscribes to friend-list additions.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnAdded(Action<FriendAddedEvent> handler, NoireSubscriptionOptions<FriendAddedEvent>? options = null)
        => On(handler, null, options, nameof(OnAdded));

    /// <inheritdoc cref="OnAdded(Action{FriendAddedEvent}, NoireSubscriptionOptions{FriendAddedEvent}?)"/>
    public NoireSubscriptionToken OnAddedAsync(Func<FriendAddedEvent, Task> handler, NoireSubscriptionOptions<FriendAddedEvent>? options = null)
        => On(null, handler, options, nameof(OnAdded));

    /// <summary>
    /// Subscribes to friend-list removals.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnRemoved(Action<FriendRemovedEvent> handler, NoireSubscriptionOptions<FriendRemovedEvent>? options = null)
        => On(handler, null, options, nameof(OnRemoved));

    /// <inheritdoc cref="OnRemoved(Action{FriendRemovedEvent}, NoireSubscriptionOptions{FriendRemovedEvent}?)"/>
    public NoireSubscriptionToken OnRemovedAsync(Func<FriendRemovedEvent, Task> handler, NoireSubscriptionOptions<FriendRemovedEvent>? options = null)
        => On(null, handler, options, nameof(OnRemoved));

    /// <summary>
    /// The friend list as currently known to the watcher. Returns the Friends source cache when it runs;
    /// otherwise an empty list (the game only populates its social data after a refresh request - subscribe
    /// to any friend event, or mark the source AlwaysOn, to keep the cache warm).
    /// Live read (framework thread only); never activates anything.
    /// </summary>
    public IReadOnlyList<FriendSnapshot> Current
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();

            var source = Watcher.GetSource<FriendSource>(SourceKind.Friends);
            return source.IsRunning ? source.CurrentFriends.ToArray() : Array.Empty<FriendSnapshot>();
        }
    }
}
