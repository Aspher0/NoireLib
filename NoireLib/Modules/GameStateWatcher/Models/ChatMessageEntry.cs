using Dalamud.Game.Text;
using System;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Represents a captured chat message entry with metadata.
/// </summary>
/// <param name="Type">The chat channel type.</param>
/// <param name="Timestamp">The message timestamp from the game.</param>
/// <param name="SenderName">The sender display name.</param>
/// <param name="MessageText">The plain-text message content.</param>
/// <param name="ReceivedAt">The UTC time the message was captured by the tracker.</param>
public sealed record ChatMessageEntry(
    XivChatType Type,
    int Timestamp,
    string SenderName,
    string MessageText,
    DateTimeOffset ReceivedAt);
