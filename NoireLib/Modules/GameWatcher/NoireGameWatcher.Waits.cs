using NoireLib.Core.Subscriptions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

public partial class NoireGameWatcher
{
    /// <summary>
    /// Waits for the next event of type <typeparamref name="TEvent"/> matching an optional filter —
    /// edge-triggered ("the <i>next</i> time X happens"). For level-triggered intent ("is it true now?"),
    /// use a <see cref="GameCondition"/>.<br/>
    /// The returned task completes on the framework thread. <b>Never sync-block on it from the framework
    /// thread — always await.</b>
    /// </summary>
    /// <typeparam name="TEvent">The event type to wait for (library or custom).</typeparam>
    /// <param name="filter">An optional filter the event must satisfy.</param>
    /// <param name="timeout">The maximum wait; null waits indefinitely.</param>
    /// <param name="ct">A cancellation token; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The matching event, or null on timeout (timeouts are normal control flow — no exception).</returns>
    public Task<TEvent?> WaitFor<TEvent>(Func<TEvent, bool>? filter = null, TimeSpan? timeout = null, CancellationToken ct = default)
        where TEvent : class
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<TEvent?>(ct);

        var tcs = new TaskCompletionSource<TEvent?>();

        var token = SubscribeCore<TEvent>(
            evt => tcs.TrySetResult(evt),
            null,
            new NoireSubscriptionOptions<TEvent> { Once = true, Filter = filter },
            LookupSource(typeof(TEvent)),
            null,
            null,
            $"WaitFor<{typeof(TEvent).Name}>");

        WireWaiter(tcs, token, timeout, ct, timeoutResult: null);

        return tcs.Task;
    }

    /// <summary>
    /// Waits until a predicate becomes true, evaluated once per framework tick (level-triggered:
    /// completes immediately when already true).<br/>
    /// The returned task completes on the framework thread. <b>Never sync-block on it from the framework
    /// thread — always await.</b>
    /// </summary>
    /// <param name="predicate">The predicate, evaluated on the framework thread.</param>
    /// <param name="timeout">The maximum wait; null waits indefinitely.</param>
    /// <param name="ct">A cancellation token; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>True when the predicate became true; false on timeout.</returns>
    public Task<bool> WaitUntil(Func<bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return GameCondition.FromPredicateInternal(predicate).WaitAsync(timeout, ct);
    }

    /// <summary>
    /// Wires timeout and cancellation handling for an event wait: the deadline is checked on the shared
    /// condition pump so completions stay on the framework thread.
    /// </summary>
    private static void WireWaiter<TResult>(
        TaskCompletionSource<TResult> tcs,
        NoireSubscriptionToken subscription,
        TimeSpan? timeout,
        CancellationToken ct,
        TResult timeoutResult)
    {
        CancellationTokenRegistration registration = default;

        if (ct.CanBeCanceled)
        {
            registration = ct.Register(() =>
            {
                if (tcs.TrySetCanceled(ct))
                    subscription.Dispose();
            });
        }

        // Dispose the subscription and the CT registration however the wait ends.
        tcs.Task.ContinueWith(
            _ =>
            {
                subscription.Dispose();
                registration.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (timeout == null)
            return;

        var deadline = DateTimeOffset.UtcNow + timeout.Value;

        GameConditionPump.Register(now =>
        {
            if (tcs.Task.IsCompleted)
                return true;

            if (now < deadline)
                return false;

            tcs.TrySetResult(timeoutResult);
            return true;
        });
    }
}
