using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker.Internal;

/// <summary>
/// Correlates outbound requests with inbound responses. Completions are posted through the
/// <see cref="DeliveryPump"/> so awaiting code resumes on the framework thread.
/// </summary>
internal sealed class RequestBroker
{
    private sealed class Pending
    {
        public required TaskCompletionSource<Envelope> Completion { get; init; }
        public required Guid TargetPeerId { get; init; }
        public required CancellationTokenSource TimeoutSource { get; init; }
    }

    private readonly ConcurrentDictionary<Guid, Pending> pendingRequests = new();
    private readonly DeliveryPump pump;

    public RequestBroker(DeliveryPump pump)
    {
        this.pump = pump;
    }

    public Task<Envelope> Track(Guid requestId, Guid targetPeerId, TimeSpan timeout)
    {
        // No RunContinuationsAsynchronously: completions are posted through the pump, so continuations
        // run inline on the framework thread — that's the "await resumes on the framework thread" guarantee.
        var completion = new TaskCompletionSource<Envelope>();
        var timeoutSource = new CancellationTokenSource();

        var pending = new Pending
        {
            Completion = completion,
            TargetPeerId = targetPeerId,
            TimeoutSource = timeoutSource,
        };

        if (!pendingRequests.TryAdd(requestId, pending))
        {
            timeoutSource.Dispose();
            throw new InvalidOperationException($"Duplicate request id {requestId}.");
        }

        timeoutSource.Token.Register(() =>
        {
            if (pendingRequests.TryRemove(requestId, out var timedOut))
                pump.Post(() => timedOut.Completion.TrySetException(new TimeoutException($"Request {requestId} to peer {targetPeerId} timed out.")));
        });

        timeoutSource.CancelAfter(timeout);
        return completion.Task;
    }

    public void Complete(Envelope response)
    {
        if (response.RequestId == null || !pendingRequests.TryRemove(response.RequestId.Value, out var pending))
            return;

        pending.TimeoutSource.Dispose();
        pump.Post(() => pending.Completion.TrySetResult(response));
    }

    /// <summary>
    /// Fails every pending request addressed to a peer that left the network.
    /// </summary>
    public void FailPeer(Guid peerId)
    {
        foreach (var pair in pendingRequests)
        {
            if (pair.Value.TargetPeerId != peerId)
                continue;

            if (pendingRequests.TryRemove(pair.Key, out var pending))
            {
                pending.TimeoutSource.Dispose();
                pump.Post(() => pending.Completion.TrySetException(new PeerLeftException(peerId)));
            }
        }
    }

    public void FailAll(Exception exception)
    {
        var keys = new List<Guid>(pendingRequests.Keys);

        foreach (var key in keys)
        {
            if (pendingRequests.TryRemove(key, out var pending))
            {
                pending.TimeoutSource.Dispose();
                pump.Post(() => pending.Completion.TrySetException(exception));
            }
        }
    }
}
