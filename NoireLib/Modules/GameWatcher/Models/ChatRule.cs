using Dalamud.Game.Text;
using System;
using System.Text.RegularExpressions;

namespace NoireLib.GameWatcher;

/// <summary>
/// A reusable, composable predicate over chat messages: exact text, wildcard, regex, command,
/// sender and channel matching. Pass a rule to <c>watcher.Chat.OnMessage</c> to filter subscriptions.
/// </summary>
public sealed class ChatRule
{
    private readonly Func<ChatMessageEvent, bool> predicate;

    /// <summary>
    /// Creates a rule from an arbitrary predicate.
    /// </summary>
    /// <param name="name">A readable rule name for logs and diagnostics.</param>
    /// <param name="predicate">The predicate a message must satisfy.</param>
    public ChatRule(string name, Func<ChatMessageEvent, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(predicate);

        Name = name;
        this.predicate = predicate;
    }

    /// <summary>The readable rule name.</summary>
    public string Name { get; }

    /// <summary>
    /// Evaluates the rule against a message.
    /// </summary>
    /// <param name="message">The message to test.</param>
    /// <returns>True when the message matches.</returns>
    public bool Matches(ChatMessageEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return predicate(message);
    }

    /// <summary>
    /// Matches messages whose plain text contains the given substring (case-insensitive).
    /// </summary>
    /// <param name="text">The substring to search for.</param>
    /// <param name="channel">An optional channel restriction.</param>
    /// <returns>The rule.</returns>
    public static ChatRule Contains(string text, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new ChatRule(
            $"Contains({text})",
            m => (channel == null || m.Type == channel.Value)
                && m.PlainText.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Matches messages whose plain text matches a regular expression.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <param name="channel">An optional channel restriction.</param>
    /// <returns>The rule.</returns>
    public static ChatRule MatchesRegex(string pattern, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return new ChatRule(
            $"Regex({pattern})",
            m => (channel == null || m.Type == channel.Value) && regex.IsMatch(m.PlainText));
    }

    /// <summary>
    /// Matches messages whose plain text matches a wildcard pattern (<c>*</c> = any run, <c>?</c> = any character).
    /// </summary>
    /// <param name="pattern">The wildcard pattern.</param>
    /// <param name="channel">An optional channel restriction.</param>
    /// <returns>The rule.</returns>
    public static ChatRule MatchesWildcard(string pattern, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return new ChatRule(
            $"Wildcard({pattern})",
            m => (channel == null || m.Type == channel.Value) && regex.IsMatch(m.PlainText));
    }

    /// <summary>
    /// Matches messages whose plain text starts with a command word (e.g. <c>"!roll"</c>), followed by
    /// whitespace or end-of-message.
    /// </summary>
    /// <param name="command">The command word, including any prefix character.</param>
    /// <param name="channel">An optional channel restriction.</param>
    /// <returns>The rule.</returns>
    public static ChatRule Command(string command, XivChatType? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return new ChatRule(
            $"Command({command})",
            m =>
            {
                if (channel != null && m.Type != channel.Value)
                    return false;

                var text = m.PlainText.TrimStart();

                if (!text.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                    return false;

                return text.Length == command.Length || char.IsWhiteSpace(text[command.Length]);
            });
    }

    /// <summary>
    /// Matches messages from a specific sender.
    /// </summary>
    /// <param name="senderName">The sender name to match.</param>
    /// <param name="exactMatch">True for exact (case-insensitive) matching; false for substring matching.</param>
    /// <returns>The rule.</returns>
    public static ChatRule FromSender(string senderName, bool exactMatch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderName);

        return new ChatRule(
            $"FromSender({senderName})",
            m => exactMatch
                ? string.Equals(m.SenderName, senderName, StringComparison.OrdinalIgnoreCase)
                : m.SenderName.Contains(senderName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Matches messages on a specific channel.
    /// </summary>
    /// <param name="channel">The channel to match.</param>
    /// <returns>The rule.</returns>
    public static ChatRule FromChannel(XivChatType channel)
        => new($"FromChannel({channel})", m => m.Type == channel);

    /// <summary>
    /// Combines this rule with another: a message matches when it matches both.
    /// </summary>
    /// <param name="other">The rule to combine with.</param>
    /// <returns>The combined rule.</returns>
    public ChatRule And(ChatRule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new ChatRule($"({Name} AND {other.Name})", m => Matches(m) && other.Matches(m));
    }

    /// <summary>
    /// Combines this rule with another: a message matches when it matches either.
    /// </summary>
    /// <param name="other">The rule to combine with.</param>
    /// <returns>The combined rule.</returns>
    public ChatRule Or(ChatRule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new ChatRule($"({Name} OR {other.Name})", m => Matches(m) || other.Matches(m));
    }

    /// <summary>
    /// Negates this rule.
    /// </summary>
    /// <returns>The negated rule.</returns>
    public ChatRule Not() => new($"NOT {Name}", m => !Matches(m));
}
