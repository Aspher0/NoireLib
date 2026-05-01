using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Abstract base class for game state sub-trackers managed by <see cref="NoireGameStateWatcher"/>.<br/>
/// Sub-trackers can be individually activated and deactivated at runtime.
/// </summary>
public abstract class GameStateSubTracker : IDisposable
{
    private readonly Dictionary<Type, Dictionary<string, (int Priority, Delegate Callback)>> callbacksByEventType = new();
    private readonly object callbackLock = new();
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameStateSubTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    protected GameStateSubTracker(NoireGameStateWatcher owner, bool active)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        IsActive = active;

        if (active)
            OnActivated();
    }

    /// <summary>
    /// Gets the owning <see cref="NoireGameStateWatcher"/> module.
    /// </summary>
    protected NoireGameStateWatcher Owner { get; }

    /// <summary>
    /// Gets a value indicating whether the sub-tracker is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the sub-tracker has been disposed.
    /// </summary>
    public bool IsDisposed => disposed;

    /// <summary>
    /// Registers or replaces a keyed callback for a tracker event payload type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
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
    /// Registers or replaces a keyed async callback for a tracker event payload type.
    /// The async callback is invoked in a fire-and-forget manner; exceptions are logged.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to observe.</typeparam>
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
    /// Determines whether a keyed callback exists for the supplied event payload type.
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
    /// Removes a keyed callback for the supplied event payload type.
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
    /// Removes every registered tracker callback.
    /// </summary>
    public void ClearCallbacks()
    {
        lock (callbackLock)
            callbacksByEventType.Clear();
    }

    /// <summary>
    /// Removes every registered callback for the supplied event payload type.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type to clear.</typeparam>
    public void ClearCallbacks<TEvent>()
    {
        lock (callbackLock)
            callbacksByEventType.Remove(typeof(TEvent));
    }

    /// <summary>
    /// Sets the active state of the sub-tracker.
    /// </summary>
    /// <param name="active">Whether the tracker should be active.</param>
    /// <param name="updateConfig"><see langword="true"/> to persist the tracker state to the owning watcher's configuration; otherwise, <see langword="false"/>.</param>
    public void SetActive(bool active, bool updateConfig = true)
    {
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (IsActive == active)
            return;

        IsActive = active;

        if (updateConfig)
            Owner.UpdateTrackerConfiguration(this, active);

        if (active)
            OnActivated();
        else
            OnDeactivated();
    }

    /// <summary>
    /// Called once per framework tick when the sub-tracker is active.<br/>
    /// Polling-based sub-trackers should override this to perform their per-frame work.
    /// </summary>
    internal virtual void Update() { }

    /// <summary>
    /// Called when the sub-tracker transitions to the active state.
    /// </summary>
    protected abstract void OnActivated();

    /// <summary>
    /// Called when the sub-tracker transitions to the inactive state.
    /// </summary>
    protected abstract void OnDeactivated();

    /// <summary>
    /// Releases implementation-specific resources used by the sub-tracker.
    /// </summary>
    protected virtual void DisposeCore() { }

    /// <summary>
    /// Safely invokes a CLR event handler, catching and logging any exceptions.
    /// </summary>
    /// <typeparam name="TEvent">The event argument type.</typeparam>
    /// <param name="handler">The event handler delegate, or <see langword="null"/> if no subscribers exist.</param>
    /// <param name="evt">The event argument to pass to the handler.</param>
    protected void RaiseEvent<TEvent>(Action<TEvent>? handler, TEvent evt)
    {
        if (handler == null)
            return;

        try
        {
            handler.Invoke(evt);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, $"A {typeof(TEvent).Name} handler threw an exception.");
        }
    }

    /// <summary>
    /// Safely invokes every registered keyed callback for the supplied event payload.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="evt">The event payload to dispatch.</param>
    protected void RaiseCallbacks<TEvent>(TEvent evt)
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
                NoireLogger.LogError(Owner, ex, $"A {typeof(TEvent).Name} callback threw an exception.");
            }
        }
    }

    /// <summary>
    /// Safely invokes an async callback in a fire-and-forget manner, logging any exceptions.
    /// </summary>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="callback">The async callback to invoke.</param>
    /// <param name="evt">The event payload to pass to the callback.</param>
    private async void InvokeAsyncSafe<TEvent>(Func<TEvent, Task> callback, TEvent evt)
    {
        try
        {
            await callback(evt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, $"An async {typeof(TEvent).Name} callback threw an exception.");
        }
    }

    /// <summary>
    /// Safely invokes a CLR event handler and publishes the event to the <see cref="NoireGameStateWatcher.EventBus"/> if available.
    /// </summary>
    /// <typeparam name="TEvent">The event argument type.</typeparam>
    /// <param name="handler">The event handler delegate, or <see langword="null"/> if no subscribers exist.</param>
    /// <param name="evt">The event argument to pass to the handler and publish.</param>
    protected void PublishEvent<TEvent>(Action<TEvent>? handler, TEvent evt)
    {
        RaiseCallbacks(evt);
        RaiseEvent(handler, evt);
        Owner.PublishCallbacks(evt);
        Owner.EventBus?.Publish(evt);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (IsActive)
        {
            IsActive = false;
            OnDeactivated();
        }

        ClearCallbacks();
        DisposeCore();
        GC.SuppressFinalize(this);
    }
}
