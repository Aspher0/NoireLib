using FFXIVClientStructs.FFXIV.Client.Game.Character;
using NoireLib.Animations.Helpers;
using Xunit;
using static FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteController;

namespace NoireLib.Tests;

/// <summary>
/// Pins the idle-pose game data down: which pap(s) each (stance, index) pair is served from, when a reported
/// stance may be trusted against the character's mode reading, and the one pose file whose declared animation
/// names need filtering. All three come from the W2-A2 report's probe-verified tables.
/// </summary>
public class IdlePoseDataTests
{
    // ----------------------------------------------------------------------------------------------------
    // IdlePosePathsFor: the (pose type, pose index) -> pap key mapping, verbatim from the W2-A2
    // report's probe-verified tables. Index 0 of every stance is a differently-named base pap; the doze
    // alternates are crossed (live sheet row 99 -> l_pose02, and EmoteData observes row 99 as bed Pose1).
    // ----------------------------------------------------------------------------------------------------

    [Fact]
    public void IdlePosePathsFor_StandingIndexZero_IsTheResidentIdleWithNoStart()
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.Idle, 0);

        Assert.Equal(new IdlePosePaths("bt_common/resident/idle.pap", null), paths);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void IdlePosePathsFor_StandingAlternates_ArePoseNNLoopWithStartCompanion(byte index)
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.Idle, index);

        Assert.Equal(
            new IdlePosePaths($"bt_common/emote/pose0{index}_loop.pap", $"bt_common/emote/pose0{index}_start.pap"),
            paths);
    }

    [Fact]
    public void IdlePosePathsFor_ChairSitIndexZero_IsSitWithTheEventBaseChairStart()
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.Sit, 0);

        Assert.Equal(
            new IdlePosePaths("bt_common/emote/sit.pap", "bt_common/event_base/event_base_chair_start.pap"),
            paths);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void IdlePosePathsFor_ChairSitAlternates_AreSPoseNNLoopWithStartCompanion(byte index)
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.Sit, index);

        Assert.Equal(
            new IdlePosePaths($"bt_common/emote/s_pose0{index}_loop.pap", $"bt_common/emote/s_pose0{index}_start.pap"),
            paths);
    }

    [Fact]
    public void IdlePosePathsFor_GroundSitIndexZero_IsJmnWithTheEventBaseGroundStart()
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.GroundSit, 0);

        Assert.Equal(
            new IdlePosePaths("bt_common/emote/jmn.pap", "bt_common/event_base/event_base_ground_start.pap"),
            paths);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IdlePosePathsFor_GroundSitAlternates_AreJPoseNNLoopWithStartCompanion(byte index)
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.GroundSit, index);

        Assert.Equal(
            new IdlePosePaths($"bt_common/emote/j_pose0{index}_loop.pap", $"bt_common/emote/j_pose0{index}_start.pap"),
            paths);
    }

    [Fact]
    public void IdlePosePathsFor_DozeIndexZero_IsBedLiedown()
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.Doze, 0);

        Assert.Equal(
            new IdlePosePaths("bt_common/emote/bed_liedown_loop.pap", "bt_common/emote/bed_liedown_start.pap"),
            paths);
    }

    /// <summary> The doze crossing: index 1 serves l_pose02 and index 2 serves l_pose01 (see the mapping's doc comment; probe P1 pending). </summary>
    [Theory]
    [InlineData(1, "l_pose02")]
    [InlineData(2, "l_pose01")]
    public void IdlePosePathsFor_DozeAlternates_AreCrossed(byte index, string key)
    {
        var paths = IdlePoseData.IdlePosePathsFor(PoseType.Doze, index);

        Assert.Equal(
            new IdlePosePaths($"bt_common/emote/{key}_loop.pap", $"bt_common/emote/{key}_start.pap"),
            paths);
    }

    /// <summary> Out-of-range indexes fail rather than guess: each stance stops at its own probed maximum. </summary>
    [Theory]
    [InlineData(PoseType.Idle, (byte)7)]
    [InlineData(PoseType.Sit, (byte)5)]
    [InlineData(PoseType.GroundSit, (byte)4)]
    [InlineData(PoseType.Doze, (byte)3)]
    public void IdlePosePathsFor_IndexPastTheStanceMaximum_ReturnsNull(PoseType poseType, byte index)
        => Assert.Null(IdlePoseData.IdlePosePathsFor(poseType, index));

    /// <summary>
    /// Weapon-drawn poses are per-job (LoadType 1) and umbrella/accessory poses live under ornament_sp -
    /// neither is a shared bt_common pap, so those stances are unmappable at any index.
    /// </summary>
    [Theory]
    [InlineData(PoseType.WeaponDrawn, (byte)0)]
    [InlineData(PoseType.WeaponDrawn, (byte)1)]
    [InlineData(PoseType.Umbrella, (byte)0)]
    [InlineData(PoseType.Umbrella, (byte)1)]
    [InlineData(PoseType.Accessory, (byte)0)]
    [InlineData(PoseType.Accessory, (byte)1)]
    public void IdlePosePathsFor_UnmappableStances_ReturnNull(PoseType poseType, byte index)
        => Assert.Null(IdlePoseData.IdlePosePathsFor(poseType, index));

    // ----------------------------------------------------------------------------------------------------
    // StanceMatchesMode: the reported pose must agree with the mode reading a caller already trusts
    // before any redirect is built off it - the pose fields' runtime semantics are compile-verified only.
    // ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PoseType.Idle, CharacterModes.Normal, (byte)0, true)]
    [InlineData(PoseType.Idle, CharacterModes.EmoteLoop, (byte)0, false)] // an emote loop is not plain standing
    [InlineData(PoseType.Idle, CharacterModes.InPositionLoop, (byte)3, false)] // dozing also reads as standing from the mode; must not pass as Idle
    [InlineData(PoseType.Sit, CharacterModes.EmoteLoop, (byte)2, true)]
    [InlineData(PoseType.Sit, CharacterModes.InPositionLoop, (byte)2, true)]
    [InlineData(PoseType.Sit, CharacterModes.EmoteLoop, (byte)1, false)] // param says ground, pose says chair
    [InlineData(PoseType.Sit, CharacterModes.Normal, (byte)2, false)]
    [InlineData(PoseType.GroundSit, CharacterModes.EmoteLoop, (byte)1, true)]
    [InlineData(PoseType.GroundSit, CharacterModes.EmoteLoop, (byte)2, false)]
    [InlineData(PoseType.Doze, CharacterModes.EmoteLoop, (byte)3, true)]
    [InlineData(PoseType.Doze, CharacterModes.InPositionLoop, (byte)3, true)]
    [InlineData(PoseType.Doze, CharacterModes.EmoteLoop, (byte)2, false)]
    [InlineData(PoseType.WeaponDrawn, CharacterModes.Normal, (byte)0, false)] // unmappable stances never match
    public void StanceMatchesMode_Matrix(PoseType poseType, CharacterModes mode, byte modeParam, bool expected)
        => Assert.Equal(expected, IdlePoseData.StanceMatchesMode(poseType, mode, modeParam));

    /// <summary> Mounted trumps everything: whatever the pose fields claim, a mounted character's pose paps are not what is on screen. </summary>
    [Theory]
    [InlineData(PoseType.Idle, CharacterModes.Mounted)]
    [InlineData(PoseType.Sit, CharacterModes.Mounted)]
    [InlineData(PoseType.Idle, CharacterModes.RidingPillion)]
    public void StanceMatchesMode_Mounted_NeverMatches(PoseType poseType, CharacterModes mode)
        => Assert.False(IdlePoseData.StanceMatchesMode(poseType, mode, 0));

    // ----------------------------------------------------------------------------------------------------
    // IdlePoseRequiredNames: resident/idle.pap declares [cbna_add_dmg_f, cbnm_id0] and a full-list
    // retarget would rename the source onto the FLINCH; only the idle name may survive. Everything else
    // passes through untouched.
    // ----------------------------------------------------------------------------------------------------

    [Fact]
    public void IdlePoseRequiredNames_ResidentIdle_KeepsOnlyTheIdleAnimation()
    {
        var names = IdlePoseData.IdlePoseRequiredNames(
            IdlePoseData.ResidentIdleRelativePapPath, ["cbna_add_dmg_f", "cbnm_id0"]);

        Assert.Equal(new[] { "cbnm_id0" }, names);
    }

    /// <summary> A resident idle without the idle name at all (a reshaped future file) yields empty, which fails the attempt instead of guessing. </summary>
    [Fact]
    public void IdlePoseRequiredNames_ResidentIdleWithoutTheIdleName_ReturnsEmpty()
    {
        var names = IdlePoseData.IdlePoseRequiredNames(
            IdlePoseData.ResidentIdleRelativePapPath, ["cbna_add_dmg_f"]);

        Assert.Empty(names);
    }

    [Fact]
    public void IdlePoseRequiredNames_AnyOtherPosePap_PassesThroughUnchanged()
    {
        var declared = new[] { "cbem_pose01_2lp" };

        var names = IdlePoseData.IdlePoseRequiredNames("bt_common/emote/pose01_loop.pap", declared);

        Assert.Same(declared, names);
    }
}
