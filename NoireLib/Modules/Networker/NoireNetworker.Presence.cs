using NoireLib.Core.Subscriptions;
using NoireLib.Networker.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Networker;

public partial class NoireNetworker
{
    private readonly object barrierGate = new();
    private readonly List<FlagBarrier> flagBarriers = new();

    private sealed class FlagBarrier
    {
        public required string Flag { get; init; }
        public required int MinimumOthers { get; init; }
        public required TaskCompletionSource<bool> Completion { get; init; }
        public CancellationTokenSource? Timeout { get; set; }
    }

    #region Peer & state callbacks

    /// <summary>
    /// Subscribes a handler invoked when a peer joins the network. Runs on the framework thread.
    /// </summary>
    /// <param name="handler">The handler, receiving the joining peer.</param>
    /// <param name="key">Optional subscription key; subscribing again with the same key replaces the previous subscription.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken OnPeerJoined(Action<NetworkerPeer> handler, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return peerJoinedRegistry.Subscribe(0, peer => handler(peer), new() { Key = key });
    }

    /// <summary>
    /// Subscribes a handler invoked when a peer leaves the network. Runs on the framework thread.
    /// </summary>
    /// <param name="handler">The handler, receiving the departed peer.</param>
    /// <param name="key">Optional subscription key; subscribing again with the same key replaces the previous subscription.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken OnPeerLeft(Action<NetworkerPeer> handler, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return peerLeftRegistry.Subscribe(0, peer => handler(peer), new() { Key = key });
    }

    /// <summary>
    /// Subscribes a handler invoked when a peer's metadata or flags change. Runs on the framework thread.
    /// </summary>
    /// <param name="handler">The handler, receiving the peer and the changed key — the metadata key, "flag:&lt;name&gt;" for flags, or "*" for full-state updates.</param>
    /// <param name="key">Optional subscription key; subscribing again with the same key replaces the previous subscription.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken OnPeerUpdated(Action<NetworkerPeer, string> handler, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return peerUpdatedRegistry.Subscribe(0, context => handler(context.Peer, context.Key), new() { Key = key });
    }

    /// <summary>
    /// Subscribes a handler invoked when the networker's connection state changes. Runs on the framework thread.
    /// </summary>
    /// <param name="handler">The handler, receiving the new state.</param>
    /// <param name="key">Optional subscription key; subscribing again with the same key replaces the previous subscription.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken OnStateChanged(Action<NetworkerState> handler, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return stateRegistry.Subscribe(0, newState => handler(newState), new() { Key = key });
    }

    #endregion

    #region Coordination flags & barrier

    /// <summary>
    /// Sets a coordination flag on the local instance, visible to every peer. Flags clear automatically when an instance leaves.
    /// </summary>
    /// <param name="flag">The flag name.</param>
    public void SetFlag(string flag)
    {
        ArgumentException.ThrowIfNullOrEmpty(flag);

        if (Self.SetFlagInternal(flag, true))
        {
            AnnounceSelf("f:" + flag);
            pump?.Post(EvaluateBarriers);
        }
    }

    /// <summary>
    /// Clears a coordination flag on the local instance.
    /// </summary>
    /// <param name="flag">The flag name.</param>
    public void ClearFlag(string flag)
    {
        ArgumentException.ThrowIfNullOrEmpty(flag);

        if (Self.SetFlagInternal(flag, false))
        {
            AnnounceSelf("f:" + flag);
            pump?.Post(EvaluateBarriers);
        }
    }

    /// <summary>
    /// Determines whether the local instance currently carries the given coordination flag.
    /// </summary>
    /// <param name="flag">The flag name.</param>
    /// <returns>True if the flag is set; otherwise, false.</returns>
    public bool HasFlag(string flag)
        => Self.HasFlag(flag);

    /// <summary>
    /// Waits until the local instance and every connected peer carry the given flag, with at least
    /// <paramref name="minimumOthers"/> other peers connected (guarding against trivially-true empty networks).<br/>
    /// Membership is evaluated live and evaluation pauses during re-election. The await resumes on the framework thread.<br/>
    /// <b>Never sync-block on the returned task from the framework thread — always await.</b>
    /// </summary>
    /// <param name="flag">The flag name.</param>
    /// <param name="timeout">Optional timeout. When exceeded, the task completes with false.</param>
    /// <param name="minimumOthers">The minimum number of other peers that must be connected. Defaults to 1.</param>
    /// <returns>True when everyone carries the flag; false on timeout.</returns>
    public Task<bool> WhenAllFlagged(string flag, TimeSpan? timeout = null, int minimumOthers = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(flag);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumOthers);

        var pumpLocal = pump;

        if (pumpLocal == null)
            return Task.FromException<bool>(new InvalidOperationException("The networker is not active."));

        var barrier = new FlagBarrier
        {
            Flag = flag,
            MinimumOthers = minimumOthers,
            Completion = new TaskCompletionSource<bool>(),
        };

        if (timeout.HasValue)
        {
            barrier.Timeout = new CancellationTokenSource();

            barrier.Timeout.Token.Register(() => pumpLocal.Post(() =>
            {
                bool removed;

                lock (barrierGate)
                    removed = flagBarriers.Remove(barrier);

                if (removed)
                {
                    barrier.Timeout.Dispose();
                    barrier.Completion.TrySetResult(false);
                }
            }));

            barrier.Timeout.CancelAfter(timeout.Value);
        }

        pumpLocal.Post(() =>
        {
            lock (barrierGate)
                flagBarriers.Add(barrier);

            EvaluateBarriers();
        });

        return barrier.Completion.Task;
    }

    /// <summary>
    /// Re-evaluates all pending flag barriers. Runs on the pump thread; evaluation pauses while not <see cref="NetworkerState.Ready"/>.
    /// </summary>
    private void EvaluateBarriers()
    {
        if (State != NetworkerState.Ready)
            return;

        lock (barrierGate)
        {
            if (flagBarriers.Count == 0)
                return;
        }

        NetworkerPeer[] others;

        lock (peersGate)
            others = peers.Values.ToArray();

        List<FlagBarrier>? completed = null;

        lock (barrierGate)
        {
            for (var i = 0; i < flagBarriers.Count; i++)
            {
                var barrier = flagBarriers[i];

                if (others.Length >= barrier.MinimumOthers
                    && Self.HasFlag(barrier.Flag)
                    && others.All(peer => peer.HasFlag(barrier.Flag)))
                {
                    completed ??= new List<FlagBarrier>();
                    completed.Add(barrier);
                    flagBarriers.RemoveAt(i--);
                }
            }
        }

        if (completed == null)
            return;

        foreach (var barrier in completed)
        {
            barrier.Timeout?.Dispose();
            barrier.Completion.TrySetResult(true);
        }
    }

    private void FailAllBarriers()
    {
        List<FlagBarrier> pending;

        lock (barrierGate)
        {
            pending = flagBarriers.ToList();
            flagBarriers.Clear();
        }

        foreach (var barrier in pending)
        {
            barrier.Timeout?.Dispose();
            barrier.Completion.TrySetResult(false);
        }
    }

    #endregion

    #region Self announcement

    private void OnSelfMetadataChanged(string key)
    {
        AnnounceSelf("m:" + key);
        pump?.Post(EvaluateBarriers);
    }

    /// <summary>
    /// Broadcasts the local instance's full presence state. The changed-key hint travels in the envelope's type field.
    /// </summary>
    private void AnnounceSelf(string? changedKey)
    {
        if (State == NetworkerState.Stopped)
            return;

        SendEnvelope(new Envelope
        {
            Kind = EnvelopeKind.PeerState,
            Origin = SelfId,
            TypeName = changedKey,
            Payload = Wire.ToPayload(CaptureSelfState()),
        });
    }

    #endregion
}
