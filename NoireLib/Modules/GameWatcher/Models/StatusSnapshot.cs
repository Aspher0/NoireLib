using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// An immutable snapshot of a single status effect on a character.
/// </summary>
public sealed record StatusSnapshot
{
    /// <summary>The status row id.</summary>
    public required uint StatusId { get; init; }

    /// <summary>The status parameter - the stack count for stacking statuses.</summary>
    public required ushort Param { get; init; }

    /// <summary>The remaining duration in seconds, or 0 for permanent statuses.</summary>
    public required float RemainingTime { get; init; }

    /// <summary>The entity id of the status source, or 0 when unknown.</summary>
    public required uint SourceEntityId { get; init; }

    /// <summary>The entity id of the character carrying the status.</summary>
    public required uint OwnerEntityId { get; init; }

    /// <summary>The UTC timestamp when the snapshot was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
