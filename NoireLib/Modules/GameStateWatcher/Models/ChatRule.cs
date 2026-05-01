using Dalamud.Game.Text;
using System;
using System.Text.RegularExpressions;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Defines a matching rule for incoming chat messages, supporting exact text, substring, wildcard, regex, and command-style patterns.
/// </summary>
public sealed class ChatRule
{
    private readonly Func<ChatMessageEntry, bool> predicate;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatRule"/> class with a custom predicate.
    /// </summary>
    /// <param name="name">A descriptive name for this rule.</param>
    /// <param name="predicate">The predicate that determines whether a message matches.</param>
    public ChatRule(string name, Func<ChatMessageEntry, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(predicate);

        Name = name;
        this.predicate = predicate;
    }

    /// <summary>
    /// Gets the descriptive name of this rule.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Evaluates whether the provided chat message matches this rule.
    /// </summary>
    /// <param name="entry">The chat message entry to evaluate.</param>
    /// <returns><see langword="true"/> if the message matches; otherwise, <see langword="false"/>.</returns>
    public bool IsMatch(ChatMessageEntry entry) => predicate(entry);

    /// <summary>
    /// Creates a rule that matches messages whose text contains the specified substring (case-insensitive).
    /// </summary>
    /// <param name="text">The substring to match.</param>
    /// <param name="channel">An optional channel filter.</param>
    /// <returns>A new chat rule.</returns>
    public static ChatRule Contains(string text, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new ChatRule($"Contains:{text}", entry =>
            (channel == null || entry.Type == channel.Value) &&
            entry.MessageText.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a rule that matches messages whose text matches the specified regular expression.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <param name="channel">An optional channel filter.</param>
    /// <returns>A new chat rule.</returns>
    public static ChatRule MatchesRegex(string pattern, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return new ChatRule($"Regex:{pattern}", entry =>
            (channel == null || entry.Type == channel.Value) &&
            regex.IsMatch(entry.MessageText));
    }

    /// <summary>
    /// Creates a rule that matches messages whose text matches the specified wildcard pattern
    /// (using <c>*</c> for any sequence and <c>?</c> for any single character).
    /// </summary>
    /// <param name="pattern">The wildcard pattern.</param>
    /// <param name="channel">An optional channel filter.</param>
    /// <returns>A new chat rule.</returns>
    public static ChatRule MatchesWildcard(string pattern, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return new ChatRule($"Wildcard:{pattern}", entry =>
            (channel == null || entry.Type == channel.Value) &&
            regex.IsMatch(entry.MessageText));
    }

    /// <summary>
    /// Creates a rule that matches messages starting with the specified command prefix (e.g. <c>/mycommand</c>).
    /// </summary>
    /// <param name="command">The command prefix to match (e.g. <c>/roll</c>).</param>
    /// <param name="channel">An optional channel filter.</param>
    /// <returns>A new chat rule.</returns>
    public static ChatRule Command(string command, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return new ChatRule($"Command:{command}", entry =>
            (channel == null || entry.Type == channel.Value) &&
            entry.MessageText.StartsWith(command, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a rule that matches messages sent from a specific sender (case-insensitive).
    /// </summary>
    /// <param name="senderName">The sender name to match.</param>
    /// <param name="exactMatch">Whether the sender name must match exactly instead of using a substring comparison.</param>
    /// <returns>A new chat rule.</returns>
    public static ChatRule FromSender(string senderName, bool exactMatch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderName);

        return new ChatRule($"Sender:{senderName}", entry => exactMatch
            ? entry.SenderName.Equals(senderName, StringComparison.OrdinalIgnoreCase)
            : entry.SenderName.Contains(senderName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a rule that matches messages from the specified chat channel.
    /// </summary>
    /// <param name="channel">The chat channel type to match.</param>
    /// <returns>A new chat rule.</returns>
    public static ChatRule FromChannel(XivChatType channel)
    {
        return new ChatRule($"Channel:{channel}", entry => entry.Type == channel);
    }
}
