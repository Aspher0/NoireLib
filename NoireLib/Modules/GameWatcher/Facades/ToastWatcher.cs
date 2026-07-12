using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Toast facts: normal, quest and error toasts shown by the game.
/// </summary>
public sealed class ToastWatcher : GameWatcherFacade
{
    internal ToastWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to normal toasts.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnToast(Action<ToastShownEvent> handler, NoireSubscriptionOptions<ToastShownEvent>? options = null)
        => On(handler, null, options, nameof(OnToast));

    /// <inheritdoc cref="OnToast(Action{ToastShownEvent}, NoireSubscriptionOptions{ToastShownEvent}?)"/>
    public NoireSubscriptionToken OnToastAsync(Func<ToastShownEvent, Task> handler, NoireSubscriptionOptions<ToastShownEvent>? options = null)
        => On(null, handler, options, nameof(OnToast));

    /// <summary>
    /// Subscribes to quest toasts.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnQuestToast(Action<QuestToastShownEvent> handler, NoireSubscriptionOptions<QuestToastShownEvent>? options = null)
        => On(handler, null, options, nameof(OnQuestToast));

    /// <inheritdoc cref="OnQuestToast(Action{QuestToastShownEvent}, NoireSubscriptionOptions{QuestToastShownEvent}?)"/>
    public NoireSubscriptionToken OnQuestToastAsync(Func<QuestToastShownEvent, Task> handler, NoireSubscriptionOptions<QuestToastShownEvent>? options = null)
        => On(null, handler, options, nameof(OnQuestToast));

    /// <summary>
    /// Subscribes to error toasts.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnErrorToast(Action<ErrorToastShownEvent> handler, NoireSubscriptionOptions<ErrorToastShownEvent>? options = null)
        => On(handler, null, options, nameof(OnErrorToast));

    /// <inheritdoc cref="OnErrorToast(Action{ErrorToastShownEvent}, NoireSubscriptionOptions{ErrorToastShownEvent}?)"/>
    public NoireSubscriptionToken OnErrorToastAsync(Func<ErrorToastShownEvent, Task> handler, NoireSubscriptionOptions<ErrorToastShownEvent>? options = null)
        => On(null, handler, options, nameof(OnErrorToast));
}
