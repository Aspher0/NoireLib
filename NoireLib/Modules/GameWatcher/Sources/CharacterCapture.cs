using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

// The scalar, allocation-free view of a character used by the compare-first gate: masked fields are compared against
// the stored snapshot straight from game memory, and a new snapshot record is allocated only when something changed.
internal struct CharacterFieldSet
{
    public uint EntityId;
    public ulong ContentId;
    public uint HomeWorldId;
    public uint ClassJobId;
    public uint Level;
    public uint CurrentHp;
    public uint MaxHp;
    public uint CurrentMp;
    public uint MaxMp;
    public uint CurrentGp;
    public uint MaxGp;
    public uint CurrentCp;
    public uint MaxCp;
    public byte ShieldPercentage;
    public bool IsCasting;
    public uint CastActionId;
    public bool IsInCombat;
    public bool IsTargetable;
    public uint? TargetEntityId;
    public bool IsDead;
    public byte Mode;
    public byte ModeParam;
    public ushort EmoteId;
    public uint OnlineStatusId;
    public SubjectFlags Flags;

    public static CharacterFieldSet FromSnapshot(CharacterSnapshot snapshot) => new()
    {
        EntityId = snapshot.EntityId,
        ContentId = snapshot.ContentId,
        HomeWorldId = snapshot.HomeWorldId,
        ClassJobId = snapshot.ClassJobId,
        Level = snapshot.Level,
        CurrentHp = snapshot.CurrentHp,
        MaxHp = snapshot.MaxHp,
        CurrentMp = snapshot.CurrentMp,
        MaxMp = snapshot.MaxMp,
        CurrentGp = snapshot.CurrentGp,
        MaxGp = snapshot.MaxGp,
        CurrentCp = snapshot.CurrentCp,
        MaxCp = snapshot.MaxCp,
        ShieldPercentage = snapshot.ShieldPercentage,
        IsCasting = snapshot.IsCasting,
        CastActionId = snapshot.CastActionId,
        IsInCombat = snapshot.IsInCombat,
        IsTargetable = snapshot.IsTargetable,
        TargetEntityId = snapshot.TargetEntityId,
        IsDead = snapshot.IsDead,
        Mode = snapshot.Mode,
        ModeParam = snapshot.ModeParam,
        EmoteId = snapshot.EmoteId,
        OnlineStatusId = snapshot.OnlineStatusId,
        Flags = snapshot.Flags,
    };
}

// The pure diff logic of the Characters source: which aspects changed between two field sets. No game access -
// unit-testable against fabricated values.
internal static class CharacterDiffEngine
{
    // The caller intersects the result with the union interest mask.
    public static CharacterAspect ComputeChangedAspects(in CharacterFieldSet prev, in CharacterFieldSet cur)
    {
        var changed = CharacterAspect.None;

        if (prev.CurrentHp != cur.CurrentHp || prev.MaxHp != cur.MaxHp
            || prev.CurrentMp != cur.CurrentMp || prev.MaxMp != cur.MaxMp
            || prev.CurrentGp != cur.CurrentGp || prev.MaxGp != cur.MaxGp
            || prev.CurrentCp != cur.CurrentCp || prev.MaxCp != cur.MaxCp)
        {
            changed |= CharacterAspect.Vitals;
        }

        if (prev.ShieldPercentage != cur.ShieldPercentage)
            changed |= CharacterAspect.Shield;

        if (prev.IsCasting != cur.IsCasting || prev.CastActionId != cur.CastActionId)
            changed |= CharacterAspect.Cast;

        if (prev.IsInCombat != cur.IsInCombat)
            changed |= CharacterAspect.Combat;

        if (prev.TargetEntityId != cur.TargetEntityId)
            changed |= CharacterAspect.Target;

        if (prev.IsTargetable != cur.IsTargetable)
            changed |= CharacterAspect.Targetable;

        if (prev.IsDead != cur.IsDead)
            changed |= CharacterAspect.Life;

        if (prev.Mode != cur.Mode || prev.ModeParam != cur.ModeParam)
            changed |= CharacterAspect.Mode;

        if (prev.EmoteId != cur.EmoteId)
            changed |= CharacterAspect.Emote;

        if (prev.OnlineStatusId != cur.OnlineStatusId)
            changed |= CharacterAspect.OnlineStatus;

        if (prev.ClassJobId != cur.ClassJobId || prev.Level != cur.Level)
            changed |= CharacterAspect.JobLevel;

        if (prev.ContentId != cur.ContentId || prev.HomeWorldId != cur.HomeWorldId)
            changed |= CharacterAspect.Identity;

        return changed;
    }
}

// Shared capture helpers: subject enumeration per iteration class, relationship flags, field sets and full snapshots.
// All reads run on the framework thread.
internal static class CharacterCapture
{
    private const ulong NoTargetSentinel = 0xE0000000;

    public static uint? ResolveTargetEntityId(ulong targetObjectId)
        => targetObjectId is 0 or NoTargetSentinel ? null : (uint)targetObjectId;

    public static IEnumerable<ICharacter> EnumerateSubjects(Scope.IterationClass iterationClass)
    {
        switch (iterationClass)
        {
            case Scope.IterationClass.LocalOnly:
                {
                    var local = NoireService.ObjectTable.LocalPlayer;

                    if (local != null)
                        yield return local;

                    yield break;
                }

            case Scope.IterationClass.Players:
                {
                    foreach (var obj in NoireService.ObjectTable.PlayerObjects)
                    {
                        if (obj is ICharacter chara)
                            yield return chara;
                    }

                    yield break;
                }

            default:
                {
                    foreach (var obj in NoireService.ObjectTable)
                    {
                        if (obj.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.Companion
                            && obj is ICharacter chara)
                        {
                            yield return chara;
                        }
                    }

                    yield break;
                }
        }
    }

    // The party, alliance and friend flags the client maintains on the native character.
    public static unsafe SubjectFlags ReadFlags(ICharacter chara, uint localEntityId)
    {
        var flags = SubjectFlags.None;

        if (chara.EntityId == localEntityId)
            flags |= SubjectFlags.IsLocalPlayer;

        var native = (NativeCharacter*)chara.Address;

        if (native != null)
        {
            if (native->IsPartyMember)
                flags |= SubjectFlags.IsPartyMember;

            if (native->IsAllianceMember)
                flags |= SubjectFlags.IsAllianceMember;

            if (native->IsFriend)
                flags |= SubjectFlags.IsFriend;
        }

        return flags;
    }

    // The name is read lazily from Dalamud's cached SeString.
    public static unsafe SubjectProbe BuildProbe(ICharacter chara, SubjectFlags flags)
    {
        var native = (NativeCharacter*)chara.Address;

        return new SubjectProbe
        {
            EntityId = chara.EntityId,
            ContentId = native != null ? native->ContentId : 0,
            Name = chara.Name.TextValue,
            HomeWorldId = native != null ? native->HomeWorld : 0u,
            Flags = flags,
            IsPlayer = chara.ObjectKind == ObjectKind.Pc,
        };
    }

    // Allocation-free: the compare-first gate reads it every tick.
    public static unsafe CharacterFieldSet ReadFields(ICharacter chara, SubjectFlags flags)
    {
        var native = (NativeCharacter*)chara.Address;
        var battleChara = chara as IBattleChara;

        return new CharacterFieldSet
        {
            EntityId = chara.EntityId,
            ContentId = native != null ? native->ContentId : 0,
            HomeWorldId = native != null ? native->HomeWorld : 0u,
            ClassJobId = chara.ClassJob.RowId,
            Level = chara.Level,
            CurrentHp = chara.CurrentHp,
            MaxHp = chara.MaxHp,
            CurrentMp = chara.CurrentMp,
            MaxMp = chara.MaxMp,
            CurrentGp = chara.CurrentGp,
            MaxGp = chara.MaxGp,
            CurrentCp = chara.CurrentCp,
            MaxCp = chara.MaxCp,
            ShieldPercentage = chara.ShieldPercentage,
            IsCasting = battleChara?.IsCasting ?? false,
            CastActionId = battleChara?.CastActionId ?? 0,
            IsInCombat = (chara.StatusFlags & StatusFlags.InCombat) != 0,
            IsTargetable = chara.IsTargetable,
            TargetEntityId = ResolveTargetEntityId(chara.TargetObjectId),
            IsDead = chara.IsDead,
            Mode = native != null ? (byte)native->Mode : (byte)0,
            ModeParam = native != null ? native->ModeParam : (byte)0,
            EmoteId = native != null ? native->EmoteController.EmoteId : (ushort)0,
            OnlineStatusId = chara.OnlineStatus.RowId,
            Flags = flags,
        };
    }

    // Only called when something changed, or for baselines and queries.
    public static unsafe CharacterSnapshot Capture(ICharacter chara, SubjectFlags flags, DateTimeOffset now)
    {
        var native = (NativeCharacter*)chara.Address;
        var battleChara = chara as IBattleChara;

        return new CharacterSnapshot
        {
            EntityId = chara.EntityId,
            GameObjectId = chara.GameObjectId,
            ContentId = native != null ? native->ContentId : 0,
            Name = chara.Name.TextValue,
            HomeWorldId = native != null ? native->HomeWorld : 0u,
            CurrentWorldId = native != null ? native->CurrentWorld : 0u,
            ObjectKind = chara.ObjectKind,
            Flags = flags,
            ClassJobId = chara.ClassJob.RowId,
            Level = chara.Level,
            CurrentHp = chara.CurrentHp,
            MaxHp = chara.MaxHp,
            CurrentMp = chara.CurrentMp,
            MaxMp = chara.MaxMp,
            CurrentGp = chara.CurrentGp,
            MaxGp = chara.MaxGp,
            CurrentCp = chara.CurrentCp,
            MaxCp = chara.MaxCp,
            ShieldPercentage = chara.ShieldPercentage,
            IsCasting = battleChara?.IsCasting ?? false,
            IsCastInterruptible = battleChara?.IsCastInterruptible ?? false,
            CastActionId = battleChara?.CastActionId ?? 0,
            CastTargetEntityId = battleChara != null ? (uint)battleChara.CastTargetObjectId : 0,
            TotalCastTime = battleChara?.TotalCastTime ?? 0,
            CurrentCastTime = battleChara?.CurrentCastTime ?? 0,
            IsInCombat = (chara.StatusFlags & StatusFlags.InCombat) != 0,
            IsTargetable = chara.IsTargetable,
            TargetEntityId = ResolveTargetEntityId(chara.TargetObjectId),
            IsDead = chara.IsDead,
            Mode = native != null ? (byte)native->Mode : (byte)0,
            ModeParam = native != null ? native->ModeParam : (byte)0,
            EmoteId = native != null ? native->EmoteController.EmoteId : (ushort)0,
            OnlineStatusId = chara.OnlineStatus.RowId,
            Position = chara.Position,
            Rotation = chara.Rotation,
            CapturedAt = now,
        };
    }

    public static uint LocalEntityId()
        => NoireService.ObjectTable.LocalPlayer?.EntityId ?? 0;
}
