using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using NoireLib.Events;
using NoireLib.Helpers;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks incoming chat messages by subscribing to <see cref="IChatGui.ChatMessage"/>.<br/>
/// Maintains a bounded history of recent messages and exposes query, filter, pattern-matching,
/// regex/wildcard subscription, per-channel history limits, duplicate suppression, spam coalescing,
/// and higher-level chat rule matching APIs.<br/>
/// Uses direct event subscription because <see cref="IChatGui.ChatMessage"/> uses <see langword="ref"/> parameters
/// which are not compatible with <see cref="NoireLib.Events.EventWrapper"/>.
/// </summary>
public sealed class ChatTracker : GameStateSubTracker
{
    private readonly LinkedList<ChatMessageEntry> messageHistory = new();
    private readonly object historyLock = new();
    private readonly int historyCapacity;
    private long totalMessagesObserved;
    private EventWrapper<IChatGui.OnChatMessageDelegate> onChatMessageEvent;

    private readonly Dictionary<string, ChatRule> registeredRules = new(StringComparer.Ordinal);
    private readonly object rulesLock = new();

    private readonly Dictionary<XivChatType, int> perChannelLimits = new();
    private readonly object channelLimitsLock = new();

    private bool enableDuplicateSuppression;
    private TimeSpan duplicateWindow = TimeSpan.FromSeconds(2);
    private bool enableSpamCoalescing;
    private int spamThreshold = 3;
    private TimeSpan spamWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    /// <param name="historyCapacity">The maximum number of recent messages to retain.</param>
    internal ChatTracker(NoireGameStateWatcher owner, bool active, int historyCapacity = 100) : base(owner, active)
    {
        this.historyCapacity = Math.Max(1, historyCapacity);
        onChatMessageEvent = new(NoireService.ChatGui, nameof(IChatGui.ChatMessage), name: $"{nameof(ChatTracker)}.ChatMessage");

        onChatMessageEvent.AddCallback("handler", HandleChatMessage);
    }

    /// <summary>
    /// Gets the total number of chat messages observed since the last activation.
    /// </summary>
    public long TotalMessagesObserved => totalMessagesObserved;

    /// <summary>
    /// Gets the configured maximum history size.
    /// </summary>
    public int HistoryCapacity => historyCapacity;

    /// <summary>
    /// Gets the current number of messages in the history buffer.
    /// </summary>
    public int HistoryCount
    {
        get
        {
            lock (historyLock)
                return messageHistory.Count;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether duplicate suppression is enabled.
    /// When enabled, messages with identical text received within <see cref="DuplicateWindow"/> are suppressed.
    /// </summary>
    public bool EnableDuplicateSuppression
    {
        get => enableDuplicateSuppression;
        set => enableDuplicateSuppression = value;
    }

    /// <summary>
    /// Gets or sets the time window within which identical messages are considered duplicates.
    /// </summary>
    public TimeSpan DuplicateWindow
    {
        get => duplicateWindow;
        set => duplicateWindow = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether spam coalescing is enabled.
    /// When enabled, identical messages received more than <see cref="SpamThreshold"/> times within <see cref="SpamWindow"/> are coalesced.
    /// </summary>
    public bool EnableSpamCoalescing
    {
        get => enableSpamCoalescing;
        set => enableSpamCoalescing = value;
    }

    /// <summary>
    /// Gets or sets the minimum repeat count within <see cref="SpamWindow"/> before messages are considered spam.
    /// </summary>
    public int SpamThreshold
    {
        get => spamThreshold;
        set => spamThreshold = Math.Max(2, value);
    }

    /// <summary>
    /// Gets or sets the time window for spam detection.
    /// </summary>
    public TimeSpan SpamWindow
    {
        get => spamWindow;
        set => spamWindow = value;
    }

    /// <summary>
    /// Raised when a chat message is received.
    /// </summary>
    public event Action<ChatMessageReceivedEvent>? OnChatMessageReceived;

    /// <summary>
    /// Raised when a chat message matches a registered <see cref="ChatRule"/>.
    /// </summary>
    public event Action<ChatRuleMatchedEvent>? OnChatRuleMatched;

    /// <summary>
    /// Returns a snapshot of all messages currently in the history buffer, newest first.
    /// </summary>
    /// <returns>An array of chat message entries.</returns>
    public ChatMessageEntry[] GetRecentMessages()
    {
        lock (historyLock)
            return messageHistory.ToArray();
    }

    /// <summary>
    /// Returns the most recent messages currently in the history buffer, newest first.
    /// </summary>
    /// <param name="maxCount">The maximum number of messages to return.</param>
    /// <returns>An array containing up to <paramref name="maxCount"/> recent chat messages.</returns>
    public ChatMessageEntry[] GetRecentMessages(int maxCount)
    {
        if (maxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        lock (historyLock)
            return messageHistory.Take(maxCount).ToArray();
    }

    /// <summary>
    /// Returns all messages in the history buffer matching the specified chat type.
    /// </summary>
    /// <param name="type">The chat type to filter by.</param>
    /// <returns>An array of matching chat message entries.</returns>
    public ChatMessageEntry[] GetMessagesByType(XivChatType type)
    {
        lock (historyLock)
            return messageHistory.Where(m => m.Type == type).ToArray();
    }

    /// <summary>
    /// Returns all messages in the history buffer sent by a sender whose name matches the specified text.
    /// </summary>
    /// <param name="senderName">The sender-name text to search for.</param>
    /// <param name="exactMatch">Whether the sender name must match exactly instead of using a substring comparison.</param>
    /// <returns>An array of matching chat message entries.</returns>
    public ChatMessageEntry[] GetMessagesFromSender(string senderName, bool exactMatch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderName);

        lock (historyLock)
        {
            return messageHistory
                .Where(message => exactMatch
                    ? message.SenderName.Equals(senderName, StringComparison.OrdinalIgnoreCase)
                    : message.SenderName.Contains(senderName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    /// <summary>
    /// Returns all messages in the history buffer whose text contains the specified substring (case-insensitive).
    /// </summary>
    /// <param name="text">The text substring to search for.</param>
    /// <returns>An array of matching chat message entries.</returns>
    public ChatMessageEntry[] SearchMessages(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        lock (historyLock)
            return messageHistory
                .Where(m => m.MessageText.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    /// <summary>
    /// Returns all messages in the history buffer matching the provided predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>An array of matching chat message entries.</returns>
    public ChatMessageEntry[] SearchMessages(Func<ChatMessageEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (historyLock)
            return messageHistory.Where(predicate).ToArray();
    }

    /// <summary>
    /// Returns all messages in the history buffer whose text matches the specified regular expression.
    /// </summary>
    /// <param name="pattern">The regular expression pattern to match.</param>
    /// <returns>An array of matching chat message entries.</returns>
    public ChatMessageEntry[] SearchMessagesByRegex(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        lock (historyLock)
            return messageHistory.Where(m => regex.IsMatch(m.MessageText)).ToArray();
    }

    /// <summary>
    /// Returns all messages in the history buffer whose text matches the specified wildcard pattern
    /// (using <c>*</c> for any sequence and <c>?</c> for any single character).
    /// </summary>
    /// <param name="wildcardPattern">The wildcard pattern to match.</param>
    /// <returns>An array of matching chat message entries.</returns>
    public ChatMessageEntry[] SearchMessagesByWildcard(string wildcardPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wildcardPattern);

        var regexPattern = "^" + Regex.Escape(wildcardPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        lock (historyLock)
            return messageHistory.Where(m => regex.IsMatch(m.MessageText)).ToArray();
    }

    /// <summary>
    /// Returns all messages in the history buffer that start with the specified command prefix (case-insensitive).
    /// </summary>
    /// <param name="command">The command prefix to match (e.g. <c>/roll</c>).</param>
    /// <returns>An array of matching chat message entries.</returns>
    public ChatMessageEntry[] GetCommandMessages(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        lock (historyLock)
            return messageHistory
                .Where(m => m.MessageText.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    /// <summary>
    /// Returns the most recent message matching the provided predicate, or <see langword="null"/> if none match.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>The most recent matching chat message entry, or <see langword="null"/> if none match.</returns>
    public ChatMessageEntry? GetLatestMessage(Func<ChatMessageEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (historyLock)
            return messageHistory.FirstOrDefault(predicate);
    }

    /// <summary>
    /// Returns the most recent message of the specified chat type, or <see langword="null"/> if none match.
    /// </summary>
    /// <param name="type">The chat type to filter by.</param>
    /// <returns>The most recent matching chat message entry, or <see langword="null"/> if none match.</returns>
    public ChatMessageEntry? GetLatestMessageByType(XivChatType type)
    {
        lock (historyLock)
            return messageHistory.FirstOrDefault(message => message.Type == type);
    }

    /// <summary>
    /// Checks whether any message in the history buffer contains the specified text.
    /// </summary>
    /// <param name="text">The text to search for.</param>
    /// <returns><see langword="true"/> if a matching message exists; otherwise, <see langword="false"/>.</returns>
    public bool ContainsMessage(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        lock (historyLock)
            return messageHistory.Any(message => message.MessageText.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sets a per-channel history limit for the specified chat type.
    /// Messages exceeding this limit for the channel are trimmed from oldest first.
    /// </summary>
    /// <param name="channelType">The chat channel type to limit.</param>
    /// <param name="maxMessages">The maximum number of messages to retain for this channel.</param>
    public void SetChannelHistoryLimit(XivChatType channelType, int maxMessages)
    {
        if (maxMessages < 1)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));

        lock (channelLimitsLock)
            perChannelLimits[channelType] = maxMessages;
    }

    /// <summary>
    /// Removes the per-channel history limit for the specified chat type.
    /// </summary>
    /// <param name="channelType">The chat channel type to unlimit.</param>
    public void RemoveChannelHistoryLimit(XivChatType channelType)
    {
        lock (channelLimitsLock)
            perChannelLimits.Remove(channelType);
    }

    /// <summary>
    /// Registers a <see cref="ChatRule"/> for automatic matching against incoming messages.
    /// </summary>
    /// <param name="key">A unique key for the rule.</param>
    /// <param name="rule">The rule to register.</param>
    public void RegisterRule(string key, ChatRule rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(rule);

        lock (rulesLock)
            registeredRules[key] = rule;
    }

    /// <summary>
    /// Removes a previously registered <see cref="ChatRule"/>.
    /// </summary>
    /// <param name="key">The key of the rule to remove.</param>
    /// <returns><see langword="true"/> if the rule was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnregisterRule(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (rulesLock)
            return registeredRules.Remove(key);
    }

    /// <summary>
    /// Removes all registered chat rules.
    /// </summary>
    public void ClearRules()
    {
        lock (rulesLock)
            registeredRules.Clear();
    }

    /// <summary>
    /// Subscribes to messages matching the specified regular expression pattern and returns a subscription token.
    /// </summary>
    /// <param name="pattern">The regular expression pattern to match.</param>
    /// <param name="callback">The callback to invoke when a matching message is received.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeRegex(string pattern, Action<ChatMessageReceivedEvent> callback, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(callback);

        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return Subscribe<ChatMessageReceivedEvent>(
            evt => regex.IsMatch(evt.MessageText),
            callback,
            priority);
    }

    /// <summary>
    /// Subscribes to messages matching the specified wildcard pattern and returns a subscription token.
    /// </summary>
    /// <param name="wildcardPattern">The wildcard pattern to match (using <c>*</c> and <c>?</c>).</param>
    /// <param name="callback">The callback to invoke when a matching message is received.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeWildcard(string wildcardPattern, Action<ChatMessageReceivedEvent> callback, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wildcardPattern);
        ArgumentNullException.ThrowIfNull(callback);

        var regexPattern = "^" + Regex.Escape(wildcardPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return Subscribe<ChatMessageReceivedEvent>(
            evt => regex.IsMatch(evt.MessageText),
            callback,
            priority);
    }

    /// <summary>
    /// Subscribes to messages starting with the specified command prefix and returns a subscription token.
    /// </summary>
    /// <param name="command">The command prefix to match (e.g. <c>/roll</c>).</param>
    /// <param name="callback">The callback to invoke when a matching message is received.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeCommand(string command, Action<ChatMessageReceivedEvent> callback, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(callback);

        return Subscribe<ChatMessageReceivedEvent>(
            evt => evt.MessageText.StartsWith(command, StringComparison.OrdinalIgnoreCase),
            callback,
            priority);
    }

    /// <summary>
    /// Clears the message history buffer and resets the counter.
    /// </summary>
    public void ClearHistory()
    {
        lock (historyLock)
        {
            messageHistory.Clear();
            totalMessagesObserved = 0;
        }
    }

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when a chat message containing the specified text is received.<br/>
    /// Compares against the most recent message in the history buffer.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="text">The text to wait for.</param>
    /// <returns>A predicate returning <see langword="true"/> when a matching message exists.</returns>
    public Func<bool> WaitForMessage(string text) => () =>
    {
        lock (historyLock)
            return messageHistory.Any(m => m.MessageText.Contains(text, StringComparison.OrdinalIgnoreCase));
    };

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when a message from the specified sender is received.
    /// </summary>
    /// <param name="senderName">The sender name to wait for.</param>
    /// <param name="exactMatch">Whether the sender name must match exactly instead of using a substring comparison.</param>
    /// <returns>A predicate returning <see langword="true"/> when a matching message exists.</returns>
    public Func<bool> WaitForMessageFromSender(string senderName, bool exactMatch = false) => () => GetMessagesFromSender(senderName, exactMatch).Length > 0;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when a message matching the specified regular expression is received.
    /// </summary>
    /// <param name="pattern">The regular expression pattern to wait for.</param>
    /// <returns>A predicate returning <see langword="true"/> when a matching message exists.</returns>
    public Func<bool> WaitForMessageRegex(string pattern) => () => SearchMessagesByRegex(pattern).Length > 0;

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        totalMessagesObserved = 0;
        onChatMessageEvent.Enable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(ChatTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        onChatMessageEvent.Disable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(ChatTracker)} deactivated.");
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        onChatMessageEvent.Dispose();
    }

    private void HandleChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        totalMessagesObserved++;

        var playerSender = SeStringHelper.ResolveSender(sender);
        var senderText = playerSender?.PlayerName ?? "Unknown";
        var messageText = SeStringHelper.SeStringToPlainText(message);

        var entry = new ChatMessageEntry(type, timestamp, senderText, messageText, DateTimeOffset.UtcNow);

        if (enableDuplicateSuppression && IsDuplicate(entry))
            return;

        if (enableSpamCoalescing && IsSpam(entry))
            return;

        lock (historyLock)
        {
            messageHistory.AddFirst(entry);

            while (messageHistory.Count > historyCapacity)
                messageHistory.RemoveLast();
        }

        EnforcePerChannelLimits(type);

        var evt = new ChatMessageReceivedEvent(type, timestamp, senderText, messageText);

        PublishEvent(OnChatMessageReceived, evt);
        EvaluateRules(entry);
    }

    private bool IsDuplicate(ChatMessageEntry entry)
    {
        lock (historyLock)
        {
            var cutoff = entry.ReceivedAt - duplicateWindow;
            return messageHistory.Any(m =>
                m.ReceivedAt >= cutoff &&
                m.Type == entry.Type &&
                m.MessageText.Equals(entry.MessageText, StringComparison.Ordinal) &&
                m.SenderName.Equals(entry.SenderName, StringComparison.Ordinal));
        }
    }

    private bool IsSpam(ChatMessageEntry entry)
    {
        lock (historyLock)
        {
            var cutoff = entry.ReceivedAt - spamWindow;
            var recentIdentical = messageHistory.Count(m =>
                m.ReceivedAt >= cutoff &&
                m.Type == entry.Type &&
                m.MessageText.Equals(entry.MessageText, StringComparison.Ordinal) &&
                m.SenderName.Equals(entry.SenderName, StringComparison.Ordinal));

            return recentIdentical >= spamThreshold;
        }
    }

    private void EnforcePerChannelLimits(XivChatType type)
    {
        int limit;
        lock (channelLimitsLock)
        {
            if (!perChannelLimits.TryGetValue(type, out limit))
                return;
        }

        lock (historyLock)
        {
            var channelCount = messageHistory.Count(m => m.Type == type);
            if (channelCount <= limit)
                return;

            var toRemove = channelCount - limit;
            var node = messageHistory.Last;
            while (node != null && toRemove > 0)
            {
                var prev = node.Previous;
                if (node.Value.Type == type)
                {
                    messageHistory.Remove(node);
                    toRemove--;
                }
                node = prev;
            }
        }
    }

    private void EvaluateRules(ChatMessageEntry entry)
    {
        ChatRule[] rulesCopy;
        lock (rulesLock)
        {
            if (registeredRules.Count == 0)
                return;
            rulesCopy = registeredRules.Values.ToArray();
        }

        foreach (var rule in rulesCopy)
        {
            try
            {
                if (rule.IsMatch(entry))
                    PublishEvent(OnChatRuleMatched, new ChatRuleMatchedEvent(rule, entry));
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(Owner, ex, $"Chat rule '{rule.Name}' threw an exception.");
            }
        }
    }
}
