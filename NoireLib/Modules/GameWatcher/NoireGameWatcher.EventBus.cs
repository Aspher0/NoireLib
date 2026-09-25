using NoireLib.Core.Subscriptions;
using System;

namespace NoireLib.GameWatcher;

public partial class NoireGameWatcher
{
    /// <summary>
    /// Mirrors every dispatched <typeparamref name="TEvent"/> to the <see cref="GameWatcherOptions.EventBus"/>. Several
    /// registrations publish an event once. Without a bus the call does nothing.
    /// </summary>
    /// <typeparam name="TEvent">The event type to mirror.</typeparam>
    /// <param name="filter">The filter an event must pass.</param>
    /// <returns>A token that stops mirroring, already inactive without a bus.</returns>
    public NoireSubscriptionToken PublishToEventBus<TEvent>(Func<TEvent, bool>? filter = null)
        where TEvent : class
    {
        if (ActiveOptions.EventBus == null)
        {
            NoireLogger.LogWarning(this, $"{nameof(PublishToEventBus)}<{typeof(TEvent).Name}> requires {nameof(GameWatcherOptions)}.{nameof(GameWatcherOptions.EventBus)} to be set; the call is inert.");

            var inert = new NoireSubscriptionToken(null, 0, static _ => { });
            inert.Invalidate();
            return inert;
        }

        EventBusMirror mirror = null!;

        var token = new NoireSubscriptionToken(null, 0, _ => RemoveEventBusMirror(typeof(TEvent), mirror));

        mirror = new EventBusMirror
        {
            Token = token,
            Filter = filter == null ? null : evt => evt is TEvent typed && filter(typed),
            Publish = evt =>
            {
                var bus = ActiveOptions.EventBus;
                bus?.Publish((TEvent)evt);
            },
        };

        lock (gate)
        {
            eventBusMirrors[typeof(TEvent)] = eventBusMirrors.TryGetValue(typeof(TEvent), out var existing)
                ? [.. existing, mirror]
                : [mirror];
        }

        return token;
    }

    private void RemoveEventBusMirror(Type type, EventBusMirror mirror)
    {
        lock (gate)
        {
            if (!eventBusMirrors.TryGetValue(type, out var existing))
                return;

            var remaining = Array.FindAll(existing, candidate => !ReferenceEquals(candidate, mirror));

            if (remaining.Length == 0)
                eventBusMirrors.Remove(type);
            else
                eventBusMirrors[type] = remaining;
        }
    }

    // Called under the gate by module disposal.
    private void ClearEventBusMirrors()
    {
        foreach (var mirrors in eventBusMirrors.Values)
        {
            foreach (var mirror in mirrors)
                mirror.Token.Invalidate();
        }

        eventBusMirrors.Clear();
    }

    private void MirrorToEventBus(Type type, object evt)
    {
        if (ActiveOptions.EventBus == null)
            return;

        EventBusMirror[]? mirrors;

        lock (gate)
            eventBusMirrors.TryGetValue(type, out mirrors);

        if (mirrors == null)
            return;

        foreach (var mirror in mirrors)
        {
            if (!mirror.Token.IsActive || (mirror.Filter != null && !mirror.Filter(evt)))
                continue;

            try
            {
                mirror.Publish(evt);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Mirroring {type.Name} to the EventBus threw.");
            }

            // Every registration shares the bus: the first match publishes, the rest would duplicate it.
            break;
        }
    }
}
