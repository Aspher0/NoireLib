using System;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Represents a registered distance-threshold watcher for the <see cref="ObjectTracker"/>.
/// When a tracked object crosses the configured distance boundary, enter or leave events are raised.
/// </summary>
public sealed class DistanceWatcherRegistration : IDisposable
{
    private Action? disposeAction;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistanceWatcherRegistration"/> class.
    /// </summary>
    /// <param name="key">The unique registration key.</param>
    /// <param name="threshold">The distance threshold in world units.</param>
    /// <param name="predicate">An optional predicate to filter which objects are watched.</param>
    /// <param name="disposeAction">The action to invoke when the registration is disposed.</param>
    internal DistanceWatcherRegistration(string key, float threshold, Func<ObjectSnapshot, bool>? predicate, Action disposeAction)
    {
        Key = key;
        Threshold = threshold;
        Predicate = predicate;
        this.disposeAction = disposeAction;
    }

    /// <summary>
    /// Gets the unique registration key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the distance threshold in world units.
    /// </summary>
    public float Threshold { get; }

    /// <summary>
    /// Gets the optional predicate used to filter watched objects.
    /// </summary>
    public Func<ObjectSnapshot, bool>? Predicate { get; }

    /// <summary>
    /// Gets a value indicating whether this registration has been disposed.
    /// </summary>
    public bool IsDisposed => disposeAction == null;

    /// <inheritdoc/>
    public void Dispose()
    {
        var action = System.Threading.Interlocked.Exchange(ref disposeAction, null);
        action?.Invoke();
    }
}
