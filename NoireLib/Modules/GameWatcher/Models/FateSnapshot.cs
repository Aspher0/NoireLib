using Dalamud.Game.ClientState.Fates;
using System;
using System.Numerics;

namespace NoireLib.GameWatcher;

/// <summary>
/// An immutable snapshot of a fate in the current zone.
/// </summary>
public sealed record FateSnapshot
{
    /// <summary>The fate id.</summary>
    public required ushort FateId { get; init; }

    /// <summary>The fate's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The fate state.</summary>
    public required FateState State { get; init; }

    /// <summary>The completion progress (0–100).</summary>
    public required byte Progress { get; init; }

    /// <summary>The fate's suggested level.</summary>
    public required byte Level { get; init; }

    /// <summary>The world-space center position.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>The fate radius in yalms.</summary>
    public required float Radius { get; init; }

    /// <summary>The remaining time in seconds, or 0 when not limited.</summary>
    public required long TimeRemaining { get; init; }

    /// <summary>Whether the fate currently grants a bonus.</summary>
    public required bool HasBonus { get; init; }

    /// <summary>The UTC timestamp when the snapshot was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
