using NoireLib.Core.Subscriptions;
using NoireLib.EventBus;
using NoireLib.Networker.Internal;
using System;
using System.Collections.Concurrent;

namespace NoireLib.Networker;

public partial class NoireNetworker
{
    private readonly ConcurrentDictionary<string, EventShareEntry> eventShares = new();

    [ThreadStatic]
    private static bool bridgePublishing;

    private sealed class EventShareEntry
    {
        public required Type EventType { get; init; }
        public required Action<object, NetworkerPeer> PublishInbound { get; init; }
    }

    /// <summary>
    /// Shares an EventBus event type with the network: local publishes of <typeparamref name="TEvent"/> on the attached bus
    /// reach every peer, and events received from peers are published on the local bus — as if all instances shared one EventBus.<br/>
    /// Loop-safe by construction: bridged-in publishes are never bridged back out.<br/>
    /// Requires <see cref="NetworkerOptions.EventBus"/> to be set. When <typeparamref name="TEvent"/> implements
    /// <see cref="INetworkerEvent"/>, its origin peer is populated on bridged-in events.
    /// </summary>
    /// <typeparam name="TEvent">The EventBus event type to share (any JSON-serializable class).</typeparam>
    /// <param name="direction">Which directions to bridge. Defaults to <see cref="NetworkerShareDirection.Both"/>.</param>
    /// <returns>A token that stops sharing the event type when disposed.</returns>
    public NoireSubscriptionToken ShareEvent<TEvent>(NetworkerShareDirection direction = NetworkerShareDirection.Both) where TEvent : class
    {
        var bus = ActiveOptions.EventBus;

        if (bus == null)
        {
            InternalLogWarning($"{nameof(ShareEvent)} requires {nameof(NetworkerOptions)}.{nameof(NetworkerOptions.EventBus)} to be set; '{typeof(TEvent).Name}' is not shared.");
            return new NoireSubscriptionToken(null, 0, _ => { });
        }

        var typeName = typeof(TEvent).FullName!;
        EventSubscriptionToken? busToken = null;

        if (direction is NetworkerShareDirection.Both or NetworkerShareDirection.Outbound)
        {
            busToken = bus.Subscribe<TEvent>(eventData =>
            {
                // Re-entrancy guard: publishes performed by the inbound bridge stay local.
                if (bridgePublishing || eventData == null)
                    return;

                SendEnvelope(new Envelope
                {
                    Kind = EnvelopeKind.Event,
                    Origin = SelfId,
                    TypeName = eventData.GetType().FullName,
                    Payload = Wire.ToPayload(eventData),
                });
            }, owner: this);
        }

        if (direction is NetworkerShareDirection.Both or NetworkerShareDirection.Inbound)
        {
            eventShares[typeName] = new EventShareEntry
            {
                EventType = typeof(TEvent),
                PublishInbound = (eventObject, origin) =>
                {
                    if (eventObject is INetworkerEvent networkerEvent)
                        networkerEvent.Origin = origin;

                    bridgePublishing = true;

                    try
                    {
                        bus.Publish((TEvent)eventObject);
                    }
                    finally
                    {
                        bridgePublishing = false;
                    }
                },
            };
        }

        return new NoireSubscriptionToken(null, 0, token =>
        {
            if (busToken.HasValue)
                bus.Unsubscribe(busToken.Value);

            eventShares.TryRemove(typeName, out _);
        });
    }

    private void DispatchInboundSharedEvent(Envelope envelope)
    {
        if (envelope.TypeName == null || !eventShares.TryGetValue(envelope.TypeName, out var share))
            return;

        object? eventObject;

        try
        {
            eventObject = Wire.FromPayload(envelope.Payload, share.EventType);
        }
        catch (Exception ex)
        {
            InternalLogWarning($"Dropping undeserializable shared event '{envelope.TypeName}': {ex.Message}");
            return;
        }

        if (eventObject == null)
            return;

        share.PublishInbound(eventObject, ResolvePeer(envelope.Origin));
    }

    /// <summary>
    /// Publishes a networker lifecycle event to the attached EventBus, when one is configured.
    /// </summary>
    private void PublishModuleEvent<TEvent>(TEvent eventData)
    {
        var bus = ActiveOptions.EventBus;

        if (bus != null && ActiveOptions.PublishModuleEvents)
            bus.Publish(eventData);
    }
}
