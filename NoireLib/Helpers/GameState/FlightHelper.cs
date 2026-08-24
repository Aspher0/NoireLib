using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace NoireLib.Helpers;

/// <summary>
/// Answers whether the character can fly from the client.
/// </summary>
public static class FlightHelper
{
    /// <summary>
    /// Whether flight is available in the zone the character is standing in, as the server stated when the zone
    /// loaded. False with no character loaded, and false while a zone is still loading.
    /// </summary>
    public static unsafe bool CanFlyHere
    {
        get
        {
            if (!CharacterHelper.IsStateReady)
                return false;

            var state = PlayerState.Instance();
            return state != null && state->CanFly;
        }
    }

    /// <summary>
    /// Whether the character could take off this instant, which additionally asks that they are mounted on a mount
    /// that flies. Use <see cref="CanFlyHere"/> to ask about the zone rather than about this moment.
    /// </summary>
    public static unsafe bool CanTakeOffNow
        => CharacterHelper.IsPlayerLoaded && Control.GetFlightAllowedStatus() == Control.FlightAllowedStatus.CanFly;

    /// <summary>
    /// Why the character can or cannot take off right now, being the game's own answer: whether they are mounted,
    /// whether the mount flies, and whether the zone allows it.
    /// </summary>
    public static unsafe Control.FlightAllowedStatus TakeOffStatus
        => CharacterHelper.IsPlayerLoaded ? Control.GetFlightAllowedStatus() : Control.FlightAllowedStatus.PlayerOrMountNull;
}
