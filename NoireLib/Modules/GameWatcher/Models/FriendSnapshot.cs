using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// An immutable snapshot of a friend-list entry — remote presence through the game's social data.<br/>
/// Backed by the game's social proxy, refreshed on a slow cadence (see <see cref="FriendWatcher"/>): values
/// are seconds-grained, not frame-grained, and can lag reality between refreshes.
/// </summary>
public sealed record FriendSnapshot
{
    /// <summary>The friend's content id.</summary>
    public required ulong ContentId { get; init; }

    /// <summary>The friend's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The friend's home world row id.</summary>
    public required uint HomeWorldId { get; init; }

    /// <summary>The friend's current world row id, or 0 when offline/unknown.</summary>
    public required uint CurrentWorldId { get; init; }

    /// <summary>The territory row id the friend is in, or 0 when offline/unknown.</summary>
    public required uint TerritoryId { get; init; }

    /// <summary>The friend's current class/job row id, or 0 when offline/unknown.</summary>
    public required uint ClassJobId { get; init; }

    /// <summary>Whether the friend is online.</summary>
    public required bool IsOnline { get; init; }

    /// <summary>The UTC timestamp when the snapshot was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
