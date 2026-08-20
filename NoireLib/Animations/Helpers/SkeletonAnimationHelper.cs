using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using NoireLib.Helpers;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// Reads and adjusts the havok animation a character's drawn model is playing. Every member walks the draw
/// object, so every member is a no-op on a character with no drawn human model.
/// </summary>
public static unsafe class SkeletonAnimationHelper
{
    /// <summary>
    /// Gets the human model a character is drawn from, checking that the draw object exists and is a human
    /// character base.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <param name="human">The drawn human model, when there is one.</param>
    /// <returns>True when <paramref name="human"/> is usable.</returns>
    public static bool TryGetHuman(ICharacter character, out Human* human)
    {
        human = null;

        if (character == null || character.Address == 0)
            return false;

        var native = CharacterHelper.GetCharacterAddress(character);
        if (native == null || native->DrawObject == null)
            return false;

        if (native->DrawObject->GetObjectType() != ObjectType.CharacterBase)
            return false;

        var drawn = (CharacterBase*)native->DrawObject;
        if (drawn->GetModelType() != CharacterBase.ModelType.Human)
            return false;

        human = (Human*)drawn;
        return true;
    }

    /// <summary>
    /// Rewinds the character's body animation to its first frame without asking the game to play anything.
    /// </summary>
    /// <param name="character">The character to rewind.</param>
    /// <returns>True when a control was found and rewound.</returns>
    public static bool ResetAnimationTime(ICharacter character)
        => SetAnimationTime(character, 0f);

    /// <summary>
    /// Sets the local time of the character's body animation control, the first control of the first partial
    /// skeleton, leaving the face and additive layers alone.
    /// </summary>
    /// <param name="character">The character to scrub.</param>
    /// <param name="localTime">The time to set, in seconds from the start of the animation.</param>
    /// <returns>True when a control was found and set.</returns>
    public static bool SetAnimationTime(ICharacter character, float localTime)
    {
        if (!TryGetHuman(character, out var human))
            return false;

        var skeleton = human->Skeleton;
        if (skeleton == null || skeleton->PartialSkeletonCount < 1)
            return false;

        var animated = skeleton->PartialSkeletons[0].GetHavokAnimatedSkeleton(0);
        if (animated == null || animated->AnimationControls.Length < 1)
            return false;

        var control = animated->AnimationControls[0].Value;
        if (control == null)
            return false;

        control->hkaAnimationControl.LocalTime = localTime;
        return true;
    }
}
