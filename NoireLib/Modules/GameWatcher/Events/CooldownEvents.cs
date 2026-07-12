namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when a watched local action goes on cooldown. Exact (read from the game's action manager).
/// </summary>
/// <param name="Cooldown">The action's recast state.</param>
public sealed record CooldownStartedEvent(CooldownSnapshot Cooldown);

/// <summary>
/// Fired when a watched local action comes off cooldown (or regains a charge from zero). Exact.
/// </summary>
/// <param name="Cooldown">The action's recast state.</param>
public sealed record CooldownEndedEvent(CooldownSnapshot Cooldown);

/// <summary>
/// Fired when a watched local action's charge count changes. Exact.
/// </summary>
/// <param name="PreviousCharges">The previous charge count.</param>
/// <param name="Cooldown">The action's recast state with the new charge count.</param>
public sealed record ChargesChangedEvent(uint PreviousCharges, CooldownSnapshot Cooldown);

/// <summary>
/// Fired when the local player's global cooldown state changes.
/// </summary>
/// <param name="IsReady">Whether the GCD is now ready.</param>
/// <param name="Remaining">The remaining GCD time in seconds, or 0 when ready.</param>
public sealed record GcdStateChangedEvent(bool IsReady, float Remaining);

/// <summary>
/// Fired when another character's action is observed and an estimated cooldown starts.<br/>
/// <b>Estimate</b> (doctrine tier 4): the client has no recast data for anyone but you — this is inferred
/// from observed usage and sheet recast data, and drifts with skill/spell speed and unseen charge usage.
/// </summary>
/// <param name="Cooldown">The estimated recast state (<see cref="CooldownSnapshot.IsEstimate"/> is true).</param>
public sealed record EstimatedCooldownStartedEvent(CooldownSnapshot Cooldown);

/// <summary>
/// Fired when another character's estimated cooldown elapses. <b>Estimate</b> — see <see cref="EstimatedCooldownStartedEvent"/>.
/// </summary>
/// <param name="Cooldown">The estimated recast state (<see cref="CooldownSnapshot.IsEstimate"/> is true).</param>
public sealed record EstimatedCooldownEndedEvent(CooldownSnapshot Cooldown);
