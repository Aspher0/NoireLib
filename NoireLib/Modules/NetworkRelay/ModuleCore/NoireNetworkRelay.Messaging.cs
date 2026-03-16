using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.NetworkRelay;

public partial class NoireNetworkRelay
{
    #region Public API

    /// <summary>
    /// Starts the relay transport.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay Start()
    {
        Activate();
        return this;
    }

    /// <summary>
    /// Stops the relay transport.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay Stop()
    {
        Deactivate();
        return this;
    }

    /// <summary>
    /// Sends a payload using the default relay channel and best-effort delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay Send<TPayload>(TPayload payload, string? channel = null)
        => Send(payload, channel, NetworkRelayDeliveryMode.BestEffort);

    /// <summary>
    /// Sends a payload using the requested delivery mode.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="deliveryMode">The delivery mode to use.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay Send<TPayload>(TPayload payload, string? channel, NetworkRelayDeliveryMode deliveryMode)
        => Broadcast(payload, channel, deliveryMode);

    /// <summary>
    /// Sends a raw byte payload using the default relay channel and best-effort delivery.
    /// </summary>
    /// <param name="payload">The raw byte payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendBytes(byte[] payload, string? channel = null)
        => SendBytes(payload, channel, NetworkRelayDeliveryMode.BestEffort);

    /// <summary>
    /// Sends a raw byte payload using the requested delivery mode.
    /// </summary>
    /// <param name="payload">The raw byte payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="deliveryMode">The delivery mode to use.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendBytes(byte[] payload, string? channel, NetworkRelayDeliveryMode deliveryMode)
        => Send(payload, channel, deliveryMode);

    /// <summary>
    /// Sends a payload using reliable TCP fan-out delivery to all currently known peers.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendReliable<TPayload>(TPayload payload, string? channel = null)
        => Broadcast(payload, channel, NetworkRelayDeliveryMode.Reliable);

    /// <summary>
    /// Sends a raw byte payload using reliable TCP fan-out delivery to all currently known peers.
    /// </summary>
    /// <param name="payload">The raw byte payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendReliableBytes(byte[] payload, string? channel = null)
        => SendBytes(payload, channel, NetworkRelayDeliveryMode.Reliable);

    /// <summary>
    /// Sends a payload directly to a registered peer using best-effort delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="peerId">The target peer identifier.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendToPeer<TPayload>(string peerId, TPayload payload, string? channel = null)
        => SendToPeer(peerId, payload, channel, NetworkRelayDeliveryMode.BestEffort);

    /// <summary>
    /// Sends a payload directly to a registered peer using the requested delivery mode.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="peerId">The target peer identifier.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="deliveryMode">The delivery mode to use.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendToPeer<TPayload>(string peerId, TPayload payload, string? channel, NetworkRelayDeliveryMode deliveryMode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(peerId))
                throw new ArgumentException("Peer ID cannot be empty.", nameof(peerId));

            EnsureCanSend();

            var trimmedPeerId = peerId.Trim();
            IPEndPoint endPoint;

            if (deliveryMode == NetworkRelayDeliveryMode.Reliable)
            {
                EnsureReliableTransportEnabled();
                if (!TryGetReliablePeerEndpoint(trimmedPeerId, out endPoint))
                {
                    if (!TryGetPeerEndpoint(trimmedPeerId, out var udpEndPoint))
                        throw new InvalidOperationException($"Peer '{peerId}' is not registered.");

                    endPoint = new IPEndPoint(udpEndPoint.Address, udpEndPoint.Port);
                }
            }
            else if (!TryGetPeerEndpoint(trimmedPeerId, out endPoint))
            {
                throw new InvalidOperationException($"Peer '{peerId}' is not registered.");
            }

            var envelope = CreateMessageEnvelope(payload, channel, trimmedPeerId, null);
            SendEnvelope(envelope, endPoint, deliveryMode);
            return this;
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"sending relay payload to peer '{peerId}'", this);
        }
    }

    /// <summary>
    /// Sends a payload directly to a registered peer using reliable TCP delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="peerId">The target peer identifier.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendReliableToPeer<TPayload>(string peerId, TPayload payload, string? channel = null)
        => SendToPeer(peerId, payload, channel, NetworkRelayDeliveryMode.Reliable);

    /// <summary>
    /// Sends a payload directly to a registered peer using reliable TCP delivery and waits for an acknowledgement from the receiver.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="peerId">The target peer identifier.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="acknowledgementTimeout">Optional override for the acknowledgement timeout.</param>
    /// <param name="onSuccess">Optional callback invoked when the send is acknowledged successfully.</param>
    /// <param name="onFailure">Optional callback invoked when the send fails or times out.</param>
    /// <param name="cancellationToken">Optional cancellation token used to cancel the send operation.</param>
    /// <returns>A task that completes when the receiver acknowledges the payload.</returns>
    public Task<NetworkRelaySendReceipt> SendReliableToPeerAsync<TPayload>(
        string peerId,
        TPayload payload,
        string? channel = null,
        TimeSpan? acknowledgementTimeout = null,
        Action<NetworkRelaySendReceipt>? onSuccess = null,
        Action<Exception>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(peerId))
                throw new ArgumentException("Peer ID cannot be empty.", nameof(peerId));

            EnsureCanSend();
            EnsureReliableTransportEnabled();

            var trimmedPeerId = peerId.Trim();
            IPEndPoint endPoint;

            if (!TryGetReliablePeerEndpoint(trimmedPeerId, out endPoint))
            {
                if (!TryGetPeerEndpoint(trimmedPeerId, out var udpEndPoint))
                    throw new InvalidOperationException($"Peer '{peerId}' is not registered.");

                endPoint = new IPEndPoint(udpEndPoint.Address, udpEndPoint.Port);
            }

            var envelope = CreateMessageEnvelope(payload, channel, trimmedPeerId, null, requiresAcknowledgement: true);
            return SendReliableEnvelopeAwaitingAcknowledgementAsync(envelope, endPoint, acknowledgementTimeout, onSuccess, onFailure, cancellationToken);
        }
        catch (Exception ex)
        {
            ReportError(ex, $"sending reliable relay payload to peer '{peerId}'");
            InvokeReliableSendFailureCallback(onFailure, ex);

            if (ShouldRethrowException())
                return Task.FromException<NetworkRelaySendReceipt>(ex);

            return Task.FromResult<NetworkRelaySendReceipt>(default!);
        }
    }

    /// <summary>
    /// Sends a payload directly to a specific endpoint using best-effort delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="hostOrAddress">The hostname or IP address of the target endpoint.</param>
    /// <param name="port">The target port.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="targetPeerId">Optional target peer identifier to include in the envelope metadata.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendTo<TPayload>(string hostOrAddress, int port, TPayload payload, string? channel = null, string? targetPeerId = null)
        => SendTo(hostOrAddress, port, payload, channel, targetPeerId, NetworkRelayDeliveryMode.BestEffort);

    /// <summary>
    /// Sends a payload directly to a specific endpoint using the requested delivery mode.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="hostOrAddress">The hostname or IP address of the target endpoint.</param>
    /// <param name="port">The target port.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="targetPeerId">Optional target peer identifier to include in the envelope metadata.</param>
    /// <param name="deliveryMode">The delivery mode to use.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendTo<TPayload>(string hostOrAddress, int port, TPayload payload, string? channel, string? targetPeerId, NetworkRelayDeliveryMode deliveryMode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hostOrAddress))
                throw new ArgumentException("Host cannot be empty.", nameof(hostOrAddress));

            EnsureCanSend();

            if (deliveryMode == NetworkRelayDeliveryMode.Reliable)
                EnsureReliableTransportEnabled();

            var address = ResolveAddress(hostOrAddress.Trim(), BindAddress.AddressFamily);
            var endPoint = new IPEndPoint(address, ValidatePort(port));
            var envelope = CreateMessageEnvelope(payload, channel, string.IsNullOrWhiteSpace(targetPeerId) ? null : targetPeerId.Trim(), null);
            SendEnvelope(envelope, endPoint, deliveryMode);
            return this;
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"sending relay payload to '{hostOrAddress}:{port}'", this);
        }
    }

    /// <summary>
    /// Sends a payload directly to a specific endpoint using reliable TCP delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="hostOrAddress">The hostname or IP address of the target endpoint.</param>
    /// <param name="port">The target TCP port.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="targetPeerId">Optional target peer identifier to include in the envelope metadata.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendReliableTo<TPayload>(string hostOrAddress, int port, TPayload payload, string? channel = null, string? targetPeerId = null)
        => SendTo(hostOrAddress, port, payload, channel, targetPeerId, NetworkRelayDeliveryMode.Reliable);

    /// <summary>
    /// Sends a payload directly to a specific endpoint using reliable TCP delivery and waits for an acknowledgement from the receiver.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="hostOrAddress">The hostname or IP address of the target endpoint.</param>
    /// <param name="port">The target TCP port.</param>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="targetPeerId">Optional target peer identifier to include in the envelope metadata.</param>
    /// <param name="acknowledgementTimeout">Optional override for the acknowledgement timeout.</param>
    /// <param name="onSuccess">Optional callback invoked when the send is acknowledged successfully.</param>
    /// <param name="onFailure">Optional callback invoked when the send fails or times out.</param>
    /// <param name="cancellationToken">Optional cancellation token used to cancel the send operation.</param>
    /// <returns>A task that completes when the receiver acknowledges the payload.</returns>
    public Task<NetworkRelaySendReceipt> SendReliableToAsync<TPayload>(
        string hostOrAddress,
        int port,
        TPayload payload,
        string? channel = null,
        string? targetPeerId = null,
        TimeSpan? acknowledgementTimeout = null,
        Action<NetworkRelaySendReceipt>? onSuccess = null,
        Action<Exception>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hostOrAddress))
                throw new ArgumentException("Host cannot be empty.", nameof(hostOrAddress));

            EnsureCanSend();
            EnsureReliableTransportEnabled();

            var address = ResolveAddress(hostOrAddress.Trim(), BindAddress.AddressFamily);
            var endPoint = new IPEndPoint(address, ValidatePort(port));
            var envelope = CreateMessageEnvelope(payload, channel, string.IsNullOrWhiteSpace(targetPeerId) ? null : targetPeerId.Trim(), null, requiresAcknowledgement: true);
            return SendReliableEnvelopeAwaitingAcknowledgementAsync(envelope, endPoint, acknowledgementTimeout, onSuccess, onFailure, cancellationToken);
        }
        catch (Exception ex)
        {
            ReportError(ex, $"sending reliable relay payload to '{hostOrAddress}:{port}'");
            InvokeReliableSendFailureCallback(onFailure, ex);

            if (ShouldRethrowException())
                return Task.FromException<NetworkRelaySendReceipt>(ex);

            return Task.FromResult<NetworkRelaySendReceipt>(default!);
        }
    }

    /// <summary>
    /// Sends a payload to all currently tracked peers using direct best-effort delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendToAllPeers<TPayload>(TPayload payload, string? channel = null)
        => SendToAllPeers(payload, channel, NetworkRelayDeliveryMode.BestEffort);

    /// <summary>
    /// Sends a payload to all currently tracked peers using the requested delivery mode.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="deliveryMode">The delivery mode to use.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendToAllPeers<TPayload>(TPayload payload, string? channel, NetworkRelayDeliveryMode deliveryMode)
    {
        try
        {
            EnsureCanSend();

            if (deliveryMode == NetworkRelayDeliveryMode.Reliable)
                EnsureReliableTransportEnabled();

            var peersSnapshot = GetPeers();
            foreach (var peer in peersSnapshot)
            {
                if (!AllowLoopbackMessages && string.Equals(peer.PeerId, InstanceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var endPoint = deliveryMode == NetworkRelayDeliveryMode.Reliable
                    ? peer.ReliableEndPoint ?? new IPEndPoint(peer.EndPoint.Address, peer.EndPoint.Port)
                    : peer.EndPoint;

                SendEnvelope(CreateMessageEnvelope(payload, channel, peer.PeerId, null), endPoint, deliveryMode);
            }

            return this;
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, "sending relay payload to all peers", this);
        }
    }

    /// <summary>
    /// Sends a payload to all currently tracked peers using reliable TCP delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SendReliableToAllPeers<TPayload>(TPayload payload, string? channel = null)
        => SendToAllPeers(payload, channel, NetworkRelayDeliveryMode.Reliable);

    /// <summary>
    /// Sends a payload to all listeners on the configured UDP port using best-effort delivery.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay Broadcast<TPayload>(TPayload payload, string? channel = null)
        => Broadcast(payload, channel, NetworkRelayDeliveryMode.BestEffort);

    /// <summary>
    /// Sends a payload using the requested delivery mode.<br/>
    /// Reliable delivery uses direct TCP fan-out to currently known peers because TCP does not support broadcast.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to send.</typeparam>
    /// <param name="payload">The payload to send.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <param name="deliveryMode">The delivery mode to use.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay Broadcast<TPayload>(TPayload payload, string? channel, NetworkRelayDeliveryMode deliveryMode)
    {
        try
        {
            EnsureCanSend();

            if (deliveryMode == NetworkRelayDeliveryMode.Reliable)
                return SendToAllPeers(payload, channel, deliveryMode);

            SendEnvelope(CreateMessageEnvelope(payload, channel, null, null), GetBroadcastEndPoint(), deliveryMode);
            return this;
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, "broadcasting relay payload", this);
        }
    }

    /// <summary>
    /// Broadcasts a simple text payload using best-effort delivery.
    /// </summary>
    /// <param name="payload">The string payload to broadcast.</param>
    /// <param name="channel">Optional relay channel to send on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay BroadcastString(string payload, string? channel = null)
        => Broadcast(payload, channel);

    /// <summary>
    /// Registers a payload callback for a relay channel.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="callback">The callback invoked when a matching payload is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific payloads.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken On<TPayload>(
        Action<TPayload> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<TPayload, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            return Subscribe<TPayload>(
                message => callback(message.Payload),
                channel,
                priority,
                filter != null ? message => filter(message.Payload) : null,
                owner);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering relay payload callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Registers a keyed payload callback for a relay channel.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="key">A unique subscription key.</param>
    /// <param name="callback">The callback invoked when a matching payload is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific payloads.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken On<TPayload>(
        string key,
        Action<TPayload> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<TPayload, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            return Subscribe<TPayload>(
                key,
                message => callback(message.Payload),
                channel,
                priority,
                filter != null ? message => filter(message.Payload) : null,
                owner);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering keyed relay payload callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Registers an async payload callback for a relay channel.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="callback">The async callback invoked when a matching payload is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific payloads.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken OnAsync<TPayload>(
        Func<TPayload, Task> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<TPayload, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            return SubscribeAsync<TPayload>(
                message => callback(message.Payload),
                channel,
                priority,
                filter != null ? message => filter(message.Payload) : null,
                owner);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering async relay payload callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Registers a keyed async payload callback for a relay channel.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="key">A unique subscription key.</param>
    /// <param name="callback">The async callback invoked when a matching payload is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific payloads.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken OnAsync<TPayload>(
        string key,
        Func<TPayload, Task> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<TPayload, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            return SubscribeAsync<TPayload>(
                key,
                message => callback(message.Payload),
                channel,
                priority,
                filter != null ? message => filter(message.Payload) : null,
                owner);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering keyed async relay payload callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Registers a raw message callback for a relay channel.
    /// </summary>
    /// <param name="callback">The callback invoked with the raw relay message.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken OnMessage(
        Action<NetworkRelayMessage> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(null, channel, callback ?? throw new ArgumentNullException(nameof(callback)), filter, priority, owner, isAsync: false);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering relay message callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Registers a keyed raw message callback for a relay channel.
    /// </summary>
    /// <param name="key">A unique subscription key.</param>
    /// <param name="callback">The callback invoked with the raw relay message.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken OnMessage(
        string key,
        Action<NetworkRelayMessage> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(key, channel, callback ?? throw new ArgumentNullException(nameof(callback)), filter, priority, owner, isAsync: false);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering keyed relay message callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Registers an async raw message callback for a relay channel.
    /// </summary>
    /// <param name="callback">The async callback invoked with the raw relay message.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken OnMessageAsync(
        Func<NetworkRelayMessage, Task> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(null, channel, callback ?? throw new ArgumentNullException(nameof(callback)), filter, priority, owner, isAsync: true);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering async relay message callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Registers a keyed async raw message callback for a relay channel.
    /// </summary>
    /// <param name="key">A unique subscription key.</param>
    /// <param name="callback">The async callback invoked with the raw relay message.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken OnMessageAsync(
        string key,
        Func<NetworkRelayMessage, Task> callback,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(key, channel, callback ?? throw new ArgumentNullException(nameof(callback)), filter, priority, owner, isAsync: true);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"registering keyed async relay message callback for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Subscribes to a logical relay channel with a typed synchronous handler.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="handler">The handler invoked when a matching message is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken Subscribe<TPayload>(
        Action<NetworkRelayMessage<TPayload>> handler,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage<TPayload>, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(null, channel, WrapHandler(handler), WrapFilter(filter), priority, owner, isAsync: false);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"subscribing typed relay handler for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Subscribes to a logical relay channel with a typed synchronous handler and custom key.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="key">A unique subscription key.</param>
    /// <param name="handler">The handler invoked when a matching message is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken Subscribe<TPayload>(
        string key,
        Action<NetworkRelayMessage<TPayload>> handler,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage<TPayload>, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(key, channel, WrapHandler(handler), WrapFilter(filter), priority, owner, isAsync: false);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"subscribing keyed typed relay handler for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Subscribes to a logical relay channel with a typed asynchronous handler.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="handler">The async handler invoked when a matching message is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken SubscribeAsync<TPayload>(
        Func<NetworkRelayMessage<TPayload>, Task> handler,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage<TPayload>, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(null, channel, WrapAsyncHandler(handler), WrapFilter(filter), priority, owner, isAsync: true);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"subscribing async typed relay handler for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Subscribes to a logical relay channel with a typed asynchronous handler and custom key.
    /// </summary>
    /// <typeparam name="TPayload">The payload type to deserialize and deliver.</typeparam>
    /// <param name="key">A unique subscription key.</param>
    /// <param name="handler">The async handler invoked when a matching message is received.</param>
    /// <param name="channel">The relay channel to subscribe to.</param>
    /// <param name="priority">The subscription priority. Higher values execute first.</param>
    /// <param name="filter">Optional filter used to ignore specific messages.</param>
    /// <param name="owner">Optional owner object for grouping subscriptions.</param>
    /// <returns>A subscription token that can be used to unsubscribe.</returns>
    public NetworkRelaySubscriptionToken SubscribeAsync<TPayload>(
        string key,
        Func<NetworkRelayMessage<TPayload>, Task> handler,
        string channel = WildcardChannel,
        int priority = 0,
        Func<NetworkRelayMessage<TPayload>, bool>? filter = null,
        object? owner = null)
    {
        try
        {
            return SubscribeInternal(key, channel, WrapAsyncHandler(handler), WrapFilter(filter), priority, owner, isAsync: true);
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"subscribing keyed async typed relay handler for channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    /// <summary>
    /// Unsubscribes a relay subscription using its token.
    /// </summary>
    /// <param name="token">The relay subscription token to remove.</param>
    /// <returns><see langword="true"/> if the subscription was removed; otherwise, <see langword="false"/>.</returns>
    public bool Unsubscribe(NetworkRelaySubscriptionToken token)
    {
        lock (subscriptionLock)
        {
            RelaySubscriptionEntry? entryToRemove = null;
            string? channelToRemove = null;

            foreach (var kvp in subscriptions)
            {
                entryToRemove = kvp.Value.FirstOrDefault(entry => entry.Token.Equals(token));
                if (entryToRemove != null)
                {
                    channelToRemove = kvp.Key;
                    break;
                }
            }

            if (entryToRemove == null || channelToRemove == null)
                return false;

            var handlers = subscriptions[channelToRemove];
            handlers.Remove(entryToRemove);

            if (entryToRemove.Key != null)
                keyToSubscription.Remove(entryToRemove.Key);

            if (handlers.Count == 0)
                subscriptions.Remove(channelToRemove);

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Unsubscribed relay handler from channel '{entryToRemove.Channel}'.");

            return true;
        }
    }

    /// <summary>
    /// Unsubscribes a relay subscription using its custom key.
    /// </summary>
    /// <param name="key">The subscription key to remove.</param>
    /// <returns><see langword="true"/> if the subscription was removed; otherwise, <see langword="false"/>.</returns>
    public bool Unsubscribe(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        NetworkRelaySubscriptionToken token;
        lock (subscriptionLock)
        {
            if (!keyToSubscription.TryGetValue(key.Trim(), out token))
                return false;
        }

        return Unsubscribe(token);
    }

    /// <summary>
    /// Unsubscribes the first relay handler found for the specified channel and owner.
    /// </summary>
    /// <param name="channel">The relay channel to search.</param>
    /// <param name="owner">Optional owner object used to constrain the search.</param>
    /// <returns><see langword="true"/> if a subscription was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnsubscribeFirst(string channel = WildcardChannel, object? owner = null)
    {
        try
        {
            channel = NormalizeSubscriptionChannel(channel);

            lock (subscriptionLock)
            {
                if (!subscriptions.TryGetValue(channel, out var handlers))
                    return false;

                var toRemove = handlers.FirstOrDefault(entry => owner == null || ReferenceEquals(entry.Owner, owner));
                if (toRemove == null)
                    return false;

                handlers.Remove(toRemove);

                if (handlers.Count == 0)
                    subscriptions.Remove(channel);

                if (toRemove.Key != null)
                    keyToSubscription.Remove(toRemove.Key);

                return true;
            }
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"unsubscribing first relay handler for channel '{channel}'", false);
        }
    }

    /// <summary>
    /// Unsubscribes all relay handlers belonging to a specific owner.
    /// </summary>
    /// <param name="owner">The owner whose subscriptions should be removed.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeAll(object owner)
    {
        try
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            int totalRemoved = 0;

            lock (subscriptionLock)
            {
                foreach (var channel in subscriptions.Keys.ToList())
                {
                    var handlers = subscriptions[channel];
                    foreach (var entry in handlers.Where(entry => ReferenceEquals(entry.Owner, owner)).ToList())
                    {
                        if (entry.Key != null)
                            keyToSubscription.Remove(entry.Key);
                    }

                    totalRemoved += handlers.RemoveAll(entry => ReferenceEquals(entry.Owner, owner));

                    if (handlers.Count == 0)
                        subscriptions.Remove(channel);
                }
            }

            return totalRemoved;
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, "unsubscribing all relay handlers for owner", 0);
        }
    }

    /// <summary>
    /// Unsubscribes all relay handlers from a specific channel, optionally filtered by owner.
    /// </summary>
    /// <param name="channel">The relay channel to clear.</param>
    /// <param name="owner">Optional owner object used to constrain which subscriptions are removed.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeAll(string channel, object? owner = null)
    {
        try
        {
            channel = NormalizeSubscriptionChannel(channel);

            lock (subscriptionLock)
            {
                if (!subscriptions.TryGetValue(channel, out var handlers))
                    return 0;

                var entriesToRemove = owner == null
                    ? handlers.ToList()
                    : handlers.Where(entry => ReferenceEquals(entry.Owner, owner)).ToList();

                foreach (var entry in entriesToRemove)
                {
                    if (entry.Key != null)
                        keyToSubscription.Remove(entry.Key);
                }

                int removed;
                if (owner == null)
                {
                    removed = handlers.Count;
                    handlers.Clear();
                    subscriptions.Remove(channel);
                }
                else
                {
                    removed = handlers.RemoveAll(entry => ReferenceEquals(entry.Owner, owner));
                    if (handlers.Count == 0)
                        subscriptions.Remove(channel);
                }

                return removed;
            }
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"unsubscribing relay handlers for channel '{channel}'", 0);
        }
    }

    /// <summary>
    /// Clears all relay subscriptions.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay ClearAllSubscriptions()
    {
        lock (subscriptionLock)
        {
            subscriptions.Clear();
            keyToSubscription.Clear();
        }

        return this;
    }

    /// <summary>
    /// Gets the number of registered relay subscriptions.
    /// </summary>
    /// <param name="channel">The relay channel to inspect.</param>
    /// <returns>The number of registered subscriptions for the specified channel.</returns>
    public int GetSubscriberCount(string channel = WildcardChannel)
    {
        try
        {
            channel = NormalizeSubscriptionChannel(channel);

            lock (subscriptionLock)
            {
                return subscriptions.TryGetValue(channel, out var handlers)
                    ? handlers.Count
                    : 0;
            }
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"reading relay subscriber count for channel '{channel}'", 0);
        }
    }

    /// <summary>
    /// Gets statistics about this relay instance.
    /// </summary>
    /// <returns>A snapshot of the relay statistics.</returns>
    public NetworkRelayStatistics GetStatistics()
    {
        lock (subscriptionLock)
        {
            lock (peerLock)
            {
                lock (eventBridgeLock)
                {
                    return new NetworkRelayStatistics(
                        ActivePeers: peers.Count,
                        ActiveSubscriptions: subscriptions.Values.Sum(list => list.Count),
                        ActiveEventBridges: keyToEventBridge.Count + eventBusBridgeRegistrations.Count,
                        ReliableTransportEnabled: EnableReliableTransport,
                        TotalMessagesSent: Interlocked.Read(ref totalMessagesSent),
                        TotalMessagesReceived: Interlocked.Read(ref totalMessagesReceived),
                        TotalBestEffortMessagesSent: Interlocked.Read(ref totalBestEffortMessagesSent),
                        TotalBestEffortMessagesReceived: Interlocked.Read(ref totalBestEffortMessagesReceived),
                        TotalReliableMessagesSent: Interlocked.Read(ref totalReliableMessagesSent),
                        TotalReliableMessagesReceived: Interlocked.Read(ref totalReliableMessagesReceived),
                        TotalReliableConnectionsAccepted: Interlocked.Read(ref totalReliableConnectionsAccepted),
                        TotalBytesSent: Interlocked.Read(ref totalBytesSent),
                        TotalBytesReceived: Interlocked.Read(ref totalBytesReceived),
                        TotalMessagesDropped: Interlocked.Read(ref totalMessagesDropped),
                        TotalDuplicateMessagesDropped: Interlocked.Read(ref totalDuplicateMessagesDropped),
                        TotalPeerAnnouncementsReceived: Interlocked.Read(ref totalPeerAnnouncementsReceived),
                        TotalSendFailures: Interlocked.Read(ref totalSendFailures),
                        TotalReceiveFailures: Interlocked.Read(ref totalReceiveFailures),
                        TotalDispatchExceptionsCaught: Interlocked.Read(ref totalDispatchExceptionsCaught),
                        TotalExceptionsCaught: Interlocked.Read(ref totalExceptionsCaught),
                        TotalEventBusEventsRelayed: Interlocked.Read(ref totalEventBusEventsRelayed),
                        TotalEventBusEventsPublishedLocally: Interlocked.Read(ref totalEventBusEventsPublishedLocally),
                        TotalSubscriptionsCreated: Interlocked.Read(ref totalSubscriptionsCreated),
                        TotalPeersRegistered: Interlocked.Read(ref totalPeersRegistered),
                        TotalPeersRemoved: Interlocked.Read(ref totalPeersRemoved));
                }
            }
        }
    }

    #endregion

    private RelayEnvelope CreateMessageEnvelope<TPayload>(TPayload payload, string? channel, string? targetPeerId, IReadOnlyCollection<string>? targetPeerIds, bool requiresAcknowledgement = false)
        => new()
        {
            Kind = EnvelopeKindMessage,
            Channel = NormalizeChannel(channel),
            MessageId = Guid.NewGuid().ToString("N"),
            SenderId = InstanceId,
            SenderDisplayName = DisplayName,
            SenderReliablePort = EnableReliableTransport ? ReliablePort : null,
            MessageType = typeof(TPayload).FullName ?? typeof(TPayload).Name,
            SentAtUtc = DateTimeOffset.UtcNow,
            TargetPeerId = targetPeerId,
            TargetPeerIds = targetPeerIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RequiresAcknowledgement = requiresAcknowledgement,
            Payload = CreatePayloadToken(payload),
        };

    private async Task<NetworkRelaySendReceipt> SendReliableEnvelopeAwaitingAcknowledgementAsync(
        RelayEnvelope envelope,
        IPEndPoint endPoint,
        TimeSpan? acknowledgementTimeout,
        Action<NetworkRelaySendReceipt>? onSuccess,
        Action<Exception>? onFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await SendReliableEnvelopeWithAcknowledgementAsync(envelope, endPoint, acknowledgementTimeout ?? ReliableAcknowledgementTimeout, cancellationToken);
            InvokeReliableSendSuccessCallback(onSuccess, receipt);
            return receipt;
        }
        catch (Exception ex)
        {
            ReportError(ex, $"awaiting reliable acknowledgement from {endPoint}");
            InvokeReliableSendFailureCallback(onFailure, ex);

            if (ShouldRethrowException())
                ExceptionDispatchInfo.Capture(ex).Throw();

            return default!;
        }
    }

    private void InvokeReliableSendSuccessCallback(Action<NetworkRelaySendReceipt>? callback, NetworkRelaySendReceipt receipt)
    {
        if (callback == null)
            return;

        try
        {
            callback(receipt);
        }
        catch (Exception ex)
        {
            ReportError(ex, "invoking reliable send success callback");
        }
    }

    private void InvokeReliableSendFailureCallback(Action<Exception>? callback, Exception exception)
    {
        if (callback == null)
            return;

        try
        {
            callback(exception);
        }
        catch (Exception ex)
        {
            ReportError(ex, "invoking reliable send failure callback");
        }
    }

    private async Task DispatchMessageAsync(NetworkRelayMessage message)
    {
        List<RelaySubscriptionEntry> handlers;

        lock (subscriptionLock)
        {
            handlers = [
                ..subscriptions.TryGetValue(message.Channel, out var channelHandlers) ? channelHandlers : [],
                ..subscriptions.TryGetValue(WildcardChannel, out var wildcardHandlers) ? wildcardHandlers : []
            ];
        }

        if (handlers.Count == 0)
            return;

        foreach (var entry in handlers.OrderByDescending(handler => handler.Priority))
        {
            try
            {
                if (entry.Filter != null && !entry.Filter(message))
                    continue;

                if (entry.IsAsync)
                {
                    var asyncHandler = (Func<NetworkRelayMessage, Task>)entry.Handler;
                    var task = asyncHandler(message);

                    if (AwaitAsyncHandlersOnReceive)
                    {
                        await task;
                    }
                    else
                    {
                        _ = task.ContinueWith(t =>
                        {
                            if (t.IsFaulted && t.Exception?.InnerException != null)
                            {
                                Interlocked.Increment(ref totalDispatchExceptionsCaught);
                                HandleException(t.Exception.InnerException, $"dispatching async relay message '{message.Channel}'");
                            }
                        }, TaskScheduler.Default);
                    }
                }
                else
                {
                    var syncHandler = (Action<NetworkRelayMessage>)entry.Handler;
                    syncHandler(message);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref totalDispatchExceptionsCaught);
                HandleException(ex, $"dispatching relay message '{message.Channel}'");
            }
        }
    }

    private NetworkRelaySubscriptionToken SubscribeInternal(
        string? key,
        string channel,
        Delegate handler,
        Func<NetworkRelayMessage, bool>? filter,
        int priority,
        object? owner,
        bool isAsync)
    {
        try
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (key != null && string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Subscription key cannot be empty.", nameof(key));

            channel = NormalizeSubscriptionChannel(channel);

            if (key != null)
                Unsubscribe(key);

            var token = new NetworkRelaySubscriptionToken(Guid.NewGuid());
            var entry = new RelaySubscriptionEntry(token, handler, priority, filter, owner, isAsync, key, channel);

            lock (subscriptionLock)
            {
                if (!subscriptions.TryGetValue(channel, out var handlers))
                {
                    handlers = [];
                    subscriptions[channel] = handlers;
                }

                handlers.Add(entry);
                handlers.Sort((left, right) => right.Priority.CompareTo(left.Priority));

                if (key != null)
                    keyToSubscription[key] = token;
            }

            Interlocked.Increment(ref totalSubscriptionsCreated);

            if (EnableLogging)
            {
                var keyInfo = key != null ? $" with key '{key}'" : "";
                var asyncInfo = isAsync ? " async" : "";
                NoireLogger.LogDebug(this, $"Subscribed{asyncInfo} relay handler to channel '{channel}'{keyInfo} (Priority: {priority})");
            }

            return token;
        }
        catch (Exception ex)
        {
            return HandleExceptionOrReturn(ex, $"subscribing relay handler to channel '{channel}'", default(NetworkRelaySubscriptionToken));
        }
    }

    private Action<NetworkRelayMessage> WrapHandler<TPayload>(Action<NetworkRelayMessage<TPayload>> handler)
    {
        if (handler == null)
            return HandleExceptionOrReturn<Action<NetworkRelayMessage>>(new ArgumentNullException(nameof(handler)), "creating typed relay handler", static _ => { });

        return message => handler(message.ToTyped<TPayload>(SerializerSettings));
    }

    private Func<NetworkRelayMessage, Task> WrapAsyncHandler<TPayload>(Func<NetworkRelayMessage<TPayload>, Task> handler)
    {
        if (handler == null)
            return HandleExceptionOrReturn<Func<NetworkRelayMessage, Task>>(new ArgumentNullException(nameof(handler)), "creating async typed relay handler", static _ => Task.CompletedTask);

        return message => handler(message.ToTyped<TPayload>(SerializerSettings));
    }

    private Func<NetworkRelayMessage, bool>? WrapFilter<TPayload>(Func<NetworkRelayMessage<TPayload>, bool>? filter)
    {
        if (filter == null)
            return null;

        return message => filter(message.ToTyped<TPayload>(SerializerSettings));
    }

    private void RaiseMessageReceived(NetworkRelayMessage message)
    {
        try
        {
            MessageReceived?.Invoke(message);
            PublishIntegrationEvent(new NetworkRelayMessageReceivedEvent(message));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalDispatchExceptionsCaught);
            HandleException(ex, "raising MessageReceived event");
        }
    }

    private JToken CreatePayloadToken<TPayload>(TPayload payload)
    {
        if (payload == null)
            return JValue.CreateNull();

        if (payload is JToken token)
            return token.DeepClone();

        return JToken.FromObject(payload, CreateJsonSerializer());
    }
}
