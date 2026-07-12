using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// One composable waiting primitive: a level-triggered game-state condition ("is it true now?") that can be
/// evaluated (<see cref="IsMet"/>), awaited (<see cref="WaitAsync"/>), combined (<see cref="And"/>,
/// <see cref="Or"/>, <see cref="Not"/>) and converted to a TaskQueue completion condition
/// (<c>builder.CompleteWhen(condition)</c>).<br/>
/// For edge-triggered intent ("the <i>next</i> time X happens"), use
/// <see cref="GameConditions.FromEvent{TEvent}"/> or <see cref="NoireGameWatcher.WaitFor{TEvent}"/>.
/// </summary>
public abstract class GameCondition
{
    /// <summary>
    /// Evaluates the condition now. Runs game reads — call on the framework thread
    /// (waits evaluate it there automatically).
    /// </summary>
    /// <returns>True when the condition currently holds.</returns>
    public abstract bool IsMet();

    /// <summary>
    /// Waits until the condition holds, evaluating once per framework tick while any waiter is active.
    /// Completes immediately when already met (level-triggered).<br/>
    /// The returned task completes on the framework thread. <b>Never sync-block on it from the framework
    /// thread — always await.</b>
    /// </summary>
    /// <param name="timeout">The maximum wait; null waits indefinitely.</param>
    /// <param name="ct">A cancellation token; cancellation throws <see cref="OperationCanceledException"/> (a cancelled wait is exceptional).</param>
    /// <returns>True when the condition was met; false on timeout (timeouts are normal control flow in game automation — no exception to catch).</returns>
    public Task<bool> WaitAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<bool>(ct);

        var tcs = new TaskCompletionSource<bool>();
        var deadline = timeout == null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow + timeout.Value;

        CancellationTokenRegistration registration = default;

        if (ct.CanBeCanceled)
            registration = ct.Register(() => tcs.TrySetCanceled(ct));

        tcs.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // Fast path: when already on the framework thread (or no game), evaluate immediately.
        if (!NoireService.IsInitialized() || NoireService.Framework.IsInFrameworkUpdateThread)
        {
            if (TryEvaluate(tcs))
                return tcs.Task;
        }

        GameConditionPump.Register(now =>
        {
            if (tcs.Task.IsCompleted)
                return true;

            if (deadline != null && now >= deadline.Value)
            {
                tcs.TrySetResult(false);
                return true;
            }

            return TryEvaluate(tcs);
        });

        return tcs.Task;
    }

    private bool TryEvaluate(TaskCompletionSource<bool> tcs)
    {
        try
        {
            if (!IsMet())
                return false;

            tcs.TrySetResult(true);
            return true;
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            return true;
        }
    }

    /// <summary>
    /// Combines this condition with another: met when both are met.
    /// </summary>
    /// <param name="other">The other condition.</param>
    /// <returns>The combined condition.</returns>
    public GameCondition And(GameCondition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var self = this;
        return new PredicateGameCondition($"({self} AND {other})", () => self.IsMet() && other.IsMet());
    }

    /// <summary>
    /// Combines this condition with another: met when either is met.
    /// </summary>
    /// <param name="other">The other condition.</param>
    /// <returns>The combined condition.</returns>
    public GameCondition Or(GameCondition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var self = this;
        return new PredicateGameCondition($"({self} OR {other})", () => self.IsMet() || other.IsMet());
    }

    /// <summary>
    /// Negates this condition.
    /// </summary>
    /// <returns>The negated condition.</returns>
    public GameCondition Not()
    {
        var self = this;
        return new PredicateGameCondition($"NOT {self}", () => !self.IsMet());
    }

    internal static GameCondition FromPredicateInternal(Func<bool> predicate, string? name = null)
        => new PredicateGameCondition(name ?? "Predicate", predicate);
}

/// <summary>
/// A <see cref="GameCondition"/> backed by a plain predicate.
/// </summary>
internal sealed class PredicateGameCondition : GameCondition
{
    private readonly string name;
    private readonly Func<bool> predicate;

    public PredicateGameCondition(string name, Func<bool> predicate)
    {
        this.name = name;
        this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <inheritdoc/>
    public override bool IsMet() => predicate();

    /// <inheritdoc/>
    public override string ToString() => name;
}

/// <summary>
/// An event latch: a condition that becomes true when a matching event is dispatched by the watcher.<br/>
/// The internal subscription arms on the first <see cref="GameCondition.IsMet"/> evaluation (or at creation
/// with <c>armImmediately</c>) and self-unsubscribes on first match. Latches are <b>one-shot per instance</b>:
/// once matched, <see cref="GameCondition.IsMet"/> stays true until an explicit <see cref="Reset"/> — this
/// matters for retried tasks, which would otherwise complete instantly on a stale match.<br/>
/// The latch subscription is owner-tagged with this condition object, so abandoned latches are visible in
/// diagnostics and reclaimable via <see cref="NoireGameWatcher.UnsubscribeOwner"/>.
/// </summary>
/// <typeparam name="TEvent">The event type the latch listens for.</typeparam>
public sealed class GameEventLatchCondition<TEvent> : GameCondition
    where TEvent : notnull
{
    private readonly NoireGameWatcher watcher;
    private readonly Func<TEvent, bool>? filter;
    private readonly object latchGate = new();

    private Core.Subscriptions.NoireSubscriptionToken? subscription;
    private volatile bool matched;

    internal GameEventLatchCondition(NoireGameWatcher watcher, Func<TEvent, bool>? filter, bool armImmediately)
    {
        this.watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        this.filter = filter;

        if (armImmediately)
            Arm();
    }

    /// <summary>The event that satisfied the latch, or null while unmatched.</summary>
    public TEvent? MatchedEvent { get; private set; }

    /// <inheritdoc/>
    public override bool IsMet()
    {
        if (matched)
            return true;

        Arm();
        return matched;
    }

    /// <summary>
    /// Re-arms the latch: clears the match so <see cref="GameCondition.IsMet"/> returns false until the next
    /// matching event.
    /// </summary>
    public void Reset()
    {
        lock (latchGate)
        {
            matched = false;
            MatchedEvent = default;
        }
    }

    private void Arm()
    {
        lock (latchGate)
        {
            if (subscription is { IsActive: true })
                return;

            subscription = watcher.Subscribe<TEvent>(
                evt =>
                {
                    lock (latchGate)
                    {
                        matched = true;
                        MatchedEvent = evt;
                    }
                },
                new Core.Subscriptions.NoireSubscriptionOptions<TEvent>
                {
                    Filter = filter,
                    Once = true,
                    Owner = this,
                });
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"EventLatch<{typeof(TEvent).Name}>({(matched ? "matched" : "armed")})";
}
