using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// The recast state of an action.<br/>
/// For the local player this is exact (read from the game's action manager).
/// For other characters it is an <b>estimate</b> inferred from observed action usage and sheet recast data -
/// <see cref="IsEstimate"/> is true, and the value drifts with skill/spell speed, haste effects and unseen
/// charge usage. Never treat estimated values as exact.
/// </summary>
public sealed record CooldownSnapshot
{
    /// <summary>The action row id.</summary>
    public required uint ActionId { get; init; }

    /// <summary>The entity id the cooldown belongs to (the local player for exact reads).</summary>
    public required uint EntityId { get; init; }

    /// <summary>Whether the action is ready (off cooldown, or at least one charge available).</summary>
    public required bool IsReady { get; init; }

    /// <summary>The remaining recast time in seconds, or 0 when ready.</summary>
    public required float Remaining { get; init; }

    /// <summary>The total recast time in seconds, or 0 when unknown.</summary>
    public required float Total { get; init; }

    /// <summary>The current number of charges, or 0 for non-charge actions on cooldown.</summary>
    public required uint CurrentCharges { get; init; }

    /// <summary>The maximum number of charges (1 for non-charge actions).</summary>
    public required uint MaxCharges { get; init; }

    /// <summary>
    /// True when this value was inferred from observed usage rather than read from the game.<br/>
    /// Estimated values drift and must not be treated as exact.
    /// </summary>
    public required bool IsEstimate { get; init; }

    /// <summary>The UTC timestamp when the snapshot was captured (or the estimate was computed).</summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
