using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// A helper class for working with Emotes.
/// </summary>
public static class EmoteHelper
{
    /// <summary>
    /// Retrieves an Emote by its command, searching through all client languages or just the specified <paramref name="clientLanguage"/> parameter.
    /// </summary>
    /// <param name="command">The emote command, in any supported game client language. With or without the "/".</param>
    /// <param name="clientLanguage">The client language to search in. If null, searches all languages.</param>
    /// <returns>The matching Emote if found; otherwise, null.</returns>
    public static Emote? GetEmoteByCommand(string command, ClientLanguage? clientLanguage = null)
    {
        if (command.StartsWith("/"))
            command = command[1..];

        foreach (var lang in Enum.GetValues<ClientLanguage>())
        {
            if (clientLanguage.HasValue && clientLanguage.Value != lang)
                continue;

            var sheet = ExcelSheetHelper.GetSheet<Emote>(lang);
            if (sheet == null) continue;

            foreach (var emote in sheet)
            {
                var textCommand = emote.TextCommand.ValueNullable;
                if (textCommand == null) continue;

                var cmd = textCommand.Value.Command.ExtractText()?.TrimStart('/');
                if (string.Equals(cmd, command, StringComparison.OrdinalIgnoreCase))
                    return emote;

                var shortCmd = textCommand.Value.ShortCommand.ExtractText()?.TrimStart('/');
                if (string.Equals(shortCmd, command, StringComparison.OrdinalIgnoreCase))
                    return emote;

                var alias = textCommand.Value.Alias.ExtractText()?.TrimStart('/');
                if (string.Equals(alias, command, StringComparison.OrdinalIgnoreCase))
                    return emote;

                var shortAlias = textCommand.Value.ShortAlias.ExtractText()?.TrimStart('/');
                if (string.Equals(shortAlias, command, StringComparison.OrdinalIgnoreCase))
                    return emote;
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to retrieve an Emote by its ID.
    /// </summary>
    /// <param name="emoteId">The ID of the Emote to retrieve.</param>
    /// <returns>The Emote if found; otherwise, null.</returns>
    public static Emote? GetEmoteById(uint emoteId)
    {
        var sheet = ExcelSheetHelper.GetSheet<Emote>();
        if (sheet == null) return null;
        try
        {
            var emote = sheet.GetRow(emoteId);
            return emote;
        }
        catch (Exception)
        {
            NoireLogger.LogError($"Failed to get Emote by ID: {emoteId}.", "[EmoteHelper] ");
            return null;
        }
    }

    /// <summary>
    /// Checks if the specified emote is unlocked for the local player.
    /// </summary>
    /// <param name="emoteId">The ID of the Emote to check.</param>
    /// <returns>True if the emote is unlocked; otherwise, false.</returns>
    public unsafe static bool IsEmoteUnlocked(uint emoteId) => UIState.Instance()->IsEmoteUnlocked((ushort)emoteId);


    /// <inheritdoc cref="IsEmoteUnlocked(uint)"/>
    /// <param name="emote">The Emote to check.</param>
    public static bool IsEmoteUnlocked(Emote emote) => IsEmoteUnlocked(emote.RowId);

    /// <summary>
    /// Retrieves the character states the specified emote can be performed in.
    /// </summary>
    /// <remarks>
    /// This has not been fully tested and may not be accurate for all emotes, although I strongly believe
    /// it is correct for all the emotes listed in the Emote addon at the very least.
    /// </remarks>
    /// <param name="emote">The Emote to retrieve conditions for.</param>
    /// <returns>The states the emote can be performed in, as an <see cref="Enums.EmoteCondition"/>.</returns>
    public static Enums.EmoteCondition GetEmoteConditions(Emote emote)
    {
        if (emote.RowId == 90) // Universal change pose
            return Enums.EmoteCondition.Standing
                | Enums.EmoteCondition.SittingInChair
                | Enums.EmoteCondition.SittingOnGround
                | Enums.EmoteCondition.HoldingUmbrella
                | Enums.EmoteCondition.WearingFashionAccessory;

        var timelines = emote.ActionTimeline;
        bool HasTimeline(int index) => index < timelines.Count && timelines[index].RowId != 0;

        var conditions = Enums.EmoteCondition.None;

        if (HasTimeline(0) || emote.Unknown1)
            conditions |= Enums.EmoteCondition.Standing | Enums.EmoteCondition.WearingFashionAccessory;

        if (emote.Unknown2)
            conditions |= Enums.EmoteCondition.Swimming;

        if (emote.Unknown3)
            conditions |= Enums.EmoteCondition.Diving;

        if (HasTimeline(2) || emote.Unknown1)
            conditions |= Enums.EmoteCondition.SittingOnGround;

        if (HasTimeline(3) || emote.Unknown1)
            conditions |= Enums.EmoteCondition.SittingInChair;

        if (emote.Unknown1)
            conditions |= Enums.EmoteCondition.Mounted;

        if (emote.Unknown1 && !emote.Unknown5)
            conditions |= Enums.EmoteCondition.HoldingUmbrella | Enums.EmoteCondition.HoldingTorch;

        if (!emote.Unknown5)
            conditions |= Enums.EmoteCondition.Fishing;

        return conditions;
    }

    /// <inheritdoc cref="GetEmoteConditions(Emote)"/>
    /// <param name="emoteId">The ID of the Emote whose conditions are to be retrieved.</param>
    public static Enums.EmoteCondition GetEmoteConditions(uint emoteId)
    {
        var emote = GetEmoteById(emoteId);
        return emote.HasValue ? GetEmoteConditions(emote.Value) : Enums.EmoteCondition.None;
    }

    /// <summary>
    /// Checks whether the specified emote can be performed in every one of the given states.
    /// </summary>
    /// <param name="emote">The Emote to check.</param>
    /// <param name="conditions">The states to test. Combine flags to require all of them at once.</param>
    /// <returns>True if the emote can be performed in every given state; otherwise, false.</returns>
    public static bool CanUseEmoteWhile(Emote emote, Enums.EmoteCondition conditions)
        => (GetEmoteConditions(emote) & conditions) == conditions;

    /// <inheritdoc cref="CanUseEmoteWhile(Emote, Enums.EmoteCondition)"/>
    /// <param name="emoteId">The ID of the Emote to check.</param>
    /// <param name="conditions">The states to test. Combine flags to require all of them at once.</param>
    public static bool CanUseEmoteWhile(uint emoteId, Enums.EmoteCondition conditions)
        => (GetEmoteConditions(emoteId) & conditions) == conditions;

    /// <summary>
    /// Retrieves the category of the specified emote.
    /// </summary>
    /// <param name="emote">The Emote whose category is to be retrieved.</param>
    /// <returns>The category of the emote as an <see cref="Enums.EmoteCategory"/>.</returns>
    public static Enums.EmoteCategory GetEmoteCategory(Emote emote)
    {
        var emoteCategory = emote.EmoteCategory;

        switch (emoteCategory.RowId)
        {
            case 1:
                return Enums.EmoteCategory.General;
            case 2:
                return Enums.EmoteCategory.Special;
            case 3:
                return Enums.EmoteCategory.Expressions;
            default:
                return Enums.EmoteCategory.Unknown;
        }
    }
}
