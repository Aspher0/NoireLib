using NoireLib.Core.Subscriptions;
using NoireLib.Networker.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.Networker;

public partial class NoireNetworker
{
    private readonly ConcurrentDictionary<string, Type> messageTypes = new();
    private readonly ConcurrentDictionary<string, RequestHandlerEntry> requestHandlers = new();

    private sealed class RequestHandlerEntry
    {
        public required Type RequestType { get; init; }
        public required Func<NetworkerPeer, object, Task<object?>> Invoke { get; init; }
    }

    #region Typed messages

    /// <summary>
    /// Subscribes a handler for messages of type <typeparamref name="TMessage"/>. The handler runs on the framework thread.
    /// </summary>
    /// <typeparam name="TMessage">The message type (any JSON-serializable class).</typeparam>
    /// <param name="handler">The handler, receiving the sending peer and the message.</param>
    /// <param name="key">Optional subscription key; subscribing again with the same key replaces the previous subscription.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken On<TMessage>(Action<NetworkerPeer, TMessage> handler, string? key = null) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var typeName = typeof(TMessage).FullName!;
        messageTypes[typeName] = typeof(TMessage);

        return messageRegistry.Subscribe(
            typeName,
            context => handler(context.Peer, (TMessage)context.Message),
            new NoireSubscriptionOptions<MessageContext> { Key = key });
    }

    /// <summary>
    /// Broadcasts a message to every other peer on the network. The local instance does not receive its own broadcasts.
    /// </summary>
    /// <typeparam name="TMessage">The message type (any JSON-serializable class).</typeparam>
    /// <param name="message">The message to send.</param>
    public void Send<TMessage>(TMessage message) where TMessage : class
        => SendMessageCore(message, target: null);

    /// <summary>
    /// Sends a message to one specific peer.
    /// </summary>
    /// <typeparam name="TMessage">The message type (any JSON-serializable class).</typeparam>
    /// <param name="peer">The peer to send to.</param>
    /// <param name="message">The message to send.</param>
    public void SendTo<TMessage>(NetworkerPeer peer, TMessage message) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(peer);

        if (peer.Id == SelfId)
        {
            InternalLogWarning("SendTo targeting the local instance was ignored.");
            return;
        }

        SendMessageCore(message, peer.Id);
    }

    private void SendMessageCore<TMessage>(TMessage message, Guid? target) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);

        SendEnvelope(new Envelope
        {
            Kind = EnvelopeKind.Message,
            Origin = SelfId,
            Target = target,
            TypeName = message.GetType().FullName,
            Payload = Wire.ToPayload(message),
        });
    }

    #endregion

    #region Request / response

    /// <summary>
    /// Registers the handler answering requests of type <typeparamref name="TRequest"/>. One handler per request type;
    /// registering again replaces the previous handler. The handler runs on the framework thread.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">The handler, receiving the requesting peer and the request, returning the response.</param>
    /// <returns>A token that unregisters the handler when disposed.</returns>
    public NoireSubscriptionToken OnRequest<TRequest, TResponse>(Func<NetworkerPeer, TRequest, TResponse> handler)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        return RegisterRequestHandler<TRequest>((peer, request) =>
        {
            try
            {
                return Task.FromResult<object?>(handler(peer, (TRequest)request));
            }
            catch (Exception ex)
            {
                return Task.FromException<object?>(ex);
            }
        });
    }

    /// <summary>
    /// Registers an asynchronous handler answering requests of type <typeparamref name="TRequest"/>. One handler per request type;
    /// registering again replaces the previous handler. The handler starts on the framework thread.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">The async handler, receiving the requesting peer and the request, returning the response.</param>
    /// <returns>A token that unregisters the handler when disposed.</returns>
    public NoireSubscriptionToken OnRequest<TRequest, TResponse>(Func<NetworkerPeer, TRequest, Task<TResponse>> handler)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        return RegisterRequestHandler<TRequest>(async (peer, request) => await handler(peer, (TRequest)request).ConfigureAwait(false));
    }

    private NoireSubscriptionToken RegisterRequestHandler<TRequest>(Func<NetworkerPeer, object, Task<object?>> invoke)
        where TRequest : class
    {
        var typeName = typeof(TRequest).FullName!;

        var entry = new RequestHandlerEntry
        {
            RequestType = typeof(TRequest),
            Invoke = invoke,
        };

        if (!requestHandlers.TryAdd(typeName, entry))
        {
            InternalLogWarning($"Replacing the existing request handler for '{typeName}'.");
            requestHandlers[typeName] = entry;
        }

        return new NoireSubscriptionToken(null, 0, _ => requestHandlers.TryRemove(new KeyValuePair<string, RequestHandlerEntry>(typeName, entry)));
    }

    /// <summary>
    /// Sends a request to a peer and awaits its response. The await resumes on the framework thread.<br/>
    /// <b>Never sync-block on the returned task from the framework thread — always await.</b>
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="peer">The peer to ask.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="timeout">Optional timeout; defaults to <see cref="NetworkerOptions.DefaultRequestTimeout"/>.</param>
    /// <returns>The peer's response.</returns>
    /// <exception cref="TimeoutException">The peer did not answer in time.</exception>
    /// <exception cref="PeerLeftException">The peer left the network before answering.</exception>
    /// <exception cref="InvalidOperationException">The remote handler failed, no handler was registered remotely, or the networker is not active.</exception>
    public Task<TResponse> Request<TRequest, TResponse>(NetworkerPeer peer, TRequest request, TimeSpan? timeout = null)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(request);

        if (peer.Id == SelfId)
            return Task.FromException<TResponse>(new InvalidOperationException("Cannot send a request to the local instance."));

        var brokerLocal = broker;

        if (brokerLocal == null)
            return Task.FromException<TResponse>(new InvalidOperationException("The networker is not active."));

        var requestId = Guid.NewGuid();
        var responseTask = brokerLocal.Track(requestId, peer.Id, timeout ?? ActiveOptions.DefaultRequestTimeout);

        SendEnvelope(new Envelope
        {
            Kind = EnvelopeKind.Request,
            Origin = SelfId,
            Target = peer.Id,
            TypeName = request.GetType().FullName,
            Payload = Wire.ToPayload(request),
            RequestId = requestId,
        });

        return AwaitResponseAsync<TResponse>(responseTask);
    }

    private static async Task<TResponse> AwaitResponseAsync<TResponse>(Task<Envelope> responseTask)
    {
        var response = await responseTask;

        if (response.Error != null)
            throw new InvalidOperationException($"The remote handler failed: {response.Error}");

        if (response.Payload == null)
            return default!;

        return (TResponse)Wire.FromPayload(response.Payload, typeof(TResponse))!;
    }

    /// <summary>
    /// Sends a request to every other peer and collects their responses.<br/>
    /// Completes when all peers answered or the timeout elapsed; contains only the successful answers (failures are logged).
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="timeout">Optional per-peer timeout; defaults to <see cref="NetworkerOptions.DefaultRequestTimeout"/>.</param>
    /// <returns>The successful responses by peer.</returns>
    public async Task<IReadOnlyDictionary<NetworkerPeer, TResponse>> RequestAll<TRequest, TResponse>(TRequest request, TimeSpan? timeout = null)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var targets = OtherPeers;
        var results = new Dictionary<NetworkerPeer, TResponse>();

        if (targets.Count == 0)
            return results;

        var tasks = targets.Select(async peer =>
        {
            try
            {
                var response = await Request<TRequest, TResponse>(peer, request, timeout);
                return (Peer: peer, Response: response, Success: true);
            }
            catch (Exception ex)
            {
                InternalLog($"RequestAll: peer {peer} failed: {ex.Message}");
                return (Peer: peer, Response: default(TResponse)!, Success: false);
            }
        }).ToArray();

        foreach (var result in await Task.WhenAll(tasks))
        {
            if (result.Success)
                results[result.Peer] = result.Response;
        }

        return results;
    }

    #endregion

    #region Inbound dispatch (pump thread)

    private void HandleInboundOnPump(Envelope envelope)
    {
        switch (envelope.Kind)
        {
            case EnvelopeKind.Message:
                DispatchInboundMessage(envelope);
                break;

            case EnvelopeKind.Request:
                DispatchInboundRequest(envelope);
                break;

            case EnvelopeKind.Response:
                broker?.Complete(envelope);
                break;

            case EnvelopeKind.Event:
                DispatchInboundSharedEvent(envelope);
                break;
        }
    }

    private void DispatchInboundMessage(Envelope envelope)
    {
        if (envelope.TypeName == null)
            return;

        if (!messageTypes.TryGetValue(envelope.TypeName, out var messageType))
        {
            InternalLog($"Dropping message of unknown type '{envelope.TypeName}'.");
            return;
        }

        object? message;

        try
        {
            message = Wire.FromPayload(envelope.Payload, messageType);
        }
        catch (Exception ex)
        {
            InternalLogWarning($"Dropping undeserializable message of type '{envelope.TypeName}': {ex.Message}");
            return;
        }

        if (message == null)
            return;

        messageRegistry.Dispatch(envelope.TypeName, new MessageContext(ResolvePeer(envelope.Origin), message));
    }

    private void DispatchInboundRequest(Envelope envelope)
    {
        if (envelope.RequestId == null || envelope.Origin == null)
            return;

        if (envelope.TypeName == null || !requestHandlers.TryGetValue(envelope.TypeName, out var entry))
        {
            SendResponse(envelope, payload: null, error: $"No handler registered for '{envelope.TypeName}'.");
            return;
        }

        object? request;

        try
        {
            request = Wire.FromPayload(envelope.Payload, entry.RequestType);
        }
        catch (Exception ex)
        {
            SendResponse(envelope, payload: null, error: $"Undeserializable request: {ex.Message}");
            return;
        }

        if (request == null)
        {
            SendResponse(envelope, payload: null, error: "Empty request payload.");
            return;
        }

        var peer = ResolvePeer(envelope.Origin);

        entry.Invoke(peer, request).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                var reason = task.Exception!.GetBaseException().Message;
                SendResponse(envelope, payload: null, error: reason);
                InternalLogWarning($"Request handler for '{envelope.TypeName}' failed: {reason}");
            }
            else
            {
                SendResponse(envelope, task.Result, error: null);
            }
        }, TaskScheduler.Default);
    }

    private void SendResponse(Envelope requestEnvelope, object? payload, string? error)
    {
        SendEnvelope(new Envelope
        {
            Kind = EnvelopeKind.Response,
            Origin = SelfId,
            Target = requestEnvelope.Origin,
            RequestId = requestEnvelope.RequestId,
            Payload = payload != null ? Wire.ToPayload(payload) : null,
            Error = error,
        });
    }

    #endregion
}
