using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Numerics;

namespace NoireLib.GameWatcher;

/// <summary>
/// An immutable snapshot of a character's observed state at a point in time.<br/>
/// Snapshots are captured once per tick per subject and shared by every event fired that tick.<br/>
/// Event records may gain new members in future versions (appended, never reordered or removed within a major
/// version) — consume snapshots by property, not by positional <c>Deconstruct</c>.
/// </summary>
public sealed record CharacterSnapshot
{
    /// <summary>The entity id (object-table slot; can be reused by the game after despawn).</summary>
    public required uint EntityId { get; init; }

    /// <summary>The full game-object id.</summary>
    public required ulong GameObjectId { get; init; }

    /// <summary>The content id (stable per person), or 0 when unavailable (NPCs, companions).</summary>
    public required ulong ContentId { get; init; }

    /// <summary>The display name.</summary>
    public required string Name { get; init; }

    /// <summary>The home world row id, or 0 when unavailable.</summary>
    public required uint HomeWorldId { get; init; }

    /// <summary>The current world row id, or 0 when unavailable.</summary>
    public required uint CurrentWorldId { get; init; }

    /// <summary>The object kind (player, battle NPC, companion, …), as the raw Dalamud enum value.</summary>
    public required Dalamud.Game.ClientState.Objects.Enums.ObjectKind ObjectKind { get; init; }

    /// <summary>Precomputed relationship flags (local player, party, alliance, friend), captured with the snapshot.</summary>
    public required SubjectFlags Flags { get; init; }

    /// <summary>The class/job row id.</summary>
    public required uint ClassJobId { get; init; }

    /// <summary>The character level.</summary>
    public required uint Level { get; init; }

    /// <summary>Current hit points.</summary>
    public required uint CurrentHp { get; init; }

    /// <summary>Maximum hit points.</summary>
    public required uint MaxHp { get; init; }

    /// <summary>Current mana points.</summary>
    public required uint CurrentMp { get; init; }

    /// <summary>Maximum mana points.</summary>
    public required uint MaxMp { get; init; }

    /// <summary>Current gathering points. Only meaningful for the local player — not synchronized for others.</summary>
    public required uint CurrentGp { get; init; }

    /// <summary>Maximum gathering points. Only meaningful for the local player.</summary>
    public required uint MaxGp { get; init; }

    /// <summary>Current crafting points. Only meaningful for the local player.</summary>
    public required uint CurrentCp { get; init; }

    /// <summary>Maximum crafting points. Only meaningful for the local player.</summary>
    public required uint MaxCp { get; init; }

    /// <summary>The shield percentage (0–100).</summary>
    public required byte ShieldPercentage { get; init; }

    /// <summary>Whether the character is casting.</summary>
    public required bool IsCasting { get; init; }

    /// <summary>Whether the current cast can be interrupted, or false when not casting.</summary>
    public required bool IsCastInterruptible { get; init; }

    /// <summary>The action row id being cast, or 0 when not casting.</summary>
    public required uint CastActionId { get; init; }

    /// <summary>The entity id of the cast target, or 0 when not casting.</summary>
    public required uint CastTargetEntityId { get; init; }

    /// <summary>Total cast time in seconds, or 0 when not casting.</summary>
    public required float TotalCastTime { get; init; }

    /// <summary>Elapsed cast time in seconds, or 0 when not casting.</summary>
    public required float CurrentCastTime { get; init; }

    /// <summary>Whether the character is in combat.</summary>
    public required bool IsInCombat { get; init; }

    /// <summary>Whether the character is targetable.</summary>
    public required bool IsTargetable { get; init; }

    /// <summary>The entity id of the character's current target, or null when none.</summary>
    public required uint? TargetEntityId { get; init; }

    /// <summary>Whether the character is dead.</summary>
    public required bool IsDead { get; init; }

    /// <summary>The raw character mode (see <see cref="CharacterModes"/>).</summary>
    public required byte Mode { get; init; }

    /// <summary>The mode-specific parameter (e.g. an emote index while in a looping emote).</summary>
    public required byte ModeParam { get; init; }

    /// <summary>
    /// The exact emote id currently played, read from the character's emote controller, or 0 when none.
    /// Unlike <see cref="Mode"/> this resolves the precise emote — one-shots, looping emotes and cposes alike
    /// (idle poses carry an id; the base pose is 0).
    /// </summary>
    public required ushort EmoteId { get; init; }

    /// <summary>The online-status row id (AFK, busy, looking for party, …).</summary>
    public required uint OnlineStatusId { get; init; }

    /// <summary>The world-space position at capture time.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>The rotation in radians at capture time.</summary>
    public required float Rotation { get; init; }

    /// <summary>The UTC timestamp when the snapshot was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Whether the character is alive.</summary>
    public bool IsAlive => !IsDead;

    /// <summary>Whether the subject is a player character.</summary>
    public bool IsPlayer => ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc;

    /// <summary>The current HP fraction (0–1).</summary>
    public float HpPercent => MaxHp == 0 ? 0f : CurrentHp / (float)MaxHp;

    /// <summary>The current MP fraction (0–1).</summary>
    public float MpPercent => MaxMp == 0 ? 0f : CurrentMp / (float)MaxMp;

    /// <summary>The effective shield HP computed from <see cref="MaxHp"/> and <see cref="ShieldPercentage"/>.</summary>
    public uint ShieldHp => (uint)(MaxHp * ShieldPercentage / 100f);

    /// <summary>Whether the character is currently performing a looping emote.</summary>
    public bool IsEmoting => (CharacterModes)Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop;

    /// <summary>Whether the character is mounted (as driver or pillion passenger).</summary>
    public bool IsMounted => (CharacterModes)Mode is CharacterModes.Mounted or CharacterModes.RidingPillion;

    /// <summary>Whether the character is crafting.</summary>
    public bool IsCrafting => (CharacterModes)Mode == CharacterModes.Crafting;

    /// <summary>Whether the character is gathering.</summary>
    public bool IsGathering => (CharacterModes)Mode == CharacterModes.Gathering;

    /// <summary>Whether the character is performing (bard performance mode).</summary>
    public bool IsPerforming => (CharacterModes)Mode == CharacterModes.Performance;
}
