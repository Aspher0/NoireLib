using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.String;
using Lumina.Text.ReadOnly;
using NoireLib.Models;
using System;
using System.Text;

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
                return new(playerName, worldName, null, worldId, null);
        }

        var senderText = sender.TextValue;
        if (senderText.IsNullOrEmpty() || NoireService.ObjectTable.LocalPlayer == null)
            return null;

        var localPlayerName = NoireService.ObjectTable.LocalPlayer.Name.TextValue;
        if (senderText.Contains(localPlayerName))
        {
            var worldName = NoireService.ObjectTable.LocalPlayer.HomeWorld.Value.Name.ExtractText();
            var worldId = NoireService.ObjectTable.LocalPlayer.HomeWorld.Value.RowId;
            var currentWorldName = NoireService.ObjectTable.LocalPlayer.CurrentWorld.Value.Name.ExtractText();
            var currentWorldId = NoireService.ObjectTable.LocalPlayer.CurrentWorld.Value.RowId;
            if (!worldName.IsNullOrEmpty())
                return new(localPlayerName, worldName, currentWorldName, worldId, currentWorldId);
        }

        return null;
    }

    /// <summary>
    /// Converts a pointer to a UTF-8 encoded string structure into plain text.
    /// </summary>
    /// <param name="utf8StringPtr">A pointer to a UTF-8 encoded string structure to convert. Must not be null.</param>
    /// <returns>A string containing the plain text representation of the specified UTF-8 string.</returns>
    public static unsafe string Utf8StringPtrToPlainText(Utf8String* utf8StringPtr)
    {
        var ut8Span = GetUtf8Span(utf8StringPtr);
        var seString = SeString.Parse(ut8Span);
        return SeStringToPlainText(seString);
    }

    /// <summary>
    /// Gets the plain text representation of a <see cref="SeString"/> by concatenating the text from all TextPayloads and evaluating any AutoTranslatePayloads using the client's current language settings.
    /// </summary>
    /// <param name="seString">The <see cref="SeString"/> to convert to plain text.</param>
    /// <param name="languageOverride">An optional language override for evaluating AutoTranslatePayloads.</param>
    /// <returns>The plain text representation of the <see cref="SeString"/>.</returns>
    public static string SeStringToPlainText(SeString seString, ClientLanguage? languageOverride = null)
    {
        var sb = new StringBuilder();
        foreach (var p in seString.Payloads)
        {
            switch (p)
            {
                case TextPayload t:
                    sb.Append(t.Text);
                    break;
                case AutoTranslatePayload a:
                    sb.Append(NoireService.SeStringEvaluator.Evaluate(new ReadOnlySeString(a.Encode()), default, languageOverride ?? NoireService.ClientState.ClientLanguage).ToString());
                    break;
            }
        }
        return sb.ToString();
    }

    private static unsafe ReadOnlySpan<byte> GetUtf8Span(Utf8String* str)
        => str == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(str->StringPtr, str->Length);
}
