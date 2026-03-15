using Newtonsoft.Json.Linq;
using NoireLib.Enums;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace NoireLib.NetworkRelay;

public partial class NoireNetworkRelay
{
    /// <summary>
    /// Relays local EventBus publishes of <typeparamref name="TEvent"/> over the network.
    /// </summary>
    /// <typeparam name="TEvent">The EventBus event type to relay.</typeparam>
    /// <param name="channel">Optional relay channel override. If omitted, the event type name is used.</param>
    /// <param name="broadcast">Whether the relayed event should be broadcast instead of sent to known peers.</param>
    /// <param name="targetPeerId">Optional target peer ID for direct delivery.</param>
    /// <param name="filter">Optional filter used to skip specific local events.</param>
    /// <param name="owner">Optional owner object for the EventBus subscription.</param>
    /// <param name="key">Optional EventBus subscription key.</param>
    /// <param name="deliveryMode">The delivery mode used for outbound relayed EventBus payloads.</param>
    /// <returns>The EventBus subscription token used for the outbound relay subscription.</returns>
    public EventSubscriptionToken RelayPublishedEvent<TEvent>(
        string? channel = null,
        bool broadcast = true,
        string? targetPeerId = null,
        Func<TEvent, bool>? filter = null,
        object? owner = null,
        string? key = null,
        NetworkRelayDeliveryMode deliveryMode = NetworkRelayDeliveryMode.BestEffort)
    {
        EnsureEventBusConfigured();

        var bridgeChannel = ResolveEventBridgeChannel<TEvent>(channel);
        EventSubscriptionToken token;

        Action<TEvent> handler = eventData =>
        {
            if (IsEventBusRelaySuppressed())
                return;

            if (filter != null && !filter(eventData))
                return;

            var payload = CreateRelayedEventPayload(eventData);
            Interlocked.Increment(ref totalEventBusEventsRelayed);

            if (!string.IsNullOrWhiteSpace(targetPeerId))
                SendToPeer(targetPeerId, payload, bridgeChannel, deliveryMode);
            else if (broadcast)
                Broadcast(payload, bridgeChannel, deliveryMode);
            else
                SendToAllPeers(payload, bridgeChannel, deliveryMode);
        };

        if (string.IsNullOrWhiteSpace(key))
            token = EventBus!.Subscribe(handler, owner: owner);
        else
            token = EventBus!.Subscribe(key, handler, owner: owner);

        lock (eventBridgeLock)
            eventBusBridgeRegistrations.Add(new EventBusBridgeRegistration(key?.Trim(), token));

        return token;
    }

    /// <summary>
    /// Republishes received relayed EventBus payloads into the local EventBus.
    /// </summary>
    /// <typeparam name="TEvent">The EventBus event type to republish locally.</typeparam>
    /// <param name="channel">Optional relay channel override. If omitted, the event type name is used.</param>
    /// <param name="priority">The relay subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to skip specific received relay events.</param>
    /// <param name="owner">Optional owner object for the relay subscription.</param>
    /// <param name="key">Optional relay subscription key prefix.</param>
    /// <returns>The relay subscription token used for the inbound bridge.</returns>
    public NetworkRelaySubscriptionToken PublishReceivedToEventBus<TEvent>(
        string? channel = null,
        int priority = 0,
        Func<NetworkRelayMessage<TEvent>, bool>? filter = null,
        object? owner = null,
        string? key = null)
    {
        EnsureEventBusConfigured();

        var bridgeChannel = ResolveEventBridgeChannel<TEvent>(channel);
        var subscriptionKey = string.IsNullOrWhiteSpace(key) ? null : $"{key.Trim()}:relay";

        Action<NetworkRelayMessage<RelayedEventPayload>> handler = relayMessage =>
        {
            if (!TryConvertRelayedEventMessage(relayMessage, out NetworkRelayMessage<TEvent>? typedMessage))
                return;

            if (filter != null && !filter(typedMessage))
                return;

            RunWithSuppressedEventBusRelay(() => EventBus!.Publish(typedMessage.Payload));
            Interlocked.Increment(ref totalEventBusEventsPublishedLocally);
        };

        return subscriptionKey == null
            ? Subscribe<RelayedEventPayload>(handler, bridgeChannel, priority, owner: owner)
            : Subscribe<RelayedEventPayload>(subscriptionKey, handler, bridgeChannel, priority, owner: owner);
    }

    /// <summary>
    /// Bridges an EventBus event type in both directions in a single call.<br/>
    /// Local EventBus publishes are relayed over the network, and received relay events are republished into the local EventBus.
    /// </summary>
    /// <typeparam name="TEvent">The EventBus event type to bridge.</typeparam>
    /// <param name="channel">Optional relay channel override. If omitted, the event type name is used.</param>
    /// <param name="relayLocalPublishes">Whether local EventBus publishes should be relayed outward.</param>
    /// <param name="publishReceivedLocally">Whether received relay events should be republished into the local EventBus.</param>
    /// <param name="broadcast">Whether outbound relayed events should use broadcast delivery.</param>
    /// <param name="targetPeerId">Optional target peer ID for direct outbound delivery.</param>
    /// <param name="priority">The inbound relay subscription priority. Higher values execute first.</param>
    /// <param name="eventBusFilter">Optional filter used for outbound EventBus events.</param>
    /// <param name="relayFilter">Optional filter used for inbound relay events.</param>
    /// <param name="owner">Optional owner object for created subscriptions.</param>
    /// <param name="key">Optional key used to manage the bridge registration.</param>
    /// <param name="deliveryMode">The delivery mode used when relaying local EventBus publishes outward.</param>
    /// <returns>A handle describing the created bridge subscriptions.</returns>
    public NetworkRelayEventBridgeHandle BridgeEvent<TEvent>(
        string? channel = null,
        bool relayLocalPublishes = true,
        bool publishReceivedLocally = true,
        bool broadcast = true,
        string? targetPeerId = null,
        int priority = 0,
        Func<TEvent, bool>? eventBusFilter = null,
        Func<NetworkRelayMessage<TEvent>, bool>? relayFilter = null,
        object? owner = null,
        string? key = null,
        NetworkRelayDeliveryMode deliveryMode = NetworkRelayDeliveryMode.BestEffort)
    {
        if (!relayLocalPublishes && !publishReceivedLocally)
            throw new ArgumentException("At least one bridge direction must be enabled.", nameof(relayLocalPublishes));

        var bridgeKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (bridgeKey != null)
            UnbridgeEvent(bridgeKey);

        var bridgeChannel = ResolveEventBridgeChannel<TEvent>(channel);
        var eventBusToken = default(EventSubscriptionToken);
        var relayToken = default(NetworkRelaySubscriptionToken);

        if (relayLocalPublishes)
            eventBusToken = RelayPublishedEvent(bridgeChannel, broadcast, targetPeerId, eventBusFilter, owner, bridgeKey == null ? null : $"{bridgeKey}:eventbus", deliveryMode);

        if (publishReceivedLocally)
            relayToken = PublishReceivedToEventBus(bridgeChannel, priority, relayFilter, owner, bridgeKey);

        var handle = new NetworkRelayEventBridgeHandle(bridgeChannel, eventBusToken, relayToken);

        if (bridgeKey != null)
        {
            lock (eventBridgeLock)
                keyToEventBridge[bridgeKey] = handle;
        }

        return handle;
    }

    /// <summary>
    /// Removes a bridged EventBus registration by key.
    /// </summary>
    /// <param name="key">The key associated with the bridge registration.</param>
    /// <returns><see langword="true"/> if a bridge registration was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnbridgeEvent(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        NetworkRelayEventBridgeHandle handle;
        lock (eventBridgeLock)
        {
            if (!keyToEventBridge.Remove(key.Trim(), out handle))
                return false;
        }

        return UnbridgeEvent(handle);
    }

    /// <summary>
    /// Removes a bridged EventBus registration.
    /// </summary>
    /// <param name="handle">The bridge handle to remove.</param>
    /// <returns><see langword="true"/> if any part of the bridge was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnbridgeEvent(NetworkRelayEventBridgeHandle handle)
    {
        var removed = false;

        if (handle.HasRelaySubscription)
            removed |= Unsubscribe(handle.RelaySubscriptionToken);

        if (handle.HasEventBusSubscription)
            removed |= RemoveEventBusBridgeSubscription(handle.EventBusSubscriptionToken);

        return removed;
    }

    /// <summary>
    /// Removes all bridged EventBus registrations tracked by this relay.
    /// </summary>
    /// <returns>The number of bridge-related subscriptions removed.</returns>
    public int ClearEventBridges()
    {
        List<EventSubscriptionToken> eventBusTokens;
        List<NetworkRelayEventBridgeHandle> bridgeHandles;

        lock (eventBridgeLock)
        {
            eventBusTokens = eventBusBridgeRegistrations.Select(registration => registration.Token).ToList();
            bridgeHandles = keyToEventBridge.Values.ToList();
            eventBusBridgeRegistrations.Clear();
            keyToEventBridge.Clear();
        }

        foreach (var handle in bridgeHandles)
        {
            if (handle.HasRelaySubscription)
                Unsubscribe(handle.RelaySubscriptionToken);
        }

        foreach (var token in eventBusTokens.Distinct())
            EventBus?.Unsubscribe(token);

        return eventBusTokens.Count + bridgeHandles.Count(handle => handle.HasRelaySubscription);
    }

    private string ResolveEventBridgeChannel<TEvent>(string? channel)
        => NormalizeChannel(string.IsNullOrWhiteSpace(channel)
            ? (typeof(TEvent).FullName ?? typeof(TEvent).Name)
            : channel);

    private RelayedEventPayload CreateRelayedEventPayload<TEvent>(TEvent eventData)
        => new(
            typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name,
            CreatePayloadToken(eventData));

    private bool TryConvertRelayedEventMessage<TEvent>(NetworkRelayMessage<RelayedEventPayload> relayMessage, out NetworkRelayMessage<TEvent>? typedMessage)
    {
        typedMessage = null;

        var expectedEventType = typeof(TEvent);
        var eventTypeName = relayMessage.Payload.EventType;
        if (!string.Equals(eventTypeName, expectedEventType.AssemblyQualifiedName, StringComparison.Ordinal)
            && !string.Equals(eventTypeName, expectedEventType.FullName, StringComparison.Ordinal)
            && !string.Equals(eventTypeName, expectedEventType.Name, StringComparison.Ordinal))
            return false;

        var payload = relayMessage.Payload.Payload.ToObject<TEvent>(CreateJsonSerializer());
        if (payload == null)
            return false;

        typedMessage = new NetworkRelayMessage<TEvent>(
            relayMessage.MessageId,
            relayMessage.Channel,
            relayMessage.SenderId,
            relayMessage.SenderDisplayName,
            relayMessage.MessageType,
            payload,
            relayMessage.SentAtUtc,
            relayMessage.RemoteEndPoint,
            relayMessage.TargetPeerId,
            relayMessage.TargetPeerIds,
            relayMessage.TransportKind);

        return true;
    }

    private bool IsEventBusRelaySuppressed() => suppressEventBusRelayScope.Value > 0;

    private void RunWithSuppressedEventBusRelay(Action action)
    {
        suppressEventBusRelayScope.Value++;
        try
        {
            action();
        }
        finally
        {
            suppressEventBusRelayScope.Value--;
        }
    }

    private void EnsureEventBusConfigured()
    {
        if (EventBus == null)
            throw new InvalidOperationException("EventBus must be configured before using EventBus bridge features.");
    }

    private bool RemoveEventBusBridgeSubscription(EventSubscriptionToken token)
    {
        lock (eventBridgeLock)
            eventBusBridgeRegistrations.RemoveAll(registration => registration.Token.Equals(token));

        return EventBus?.Unsubscribe(token) == true;
    }

    private void PublishIntegrationEvent<TEvent>(TEvent eventData)
    {
        if (EventBus == null)
            return;

        try
        {
            EventBus.Publish(eventData);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalDispatchExceptionsCaught);

            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"Exception while publishing relay integration event '{typeof(TEvent).Name}'");

            if (ExceptionHandling is ExceptionBehavior.LogAndThrow or ExceptionBehavior.Throw)
                ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }
}
