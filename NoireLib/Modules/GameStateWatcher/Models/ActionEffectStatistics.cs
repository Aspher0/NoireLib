using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Maintains rolling statistics for observed action effects, with optional grouping by source, target, or action.
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

    /// <summary>
    /// Gets the total accumulated damage across all observed actions.
    /// </summary>
    public long TotalDamage { get { lock (statsLock) return totalDamage; } }

    /// <summary>
    /// Gets the total accumulated healing across all observed actions.
    /// </summary>
    public long TotalHealing { get { lock (statsLock) return totalHealing; } }

    /// <summary>
    /// Gets the total number of action-effect entries observed.
    /// </summary>
    public long TotalActions { get { lock (statsLock) return totalActions; } }

    /// <summary>
    /// Gets the total number of critical hits observed.
    /// </summary>
    public long TotalCrits { get { lock (statsLock) return totalCrits; } }

    /// <summary>
    /// Gets the total number of direct hits observed.
    /// </summary>
    public long TotalDirectHits { get { lock (statsLock) return totalDirectHits; } }

    /// <summary>
    /// Gets the total number of misses observed.
    /// </summary>
    public long TotalMisses { get { lock (statsLock) return totalMisses; } }

    /// <summary>
    /// Gets the total number of blocked hits observed.
    /// </summary>
    public long TotalBlocks { get { lock (statsLock) return totalBlocks; } }

    /// <summary>
    /// Gets the total number of parried hits observed.
    /// </summary>
    public long TotalParries { get { lock (statsLock) return totalParries; } }

    /// <summary>
    /// Gets the critical-hit rate as a percentage (0–100).
    /// </summary>
    public double CritRate
    {
        get
        {
            lock (statsLock)
                return totalActions == 0 ? 0d : totalCrits / (double)totalActions * 100d;
        }
    }

    /// <summary>
    /// Gets the direct-hit rate as a percentage (0–100).
    /// </summary>
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

    /// <summary>
    /// Resets all running statistics to zero.
    /// </summary>
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

/// <summary>
/// A precomputed summary of action-effect history grouped by a key (source, target, or action identifier).
/// </summary>
/// <param name="Key">The grouping key value.</param>
/// <param name="Count">The total number of actions in this group.</param>
/// <param name="TotalDamage">The accumulated damage for this group.</param>
/// <param name="TotalHealing">The accumulated healing for this group.</param>
/// <param name="CritCount">The number of critical-hit effects in this group.</param>
/// <param name="DirectHitCount">The number of direct-hit effects in this group.</param>
public sealed record ActionGroupSummary(
    ulong Key,
    int Count,
    long TotalDamage,
    long TotalHealing,
    int CritCount,
    int DirectHitCount);
