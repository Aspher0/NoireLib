using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NoireLib.Animations.Timelines;

/// <summary>
/// Puts a character into an arbitrary animation channel, for any <see cref="ICharacter"/> and outside GPose.
/// Adapted from Brio.
/// A low-level channel primitive: a base override set on a character the game is already animating is never taken
/// back and glide-locks them, so emotes go through <see cref="EmoteHelper.ExecuteEmote"/> instead. The caller owns
/// restoring the character with <see cref="ResetBase"/>.
/// </summary>
public sealed class ActionTimelineDriver : IDisposable
{
    /// <summary> The size the sequencer expects its parameter block to be. </summary>
    private const int AnimParamsSize = 0x60;

    /// <summary> The neutral standing timeline, played to settle a character after an override is lifted. </summary>
    private const ushort IdleTimelineId = 3;

    /// <summary> The one timeline the game flags differently in the parameter block. </summary>
    private const ushort SpeciallyFlaggedTimelineId = 3123;

    /// <summary> Priority value meaning "use whatever the animation itself asks for". </summary>
    public const int DefaultPriority = -1;

    private readonly Dictionary<nint, ushort> originalOverrides = [];

    /// <summary> Forgets every remembered override without restoring any of them. </summary>
    public void Dispose() => originalOverrides.Clear();

    /// <summary>
    /// Puts the character's base animation channel onto a timeline, remembering what was there so
    /// <see cref="ResetBase"/> can undo it.
    /// </summary>
    /// <param name="character">The character to drive.</param>
    /// <param name="actionTimelineId">The ActionTimeline row to hold.</param>
    /// <param name="captureBase">
    /// Whether to remember the current override, false when the caller already owns it and a second capture would
    /// record the caller's own value as the original.
    /// </param>
    /// <param name="interrupt">Whether to blend into the timeline immediately rather than waiting for the next transition.</param>
    /// <param name="targetId">The target the animation is aimed at, or <see cref="EmoteHelper.NoEmoteTargetId"/> for none.</param>
    public unsafe void Play(
        ICharacter character, ushort actionTimelineId, bool captureBase = true, bool interrupt = true,
        ulong targetId = EmoteHelper.NoEmoteTargetId)
    {
        if (character == null || character.Address == 0)
            return;

        var native = (Character*)character.Address;

        if (captureBase && !originalOverrides.ContainsKey(character.Address))
            originalOverrides[character.Address] = native->Timeline.BaseOverride;

        native->Timeline.BaseOverride = actionTimelineId;

        if (interrupt)
            Blend(character, actionTimelineId, targetId: targetId);
    }

    /// <summary>
    /// Blends a character into a timeline without touching their base override, so the game takes the animation back
    /// once it ends.
    /// </summary>
    /// <param name="character">The character to drive.</param>
    /// <param name="actionTimelineId">The ActionTimeline row to blend into.</param>
    /// <param name="priority">The animation channel, 0 to 7, or <see cref="DefaultPriority"/>.</param>
    /// <param name="targetId">The target the animation is aimed at, or <see cref="EmoteHelper.NoEmoteTargetId"/> for none.</param>
    /// <param name="collapseFade">Whether to collapse the transition into a single frame instead of fading.</param>
    public unsafe void Blend(
        ICharacter character, ushort actionTimelineId, int priority = DefaultPriority,
        ulong targetId = EmoteHelper.NoEmoteTargetId, bool collapseFade = false)
    {
        if (character == null || character.Address == 0)
            return;

        var native = (Character*)character.Address;

        var animParams = (ActionTimelineAnimParams*)MemoryHelper.Allocate(AnimParamsSize);

        try
        {
            Unsafe.InitBlockUnaligned(animParams, 0, AnimParamsSize);

            animParams->Intensity = 1.0f;
            animParams->StartTimestamp = 0.0f;
            animParams->Unk1C = -1.0f;
            animParams->TargetObjectId = targetId;
            animParams->Priority = (uint)priority;
            animParams->Unk38 = -1;
            animParams->Unk3C = actionTimelineId == SpeciallyFlaggedTimelineId ? (byte)0 : (byte)0xFF;
            animParams->OverridesBlendDuration = collapseFade ? (byte)1 : (byte)0;

            native->Timeline.TimelineSequencer.PlayTimeline(actionTimelineId, animParams);
        }
        finally
        {
            MemoryHelper.Free((nint)animParams);
        }
    }

    /// <summary>
    /// Puts the character's base override back to whatever it was before <see cref="Play"/> took it, and
    /// settles them into the neutral standing timeline.
    /// </summary>
    /// <param name="character">The character to restore.</param>
    /// <returns>True when there was an override to restore.</returns>
    public unsafe bool ResetBase(ICharacter character)
    {
        if (character == null || character.Address == 0)
            return false;

        if (!originalOverrides.Remove(character.Address, out var original))
            return false;

        ((Character*)character.Address)->Timeline.BaseOverride = original;

        Blend(character, IdleTimelineId);
        return true;
    }

    /// <summary> Whether this driver is currently holding a character's base override. </summary>
    /// <param name="character">The character to check.</param>
    /// <returns>True when an override is remembered for the character.</returns>
    public bool HasBaseOverride(ICharacter character)
        => character != null && originalOverrides.ContainsKey(character.Address);

    /// <summary> Forgets a character's remembered override without restoring it. </summary>
    /// <param name="characterAddress">The character's address.</param>
    /// <returns>True when something was forgotten.</returns>
    public bool Forget(nint characterAddress) => originalOverrides.Remove(characterAddress);
}
