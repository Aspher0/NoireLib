using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks the open and close lifecycle of explicitly registered addon names by polling their current state.
/// </summary>
public sealed class AddonTracker : GameStateSubTracker
{
    private readonly Dictionary<string, string> watchedTextNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> watchedNodeVisibility = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool Exists, bool Visible)> watchedComponentNodes = new(StringComparer.Ordinal);
    private readonly LinkedList<AddonStateChangedEvent> history = new();
    private readonly int historyCapacity;
    private readonly object snapshotLock = new();
    private readonly Dictionary<string, AddonStateSnapshot> trackedAddons = new(StringComparer.Ordinal);
    private IDisposable? preSetupRegistration;
    private IDisposable? postSetupRegistration;
    private IDisposable? preFinalizeRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddonTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    /// <param name="historyCapacity">The maximum number of addon state transitions to retain.</param>
    internal AddonTracker(NoireGameStateWatcher owner, bool active, int historyCapacity = 100) : base(owner, active)
    {
        this.historyCapacity = Math.Max(1, historyCapacity);
    }

    /// <summary>
    /// Gets a snapshot of every currently watched addon state.
    /// </summary>
    public IReadOnlyList<AddonStateSnapshot> TrackedAddons
    {
        get
        {
            lock (snapshotLock)
                return trackedAddons.Values.ToArray();
        }
    }

    /// <summary>
    /// Gets the configured maximum history size.
    /// </summary>
    public int HistoryCapacity => historyCapacity;

    /// <summary>
    /// Gets the current number of retained addon state transitions.
    /// </summary>
    public int HistoryCount
    {
        get
        {
            lock (snapshotLock)
                return history.Count;
        }
    }

    /// <summary>
    /// Gets the names of every currently watched addon.
    /// </summary>
    public IReadOnlyList<string> TrackedAddonNames
    {
        get
        {
            lock (snapshotLock)
                return trackedAddons.Keys.ToArray();
        }
    }

    /// <summary>
    /// Raised when a watched addon changes state.
    /// </summary>
    public event Action<AddonStateChangedEvent>? OnAddonStateChanged;

    /// <summary>
    /// Raised when a watched addon opens.
    /// </summary>
    public event Action<AddonOpenedEvent>? OnAddonOpened;

    /// <summary>
    /// Raised when a tracked addon reaches its creation lifecycle stage.
    /// </summary>
    public event Action<AddonCreatedEvent>? OnAddonCreated;

    /// <summary>
    /// Raised when a tracked addon completes setup.
    /// </summary>
    public event Action<AddonSetupEvent>? OnAddonSetup;

    /// <summary>
    /// Raised when a watched addon becomes available in memory.
    /// </summary>
    public event Action<AddonAvailableEvent>? OnAddonAvailable;

    /// <summary>
    /// Raised when a watched addon becomes unavailable in memory.
    /// </summary>
    public event Action<AddonUnavailableEvent>? OnAddonUnavailable;

    /// <summary>
    /// Raised when a watched addon becomes visible.
    /// </summary>
    public event Action<AddonShownEvent>? OnAddonShown;

    /// <summary>
    /// Raised when a watched addon becomes hidden.
    /// </summary>
    public event Action<AddonHiddenEvent>? OnAddonHidden;

    /// <summary>
    /// Raised when a watched addon becomes ready for interaction.
    /// </summary>
    public event Action<AddonReadyEvent>? OnAddonReady;

    /// <summary>
    /// Raised when a watched addon is no longer ready for interaction.
    /// </summary>
    public event Action<AddonUnreadyEvent>? OnAddonUnready;

    /// <summary>
    /// Raised when a tracked addon enters finalization.
    /// </summary>
    public event Action<AddonFinalizedEvent>? OnAddonFinalized;

    /// <summary>
    /// Raised when a watched addon closes.
    /// </summary>
    public event Action<AddonClosedEvent>? OnAddonClosed;

    /// <summary>
    /// Raised when a watched addon text node changes.
    /// </summary>
    public event Action<AddonNodeTextChangedEvent>? OnAddonNodeTextChanged;

    /// <summary>
    /// Raised when a watched addon node visibility changes.
    /// </summary>
    public event Action<AddonNodeVisibilityChangedEvent>? OnAddonNodeVisibilityChanged;

    /// <summary>
    /// Raised when a watched addon component node state changes.
    /// </summary>
    public event Action<AddonComponentNodeChangedEvent>? OnAddonComponentNodeChanged;

    /// <summary>
    /// Returns a snapshot of retained addon state transitions, newest first.
    /// </summary>
    /// <returns>An array of retained addon state transitions.</returns>
    public AddonStateChangedEvent[] GetRecentStateChanges()
    {
        lock (snapshotLock)
            return history.ToArray();
    }

    /// <summary>
    /// Returns retained state transitions for the supplied addon name, newest first.
    /// </summary>
    /// <param name="addonName">The addon name to filter by.</param>
    /// <returns>An array of retained transitions for the supplied addon.</returns>
    public AddonStateChangedEvent[] GetRecentStateChanges(string addonName)
    {
        ValidateAddonName(addonName);

        lock (snapshotLock)
            return history.Where(entry => entry.Current.AddonName.Equals(addonName, StringComparison.Ordinal)).ToArray();
    }

    /// <summary>
    /// Returns the most recent retained addon state transitions, newest first.
    /// </summary>
    /// <param name="maxCount">The maximum number of transitions to return.</param>
    /// <returns>An array containing up to <paramref name="maxCount"/> retained transitions.</returns>
    public AddonStateChangedEvent[] GetRecentStateChanges(int maxCount)
    {
        if (maxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        lock (snapshotLock)
            return history.Take(maxCount).ToArray();
    }

    /// <summary>
    /// Starts watching the supplied addon name.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns><see langword="true"/> if the addon name was newly added; otherwise, <see langword="false"/>.</returns>
    public bool WatchAddon(string addonName)
    {
        ValidateAddonName(addonName);

        lock (snapshotLock)
        {
            var isNew = !trackedAddons.ContainsKey(addonName);
            trackedAddons[addonName] = CaptureAddonState(addonName);
            return isNew;
        }
    }

    /// <summary>
    /// Starts watching multiple addon names.
    /// </summary>
    /// <param name="addonNames">The addon names to watch.</param>
    /// <returns>The number of addon names that were newly added.</returns>
    public int WatchAddons(IEnumerable<string> addonNames)
    {
        ArgumentNullException.ThrowIfNull(addonNames);

        var added = 0;

        foreach (var addonName in addonNames)
        {
            if (WatchAddon(addonName))
                added++;
        }

        return added;
    }

    /// <summary>
    /// Stops watching the supplied addon name.
    /// </summary>
    /// <param name="addonName">The addon name to stop watching.</param>
    /// <returns><see langword="true"/> if the addon name was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnwatchAddon(string addonName)
    {
        ValidateAddonName(addonName);

        lock (snapshotLock)
            return trackedAddons.Remove(addonName);
    }

    /// <summary>
    /// Stops watching every addon name.
    /// </summary>
    public void ClearWatchedAddons()
    {
        lock (snapshotLock)
            trackedAddons.Clear();
    }

    /// <summary>
    /// Determines whether the supplied addon name is currently being watched.
    /// </summary>
    /// <param name="addonName">The addon name to inspect.</param>
    /// <returns><see langword="true"/> if the addon is being watched; otherwise, <see langword="false"/>.</returns>
    public bool IsWatched(string addonName)
    {
        ValidateAddonName(addonName);

        lock (snapshotLock)
            return trackedAddons.ContainsKey(addonName);
    }

    /// <summary>
    /// Retrieves the last captured state for the supplied watched addon.
    /// </summary>
    /// <param name="addonName">The addon name to inspect.</param>
    /// <returns>The last captured addon-state snapshot, or <see langword="null"/> if the addon is not being watched.</returns>
    public AddonStateSnapshot? GetAddonState(string addonName)
    {
        ValidateAddonName(addonName);

        lock (snapshotLock)
            return trackedAddons.TryGetValue(addonName, out var snapshot) ? snapshot : null;
    }

    /// <summary>
    /// Determines whether the supplied addon currently exists in memory.
    /// </summary>
    /// <param name="addonName">The addon name to inspect.</param>
    /// <returns><see langword="true"/> if the addon exists; otherwise, <see langword="false"/>.</returns>
    public bool IsAddonAvailable(string addonName) => CaptureAddonState(addonName).IsAvailable;

    /// <summary>
    /// Determines whether the supplied addon is currently visible.
    /// </summary>
    /// <param name="addonName">The addon name to inspect.</param>
    /// <returns><see langword="true"/> if the addon is visible; otherwise, <see langword="false"/>.</returns>
    public bool IsAddonVisible(string addonName) => CaptureAddonState(addonName).IsVisible;

    /// <summary>
    /// Determines whether the supplied addon is currently ready for interaction.
    /// </summary>
    /// <param name="addonName">The addon name to inspect.</param>
    /// <returns><see langword="true"/> if the addon is ready; otherwise, <see langword="false"/>.</returns>
    public bool IsAddonReady(string addonName) => CaptureAddonState(addonName).IsReady;

    /// <summary>
    /// Starts watching a text node within the supplied addon.
    /// </summary>
    /// <param name="addonName">The addon name that owns the node.</param>
    /// <param name="nodeIds">The node-id chain used to resolve the node.</param>
    /// <returns><see langword="true"/> if the watch was newly added; otherwise, <see langword="false"/>.</returns>
    public bool WatchTextNode(string addonName, params int[] nodeIds)
    {
        ValidateAddonName(addonName);
        ValidateNodeIds(nodeIds);

        var key = CreateNodeWatchKey(addonName, nodeIds);
        var currentText = TryReadNodeText(addonName, nodeIds, out var text) ? text : string.Empty;

        lock (snapshotLock)
        {
            var isNew = !watchedTextNodes.ContainsKey(key);
            watchedTextNodes[key] = currentText;
            return isNew;
        }
    }

    /// <summary>
    /// Starts watching visibility for a node within the supplied addon.
    /// </summary>
    /// <param name="addonName">The addon name that owns the node.</param>
    /// <param name="nodeIds">The node-id chain used to resolve the node.</param>
    /// <returns><see langword="true"/> if the watch was newly added; otherwise, <see langword="false"/>.</returns>
    public bool WatchNodeVisibility(string addonName, params int[] nodeIds)
    {
        ValidateAddonName(addonName);
        ValidateNodeIds(nodeIds);

        var key = CreateNodeWatchKey(addonName, nodeIds);
        var visible = TryReadNodeVisibility(addonName, nodeIds, out var isVisible) && isVisible;

        lock (snapshotLock)
        {
            var isNew = !watchedNodeVisibility.ContainsKey(key);
            watchedNodeVisibility[key] = visible;
            return isNew;
        }
    }

    /// <summary>
    /// Starts watching a component node within the supplied addon.
    /// </summary>
    /// <param name="addonName">The addon name that owns the component node.</param>
    /// <param name="nodeIds">The node-id chain used to resolve the node.</param>
    /// <returns><see langword="true"/> if the watch was newly added; otherwise, <see langword="false"/>.</returns>
    public bool WatchComponentNode(string addonName, params int[] nodeIds)
    {
        ValidateAddonName(addonName);
        ValidateNodeIds(nodeIds);

        var key = CreateNodeWatchKey(addonName, nodeIds);
        var state = ReadComponentNodeState(addonName, nodeIds);

        lock (snapshotLock)
        {
            var isNew = !watchedComponentNodes.ContainsKey(key);
            watchedComponentNodes[key] = state;
            return isNew;
        }
    }

    /// <summary>
    /// Stops watching a text node within the supplied addon.
    /// </summary>
    /// <param name="addonName">The addon name that owns the node.</param>
    /// <param name="nodeIds">The node-id chain used to resolve the node.</param>
    /// <returns><see langword="true"/> if the watch was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnwatchTextNode(string addonName, params int[] nodeIds)
    {
        ValidateAddonName(addonName);
        ValidateNodeIds(nodeIds);

        lock (snapshotLock)
            return watchedTextNodes.Remove(CreateNodeWatchKey(addonName, nodeIds));
    }

    /// <summary>
    /// Stops watching a node visibility within the supplied addon.
    /// </summary>
    /// <param name="addonName">The addon name that owns the node.</param>
    /// <param name="nodeIds">The node-id chain used to resolve the node.</param>
    /// <returns><see langword="true"/> if the watch was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnwatchNodeVisibility(string addonName, params int[] nodeIds)
    {
        ValidateAddonName(addonName);
        ValidateNodeIds(nodeIds);

        lock (snapshotLock)
            return watchedNodeVisibility.Remove(CreateNodeWatchKey(addonName, nodeIds));
    }

    /// <summary>
    /// Stops watching a component node within the supplied addon.
    /// </summary>
    /// <param name="addonName">The addon name that owns the component node.</param>
    /// <param name="nodeIds">The node-id chain used to resolve the node.</param>
    /// <returns><see langword="true"/> if the watch was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnwatchComponentNode(string addonName, params int[] nodeIds)
    {
        ValidateAddonName(addonName);
        ValidateNodeIds(nodeIds);

        lock (snapshotLock)
            return watchedComponentNodes.Remove(CreateNodeWatchKey(addonName, nodeIds));
    }

    /// <summary>
    /// Determines whether the supplied addon is currently open and interactable.
    /// </summary>
    /// <param name="addonName">The addon name to inspect.</param>
    /// <returns><see langword="true"/> if the addon is open; otherwise, <see langword="false"/>.</returns>
    public bool IsAddonOpen(string addonName) => CaptureAddonState(addonName).IsOpen;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied addon is open.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns>A predicate returning <see langword="true"/> when the addon is open.</returns>
    public Func<bool> WaitForAddonOpen(string addonName) => () => IsAddonOpen(addonName);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied addon exists in memory.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns>A predicate returning <see langword="true"/> when the addon exists.</returns>
    public Func<bool> WaitForAddonAvailable(string addonName) => () => IsAddonAvailable(addonName);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied addon becomes visible.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns>A predicate returning <see langword="true"/> when the addon is visible.</returns>
    public Func<bool> WaitForAddonVisible(string addonName) => () => IsAddonVisible(addonName);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied addon becomes ready for interaction.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns>A predicate returning <see langword="true"/> when the addon is ready.</returns>
    public Func<bool> WaitForAddonReady(string addonName) => () => IsAddonReady(addonName);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied addon is closed.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns>A predicate returning <see langword="true"/> when the addon is closed.</returns>
    public Func<bool> WaitForAddonClose(string addonName) => () => !IsAddonOpen(addonName);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied addon becomes hidden.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns>A predicate returning <see langword="true"/> when the addon is hidden.</returns>
    public Func<bool> WaitForAddonHidden(string addonName) => () => !IsAddonVisible(addonName);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied addon becomes unavailable in memory.
    /// </summary>
    /// <param name="addonName">The addon name to watch.</param>
    /// <returns>A predicate returning <see langword="true"/> when the addon is unavailable.</returns>
    public Func<bool> WaitForAddonUnavailable(string addonName) => () => !IsAddonAvailable(addonName);

    /// <summary>
    /// Subscribes to open notifications for the supplied addon name.
    /// </summary>
    /// <param name="addonName">The addon name to filter by.</param>
    /// <param name="callback">The callback to invoke.</param>
    /// <returns>A disposable subscription that unregisters the callback when disposed.</returns>
    public IDisposable SubscribeToAddonOpen(string addonName, Action<AddonOpenedEvent> callback)
    {
        ValidateAddonName(addonName);
        ArgumentNullException.ThrowIfNull(callback);
        return Subscribe<AddonOpenedEvent>(evt => evt.Addon.AddonName.Equals(addonName, StringComparison.Ordinal), callback);
    }

    /// <summary>
    /// Subscribes to close notifications for the supplied addon name.
    /// </summary>
    /// <param name="addonName">The addon name to filter by.</param>
    /// <param name="callback">The callback to invoke.</param>
    /// <returns>A disposable subscription that unregisters the callback when disposed.</returns>
    public IDisposable SubscribeToAddonClose(string addonName, Action<AddonClosedEvent> callback)
    {
        ValidateAddonName(addonName);
        ArgumentNullException.ThrowIfNull(callback);
        return Subscribe<AddonClosedEvent>(evt => evt.Addon.AddonName.Equals(addonName, StringComparison.Ordinal), callback);
    }

    /// <summary>
    /// Subscribes to ready notifications for the supplied addon name.
    /// </summary>
    /// <param name="addonName">The addon name to filter by.</param>
    /// <param name="callback">The callback to invoke.</param>
    /// <returns>A disposable subscription that unregisters the callback when disposed.</returns>
    public IDisposable SubscribeToAddonReady(string addonName, Action<AddonReadyEvent> callback)
    {
        ValidateAddonName(addonName);
        ArgumentNullException.ThrowIfNull(callback);
        return Subscribe<AddonReadyEvent>(evt => evt.Addon.AddonName.Equals(addonName, StringComparison.Ordinal), callback);
    }

    /// <summary>
    /// Subscribes to shown notifications for the supplied addon name.
    /// </summary>
    /// <param name="addonName">The addon name to filter by.</param>
    /// <param name="callback">The callback to invoke.</param>
    /// <returns>A disposable subscription that unregisters the callback when disposed.</returns>
    public IDisposable SubscribeToAddonShown(string addonName, Action<AddonShownEvent> callback)
    {
        ValidateAddonName(addonName);
        ArgumentNullException.ThrowIfNull(callback);
        return Subscribe<AddonShownEvent>(evt => evt.Addon.AddonName.Equals(addonName, StringComparison.Ordinal), callback);
    }

    /// <summary>
    /// Clears retained addon transition history.
    /// </summary>
    public void ClearHistory()
    {
        lock (snapshotLock)
            history.Clear();
    }

    /// <summary>
    /// Clears every watched addon node registration.
    /// </summary>
    public void ClearNodeWatchers()
    {
        lock (snapshotLock)
        {
            watchedTextNodes.Clear();
            watchedNodeVisibility.Clear();
            watchedComponentNodes.Clear();
        }
    }

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        preSetupRegistration = AddonHelper.RegisterLifecycleListener(AddonEvent.PreSetup, HandleAddonPreSetup);
        postSetupRegistration = AddonHelper.RegisterLifecycleListener(AddonEvent.PostSetup, HandleAddonPostSetup);
        preFinalizeRegistration = AddonHelper.RegisterLifecycleListener(AddonEvent.PreFinalize, HandleAddonPreFinalize);

        lock (snapshotLock)
        {
            foreach (var addonName in trackedAddons.Keys.ToArray())
                trackedAddons[addonName] = CaptureAddonState(addonName);
        }

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(AddonTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        preSetupRegistration?.Dispose();
        postSetupRegistration?.Dispose();
        preFinalizeRegistration?.Dispose();
        preSetupRegistration = null;
        postSetupRegistration = null;
        preFinalizeRegistration = null;

        lock (snapshotLock)
        {
            history.Clear();
            watchedTextNodes.Clear();
            watchedNodeVisibility.Clear();
            watchedComponentNodes.Clear();
        }

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(AddonTracker)} deactivated.");
    }

    /// <inheritdoc/>
    internal override void Update()
    {
        KeyValuePair<string, AddonStateSnapshot>[] snapshots;

        lock (snapshotLock)
            snapshots = trackedAddons.ToArray();

        foreach (var (addonName, previousState) in snapshots)
        {
            var currentState = CaptureAddonState(addonName);

            lock (snapshotLock)
                trackedAddons[addonName] = currentState;

            if (currentState.HasSameObservedState(previousState))
                continue;

            var stateChangedEvent = new AddonStateChangedEvent(previousState, currentState);

            lock (snapshotLock)
            {
                history.AddFirst(stateChangedEvent);

                while (history.Count > historyCapacity)
                    history.RemoveLast();
            }

            PublishEvent(OnAddonStateChanged, stateChangedEvent);

            if (!previousState.IsAvailable && currentState.IsAvailable)
                PublishEvent(OnAddonAvailable, new AddonAvailableEvent(currentState));
            else if (previousState.IsAvailable && !currentState.IsAvailable)
                PublishEvent(OnAddonUnavailable, new AddonUnavailableEvent(currentState));

            if (!previousState.IsVisible && currentState.IsVisible)
                PublishEvent(OnAddonShown, new AddonShownEvent(currentState));
            else if (previousState.IsVisible && !currentState.IsVisible)
                PublishEvent(OnAddonHidden, new AddonHiddenEvent(currentState));

            if (!previousState.IsReady && currentState.IsReady)
                PublishEvent(OnAddonReady, new AddonReadyEvent(currentState));
            else if (previousState.IsReady && !currentState.IsReady)
                PublishEvent(OnAddonUnready, new AddonUnreadyEvent(currentState));

            if (!previousState.IsOpen && currentState.IsOpen)
                PublishEvent(OnAddonOpened, new AddonOpenedEvent(currentState));
            else if (previousState.IsOpen && !currentState.IsOpen)
                PublishEvent(OnAddonClosed, new AddonClosedEvent(currentState));
        }

        UpdateTextNodeWatchers();
        UpdateVisibilityNodeWatchers();
        UpdateComponentNodeWatchers();
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        preSetupRegistration?.Dispose();
        postSetupRegistration?.Dispose();
        preFinalizeRegistration?.Dispose();
    }

    private static void ValidateAddonName(string addonName)
    {
        if (string.IsNullOrWhiteSpace(addonName))
            throw new ArgumentException("Addon name cannot be null or whitespace.", nameof(addonName));
    }

    private static void ValidateNodeIds(int[] nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        if (nodeIds.Length == 0)
            throw new ArgumentException("At least one node id must be supplied.", nameof(nodeIds));
    }

    private void HandleAddonPreSetup(AddonEvent eventType, AddonArgs addonArgs)
    {
        if (!IsTrackedLifecycleAddon(addonArgs.AddonName))
            return;

        PublishEvent(OnAddonCreated, new AddonCreatedEvent(CaptureAddonState(addonArgs.AddonName)));
    }

    private void HandleAddonPostSetup(AddonEvent eventType, AddonArgs addonArgs)
    {
        if (!IsTrackedLifecycleAddon(addonArgs.AddonName))
            return;

        PublishEvent(OnAddonSetup, new AddonSetupEvent(CaptureAddonState(addonArgs.AddonName)));
    }

    private void HandleAddonPreFinalize(AddonEvent eventType, AddonArgs addonArgs)
    {
        if (!IsTrackedLifecycleAddon(addonArgs.AddonName))
            return;

        PublishEvent(OnAddonFinalized, new AddonFinalizedEvent(CaptureAddonState(addonArgs.AddonName)));
    }

    private bool IsTrackedLifecycleAddon(string addonName)
    {
        if (string.IsNullOrWhiteSpace(addonName))
            return false;

        lock (snapshotLock)
            return trackedAddons.ContainsKey(addonName);
    }

    private void UpdateTextNodeWatchers()
    {
        KeyValuePair<string, string>[] watchers;

        lock (snapshotLock)
            watchers = watchedTextNodes.ToArray();

        foreach (var (key, previousText) in watchers)
        {
            ParseNodeWatchKey(key, out var addonName, out var nodeIds);
            var currentText = TryReadNodeText(addonName, nodeIds, out var text) ? text : string.Empty;

            if (string.Equals(previousText, currentText, StringComparison.Ordinal))
                continue;

            lock (snapshotLock)
                watchedTextNodes[key] = currentText;

            PublishEvent(OnAddonNodeTextChanged, new AddonNodeTextChangedEvent(addonName, nodeIds, previousText, currentText));
        }
    }

    private void UpdateVisibilityNodeWatchers()
    {
        KeyValuePair<string, bool>[] watchers;

        lock (snapshotLock)
            watchers = watchedNodeVisibility.ToArray();

        foreach (var (key, previousVisible) in watchers)
        {
            ParseNodeWatchKey(key, out var addonName, out var nodeIds);
            var currentVisible = TryReadNodeVisibility(addonName, nodeIds, out var isVisible) && isVisible;

            if (previousVisible == currentVisible)
                continue;

            lock (snapshotLock)
                watchedNodeVisibility[key] = currentVisible;

            PublishEvent(OnAddonNodeVisibilityChanged, new AddonNodeVisibilityChangedEvent(addonName, nodeIds, previousVisible, currentVisible));
        }
    }

    private void UpdateComponentNodeWatchers()
    {
        KeyValuePair<string, (bool Exists, bool Visible)>[] watchers;

        lock (snapshotLock)
            watchers = watchedComponentNodes.ToArray();

        foreach (var (key, previousState) in watchers)
        {
            ParseNodeWatchKey(key, out var addonName, out var nodeIds);
            var currentState = ReadComponentNodeState(addonName, nodeIds);

            if (previousState == currentState)
                continue;

            lock (snapshotLock)
                watchedComponentNodes[key] = currentState;

            PublishEvent(OnAddonComponentNodeChanged, new AddonComponentNodeChangedEvent(addonName, nodeIds, previousState.Exists, currentState.Exists, previousState.Visible, currentState.Visible));
        }
    }

    private static string CreateNodeWatchKey(string addonName, IReadOnlyList<int> nodeIds)
        => $"{addonName}|{string.Join('.', nodeIds)}";

    private static void ParseNodeWatchKey(string key, out string addonName, out int[] nodeIds)
    {
        var parts = key.Split('|', 2, StringSplitOptions.None);
        addonName = parts[0];
        nodeIds = parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1])
            ? []
            : parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    }

    private static unsafe bool TryReadNodeText(string addonName, int[] nodeIds, out string text)
    {
        text = string.Empty;

        return AddonHelper.TryGetReadyAddon(addonName, out var addonPtr)
            && AddonHelper.TryReadText(addonPtr, out text, nodeIds);
    }

    private static unsafe bool TryReadNodeVisibility(string addonName, int[] nodeIds, out bool isVisible)
    {
        isVisible = false;

        return AddonHelper.TryGetReadyAddon(addonName, out var addonPtr)
            && AddonHelper.TryGetNode(addonPtr, out var nodePtr, nodeIds)
            && (isVisible = nodePtr->IsVisible()) == nodePtr->IsVisible();
    }

    private static unsafe (bool Exists, bool Visible) ReadComponentNodeState(string addonName, int[] nodeIds)
    {
        if (!AddonHelper.TryGetReadyAddon(addonName, out var addonPtr)
            || !AddonHelper.TryGetComponentNode(addonPtr, out var componentNodePtr, nodeIds))
            return (false, false);

        return (true, componentNodePtr->AtkResNode.IsVisible());
    }

    private static unsafe AddonStateSnapshot CaptureAddonState(string addonName)
    {
        ValidateAddonName(addonName);

        if (!AddonHelper.TryGetAddon(addonName, out AtkUnitBase* addonPtr))
            return new AddonStateSnapshot(addonName, false, false, false, false, DateTimeOffset.UtcNow);

        var isLoaded = addonPtr->UldManager.LoadedState == AtkLoadState.Loaded;
        var isVisible = addonPtr->IsVisible;
        var isReady = addonPtr->IsReady && addonPtr->IsFullyLoaded();

        return new AddonStateSnapshot(addonName, true, isLoaded, isVisible, isReady, DateTimeOffset.UtcNow);
    }
}
