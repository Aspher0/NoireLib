using Dalamud.Plugin.Services;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// A module that provides unified observation of game state through independently toggleable sub-trackers.
/// </summary>
public class NoireGameStateWatcher : NoireModuleBase<NoireGameStateWatcher>
{
    private readonly Dictionary<Type, Dictionary<string, (int Priority, Delegate Callback)>> callbacksByEventType = new();
    private readonly object callbackLock = new();
    private readonly GameStateWatcherConfig config;
    private EventWrapper<IFramework.OnUpdateDelegate> frameworkUpdateEvent = null!;

    /// <summary>
    /// Gets or sets the optional <see cref="NoireEventBus"/> instance used to publish watcher events.<br/>
    /// If <see langword="null"/>, events are only exposed through the CLR events on each sub-tracker.
    /// </summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>
    /// Sets the <see cref="EventBus"/> instance to use for publishing watcher events.
    /// </summary>
    /// <param name="eventBus">The event bus instance, or <see langword="null"/> to disable event bus integration.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireGameStateWatcher SetEventBus(NoireEventBus? eventBus)
    {
        EventBus = eventBus;
        return this;
    }

    #region Constructor & Module Lifecycle

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireGameStateWatcher() : base()
    {
        config = new GameStateWatcherConfig();
    }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireGameStateWatcher"/> module.
    /// </summary>
    /// <param name="moduleId">An optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="config">The configuration controlling which sub-trackers should remain enabled across module activation changes. If <see langword="null"/>, all defaults apply.</param>
    /// <param name="eventBus">An optional <see cref="NoireEventBus"/> instance for publishing watcher events.</param>
    public NoireGameStateWatcher(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        GameStateWatcherConfig? config = null,
        NoireEventBus? eventBus = null)
        : base(moduleId, active, enableLogging, config, eventBus)
    {
        this.config = config ?? new GameStateWatcherConfig();
    }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireGameStateWatcher(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging)
    {
        config = new GameStateWatcherConfig();
    }

    /// <inheritdoc/>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 1 && args[1] is NoireEventBus eventBus)
            EventBus = eventBus;

        Territory = new TerritoryTracker(this, false);
        Objects = new ObjectTracker(this, false);
        Party = new PartyTracker(this, false);
        Inventory = new InventoryTracker(this, false);
        StatusEffects = new StatusEffectTracker(this, false);
        Duty = new DutyTracker(this, false);
        PlayerState = new PlayerStateTracker(this, false);
        Addons = new AddonTracker(this, false, config.AddonHistoryCapacity);
        Chat = new ChatTracker(this, false, config.ChatHistoryCapacity);
        ActionEffect = new ActionEffectTracker(this, false, config.ActionEffectHistoryCapacity);

        frameworkUpdateEvent = new(NoireService.Framework, nameof(IFramework.Update), OnFrameworkUpdate, name: $"{nameof(NoireGameStateWatcher)}.FrameworkUpdate");

        if (EnableLogging)
            NoireLogger.LogInfo(this, "GameStateWatcher module initialized.");
    }

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        frameworkUpdateEvent.Enable();

        ActivateConfiguredTrackers();

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"GameStateWatcher module activated ({GetActiveTrackerCount()} trackers active).");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        frameworkUpdateEvent.Disable();

        DeactivateAllTrackers(false);

        if (EnableLogging)
            NoireLogger.LogInfo(this, "GameStateWatcher module deactivated.");
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the live configuration controlling which sub-trackers should be enabled.
    /// </summary>
    public GameStateWatcherConfig Config => config;

    /// <summary>
    /// Gets the territory tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public TerritoryTracker Territory { get; private set; } = null!;

    /// <summary>
    /// Gets the object tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public ObjectTracker Objects { get; private set; } = null!;

    /// <summary>
    /// Gets the party tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public PartyTracker Party { get; private set; } = null!;

    /// <summary>
    /// Gets the inventory tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public InventoryTracker Inventory { get; private set; } = null!;

    /// <summary>
    /// Gets the status effect tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public StatusEffectTracker StatusEffects { get; private set; } = null!;

    /// <summary>
    /// Gets the duty tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public DutyTracker Duty { get; private set; } = null!;

    /// <summary>
    /// Gets the local player-state tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public PlayerStateTracker PlayerState { get; private set; } = null!;

    /// <summary>
    /// Gets the addon tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public AddonTracker Addons { get; private set; } = null!;

    /// <summary>
    /// Gets the chat tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public ChatTracker Chat { get; private set; } = null!;

    /// <summary>
    /// Gets the action effect tracker. Activate with <see cref="GameStateSubTracker.SetActive"/>.
    /// </summary>
    public ActionEffectTracker ActionEffect { get; private set; } = null!;

    /// <summary>
    /// Gets all sub-trackers managed by this module.
    /// </summary>
    public IReadOnlyList<GameStateSubTracker> Trackers => [Territory, Objects, Party, Inventory, StatusEffects, Duty, PlayerState, Addons, Chat, ActionEffect];

    #endregion

    #region Public API

    /// <summary>
    /// Gets the number of currently active sub-trackers.
    /// </summary>
    /// <returns>The count of active sub-trackers.</returns>
    public int GetActiveTrackerCount() => Trackers.Count(t => t.IsActive);

    /// <summary>
    /// Registers or replaces a keyed processor callback for a published tracker event type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to process.</typeparam>
    /// <param name="key">The unique callback key.</param>
    /// <param name="callback">The callback to register.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    public void RegisterCallback<TEvent>(string key, Action<TEvent> callback, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);

        lock (callbackLock)
        {
            if (!callbacksByEventType.TryGetValue(typeof(TEvent), out var callbacks))
            {
                callbacks = new(StringComparer.Ordinal);
                callbacksByEventType[typeof(TEvent)] = callbacks;
            }

            callbacks[key] = (priority, callback);
        }
    }

    /// <summary>
    /// Registers or replaces a keyed async processor callback for a published tracker event type.
    /// The async callback is invoked in a fire-and-forget manner; exceptions are logged.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to process.</typeparam>
    /// <param name="key">The unique callback key.</param>
    /// <param name="callback">The async callback to register.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    public void RegisterCallbackAsync<TEvent>(string key, Func<TEvent, Task> callback, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);

        RegisterCallback<TEvent>(key, evt => InvokeAsyncSafe(callback, evt), priority);
    }

    /// <summary>
    /// Registers or replaces a keyed callback routed to a specific tracker for the supplied event payload type.
    /// </summary>
    /// <typeparam name="TTracker">The tracker type to register the callback on.</typeparam>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="key">The unique callback key.</param>
    /// <param name="callback">The callback to register.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    public void RegisterCallback<TTracker, TEvent>(string key, Action<TEvent> callback, int priority = 0)
        where TTracker : GameStateSubTracker
        => GetTracker<TTracker>().RegisterCallback(key, callback, priority);

    /// <summary>
    /// Registers a callback for the supplied event payload type and returns a subscription token.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="callback">The callback to register.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken Subscribe<TEvent>(Action<TEvent> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var key = $"subscription:{Guid.NewGuid():N}";
        RegisterCallback(key, callback, priority);
        return new GameStateWatcherSubscriptionToken(typeof(TEvent), key, priority, () => UnregisterCallback<TEvent>(key));
    }

    /// <summary>
    /// Registers an async callback for the supplied event payload type and returns a subscription token.
    /// The async callback is invoked in a fire-and-forget manner; exceptions are logged.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="callback">The async callback to register.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeAsync<TEvent>(Func<TEvent, Task> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return Subscribe<TEvent>(evt => InvokeAsyncSafe(callback, evt), priority);
    }

    /// <summary>
    /// Registers a filtered callback for the supplied event payload type and returns a subscription token.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="predicate">The predicate that must return <see langword="true"/> before the callback is invoked.</param>
    /// <param name="callback">The callback to invoke when the predicate matches.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken Subscribe<TEvent>(Func<TEvent, bool> predicate, Action<TEvent> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(callback);

        return Subscribe<TEvent>(evt =>
        {
            if (predicate(evt))
                callback(evt);
        }, priority);
    }

    /// <summary>
    /// Registers a filtered async callback for the supplied event payload type and returns a subscription token.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="predicate">The predicate that must return <see langword="true"/> before the async callback is invoked.</param>
    /// <param name="callback">The async callback to invoke when the predicate matches.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeAsync<TEvent>(Func<TEvent, bool> predicate, Func<TEvent, Task> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(callback);

        return Subscribe<TEvent>(evt =>
        {
            if (predicate(evt))
                InvokeAsyncSafe(callback, evt);
        }, priority);
    }

    /// <summary>
    /// Registers a callback that is invoked only once for the supplied event payload type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="callback">The callback to invoke once.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeOnce<TEvent>(Action<TEvent> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var key = $"subscription:{Guid.NewGuid():N}";

        RegisterCallback<TEvent>(key, evt =>
        {
            try
            {
                callback(evt);
            }
            finally
            {
                UnregisterCallback<TEvent>(key);
            }
        }, priority);

        return new GameStateWatcherSubscriptionToken(typeof(TEvent), key, priority, () => UnregisterCallback<TEvent>(key));
    }

    /// <summary>
    /// Registers a callback directly against a specific tracker event payload type and returns a subscription token.
    /// </summary>
    /// <typeparam name="TTracker">The tracker type to subscribe through.</typeparam>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="callback">The callback to register.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken Subscribe<TTracker, TEvent>(Action<TEvent> callback, int priority = 0)
        where TTracker : GameStateSubTracker
        => GetTracker<TTracker>().Subscribe(callback, priority);

    /// <summary>
    /// Registers a filtered callback directly against a specific tracker event payload type and returns a subscription token.
    /// </summary>
    /// <typeparam name="TTracker">The tracker type to subscribe through.</typeparam>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
    /// <param name="predicate">The predicate that must return <see langword="true"/> before the callback is invoked.</param>
    /// <param name="callback">The callback to invoke when the predicate matches.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken Subscribe<TTracker, TEvent>(Func<TEvent, bool> predicate, Action<TEvent> callback, int priority = 0)
        where TTracker : GameStateSubTracker
        => GetTracker<TTracker>().Subscribe(predicate, callback, priority);

    /// <summary>
    /// Determines whether a keyed processor callback exists for the supplied event type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to inspect.</typeparam>
    /// <param name="key">The callback key to look up.</param>
    /// <returns><see langword="true"/> if a callback exists for the supplied key; otherwise, <see langword="false"/>.</returns>
    public bool HasCallback<TEvent>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (callbackLock)
            return callbacksByEventType.TryGetValue(typeof(TEvent), out var callbacks) && callbacks.ContainsKey(key);
    }

    /// <summary>
    /// Removes a keyed processor callback for the supplied event type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to modify.</typeparam>
    /// <param name="key">The callback key to remove.</param>
    /// <returns><see langword="true"/> if a callback was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnregisterCallback<TEvent>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (callbackLock)
        {
            if (!callbacksByEventType.TryGetValue(typeof(TEvent), out var callbacks))
                return false;

            var removed = callbacks.Remove(key);

            if (callbacks.Count == 0)
                callbacksByEventType.Remove(typeof(TEvent));

            return removed;
        }
    }

    /// <summary>
    /// Removes every registered module-level processor callback.
    /// </summary>
    public void ClearCallbacks()
    {
        lock (callbackLock)
            callbacksByEventType.Clear();
    }

    /// <summary>
    /// Removes every registered module-level processor callback for the supplied event type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to clear.</typeparam>
    public void ClearCallbacks<TEvent>()
    {
        lock (callbackLock)
            callbacksByEventType.Remove(typeof(TEvent));
    }

    /// <summary>
    /// Retrieves a tracker instance by its concrete type.
    /// </summary>
    /// <typeparam name="TTracker">The tracker type to retrieve.</typeparam>
    /// <returns>The matching tracker instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the requested tracker type is not managed by this module.</exception>
    public TTracker GetTracker<TTracker>()
        where TTracker : GameStateSubTracker
    {
        if (TryGetTracker<TTracker>(out var tracker))
        {
            if (tracker == null)
                throw new InvalidOperationException($"Tracker '{typeof(TTracker).Name}' not found.");

            return tracker;
        }

        throw new InvalidOperationException($"Tracker '{typeof(TTracker).Name}' is not managed by this watcher.");
    }

    /// <summary>
    /// Attempts to retrieve a tracker instance by its concrete type.
    /// </summary>
    /// <typeparam name="TTracker">The tracker type to retrieve.</typeparam>
    /// <param name="tracker">When this method returns <see langword="true"/>, contains the matching tracker instance.</param>
    /// <returns><see langword="true"/> if the tracker was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetTracker<TTracker>(out TTracker? tracker)
        where TTracker : GameStateSubTracker
    {
        tracker = Trackers.OfType<TTracker>().FirstOrDefault();
        return tracker != null;
    }

    /// <summary>
    /// Activates all sub-trackers that are not already active.
    /// </summary>
    /// <param name="updateConfig"><see langword="true"/> to persist the activation state to the configuration; otherwise, <see langword="false"/>.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireGameStateWatcher ActivateAllTrackers(bool updateConfig = true)
    {
        foreach (var tracker in Trackers)
        {
            if (!tracker.IsActive && !tracker.IsDisposed)
                tracker.SetActive(true, updateConfig);
        }

        return this;
    }

    /// <summary>
    /// Deactivates all sub-trackers that are currently active.
    /// </summary>
    /// <param name="updateConfig"><see langword="true"/> to persist the deactivation state to the configuration; otherwise, <see langword="false"/>.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireGameStateWatcher DeactivateAllTrackers(bool updateConfig = true)
    {
        foreach (var tracker in Trackers)
        {
            if (tracker.IsActive && !tracker.IsDisposed)
                tracker.SetActive(false, updateConfig);
        }

        return this;
    }

    #endregion

    /// <inheritdoc/>
    protected override void DisposeInternal()
    {
        frameworkUpdateEvent.Dispose();

        foreach (var tracker in Trackers)
        {
            try
            {
                tracker.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Failed to dispose sub-tracker {tracker.GetType().Name}.");
            }
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, "GameStateWatcher module disposed.");
    }

    #region Private/Internal Methods

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsActive)
            return;

        foreach (var tracker in Trackers)
        {
            if (!tracker.IsActive || tracker.IsDisposed)
                continue;

            try
            {
                tracker.Update();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Sub-tracker {tracker.GetType().Name} threw an exception during Update.");
            }
        }
    }

    internal void UpdateTrackerConfiguration(GameStateSubTracker tracker, bool active)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        switch (tracker)
        {
            case TerritoryTracker:
                config.EnableTerritoryTracker = active;
                break;
            case ObjectTracker:
                config.EnableObjectTracker = active;
                break;
            case PartyTracker:
                config.EnablePartyTracker = active;
                break;
            case InventoryTracker:
                config.EnableInventoryTracker = active;
                break;
            case StatusEffectTracker:
                config.EnableStatusEffectTracker = active;
                break;
            case DutyTracker:
                config.EnableDutyTracker = active;
                break;
            case PlayerStateTracker:
                config.EnablePlayerStateTracker = active;
                break;
            case AddonTracker:
                config.EnableAddonTracker = active;
                break;
            case ChatTracker:
                config.EnableChatTracker = active;
                break;
            case ActionEffectTracker:
                config.EnableActionEffectTracker = active;
                break;
            default:
                throw new InvalidOperationException($"Tracker '{tracker.GetType().Name}' is not managed by this watcher.");
        }
    }

    private void ActivateConfiguredTrackers()
    {
        if (config.EnableTerritoryTracker) Territory.SetActive(true, updateConfig: false);
        if (config.EnableObjectTracker) Objects.SetActive(true, updateConfig: false);
        if (config.EnablePartyTracker) Party.SetActive(true, updateConfig: false);
        if (config.EnableInventoryTracker) Inventory.SetActive(true, updateConfig: false);
        if (config.EnableStatusEffectTracker) StatusEffects.SetActive(true, updateConfig: false);
        if (config.EnableDutyTracker) Duty.SetActive(true, updateConfig: false);
        if (config.EnablePlayerStateTracker) PlayerState.SetActive(true, updateConfig: false);
        if (config.EnableAddonTracker) Addons.SetActive(true, updateConfig: false);
        if (config.EnableChatTracker) Chat.SetActive(true, updateConfig: false);
        if (config.EnableActionEffectTracker) ActionEffect.SetActive(true, updateConfig: false);
    }

    internal void PublishCallbacks<TEvent>(TEvent evt)
    {
        Action<TEvent>[] callbacksSnapshot;

        lock (callbackLock)
        {
            if (!callbacksByEventType.TryGetValue(typeof(TEvent), out var callbacks) || callbacks.Count == 0)
                return;

            callbacksSnapshot = callbacks.Values
                .OrderBy(entry => entry.Priority)
                .Select(entry => (Action<TEvent>)entry.Callback)
                .ToArray();
        }

        foreach (var callback in callbacksSnapshot)
        {
            try
            {
                callback.Invoke(evt);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"A {typeof(TEvent).Name} module callback threw an exception.");
            }
        }
    }

    private async void InvokeAsyncSafe<TEvent>(Func<TEvent, Task> callback, TEvent evt)
    {
        try
        {
            await callback(evt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"An async {typeof(TEvent).Name} module callback threw an exception.");
        }
    }

    #endregion
}
