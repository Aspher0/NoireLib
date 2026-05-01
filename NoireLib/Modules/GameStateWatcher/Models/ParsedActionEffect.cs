namespace NoireLib.GameStateWatcher;

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
    /// <summary>
    /// Gets a value indicating whether this effect represents damage (direct, blocked, or parried).
    /// </summary>
    public bool IsDamage => Kind is ActionEffectKind.Damage or ActionEffectKind.BlockedDamage or ActionEffectKind.ParriedDamage;

    /// <summary>
    /// Gets a value indicating whether this effect represents healing.
    /// </summary>
    public bool IsHeal => Kind == ActionEffectKind.Heal;

    /// <summary>
    /// Gets a value indicating whether the damage was blocked.
    /// </summary>
    public bool IsBlocked => Kind == ActionEffectKind.BlockedDamage;

    /// <summary>
    /// Gets a value indicating whether the damage was parried.
    /// </summary>
    public bool IsParried => Kind == ActionEffectKind.ParriedDamage;

    /// <summary>
    /// Gets a value indicating whether this effect represents a miss.
    /// </summary>
    public bool IsMiss => Kind == ActionEffectKind.Miss;
}
