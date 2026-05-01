using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Represents a captured action effect entry with metadata and parsed per-target effects.
/// </summary>
/// <param name="SourceEntityId">The entity identifier of the action source.</param>
/// <param name="ActionId">The action row identifier.</param>
/// <param name="ActionKind">The raw action kind byte from the packet header.</param>
/// <param name="AnimationTargetId">The animation target entity identifier.</param>
/// <param name="TargetEntityIds">The entity identifiers of the targets hit.</param>
/// <param name="PerTargetEffects">The parsed effects per target.</param>
/// <param name="ReceivedAt">The UTC time the action effect was captured by the tracker.</param>
public sealed record ActionEffectEntry(
    uint SourceEntityId,
    uint ActionId,
    byte ActionKind,
    ulong AnimationTargetId,
    IReadOnlyList<ulong> TargetEntityIds,
    IReadOnlyList<PerTargetActionEffect> PerTargetEffects,
    DateTimeOffset ReceivedAt)
{
    /// <summary>
    /// Gets the total damage dealt across all targets.
    /// </summary>
    public uint TotalDamage => (uint)PerTargetEffects.Sum(t => (long)t.TotalDamage);

    /// <summary>
    /// Gets the total healing done across all targets.
    /// </summary>
    public uint TotalHealing => (uint)PerTargetEffects.Sum(t => (long)t.TotalHealing);

    /// <summary>
    /// Gets a value indicating whether this entry contains any damage effects.
    /// </summary>
    public bool HasDamage => PerTargetEffects.Any(t => t.TotalDamage > 0);

    /// <summary>
    /// Gets a value indicating whether this entry contains any healing effects.
    /// </summary>
    public bool HasHealing => PerTargetEffects.Any(t => t.TotalHealing > 0);

    /// <summary>
    /// Gets a value indicating whether any effect in this entry was a critical hit.
    /// </summary>
    public bool HasCritical => PerTargetEffects.Any(t => t.HasCritical);

    /// <summary>
    /// Gets a value indicating whether any effect in this entry was a direct hit.
    /// </summary>
    public bool HasDirectHit => PerTargetEffects.Any(t => t.HasDirectHit);

    /// <summary>
    /// Gets the parsed effects for the specified target entity, or <see langword="null"/> if none exist.
    /// </summary>
    /// <param name="targetEntityId">The target entity identifier to look up.</param>
    /// <returns>The per-target effect data, or <see langword="null"/> if the target was not hit.</returns>
    public PerTargetActionEffect? GetEffectsForTarget(ulong targetEntityId)
        => PerTargetEffects.FirstOrDefault(t => t.TargetEntityId == targetEntityId);
}
