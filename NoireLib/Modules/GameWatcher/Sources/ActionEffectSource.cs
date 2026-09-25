using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NoireLib.GameWatcher;

// Hooks the action-effect packet handler, dispatches a parsed ActionEffectEvent per packet, and keeps rolling
// statistics plus an opt-in bounded history. The hook is created on first activation and disabled, not disposed, on
// deactivation.
internal sealed class ActionEffectSource : GameWatcherSource
{
    private readonly LinkedList<ActionEffectEntry> history = new();
    private readonly object historyLock = new();
    private NoireHook<ActionEffectHandler.Delegates.Receive>? receiveHook;

    public ActionEffectSource(NoireGameWatcher owner) : base(owner, SourceKind.ActionEffect) { }

    public override bool IsPolling => false;

    // Rolling statistics over every observed action effect since the last activation.
    internal ActionEffectStatistics Statistics { get; } = new();

    protected override unsafe void OnActivate()
    {
        Statistics.Reset();
        receiveHook ??= new NoireHook<ActionEffectHandler.Delegates.Receive>(OnReceiveDetour, name: "NoireGameWatcher.ActionEffect.Receive");
        receiveHook.Enable();
    }

    protected override void OnDeactivate()
    {
        receiveHook?.Disable();
    }

    public override void DisposeSource()
    {
        receiveHook?.Dispose();
        receiveHook = null;
    }

    internal ActionEffectEntry[] GetHistory()
    {
        lock (historyLock)
            return history.ToArray();
    }

    internal void ClearHistory()
    {
        lock (historyLock)
            history.Clear();
    }

    private unsafe void OnReceiveDetour(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        receiveHook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);

        try
        {
            ProcessActionEffect(casterEntityId, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, "Failed to process an action effect packet.");
        }
    }

    private unsafe void ProcessActionEffect(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        var actionId = header->ActionId;
        var animationTargetId = header->AnimationTargetId.ObjectId;
        var targetCount = header->NumTargets;

        var targets = new List<ulong>(targetCount);
        var perTargetEffects = new List<PerTargetActionEffect>(targetCount);

        for (uint i = 0; i < targetCount; i++)
        {
            var targetId = targetEntityIds[i].ObjectId;
            targets.Add(targetId);
            perTargetEffects.Add(new PerTargetActionEffect(targetId, ParseTargetEffects(effects, i)));
        }

        var entry = new ActionEffectEntry
        {
            SourceEntityId = casterEntityId,
            ActionId = actionId,
            AnimationTargetId = animationTargetId,
            TargetEntityIds = targets.AsReadOnly(),
            PerTargetEffects = perTargetEffects.AsReadOnly(),
            ObservedAt = DateTimeOffset.UtcNow,
        };

        Statistics.Record(entry);

        var capacity = Owner.ActiveOptions.Combat.HistoryCapacity;

        if (capacity > 0)
        {
            lock (historyLock)
            {
                history.AddFirst(entry);

                while (history.Count > capacity)
                    history.RemoveLast();
            }
        }

        Owner.DispatchEvent(new ActionEffectEvent(entry));
        Owner.GetSource<CooldownSource>(SourceKind.Cooldowns).ObserveAction(entry);
    }

    private static unsafe List<ParsedActionEffect> ParseTargetEffects(ActionEffectHandler.TargetEffects* allEffects, uint targetIndex)
    {
        var parsed = new List<ParsedActionEffect>();

        // The packet carries eight packed 64-bit effect slots per target, contiguously.
        var effectsPtr = (ulong*)allEffects;

        for (var effectSlot = 0; effectSlot < 8; effectSlot++)
        {
            var raw = effectsPtr[targetIndex * 8 + effectSlot];

            var type = (byte)(raw & 0xFF);

            if (type == 0)
                continue;

            var param0 = (byte)((raw >> 8) & 0xFF);
            var param1 = (byte)((raw >> 16) & 0xFF);
            var param2 = (byte)((raw >> 24) & 0xFF);
            var flags1 = (byte)((raw >> 32) & 0xFF);
            var flags2 = (byte)((raw >> 40) & 0xFF);
            var value = (ushort)((raw >> 48) & 0xFFFF);

            uint fullValue = value;

            if ((flags2 & 0x40) != 0)
                fullValue += (uint)param2 << 16;

            var kind = Enum.IsDefined(typeof(ActionEffectKind), type) ? (ActionEffectKind)type : ActionEffectKind.Unknown;
            var isCrit = (flags1 & 0x01) != 0;
            var isDirectHit = (flags1 & 0x02) != 0;

            parsed.Add(new ParsedActionEffect(kind, fullValue, isCrit, isDirectHit, param0, param1, param2, flags1, flags2));
        }

        return parsed;
    }
}
