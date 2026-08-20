using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// The pap or paps one (stance, index) pair is served from, skeleton-relative in the form
/// <see cref="EmotePathHelper.GetSkeletonPath"/> turns into a full game path.
/// </summary>
/// <param name="LoopRelativePapPath">The pose's looping pap.</param>
/// <param name="StartRelativePapPath">The pap played on entering the pose, or null when the stance has none.</param>
public sealed record IdlePosePaths(string LoopRelativePapPath, string? StartRelativePapPath);

/// <summary>
/// Static tables naming the paps each idle stance and pose index is served from, plus the pap-name rule that
/// goes with the standing base. Nothing here reads or writes game state.
/// </summary>
public static class IdlePoseData
{
    /// <summary> The standing cpose-0 base pap, the one pose file whose required names <see cref="IdlePoseRequiredNames"/> filters. </summary>
    public const string ResidentIdleRelativePapPath = "bt_common/resident/idle.pap";

    /// <summary> The idle animation inside <see cref="ResidentIdleRelativePapPath"/>, whose other declared name is an additive damage flinch. </summary>
    private const string ResidentIdleAnimationName = "cbnm_id0";

    /// <summary>
    /// Maps a (pose type, pose index) pair to the pap or paps that pose is served from, or null when the pose
    /// cannot be redirected: an index past the stance's maximum, or a stance whose paps are not shared
    /// bt_common files (weapon-drawn poses are per-job, umbrella and accessory poses live under ornament_sp).
    /// </summary>
    /// <param name="poseType">The stance the character is in, as the EmoteController reports it.</param>
    /// <param name="poseIndex">The cpose index within that stance, 0 being the stance's base pose.</param>
    /// <returns>The paps, or null when the pose cannot be redirected.</returns>
    public static IdlePosePaths? IdlePosePathsFor(EmoteController.PoseType poseType, byte poseIndex) => poseType switch
    {
        EmoteController.PoseType.Idle => poseIndex switch
        {
            0 => new IdlePosePaths(ResidentIdleRelativePapPath, null),
            <= 6 => new IdlePosePaths($"bt_common/emote/pose{poseIndex:D2}_loop.pap", $"bt_common/emote/pose{poseIndex:D2}_start.pap"),
            _ => null,
        },
        EmoteController.PoseType.Sit => poseIndex switch
        {
            0 => new IdlePosePaths("bt_common/emote/sit.pap", "bt_common/event_base/event_base_chair_start.pap"),
            <= 4 => new IdlePosePaths($"bt_common/emote/s_pose{poseIndex:D2}_loop.pap", $"bt_common/emote/s_pose{poseIndex:D2}_start.pap"),
            _ => null,
        },
        EmoteController.PoseType.GroundSit => poseIndex switch
        {
            0 => new IdlePosePaths("bt_common/emote/jmn.pap", "bt_common/event_base/event_base_ground_start.pap"),
            <= 3 => new IdlePosePaths($"bt_common/emote/j_pose{poseIndex:D2}_loop.pap", $"bt_common/emote/j_pose{poseIndex:D2}_start.pap"),
            _ => null,
        },
        // The doze alternates are crossed: the sheet maps Emote row 99 to l_pose02 and row 100 to l_pose01.
        EmoteController.PoseType.Doze => poseIndex switch
        {
            0 => new IdlePosePaths("bt_common/emote/bed_liedown_loop.pap", "bt_common/emote/bed_liedown_start.pap"),
            1 => new IdlePosePaths("bt_common/emote/l_pose02_loop.pap", "bt_common/emote/l_pose02_start.pap"),
            2 => new IdlePosePaths("bt_common/emote/l_pose01_loop.pap", "bt_common/emote/l_pose01_start.pap"),
            _ => null,
        },
        _ => null,
    };

    /// <summary>
    /// Reports whether the stance the EmoteController names agrees with the one the character's mode implies.
    /// </summary>
    /// <param name="poseType">The stance the character is in, as the EmoteController reports it.</param>
    /// <param name="mode">The character's Mode reading.</param>
    /// <param name="modeParam">The character's ModeParam reading, which tells the seated stances apart.</param>
    /// <returns>True when the two agree.</returns>
    public static bool StanceMatchesMode(EmoteController.PoseType poseType, CharacterModes mode, byte modeParam)
        => StanceFromMode(mode, modeParam) == poseType;

    /// <summary>
    /// Resolves the stance a character is in from their mode, which EmoteController's CurrentPoseType and
    /// CPoseState cannot answer since both are only written when a pose is cycled.
    /// </summary>
    /// <param name="mode">The character's Mode reading.</param>
    /// <param name="modeParam">The character's ModeParam reading: 1 ground sit, 2 chair sit, 3 doze.</param>
    /// <returns>The stance, or null when mounted, riding pillion, or in an emote loop rather than a pose.</returns>
    public static EmoteController.PoseType? StanceFromMode(CharacterModes mode, byte modeParam)
    {
        if (mode is CharacterModes.Mounted or CharacterModes.RidingPillion)
            return null;

        if (mode is not (CharacterModes.EmoteLoop or CharacterModes.InPositionLoop))
            return EmoteController.PoseType.Idle;

        return modeParam switch
        {
            1 => EmoteController.PoseType.GroundSit,
            2 => EmoteController.PoseType.Sit,
            3 => EmoteController.PoseType.Doze,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves the pose index that applies to a stance, the reported index counting only for the stance it
    /// was reported in since the game leaves it behind when a stance changes without a cpose.
    /// </summary>
    /// <param name="stance">The stance actually in effect, as <see cref="StanceFromMode"/> resolves it.</param>
    /// <param name="reportedStance">The stance EmoteController's CurrentPoseType names.</param>
    /// <param name="reportedIndex">The index EmoteController's CPoseState names.</param>
    /// <returns>The reported index when the two stances agree, otherwise 0.</returns>
    public static byte PoseIndexFor(
        EmoteController.PoseType stance, EmoteController.PoseType reportedStance, byte reportedIndex)
        => reportedStance == stance ? reportedIndex : (byte)0;

    /// <summary>
    /// The Emote rows that are pose-cycle members rather than emotes of their own, mapped to the stance and
    /// index they select. No sheet column marks them, so the mapping is fixed here.
    /// </summary>
    private static readonly Dictionary<uint, (EmoteController.PoseType PoseType, byte Index)> PoseFamilyRows = new()
    {
        // Chair sit
        [95] = (EmoteController.PoseType.Sit, 1),
        [96] = (EmoteController.PoseType.Sit, 2),
        [254] = (EmoteController.PoseType.Sit, 3),
        [255] = (EmoteController.PoseType.Sit, 4),

        // Ground sit
        [97] = (EmoteController.PoseType.GroundSit, 1),
        [98] = (EmoteController.PoseType.GroundSit, 2),
        [117] = (EmoteController.PoseType.GroundSit, 3),

        // Standing change pose
        [91] = (EmoteController.PoseType.Idle, 1),
        [92] = (EmoteController.PoseType.Idle, 2),
        [107] = (EmoteController.PoseType.Idle, 3),
        [108] = (EmoteController.PoseType.Idle, 4),
        [218] = (EmoteController.PoseType.Idle, 5),
        [219] = (EmoteController.PoseType.Idle, 6),

        // Doze
        [99] = (EmoteController.PoseType.Doze, 1),
        [100] = (EmoteController.PoseType.Doze, 2),

        // Umbrella and drawn weapon: real cycle members, but neither stance has redirectable paps.
        [243] = (EmoteController.PoseType.Umbrella, 1),
        [244] = (EmoteController.PoseType.Umbrella, 2),
        [253] = (EmoteController.PoseType.Umbrella, 3),
        [93] = (EmoteController.PoseType.WeaponDrawn, 1),
    };

    /// <summary>
    /// Resolves the stance and index an Emote row selects when that row is a pose-cycle member.
    /// </summary>
    /// <param name="emoteRowId">The Emote sheet row id.</param>
    /// <returns>The stance and index, or null when the row is an ordinary emote.</returns>
    public static (EmoteController.PoseType PoseType, byte Index)? PoseFamilyFor(uint emoteRowId)
        => PoseFamilyRows.TryGetValue(emoteRowId, out var entry) ? entry : null;

    /// <summary> Reports whether an Emote row is a pose-cycle member rather than an emote of its own. </summary>
    /// <param name="emoteRowId">The Emote sheet row id.</param>
    /// <returns>True when the row is a pose-cycle member.</returns>
    public static bool IsPoseFamilyRow(uint emoteRowId) => PoseFamilyRows.ContainsKey(emoteRowId);

    /// <summary> Gets every Emote row that is a pose-cycle member. </summary>
    public static IReadOnlyCollection<uint> PoseFamilyRowIds => PoseFamilyRows.Keys;

    /// <summary>
    /// Filters the animation names an idle-pose retarget must produce for one pose target, dropping the
    /// additive damage flinch that <see cref="ResidentIdleRelativePapPath"/> declares ahead of its idle so an
    /// in-order fill cannot rename a one-animation source onto the flinch.
    /// </summary>
    /// <param name="targetRelativePapPath">The pose pap being retargeted, skeleton-relative.</param>
    /// <param name="declaredNames">The animation names that pap's vanilla bytes declare, in order.</param>
    /// <returns>The names to produce, which is every declared name for any other pose pap.</returns>
    public static IReadOnlyList<string> IdlePoseRequiredNames(string targetRelativePapPath, IReadOnlyList<string> declaredNames)
        => targetRelativePapPath == ResidentIdleRelativePapPath
            ? declaredNames.Where(name => name == ResidentIdleAnimationName).ToList()
            : declaredNames;
}
