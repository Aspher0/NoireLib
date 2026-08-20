using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using NoireLib.Helpers;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// Which pose a character is holding, read in one go: the mode pair saying what they are doing, and the
/// EmoteController pair saying what the last cpose selected. The controller's fields are written only when a pose
/// is cycled, so entering or leaving a stance any other way leaves them naming the stance of the last cycle.
/// </summary>
/// <param name="Mode">The character's Mode.</param>
/// <param name="ModeParam">The character's ModeParam: 1 ground sit, 2 chair sit, 3 doze.</param>
/// <param name="ReportedPoseType">The stance EmoteController's CurrentPoseType names.</param>
/// <param name="ReportedPoseIndex">The index EmoteController's CPoseState names.</param>
public readonly record struct CharacterPoseState(
    CharacterModes Mode,
    byte ModeParam,
    EmoteController.PoseType ReportedPoseType,
    byte ReportedPoseIndex)
{
    /// <summary>
    /// Reads a character's pose state.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The pose state, or every field at its default when there is no character to read.</returns>
    public static unsafe CharacterPoseState Read(ICharacter character)
    {
        if (character == null || character.Address == 0)
            return default;

        var native = CharacterHelper.GetCharacterAddress(character);

        if (native == null)
            return default;

        return new CharacterPoseState(
            native->Mode,
            native->ModeParam,
            native->EmoteController.CurrentPoseType,
            native->EmoteController.CPoseState);
    }

    /// <summary>
    /// The stance in effect, or null when the character is mounted or in an emote that leaves them standing.
    /// </summary>
    public EmoteController.PoseType? Stance => IdlePoseData.StanceFromMode(Mode, ModeParam);

    /// <summary>
    /// The pose index that applies, which is the reported one only when it was reported in the stance now in
    /// effect, and otherwise zero for the stance's base pose.
    /// </summary>
    public byte Index => Stance is { } stance
        ? IdlePoseData.PoseIndexFor(stance, ReportedPoseType, ReportedPoseIndex)
        : (byte)0;

    /// <summary>Whether the character is holding a pose that can be borrowed or redirected.</summary>
    public bool HasStance => Stance != null;
}
