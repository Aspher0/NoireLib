namespace NoireLib.GameStateWatcher;

/// <summary>
/// An immutable snapshot of a status effect on a character.
/// </summary>
/// <param name="StatusId">The row identifier of the status effect.</param>
/// <param name="SourceId">The entity identifier of the source that applied the status.</param>
/// <param name="RemainingTime">The remaining duration in seconds, or 0 for permanent effects.</param>
/// <param name="Param">The parameter value of the status effect, which often encodes stack count.</param>
public sealed record StatusEffectSnapshot(
    uint StatusId,
    uint SourceId,
    float RemainingTime,
    ushort Param);
