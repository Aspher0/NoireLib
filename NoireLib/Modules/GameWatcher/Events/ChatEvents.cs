using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when a chat message is received.<br/>
/// The original <see cref="SeString"/>s are preserved with all payloads; the sender is resolved
/// (name and world) when the payloads allow it.
/// </summary>
public sealed record ChatMessageEvent
{
    /// <summary>The chat channel type.</summary>
    public required XivChatType Type { get; init; }

    /// <summary>The message timestamp reported by the game (Unix seconds).</summary>
    public required int Timestamp { get; init; }

    /// <summary>The sender as a full <see cref="SeString"/> with payloads preserved.</summary>
    public required SeString Sender { get; init; }

    /// <summary>The message as a full <see cref="SeString"/> with payloads preserved.</summary>
    public required SeString Message { get; init; }

    /// <summary>The plain-text form of the message.</summary>
    public required string PlainText { get; init; }

    /// <summary>The resolved sender name, or the raw sender text when no player payload was present.</summary>
    public required string SenderName { get; init; }

    /// <summary>The resolved sender home-world row id, or null when the payloads did not allow resolution.</summary>
    public required uint? SenderWorldId { get; init; }

    /// <summary>The resolved sender home-world name, or null when the payloads did not allow resolution.</summary>
    public required string? SenderWorldName { get; init; }

    /// <summary>
    /// The number of identical messages this event represents. 1 for a normal message; greater than 1 when
    /// duplicate suppression coalesced a burst of identical messages into this dispatch.
    /// </summary>
    public required int RepeatCount { get; init; }
}
