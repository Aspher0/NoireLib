using Dalamud.Game.ClientState.Conditions;
using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Duty facts: started/wiped/recommenced/completed (native events) and queue tracking
/// (entered/left/pop with measured queue duration).
/// </summary>
public sealed class DutyWatcher : GameWatcherFacade
{
    internal DutyWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to duty starts (barriers dropping).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnStarted(Action<DutyStartedEvent> handler, NoireSubscriptionOptions<DutyStartedEvent>? options = null)
        => On(handler, null, options, nameof(OnStarted));

    /// <inheritdoc cref="OnStarted(Action{DutyStartedEvent}, NoireSubscriptionOptions{DutyStartedEvent}?)"/>
    public NoireSubscriptionToken OnStartedAsync(Func<DutyStartedEvent, Task> handler, NoireSubscriptionOptions<DutyStartedEvent>? options = null)
        => On(null, handler, options, nameof(OnStarted));

    /// <summary>
    /// Subscribes to duty wipes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnWiped(Action<DutyWipedEvent> handler, NoireSubscriptionOptions<DutyWipedEvent>? options = null)
        => On(handler, null, options, nameof(OnWiped));

    /// <inheritdoc cref="OnWiped(Action{DutyWipedEvent}, NoireSubscriptionOptions{DutyWipedEvent}?)"/>
    public NoireSubscriptionToken OnWipedAsync(Func<DutyWipedEvent, Task> handler, NoireSubscriptionOptions<DutyWipedEvent>? options = null)
        => On(null, handler, options, nameof(OnWiped));

    /// <summary>
    /// Subscribes to duty recommencements after wipes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnRecommenced(Action<DutyRecommencedEvent> handler, NoireSubscriptionOptions<DutyRecommencedEvent>? options = null)
        => On(handler, null, options, nameof(OnRecommenced));

    /// <inheritdoc cref="OnRecommenced(Action{DutyRecommencedEvent}, NoireSubscriptionOptions{DutyRecommencedEvent}?)"/>
    public NoireSubscriptionToken OnRecommencedAsync(Func<DutyRecommencedEvent, Task> handler, NoireSubscriptionOptions<DutyRecommencedEvent>? options = null)
        => On(null, handler, options, nameof(OnRecommenced));

    /// <summary>
    /// Subscribes to duty completions.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCompleted(Action<DutyCompletedEvent> handler, NoireSubscriptionOptions<DutyCompletedEvent>? options = null)
        => On(handler, null, options, nameof(OnCompleted));

    /// <inheritdoc cref="OnCompleted(Action{DutyCompletedEvent}, NoireSubscriptionOptions{DutyCompletedEvent}?)"/>
    public NoireSubscriptionToken OnCompletedAsync(Func<DutyCompletedEvent, Task> handler, NoireSubscriptionOptions<DutyCompletedEvent>? options = null)
        => On(null, handler, options, nameof(OnCompleted));

    /// <summary>
    /// Subscribes to duty-queue entries.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnQueueEntered(Action<DutyQueueEnteredEvent> handler, NoireSubscriptionOptions<DutyQueueEnteredEvent>? options = null)
        => On(handler, null, options, nameof(OnQueueEntered));

    /// <inheritdoc cref="OnQueueEntered(Action{DutyQueueEnteredEvent}, NoireSubscriptionOptions{DutyQueueEnteredEvent}?)"/>
    public NoireSubscriptionToken OnQueueEnteredAsync(Func<DutyQueueEnteredEvent, Task> handler, NoireSubscriptionOptions<DutyQueueEnteredEvent>? options = null)
        => On(null, handler, options, nameof(OnQueueEntered));

    /// <summary>
    /// Subscribes to duty-queue exits without a pop (withdrawal or cancellation).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnQueueLeft(Action<DutyQueueLeftEvent> handler, NoireSubscriptionOptions<DutyQueueLeftEvent>? options = null)
        => On(handler, null, options, nameof(OnQueueLeft));

    /// <inheritdoc cref="OnQueueLeft(Action{DutyQueueLeftEvent}, NoireSubscriptionOptions{DutyQueueLeftEvent}?)"/>
    public NoireSubscriptionToken OnQueueLeftAsync(Func<DutyQueueLeftEvent, Task> handler, NoireSubscriptionOptions<DutyQueueLeftEvent>? options = null)
        => On(null, handler, options, nameof(OnQueueLeft));

    /// <summary>
    /// Subscribes to duty pops.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnPop(Action<DutyPopEvent> handler, NoireSubscriptionOptions<DutyPopEvent>? options = null)
        => On(handler, null, options, nameof(OnPop));

    /// <inheritdoc cref="OnPop(Action{DutyPopEvent}, NoireSubscriptionOptions{DutyPopEvent}?)"/>
    public NoireSubscriptionToken OnPopAsync(Func<DutyPopEvent, Task> handler, NoireSubscriptionOptions<DutyPopEvent>? options = null)
        => On(null, handler, options, nameof(OnPop));

    /// <summary>Whether the local player is bound by a duty. Live read (framework thread only).</summary>
    public bool IsInDuty
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return NoireService.Condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95);
        }
    }

    /// <summary>Whether the current duty has started (barriers dropped). Live read (framework thread only).</summary>
    public bool IsDutyStarted
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return NoireService.DutyState.IsDutyStarted;
        }
    }

    /// <summary>Whether the local player is queued for a duty. Live read (framework thread only).</summary>
    public bool IsInQueue
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return NoireService.Condition.Any(ConditionFlag.WaitingForDuty, ConditionFlag.WaitingForDutyFinder, ConditionFlag.InDutyQueue);
        }
    }
}
