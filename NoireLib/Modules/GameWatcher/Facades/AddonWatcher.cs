using Dalamud.Game.Addon.Lifecycle;
using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Addon (game UI window) facts: exact lifecycle events, shown/hidden transitions, and node text watchers
/// (re-evaluated on refresh events plus a low-frequency safety poll for addons that mutate nodes without one).
/// </summary>
public sealed class AddonWatcher : GameWatcherFacade
{
    internal AddonWatcher(NoireGameWatcher watcher) : base(watcher) { }

    private NoireSubscriptionToken ForAddon<TEvent>(
        string addonName,
        Action<TEvent>? handler,
        Func<TEvent, Task>? asyncHandler,
        NoireSubscriptionOptions<TEvent>? options,
        Func<TEvent, string> selectName,
        string description)
        where TEvent : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonName);

        if (handler == null && asyncHandler == null)
            throw new ArgumentNullException(nameof(handler));

        var remove = Watcher.GetSource<AddonSource>(SourceKind.Addons).AddAddonInterest(addonName);

        return Watcher.SubscribeCore(
            handler,
            asyncHandler,
            WithFilter(options, evt => string.Equals(selectName(evt), addonName, StringComparison.Ordinal)),
            SourceKind.Addons,
            null,
            remove,
            $"{description}({addonName})");
    }

    /// <summary>
    /// Subscribes to an addon's lifecycle transitions (setup, refresh, requested update, finalize).
    /// </summary>
    /// <param name="addonName">The addon's internal name (e.g. "Talk").</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnLifecycle(string addonName, Action<AddonLifecycleEvent> handler, NoireSubscriptionOptions<AddonLifecycleEvent>? options = null)
        => ForAddon(addonName, handler, null, options, e => e.AddonName, nameof(OnLifecycle));

    /// <inheritdoc cref="OnLifecycle(string, Action{AddonLifecycleEvent}, NoireSubscriptionOptions{AddonLifecycleEvent}?)"/>
    public NoireSubscriptionToken OnLifecycleAsync(string addonName, Func<AddonLifecycleEvent, Task> handler, NoireSubscriptionOptions<AddonLifecycleEvent>? options = null)
        => ForAddon(addonName, null, handler, options, e => e.AddonName, nameof(OnLifecycle));

    /// <summary>
    /// Subscribes to an addon becoming visible.
    /// </summary>
    /// <param name="addonName">The addon's internal name.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnShown(string addonName, Action<AddonShownEvent> handler, NoireSubscriptionOptions<AddonShownEvent>? options = null)
        => ForAddon(addonName, handler, null, options, e => e.AddonName, nameof(OnShown));

    /// <inheritdoc cref="OnShown(string, Action{AddonShownEvent}, NoireSubscriptionOptions{AddonShownEvent}?)"/>
    public NoireSubscriptionToken OnShownAsync(string addonName, Func<AddonShownEvent, Task> handler, NoireSubscriptionOptions<AddonShownEvent>? options = null)
        => ForAddon(addonName, null, handler, options, e => e.AddonName, nameof(OnShown));

    /// <summary>
    /// Subscribes to an addon being hidden or finalized.
    /// </summary>
    /// <param name="addonName">The addon's internal name.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnHidden(string addonName, Action<AddonHiddenEvent> handler, NoireSubscriptionOptions<AddonHiddenEvent>? options = null)
        => ForAddon(addonName, handler, null, options, e => e.AddonName, nameof(OnHidden));

    /// <inheritdoc cref="OnHidden(string, Action{AddonHiddenEvent}, NoireSubscriptionOptions{AddonHiddenEvent}?)"/>
    public NoireSubscriptionToken OnHiddenAsync(string addonName, Func<AddonHiddenEvent, Task> handler, NoireSubscriptionOptions<AddonHiddenEvent>? options = null)
        => ForAddon(addonName, null, handler, options, e => e.AddonName, nameof(OnHidden));

    /// <summary>
    /// Watches a text node's value: the callback fires with (previous, current) whenever the text changes.
    /// Re-evaluated on the addon's refresh events plus the safety poll
    /// (<see cref="GameWatcherOptions.AddonSafetyPollInterval"/>). A null value means the addon or node is gone.
    /// </summary>
    /// <param name="addonName">The addon's internal name.</param>
    /// <param name="nodeId">The text node id.</param>
    /// <param name="onChanged">Invoked with (previous, current) on change.</param>
    /// <param name="owner">An optional owner for bulk removal.</param>
    /// <param name="key">An optional key for keyed replacement.</param>
    /// <returns>A token that stops the watcher when disposed.</returns>
    public NoireSubscriptionToken WatchNodeText(
        string addonName,
        uint nodeId,
        Action<string?, string?> onChanged,
        object? owner = null,
        string? key = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonName);
        ArgumentNullException.ThrowIfNull(onChanged);

        var remove = Watcher.GetSource<AddonSource>(SourceKind.Addons).AddNodeWatcher(new AddonSource.NodeWatcherRegistration
        {
            AddonName = addonName,
            NodeId = nodeId,
            OnChanged = onChanged,
        });

        return Watcher.RegisterExternalWatch($"WatchNodeText({addonName}#{nodeId})", SourceKind.Addons, owner, key, remove);
    }

    /// <summary>Whether an addon exists, is visible and fully loaded. Live read (framework thread only).</summary>
    /// <param name="addonName">The addon's internal name.</param>
    /// <returns>True when ready.</returns>
    public bool IsReady(string addonName)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return AddonSource.ReadIsReady(addonName);
    }

    /// <summary>Whether an addon exists and is visible. Live read (framework thread only).</summary>
    /// <param name="addonName">The addon's internal name.</param>
    /// <returns>True when visible.</returns>
    public bool IsVisible(string addonName)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return AddonSource.ReadIsVisible(addonName);
    }

    /// <summary>Reads a text node's value, or null when the addon or node is unavailable. Live read (framework thread only).</summary>
    /// <param name="addonName">The addon's internal name.</param>
    /// <param name="nodeId">The text node id.</param>
    /// <returns>The text, or null.</returns>
    public string? GetNodeText(string addonName, uint nodeId)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return AddonSource.ReadNodeText(addonName, nodeId);
    }
}
