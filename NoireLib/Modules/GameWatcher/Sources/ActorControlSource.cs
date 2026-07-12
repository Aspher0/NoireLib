using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Hooking;
using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// Hooks the ActorControl packet handler — the packet family behind otherwise-invisible facts:
/// one-shot emote plays with exact emote ids for any character, and authoritative cast interrupts
/// (fed to the Characters source so interrupt-vs-complete stops being poll-inferred).<br/>
/// Unmodeled categories stay reachable through <see cref="RawActorControlEvent"/> (tier 5, advanced).<br/>
/// The detour signature is reverse-engineered territory: when it breaks after a game patch, source isolation
/// keeps everything else alive (looping-emote coverage via mode polling included) while the hook is repaired.
/// </summary>
internal sealed class ActorControlSource : GameWatcherSource
{
    private delegate void ProcessActorControlDelegate(
        uint entityId, uint category, uint arg0, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, ulong targetId, byte flag);

    /// <summary>The widely used ProcessPacketActorControl signature (validated in-game per the phase-3 gate).</summary>
    private const string ActorControlSignature = "E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64";

    private const uint CategoryCancelCast = 0x0F;
    private const uint CategoryEmote = 0x1F;

    private HookWrapper<ProcessActorControlDelegate>? hook;

    public ActorControlSource(NoireGameWatcher owner) : base(owner, SourceKind.ActorControl) { }

    /// <inheritdoc/>
    public override bool IsPolling => false;

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        hook ??= new HookWrapper<ProcessActorControlDelegate>(ActorControlSignature, OnActorControlDetour, name: "NoireGameWatcher.ActorControl");
        hook.Enable();
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        hook?.Disable();
    }

    /// <inheritdoc/>
    public override void DisposeSource()
    {
        hook?.Dispose();
        hook = null;
    }

    private void OnActorControlDetour(
        uint entityId, uint category, uint arg0, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, ulong targetId, byte flag)
    {
        hook!.Original(entityId, category, arg0, arg1, arg2, arg3, arg4, arg5, targetId, flag);

        try
        {
            ProcessActorControl(entityId, category, arg0, arg1, arg2, arg3, arg4, arg5, targetId);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, "Failed to process an ActorControl packet.");
        }
    }

    private void ProcessActorControl(
        uint entityId, uint category, uint arg0, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, ulong targetId)
    {
        Owner.DispatchEvent(new RawActorControlEvent(entityId, category, arg0, arg1, arg2, arg3, arg4, arg5, targetId));

        switch (category)
        {
            case CategoryEmote:
                EmitEmotePlayed(entityId, arg0);
                break;

            case CategoryCancelCast:
            {
                // Authoritative interrupt: the Characters source consumes this on the tick the cast disappears.
                var characterSource = Owner.GetSource<CharacterSource>(SourceKind.Characters);

                if (characterSource.IsRunning)
                    characterSource.PendingCastInterrupts.Add(entityId);

                break;
            }
        }
    }

    private void EmitEmotePlayed(uint entityId, uint emoteId)
    {
        var chara = FindCharacter(entityId);

        if (chara == null)
            return;

        var flags = CharacterCapture.ReadFlags(chara, CharacterCapture.LocalEntityId());
        var snapshot = CharacterCapture.Capture(chara, flags, DateTimeOffset.UtcNow);

        Owner.DispatchEvent(new CharacterEmotePlayedEvent(snapshot, emoteId));
    }

    private static ICharacter? FindCharacter(uint entityId)
    {
        foreach (var obj in NoireService.ObjectTable)
        {
            if (obj.EntityId == entityId && obj is ICharacter chara)
                return chara;
        }

        return null;
    }
}
