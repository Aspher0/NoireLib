using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Chat facts: messages with SeString payloads preserved and senders resolved, filtered by composable
/// <see cref="ChatRule"/>s, with opt-in duplicate suppression and bounded history
/// (see <see cref="GameWatcherOptions.Chat"/>).
/// </summary>
public sealed class ChatWatcher : GameWatcherFacade
{
    internal ChatWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to chat messages, optionally filtered by a rule.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="rule">An optional rule the message must match (see <see cref="ChatRule"/> factories).</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMessage(Action<ChatMessageEvent> handler, ChatRule? rule = null, NoireSubscriptionOptions<ChatMessageEvent>? options = null)
        => On(handler, null, rule == null ? options : WithFilter(options, rule.Matches), nameof(OnMessage));

    /// <inheritdoc cref="OnMessage(Action{ChatMessageEvent}, ChatRule?, NoireSubscriptionOptions{ChatMessageEvent}?)"/>
    public NoireSubscriptionToken OnMessageAsync(Func<ChatMessageEvent, Task> handler, ChatRule? rule = null, NoireSubscriptionOptions<ChatMessageEvent>? options = null)
        => On(null, handler, rule == null ? options : WithFilter(options, rule.Matches), nameof(OnMessage));

    /// <summary>
    /// The retained message history, newest first. Only collects while the Chat source runs with a configured
    /// <see cref="ChatSourceOptions.HistoryCapacity"/> (which marks the source always-on).
    /// </summary>
    /// <returns>The history snapshot.</returns>
    public IReadOnlyList<ChatMessageEvent> GetHistory()
        => Watcher.GetSource<ChatSource>(SourceKind.Chat).GetHistory();

    /// <summary>Clears the retained message history.</summary>
    public void ClearHistory()
        => Watcher.GetSource<ChatSource>(SourceKind.Chat).ClearHistory();
}
