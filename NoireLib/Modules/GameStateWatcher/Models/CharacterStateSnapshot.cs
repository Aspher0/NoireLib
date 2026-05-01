using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// An immutable snapshot of a live player character's state at a point in time.
/// </summary>
/// <param name="EntityId">The entity identifier of the player character.</param>
/// <param name="Name">The display name of the player character.</param>
/// <param name="ClassJobId">The class/job row identifier of the player character.</param>
/// <param name="Level">The current level of the player character.</param>
/// <param name="CurrentHp">The current hit points of the player character.</param>
/// <param name="MaxHp">The maximum hit points of the player character.</param>
/// <param name="CurrentMp">The current mana/resource points of the player character.</param>
/// <param name="MaxMp">The maximum mana/resource points of the player character.</param>
/// <param name="ShieldPercentage">The shield percentage (0–100) applied to the player character.</param>
/// <param name="IsCasting">Whether the player character is currently casting an action.</param>
/// <param name="CastActionId">The action row identifier of the action being cast, or 0 if not casting.</param>
/// <param name="CastTargetEntityId">The entity identifier of the cast target, or 0 if not casting.</param>
/// <param name="TotalCastTime">The total cast time in seconds of the current cast, or 0 if not casting.</param>
/// <param name="CurrentCastTime">The elapsed cast time in seconds of the current cast, or 0 if not casting.</param>
/// <param name="IsInCombat">Whether the player character is currently in combat.</param>
/// <param name="IsTargetable">Whether the player character is currently targetable by other entities.</param>
/// <param name="TargetEntityId">The entity identifier of the player character's current target, or <see langword="null"/> if none.</param>
/// <param name="IsDead">Whether the player character is currently dead.</param>
/// <param name="CharacterMode">The raw <see cref="CharacterModes"/> value representing the character's current mode (normal, emoting, mounted, etc.).</param>
/// <param name="CharacterModeParam">The mode-specific parameter (e.g. the emote identifier when emoting).</param>
/// <param name="OnlineStatusId">The online-status row identifier (e.g. AFK, busy, looking for party).</param>
/// <param name="CapturedAt">The UTC timestamp when the snapshot was captured.</param>
public sealed record CharacterStateSnapshot(
    uint EntityId,
    string Name,
    uint ClassJobId,
    uint Level,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    byte ShieldPercentage,
    bool IsCasting,
    uint CastActionId,
    uint CastTargetEntityId,
    float TotalCastTime,
    float CurrentCastTime,
    bool IsInCombat,
    bool IsTargetable,
    uint? TargetEntityId,
    bool IsDead,
    byte CharacterMode,
    ushort CharacterModeParam,
    uint OnlineStatusId,
    DateTimeOffset CapturedAt)
{
    /// <summary>
    /// Gets a value indicating whether the player character is currently alive.
    /// </summary>
    public bool IsAlive => !IsDead;

    /// <summary>
    /// Gets the current HP percentage for the player character.
    /// </summary>
    public float HpPercent => MaxHp == 0 ? 0f : CurrentHp / (float)MaxHp;

    /// <summary>
    /// Gets the current MP/resource percentage for the player character.
    /// </summary>
    public float MpPercent => MaxMp == 0 ? 0f : CurrentMp / (float)MaxMp;

    /// <summary>
    /// Gets the effective shield HP computed from <see cref="MaxHp"/> and <see cref="ShieldPercentage"/>.
    /// </summary>
    public uint ShieldHp => (uint)(MaxHp * ShieldPercentage / 100f);

    /// <summary>
    /// Gets a value indicating whether the player character is currently performing a looping emote.
    /// </summary>
    public bool IsEmoting => (CharacterModes)CharacterMode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop;

    /// <summary>
    /// Gets a value indicating whether the player character is currently mounted (as driver or pillion passenger).
    /// </summary>
    public bool IsMounted => (CharacterModes)CharacterMode is CharacterModes.Mounted or CharacterModes.RidingPillion;

    /// <summary>
    /// Gets a value indicating whether the player character is riding as a pillion passenger.
    /// </summary>
    public bool IsRidingPillion => (CharacterModes)CharacterMode == CharacterModes.RidingPillion;

    /// <summary>
    /// Determines whether another snapshot represents the same observed character state, ignoring capture timestamp
    /// and continuously changing cast-time progress.
    /// </summary>
    /// <param name="other">The snapshot to compare against.</param>
    /// <returns><see langword="true"/> if the observed character state matches; otherwise, <see langword="false"/>.</returns>
    public bool HasSameObservedState(CharacterStateSnapshot? other)
        => other != null
        && EntityId == other.EntityId
        && Name.Equals(other.Name, StringComparison.Ordinal)
        && ClassJobId == other.ClassJobId
        && Level == other.Level
        && CurrentHp == other.CurrentHp
        && MaxHp == other.MaxHp
        && CurrentMp == other.CurrentMp
        && MaxMp == other.MaxMp
        && ShieldPercentage == other.ShieldPercentage
        && IsCasting == other.IsCasting
        && CastActionId == other.CastActionId
        && CastTargetEntityId == other.CastTargetEntityId
        && IsInCombat == other.IsInCombat
        && IsTargetable == other.IsTargetable
        && TargetEntityId == other.TargetEntityId
        && IsDead == other.IsDead
        && CharacterMode == other.CharacterMode
        && CharacterModeParam == other.CharacterModeParam
        && OnlineStatusId == other.OnlineStatusId;
}
