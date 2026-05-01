using System.Collections.Generic;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Represents the parsed action effects applied to a single target entity.
/// </summary>
/// <param name="TargetEntityId">The entity identifier of the target.</param>
/// <param name="Effects">The parsed effects applied to this target.</param>
public sealed record PerTargetActionEffect(
    ulong TargetEntityId,
    IReadOnlyList<ParsedActionEffect> Effects)
{
    /// <summary>
    /// Gets the total damage dealt to this target across all effect entries.
    /// </summary>
    public uint TotalDamage
    {
        get
        {
            uint total = 0;
            foreach (var effect in Effects)
            {
                if (effect.IsDamage)
                    total += effect.Value;
            }
            return total;
        }
    }

    /// <summary>
    /// Gets the total healing applied to this target across all effect entries.
    /// </summary>
    public uint TotalHealing
    {
        get
        {
            uint total = 0;
            foreach (var effect in Effects)
            {
                if (effect.IsHeal)
                    total += effect.Value;
            }
            return total;
        }
    }

    /// <summary>
    /// Gets a value indicating whether any effect on this target was a critical hit.
    /// </summary>
    public bool HasCritical
    {
        get
        {
            foreach (var effect in Effects)
            {
                if (effect.IsCritical)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether any effect on this target was a direct hit.
    /// </summary>
    public bool HasDirectHit
    {
        get
        {
            foreach (var effect in Effects)
            {
                if (effect.IsDirectHit)
                    return true;
            }
            return false;
        }
    }
}
