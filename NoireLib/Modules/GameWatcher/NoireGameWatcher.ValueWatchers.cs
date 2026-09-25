using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

public partial class NoireGameWatcher
{
    private abstract class ValueWatcherRegistration
    {
        public required string Description { get; init; }
        public object? Owner { get; init; }
        public TimeSpan Interval { get; init; }
        public DateTimeOffset NextDue { get; set; } = DateTimeOffset.MinValue;
        public NoireSubscriptionToken? Token { get; set; }

        // Invokes the callback on a change. False removes the watcher.
        public abstract bool Evaluate(NoireGameWatcher owner);
    }

    private sealed class ValueWatcherRegistration<T> : ValueWatcherRegistration
    {
        public required Func<T> Sampler { get; init; }
        public required Action<T?, T> OnChanged { get; init; }
        public required IEqualityComparer<T> Comparer { get; init; }

        private T? previous;
        private bool hasBaseline;

        public override bool Evaluate(NoireGameWatcher owner)
        {
            T current;

            try
            {
                current = Sampler();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(owner, ex, $"Value watcher '{Description}' sampler threw and the watcher was removed.");
                return false;
            }

            if (!hasBaseline)
            {
                // Baseline seeding: the first sample never fires - subscribers observe changes from now on.
                previous = current;
                hasBaseline = true;
                return true;
            }

            if (Comparer.Equals(previous!, current))
                return true;

            var old = previous;
            previous = current;

            try
            {
                OnChanged(old, current);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(owner, ex, $"Value watcher '{Description}' callback threw.");
            }

            return true;
        }
    }

    private sealed class TickCallbackRegistration : ValueWatcherRegistration
    {
        public required Action Callback { get; init; }

        public override bool Evaluate(NoireGameWatcher owner)
        {
            try
            {
                Callback();
                return true;
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(owner, ex, $"Tick callback '{Description}' threw and was removed.");
                return false;
            }
        }
    }

    private readonly List<ValueWatcherRegistration> valueWatchers = new();

    // Republished under the gate on every change: the tick iterates it without copying.
    private volatile ValueWatcherRegistration[] valueWatcherSnapshot = Array.Empty<ValueWatcherRegistration>();

    // Registers a raw per-tick callback on the value-watcher pump - the plumbing behind scoped value watchers.
    // Internal.
    internal NoireSubscriptionToken WatchTick(Action onTick, TimeSpan? interval, object? owner, string description)
    {
        ArgumentNullException.ThrowIfNull(onTick);

        var registration = new TickCallbackRegistration
        {
            Description = description,
            Owner = owner,
            Interval = interval ?? TimeSpan.Zero,
            Callback = onTick,
        };

        var token = new NoireSubscriptionToken(null, 0, _ => RemoveValueWatcher(registration));
        registration.Token = token;

        AddValueWatcher(registration);
        return token;
    }

    /// <summary>
    /// Diffs any value you can read, per tick or at an interval, on the framework thread. The first sample seeds the
    /// baseline without firing.
    /// </summary>
    /// <typeparam name="T">The sampled value type.</typeparam>
    /// <param name="sampler">Reads the current value.</param>
    /// <param name="onChanged">Called with the previous and current values on a change.</param>
    /// <param name="interval">The sampling interval, every tick when null.</param>
    /// <param name="comparer">The equality comparer, <see cref="EqualityComparer{T}.Default"/> when null.</param>
    /// <param name="owner">The owner the watcher is removed with, see <see cref="UnsubscribeOwner"/>.</param>
    /// <param name="description">A description for logs and diagnostics.</param>
    /// <returns>A token that stops the watcher when disposed.</returns>
    public NoireSubscriptionToken WatchValue<T>(
        Func<T> sampler,
        Action<T?, T> onChanged,
        TimeSpan? interval = null,
        IEqualityComparer<T>? comparer = null,
        object? owner = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(onChanged);

        var registration = new ValueWatcherRegistration<T>
        {
            Description = description ?? $"WatchValue<{typeof(T).Name}>",
            Owner = owner,
            Interval = interval ?? TimeSpan.Zero,
            Sampler = sampler,
            OnChanged = onChanged,
            Comparer = comparer ?? EqualityComparer<T>.Default,
        };

        var token = new NoireSubscriptionToken(null, 0, _ => RemoveValueWatcher(registration));
        registration.Token = token;

        AddValueWatcher(registration);
        return token;
    }

    private void AddValueWatcher(ValueWatcherRegistration registration)
    {
        lock (gate)
        {
            valueWatchers.Add(registration);
            valueWatcherSnapshot = valueWatchers.ToArray();
        }
    }

    private void RemoveValueWatcher(ValueWatcherRegistration registration)
    {
        lock (gate)
        {
            if (valueWatchers.Remove(registration))
                valueWatcherSnapshot = valueWatchers.ToArray();
        }
    }

    // Called under the gate by module disposal.
    private void ClearValueWatchers()
    {
        valueWatchers.Clear();
        valueWatcherSnapshot = Array.Empty<ValueWatcherRegistration>();
    }

    private int RemoveValueWatchersByOwner(object owner)
    {
        List<ValueWatcherRegistration> matches;

        lock (gate)
            matches = valueWatchers.Where(w => ReferenceEquals(w.Owner, owner)).ToList();

        foreach (var watcher in matches)
            watcher.Token?.Dispose();

        return matches.Count;
    }

    private void TickValueWatchers(DateTimeOffset now)
    {
        foreach (var watcher in valueWatcherSnapshot)
        {
            if (watcher.Interval > TimeSpan.Zero)
            {
                if (now < watcher.NextDue)
                    continue;

                watcher.NextDue = now + watcher.Interval;
            }

            if (!watcher.Evaluate(this))
                watcher.Token?.Dispose();
        }
    }

    internal int ValueWatcherCount
    {
        get
        {
            lock (gate)
                return valueWatchers.Count;
        }
    }
}
