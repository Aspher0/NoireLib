using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// Describes the type of an individual action effect entry parsed from the server packet.
/// </summary>
public enum ActionEffectKind : byte
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    Nothing = 0,
    Miss = 1,
    FullResist = 2,
    Damage = 3,
    Heal = 4,
    BlockedDamage = 5,
    ParriedDamage = 6,
    Invulnerable = 7,
    NoEffect = 8,
    Unknown9 = 9,
    MpLoss = 10,
    MpGain = 11,
    TpLoss = 12,
    GpGain = 13,

    ApplyStatusTarget = 14,
    ApplyStatusSource = 15,
    RecoveredStatus = 16,
    LoseStatusTarget = 17,
    LoseStatusSource = 18,
    StatusNoEffect = 20,

    EnminityIndex = 24,
    EnmityAmountUp = 25,
    Unk_EnmityAmountDown = 26,
    StartActionCombo = 27,
    ComboStep = 28,

    Knockback = 31,
    Attract = 32,
    Attract2 = 33,
    Dash = 34,
    Dash2 = 35,
    Dash3 = 36,

    MountSfx = 39,

    StatusDispel1 = 47,
    StatusDispel2 = 48,
    StatusDispel3 = 49,

    InstantDeath = 50,
    InstantDeath2 = 51,

    FullResistStatus = 55,
    Vulnerability = 57,

    SxtBattleLogMessage = 60,
    ActionChange = 61,
    Unknown62 = 62,
    ToggleVis = 65,
    SetModelScale = 68,
    Unk_SetModelState = 73,

    SetHP = 74,
    PartialInvulnerable = 75,
    Interrupt = 76,

    Unk_MountJapaneseVersion = 240,
    Unknown = 255,
#pragma warning restore // Missing XML comment for publicly visible type or member
}

/// <summary>
/// Represents a single parsed action effect entry from the server packet.
/// </summary>
/// <param name="Kind">The classification of this effect (damage, heal, blocked, parried, etc.).</param>
/// <param name="Value">The numeric value of the effect (damage dealt, HP healed, etc.).</param>
/// <param name="IsCritical">Whether the effect was a critical hit.</param>
/// <param name="IsDirectHit">Whether the effect was a direct hit.</param>
/// <param name="Param0">The raw first parameter byte from the effect entry.</param>
/// <param name="Param1">The raw second parameter byte from the effect entry.</param>
/// <param name="Param2">The raw third parameter byte from the effect entry.</param>
/// <param name="Flags1">The raw first flags byte from the effect entry.</param>
/// <param name="Flags2">The raw second flags byte from the effect entry.</param>
public sealed record ParsedActionEffect(
    ActionEffectKind Kind,
    uint Value,
    bool IsCritical,
    bool IsDirectHit,
    byte Param0,
    byte Param1,
    byte Param2,
    byte Flags1,
    byte Flags2)
{
    /// <summary>Whether this effect represents damage (direct, blocked, or parried).</summary>
    public bool IsDamage => Kind is ActionEffectKind.Damage or ActionEffectKind.BlockedDamage or ActionEffectKind.ParriedDamage;

    /// <summary>Whether this effect represents healing.</summary>
    public bool IsHeal => Kind == ActionEffectKind.Heal;

    /// <summary>Whether the damage was blocked.</summary>
    public bool IsBlocked => Kind == ActionEffectKind.BlockedDamage;

    /// <summary>Whether the damage was parried.</summary>
    public bool IsParried => Kind == ActionEffectKind.ParriedDamage;

    /// <summary>Whether this effect represents a miss.</summary>
    public bool IsMiss => Kind == ActionEffectKind.Miss;
}

/// <summary>
/// The parsed effects applied to a single target of an action.
/// </summary>
/// <param name="TargetEntityId">The target's entity id.</param>
/// <param name="Effects">The parsed effects applied to the target.</param>
public sealed record PerTargetActionEffect(
    ulong TargetEntityId,
    IReadOnlyList<ParsedActionEffect> Effects);

/// <summary>
/// A fully captured action-effect packet: one action resolving on one or more targets.
/// </summary>
public sealed record ActionEffectEntry
{
    /// <summary>The entity id of the caster.</summary>
    public required uint SourceEntityId { get; init; }

    /// <summary>The action row id.</summary>
    public required uint ActionId { get; init; }

    /// <summary>The animation target id from the packet header.</summary>
    public required ulong AnimationTargetId { get; init; }

    /// <summary>The entity ids of every target hit by the action.</summary>
    public required IReadOnlyList<ulong> TargetEntityIds { get; init; }

    /// <summary>The parsed effects per target.</summary>
    public required IReadOnlyList<PerTargetActionEffect> PerTargetEffects { get; init; }

    /// <summary>The UTC timestamp when the packet was observed.</summary>
    public required DateTimeOffset ObservedAt { get; init; }
}

/// <summary>
/// Maintains rolling statistics for observed action effects.
/// </summary>
public sealed class ActionEffectStatistics
{
    private readonly object statsLock = new();
    private long totalDamage;
    private long totalHealing;
    private long totalActions;
    private long totalCrits;
    private long totalDirectHits;
    private long totalMisses;
    private long totalBlocks;
    private long totalParries;

    /// <summary>The total accumulated damage across all observed actions.</summary>
    public long TotalDamage { get { lock (statsLock) return totalDamage; } }

    /// <summary>The total accumulated healing across all observed actions.</summary>
    public long TotalHealing { get { lock (statsLock) return totalHealing; } }

    /// <summary>The total number of action-effect entries observed.</summary>
    public long TotalActions { get { lock (statsLock) return totalActions; } }

    /// <summary>The total number of critical hits observed.</summary>
    public long TotalCrits { get { lock (statsLock) return totalCrits; } }

    /// <summary>The total number of direct hits observed.</summary>
    public long TotalDirectHits { get { lock (statsLock) return totalDirectHits; } }

    /// <summary>The total number of misses observed.</summary>
    public long TotalMisses { get { lock (statsLock) return totalMisses; } }

    /// <summary>The total number of blocked hits observed.</summary>
    public long TotalBlocks { get { lock (statsLock) return totalBlocks; } }

    /// <summary>The total number of parried hits observed.</summary>
    public long TotalParries { get { lock (statsLock) return totalParries; } }

    /// <summary>The critical-hit rate as a percentage (0–100).</summary>
    public double CritRate
    {
        get
        {
            lock (statsLock)
                return totalActions == 0 ? 0d : totalCrits / (double)totalActions * 100d;
        }
    }

    /// <summary>The direct-hit rate as a percentage (0–100).</summary>
    public double DirectHitRate
    {
        get
        {
            lock (statsLock)
                return totalActions == 0 ? 0d : totalDirectHits / (double)totalActions * 100d;
        }
    }

    /// <summary>
    /// Records a single action-effect entry into the running statistics.
    /// </summary>
    /// <param name="entry">The action-effect entry to record.</param>
    internal void Record(ActionEffectEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (statsLock)
        {
            totalActions++;

            foreach (var targetEffect in entry.PerTargetEffects)
            {
                foreach (var effect in targetEffect.Effects)
                {
                    if (effect.IsDamage)
                        totalDamage += effect.Value;
                    if (effect.IsHeal)
                        totalHealing += effect.Value;
                    if (effect.IsCritical)
                        totalCrits++;
                    if (effect.IsDirectHit)
                        totalDirectHits++;
                    if (effect.IsMiss)
                        totalMisses++;
                    if (effect.IsBlocked)
                        totalBlocks++;
                    if (effect.IsParried)
                        totalParries++;
                }
            }
        }
    }

    /// <summary>Resets all running statistics to zero.</summary>
    public void Reset()
    {
        lock (statsLock)
        {
            totalDamage = 0;
            totalHealing = 0;
            totalActions = 0;
            totalCrits = 0;
            totalDirectHits = 0;
            totalMisses = 0;
            totalBlocks = 0;
            totalParries = 0;
        }
    }
}
