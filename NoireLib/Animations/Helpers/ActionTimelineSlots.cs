using FFXIVClientStructs.FFXIV.Client.Game.Character;
using NoireLib.Enums;
using System;
using System.Collections.Generic;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// Which body an animation drives, as opposed to <see cref="EmoteCondition"/>, which says which states the Emote
/// sheet permits an emote in. <see cref="Mounted"/> is the upper-body additive channel, so it also covers riding
/// pillion, swimming and diving.
/// </summary>
[Flags]
public enum PostureFlags
{
    /// <summary> No posture: an intro, a facial or an adjust slot. </summary>
    None = 0,

    /// <summary> Full-body standing. </summary>
    Standing = 1,

    /// <summary> Seated in a chair. </summary>
    ChairSit = 2,

    /// <summary> Seated on the ground. </summary>
    GroundSit = 4,

    /// <summary> The upper-body additive channel: mounted, riding pillion, swimming and diving. </summary>
    Mounted = 8,
}

/// <summary>
/// What each of an Emote row's seven unnamed ActionTimeline slots is for, and which of them can serve a
/// character in a given state.
/// </summary>
public static class ActionTimelineSlots
{
    /// <summary> How many ActionTimeline slots an Emote row carries. </summary>
    public const int SlotCount = 7;

    /// <summary> Full-body standing animation. </summary>
    public const int Standing = 0;

    /// <summary> The windup played before a looping animation, belonging to the standing channel. </summary>
    public const int Intro = 1;

    /// <summary> Seated on the ground; the j_ paps. </summary>
    public const int GroundSit = 2;

    /// <summary> Seated in a chair; the s_ paps. </summary>
    public const int ChairSit = 3;

    /// <summary> Upper-body additive (the u_ and add_ paps), played while mounted, swimming or diving. </summary>
    public const int UpperBody = 4;

    /// <summary> Facial animation only. </summary>
    public const int Facial = 5;

    /// <summary> The emote_adjust channel. </summary>
    public const int Adjust = 6;

    private static readonly int[] StandingSlots = [Standing];
    private static readonly int[] GroundSitSlots = [GroundSit, UpperBody];
    private static readonly int[] ChairSitSlots = [ChairSit, UpperBody];
    private static readonly int[] UpperBodySlots = [UpperBody];
    private static readonly int[] FacialSlots = [Facial];

    /// <summary>
    /// The posture an animation in a slot drives.
    /// </summary>
    /// <param name="slotIndex">The slot index within the Emote row's ActionTimeline array.</param>
    /// <returns>The posture, or <see cref="PostureFlags.None"/> for slots 1, 5, 6 and anything outside 0 to 6.</returns>
    public static PostureFlags PostureForSlot(int slotIndex) => slotIndex switch
    {
        Standing => PostureFlags.Standing,
        GroundSit => PostureFlags.GroundSit,
        ChairSit => PostureFlags.ChairSit,
        UpperBody => PostureFlags.Mounted,
        _ => PostureFlags.None,
    };

    /// <summary>
    /// The state an emote effectively plays in, given that the game stows an umbrella or a torch first.
    /// </summary>
    /// <param name="condition">The state the character is actually in.</param>
    /// <returns>The state to resolve slots against.</returns>
    public static EmoteCondition PlayableAsFor(EmoteCondition condition)
        => condition is EmoteCondition.HoldingUmbrella or EmoteCondition.HoldingTorch
            ? EmoteCondition.Standing
            : condition;

    /// <summary>
    /// The slots that can serve a character in a state, best first.
    /// </summary>
    /// <param name="condition">The state to serve, already passed through <see cref="PlayableAsFor"/>.</param>
    /// <returns>
    /// The slot indices in preference order; a seated state falls back to the upper-body channel, and fishing
    /// leaves only the facial one.
    /// </returns>
    public static IReadOnlyList<int> SlotPreferenceFor(EmoteCondition condition) => condition switch
    {
        EmoteCondition.SittingOnGround => GroundSitSlots,
        EmoteCondition.SittingInChair => ChairSitSlots,
        EmoteCondition.Mounted or EmoteCondition.Swimming or EmoteCondition.Diving => UpperBodySlots,
        EmoteCondition.Fishing => FacialSlots,
        _ => StandingSlots,
    };

    /// <summary>
    /// The posture the channel serving a state drives, which is <see cref="SlotPreferenceFor"/> read through
    /// <see cref="PostureForSlot"/>.
    /// </summary>
    /// <param name="condition">The state the character is in.</param>
    /// <returns>The posture driven.</returns>
    public static PostureFlags PostureForCondition(EmoteCondition condition) => condition switch
    {
        EmoteCondition.SittingOnGround => PostureFlags.GroundSit,
        EmoteCondition.SittingInChair => PostureFlags.ChairSit,
        EmoteCondition.Mounted or EmoteCondition.Swimming or EmoteCondition.Diving => PostureFlags.Mounted,
        _ => PostureFlags.Standing,
    };

    /// <summary>
    /// The posture the channel serving a character's current mode drives.
    /// </summary>
    /// <remarks>
    /// Not the same answer as <see cref="IdlePoseData.StanceFromMode"/>: a dozing character reads as standing here.
    /// </remarks>
    /// <param name="mode">The character's Mode reading.</param>
    /// <param name="modeParam">The character's ModeParam reading: 1 ground sit, 2 chair sit.</param>
    /// <returns>The posture driven.</returns>
    public static PostureFlags PostureForMode(CharacterModes mode, byte modeParam)
    {
        if (mode is CharacterModes.Mounted or CharacterModes.RidingPillion)
            return PostureFlags.Mounted;

        if (mode is not (CharacterModes.EmoteLoop or CharacterModes.InPositionLoop))
            return PostureFlags.Standing;

        return modeParam switch
        {
            1 => PostureFlags.GroundSit,
            2 => PostureFlags.ChairSit,
            _ => PostureFlags.Standing,
        };
    }
}
