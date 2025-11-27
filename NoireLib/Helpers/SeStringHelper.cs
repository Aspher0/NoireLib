using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using NoireLib.Models;

namespace NoireLib.Helpers;

/// <summary>
/// A class containing helper methods for working with SeString objects.
/// </summary>
public static class SeStringHelper
{
    /// <summary>
    /// Resolves the sender of a message represented by a SeString into a PlayerModel.<br/>
    /// </summary>
    /// <param name="sender">The SeString representing the sender of the message.</param>
    /// <returns>A <see cref="PlayerModel"/> if the sender could be resolved; otherwise, null.</returns>
    public static PlayerModel? ResolveSender(SeString sender)
    {
        PlayerPayload? playerPayload = null;

        foreach (var payload in sender.Payloads)
        {
            if (payload is PlayerPayload pp)
            {
                playerPayload = pp;
                break;
            }
        }

        if (playerPayload != null)
        {
            var playerName = playerPayload.PlayerName;
            var worldName = playerPayload.World.Value.Name.ExtractText();
            var worldId = playerPayload.World.Value.RowId;

            if (!playerName.IsNullOrEmpty() && !worldName.IsNullOrEmpty())
                return new(playerName, worldName, worldId);
        }

        var senderText = sender.TextValue;
        if (senderText.IsNullOrEmpty() || NoireService.ObjectTable.LocalPlayer == null)
            return null;

        var localPlayerName = NoireService.ObjectTable.LocalPlayer.Name.TextValue;
        if (senderText.Contains(localPlayerName))
        {
            var worldName = NoireService.ObjectTable.LocalPlayer.HomeWorld.Value.Name.ExtractText();
            var worldId = NoireService.ObjectTable.LocalPlayer.HomeWorld.Value.RowId;
            if (!worldName.IsNullOrEmpty())
                return new(localPlayerName, worldName, worldId);
        }

        return null;
    }
}
