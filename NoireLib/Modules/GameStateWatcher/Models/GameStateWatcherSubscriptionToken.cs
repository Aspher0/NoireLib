using System;
using System.Threading;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// A strongly-typed subscription token that represents a registered callback in the game state watcher system.<br/>
/// Provides metadata about the subscription and a thread-safe <see cref="IDisposable.Dispose"/> method to unregister the callback.
/// </summary>
public sealed class GameStateWatcherSubscriptionToken : IDisposable
{
    private Action? disposeAction;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameStateWatcherSubscriptionToken"/> class.
    /// </summary>
    /// <param name="eventType">The event payload type this subscription observes.</param>
    /// <param name="key">The unique callback key.</param>
    /// <param name="priority">The callback priority used for invocation ordering.</param>
    /// <param name="disposeAction">The action to invoke when the subscription is disposed.</param>
    internal GameStateWatcherSubscriptionToken(Type eventType, string key, int priority, Action disposeAction)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(disposeAction);

        EventType = eventType;
        Key = key;
        Priority = priority;
        this.disposeAction = disposeAction;
    }

    /// <summary>
    /// Gets the event payload type this subscription observes.
    /// </summary>
    public Type EventType { get; }

    /// <summary>
    /// Gets the unique callback key for this subscription.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the callback priority used for invocation ordering. Lower values are invoked first.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Gets a value indicating whether this subscription has been disposed.
    /// </summary>
    public bool IsDisposed => disposeAction == null;

    /// <inheritdoc/>
    public void Dispose()
    {
        var action = Interlocked.Exchange(ref disposeAction, null);
        action?.Invoke();
    }
}
