using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

public partial class NoireGameWatcher
{
    /// <summary>
    /// Opts an event type into EventBus mirroring: from now on, every dispatched <typeparamref name="TEvent"/>
    /// is also published on the bus configured in <see cref="GameWatcherOptions.EventBus"/>.<br/>
    /// Nothing is published by default — a firehose of character diffs would flood a shared bus.
    /// Without an attached bus the call is inert (logged).
    /// </summary>
    /// <typeparam name="TEvent">The event type to mirror.</typeparam>
    /// <param name="filter">An optional filter; only matching events are mirrored.</param>
    /// <returns>A token that stops mirroring when disposed.</returns>
    public NoireSubscriptionToken PublishToEventBus<TEvent>(Func<TEvent, bool>? filter = null)
        where TEvent : class
    {
        if (ActiveOptions.EventBus == null)
        {
            NoireLogger.LogWarning(this, $"{nameof(PublishToEventBus)}<{typeof(TEvent).Name}> requires {nameof(GameWatcherOptions)}.{nameof(GameWatcherOptions.EventBus)} to be set; the call is inert.");
            return new NoireSubscriptionToken(null, 0, _ => { });
        }

        EventBusMirror mirror = null!;

        var token = new NoireSubscriptionToken(null, 0, _ =>
        {
            lock (gate)
            {
                if (eventBusMirrors.TryGetValue(typeof(TEvent), out var list))
                {
                    list.Remove(mirror);

                    if (list.Count == 0)
                        eventBusMirrors.Remove(typeof(TEvent));
                }
            }
        });

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
            if (!eventBusMirrors.TryGetValue(typeof(TEvent), out var list))
            {
                list = new List<EventBusMirror>();
                eventBusMirrors[typeof(TEvent)] = list;
            }

            list.Add(mirror);
        }

        return token;
    }

    private void MirrorToEventBus(Type type, object evt)
    {
        if (ActiveOptions.EventBus == null)
            return;

        EventBusMirror[] mirrors;

        lock (gate)
        {
            if (!eventBusMirrors.TryGetValue(type, out var list) || list.Count == 0)
                return;

            mirrors = list.ToArray();
        }

        foreach (var mirror in mirrors)
        {
            if (mirror.Filter != null && !mirror.Filter(evt))
                continue;

            try
            {
                mirror.Publish(evt);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Mirroring {type.Name} to the EventBus threw.");
            }

            // One matching mirror registration is enough — several matching filters must not duplicate
            // the event on the bus.
            break;
        }
    }
}
