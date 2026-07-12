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

        /// <summary>Samples and compares; invokes the callback on change. Returns false when the watcher must be removed.</summary>
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
                // Baseline seeding: the first sample never fires — subscribers observe changes from now on.
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

    /// <summary>
    /// Registers a raw per-tick callback on the value-watcher pump — the plumbing behind scoped value
    /// watchers. Internal.
    /// </summary>
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

        lock (gate)
            valueWatchers.Add(registration);

        return token;
    }

    /// <summary>
    /// Diffs <b>any</b> value you can read, polled per tick or at an interval — the permanent escape hatch:
    /// game state the library never modeled is still watchable with full semantics.<br/>
    /// The sampler and the comparison run on the framework thread. The first sample seeds the baseline
    /// without firing. Returns the same token type as every other subscription.
    /// </summary>
    /// <typeparam name="T">The sampled value type.</typeparam>
    /// <param name="sampler">Reads the current value.</param>
    /// <param name="onChanged">Invoked with (previous, current) when the value changes.</param>
    /// <param name="interval">The sampling interval; null = every tick.</param>
    /// <param name="comparer">An optional equality comparer; defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <param name="owner">An optional owner for bulk removal via <see cref="UnsubscribeOwner"/>.</param>
    /// <param name="description">An optional readable description for logs and diagnostics.</param>
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

        lock (gate)
            valueWatchers.Add(registration);

        return token;
    }

    private void RemoveValueWatcher(ValueWatcherRegistration registration)
    {
        lock (gate)
            valueWatchers.Remove(registration);
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
        ValueWatcherRegistration[] snapshot;

        lock (gate)
        {
            if (valueWatchers.Count == 0)
                return;

            snapshot = valueWatchers.ToArray();
        }

        foreach (var watcher in snapshot)
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

    /// <summary>The number of live value watchers, for diagnostics.</summary>
    internal int ValueWatcherCount
    {
        get
        {
            lock (gate)
                return valueWatchers.Count;
        }
    }
}
