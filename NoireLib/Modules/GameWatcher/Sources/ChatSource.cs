using Dalamud.Game.Chat;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Wraps chat messages: SeStrings and payloads preserved, sender resolved (name/world) when payloads allow,
/// opt-in duplicate suppression with coalescing, and an opt-in bounded history. Event-driven - zero tick cost.
/// </summary>
internal sealed class ChatSource : GameWatcherSource
{
    private readonly Dictionary<(Dalamud.Game.Text.XivChatType Type, string Sender, string Text), (DateTimeOffset LastDispatched, int Suppressed)> duplicateTracker = new();
    private readonly LinkedList<ChatMessageEvent> history = new();
    private readonly object historyLock = new();

    public ChatSource(NoireGameWatcher owner) : base(owner, SourceKind.Chat) { }

    /// <inheritdoc/>
    public override bool IsPolling => false;

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        duplicateTracker.Clear();
        NoireService.ChatGui.ChatMessage += OnChatMessage;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        NoireService.ChatGui.ChatMessage -= OnChatMessage;
        duplicateTracker.Clear();
    }

    /// <summary>A snapshot of the retained history, newest first.</summary>
    internal ChatMessageEvent[] GetHistory()
    {
        lock (historyLock)
            return history.ToArray();
    }

    /// <summary>Clears the retained history.</summary>
    internal void ClearHistory()
    {
        lock (historyLock)
            history.Clear();
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        var plainText = SeStringHelper.SeStringToPlainText(message.Message);
        var resolvedSender = SeStringHelper.ResolveSender(message.Sender);
        var senderName = resolvedSender?.PlayerName ?? message.Sender.TextValue;

        var repeatCount = 1;
        var suppressionWindow = Owner.ActiveOptions.Chat.DuplicateSuppressionWindow;

        if (suppressionWindow is { } window && window > TimeSpan.Zero)
        {
            var key = (message.LogKind, senderName, plainText);
            var now = DateTimeOffset.UtcNow;

            if (duplicateTracker.TryGetValue(key, out var tracked) && now - tracked.LastDispatched < window)
            {
                // Suppress and coalesce: the next dispatched instance carries the count.
                duplicateTracker[key] = (tracked.LastDispatched, tracked.Suppressed + 1);
                return;
            }

            if (duplicateTracker.TryGetValue(key, out tracked))
                repeatCount = tracked.Suppressed + 1;

            duplicateTracker[key] = (now, 0);

            // Bound the tracker so long sessions do not accumulate stale keys.
            if (duplicateTracker.Count > 512)
            {
                var cutoff = now - window;
                var stale = duplicateTracker.Where(pair => pair.Value.LastDispatched < cutoff).Select(pair => pair.Key).ToList();

                foreach (var staleKey in stale)
                    duplicateTracker.Remove(staleKey);
            }
        }

        var evt = new ChatMessageEvent
        {
            Type = message.LogKind,
            Timestamp = message.Timestamp,
            Sender = message.Sender,
            Message = message.Message,
            PlainText = plainText,
            SenderName = senderName,
            SenderWorldId = resolvedSender?.HomeWorldId,
            SenderWorldName = resolvedSender?.HomeWorld,
            RepeatCount = repeatCount,
        };

        var capacity = Owner.ActiveOptions.Chat.HistoryCapacity;

        if (capacity > 0)
        {
            lock (historyLock)
            {
                history.AddFirst(evt);

                while (history.Count > capacity)
                    history.RemoveLast();
            }
        }

        Owner.DispatchEvent(evt);
    }
}
