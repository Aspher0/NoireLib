using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using NoireLib.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NoireLib.Helpers;

/// <summary>Helpers for reading, gating and playing emotes.</summary>
public static class EmoteHelper
{
    /// <summary>Retrieves an emote by its command, alias, or either short form.</summary>
    /// <param name="command">The emote command, with or without the leading slash.</param>
    /// <param name="clientLanguage">The client language to search in, or null for all of them.</param>
    /// <returns>The matching emote, or null when none matches.</returns>
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

    /// <summary>Retrieves an emote by its row id.</summary>
    /// <param name="emoteId">The emote row id.</param>
    /// <returns>The emote, or null when the row cannot be read.</returns>
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

    /// <summary>Whether the local player has learnt the emote.</summary>
    /// <param name="emoteId">The emote row id.</param>
    /// <returns>True when the emote is unlocked.</returns>
    public unsafe static bool IsEmoteUnlocked(uint emoteId) => UIState.Instance()->IsEmoteUnlocked((ushort)emoteId);

    /// <inheritdoc cref="IsEmoteUnlocked(uint)"/>
    /// <param name="emote">The emote to check.</param>
    public static bool IsEmoteUnlocked(Emote emote) => IsEmoteUnlocked(emote.RowId);

    /// <summary>Resolves a TextCommand row id to its command string.</summary>
    /// <param name="textCommandId">The TextCommand row id.</param>
    /// <returns>The command string, or an empty string when the row cannot be read.</returns>
    private static string GetTextCommandString(int textCommandId)
    {
        if (textCommandId <= 0)
            return string.Empty;

        var sheet = ExcelSheetHelper.GetSheet<TextCommand>();
        if (sheet == null)
            return string.Empty;

        return sheet.TryGetRow((uint)textCommandId, out var row)
            ? row.Command.ExtractText() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Resolves an emote's name, text command, icon, category and usable states through
    /// <see cref="EmoteController.TryGetEmoteDetails(uint, EmoteController.EmoteDetails*)"/>.
    /// </summary>
    /// <param name="emoteId">The emote row id.</param>
    /// <param name="details">Receives the resolved details.</param>
    /// <returns>True when the client resolved the emote.</returns>
    public static unsafe bool TryGetEmoteDetails(uint emoteId, [NotNullWhen(true)] out Models.EmoteDetails? details)
    {
        details = null;

        EmoteController.EmoteDetails raw;
        if (!EmoteController.TryGetEmoteDetails(emoteId, &raw))
            return false;

        var category = raw.EmoteCategory switch
        {
            1 => Enums.EmoteCategory.General,
            2 => Enums.EmoteCategory.Special,
            3 => Enums.EmoteCategory.Expressions,
            _ => Enums.EmoteCategory.Unknown,
        };

        Enums.EmoteCondition conditions;
        if (category == Enums.EmoteCategory.Expressions)
        {
            // The client forces all conditions to true for expressions; the raw flags are not meaningful.
            conditions = Enums.EmoteCondition.All;
        }
        else
        {
            conditions = Enums.EmoteCondition.None;
            if (raw.Standing) conditions |= Enums.EmoteCondition.Standing;
            if (raw.Swimming) conditions |= Enums.EmoteCondition.Swimming;
            if (raw.Diving) conditions |= Enums.EmoteCondition.Diving;
            if (raw.SittingOnGround) conditions |= Enums.EmoteCondition.SittingOnGround;
            if (raw.SittingInChair) conditions |= Enums.EmoteCondition.SittingInChair;
            if (raw.Mounted) conditions |= Enums.EmoteCondition.Mounted;
            if (raw.HoldingUmbrella) conditions |= Enums.EmoteCondition.HoldingUmbrella;
            if (raw.HoldingTorch) conditions |= Enums.EmoteCondition.HoldingTorch;
            if (raw.WearingFashionAccessory) conditions |= Enums.EmoteCondition.WearingFashionAccessory;
            if (raw.Fishing) conditions |= Enums.EmoteCondition.Fishing;
        }

        details = new Models.EmoteDetails
        {
            EmoteId = emoteId,
            Name = raw.Name.ToString(),
            TextCommand = GetTextCommandString(raw.TextCommand),
            Icon = raw.Icon,
            Order = raw.Order,
            UnlockLink = raw.UnlockLink,
            Category = category,
            Conditions = conditions,
        };

        return true;
    }

    /// <inheritdoc cref="TryGetEmoteDetails(uint, out Models.EmoteDetails?)"/>
    /// <returns>The resolved details, or null when the emote could not be resolved.</returns>
    public static Models.EmoteDetails? GetEmoteDetails(uint emoteId)
        => TryGetEmoteDetails(emoteId, out var details) ? details : null;

    /// <summary>The character states an emote can be performed in.</summary>
    /// <param name="emote">The emote to read.</param>
    /// <returns>The permitted states.</returns>
    public static Enums.EmoteCondition GetEmoteConditions(Emote emote)
    {
        if (TryGetEmoteDetails(emote.RowId, out var details))
            return details.Conditions;

        return Enums.EmoteCondition.None;
    }

    /// <inheritdoc cref="GetEmoteConditions(Emote)"/>
    /// <param name="emoteId">The emote row id.</param>
    public static Enums.EmoteCondition GetEmoteConditions(uint emoteId)
    {
        if (TryGetEmoteDetails(emoteId, out var details))
            return details.Conditions;

        return Enums.EmoteCondition.None;
    }

    /// <summary>Whether an emote can be performed in every one of the given states.</summary>
    /// <param name="emote">The emote to check.</param>
    /// <param name="conditions">The states to test, combined as flags.</param>
    /// <returns>True when the emote permits all of them.</returns>
    public static bool CanUseEmoteWhile(Emote emote, Enums.EmoteCondition conditions)
        => (GetEmoteConditions(emote) & conditions) == conditions;

    /// <inheritdoc cref="CanUseEmoteWhile(Emote, Enums.EmoteCondition)"/>
    /// <param name="emoteId">The emote row id.</param>
    /// <param name="conditions">The states to test, combined as flags.</param>
    public static bool CanUseEmoteWhile(uint emoteId, Enums.EmoteCondition conditions)
        => (GetEmoteConditions(emoteId) & conditions) == conditions;

    /// <summary>The category an emote belongs to.</summary>
    /// <param name="emote">The emote to read.</param>
    /// <returns>The category, or <see cref="Enums.EmoteCategory.Unknown"/> for an unrecognised row.</returns>
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

    /// <summary>The sentinel an emote option's target id carries when there is no target.</summary>
    public const ulong NoEmoteTargetId = GameObjectHelper.NoTargetId;

    /// <summary>
    /// The rows the game swaps in when the target is out of reach, transcribed from its own table. It applies these
    /// in the play path, after <c>Character::ResolveTargetedEmoteId</c> has answered, so the resolver never reports
    /// them. Distances are hitbox to hitbox, height ignored.
    /// </summary>
    private static readonly (uint Emote, uint OutOfRange, float MaxDistance)[] OutOfRangeEmotes =
    [
        (86u, 87u, 15f),   // Snowball, what /throw becomes
        (146u, 147u, 15f), // Dote
        (178u, 179u, 5f),  // Splash
        (267u, 268u, 15f), // All Saints' Charm
    ];

    /// <summary>Builds the play option the game's emote execution reads, which is dereferenced without a null check.</summary>
    /// <param name="targetId">The target's game object id, or <see cref="NoEmoteTargetId"/> for none.</param>
    /// <returns>The option.</returns>
    public static unsafe EmoteController.PlayEmoteOption EmoteOptionFor(ulong targetId)
        => new()
        {
            VirtualTable = EmoteController.PlayEmoteOption.StaticVirtualTablePointer,
            TargetId = targetId,
        };

    /// <summary>
    /// The row the game would play for <paramref name="emoteRowId"/> against a given target, applying both
    /// <c>Character::ResolveTargetedEmoteId</c> and the out-of-range substitutions the resolver does not report.
    /// </summary>
    /// <param name="chara">The character playing the emote.</param>
    /// <param name="emoteRowId">The row that was asked for.</param>
    /// <param name="targetId">The target's game object id, or <see cref="NoEmoteTargetId"/> for none.</param>
    /// <returns>The row the game would play, unchanged when it has no variant.</returns>
    public static unsafe uint ResolveTargetedEmote(ICharacter chara, uint emoteRowId, ulong targetId)
    {
        if (chara == null || emoteRowId > ushort.MaxValue)
            return emoteRowId;

        var native = CharacterHelper.GetCharacterAddress(chara);

        if (native == null)
            return emoteRowId;

        var option = EmoteOptionFor(targetId);
        var resolved = (uint)native->ResolveTargetedEmoteId((ushort)emoteRowId, &option);

        if (targetId == NoEmoteTargetId)
            return resolved;

        var match = Array.Find(OutOfRangeEmotes, entry => entry.Emote == resolved);

        if (match.Emote != resolved)
            return resolved;

        var target = NoireService.ObjectTable.SearchById(targetId);

        if (target == null)
            return resolved;

        return GameObjectHelper.DistanceBetween(chara, target) > match.MaxDistance
            ? match.OutOfRange
            : resolved;
    }

    /// <summary>
    /// Whether the game's own gate would let the player use this emote right now, which covers the unlock, the
    /// shared cooldown, mode and posture, and the per-emote conditions the sheet does not express.
    /// </summary>
    /// <param name="emoteId">The emote row id.</param>
    /// <returns>True when the game would accept it.</returns>
    public static unsafe bool CanUseEmote(uint emoteId)
    {
        if (emoteId > ushort.MaxValue)
            return false;

        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentEmote.Instance();

        return agent != null && agent->CanUseEmote((ushort)emoteId);
    }

    /// <summary>
    /// The conditions an emote puts on where the character is standing, and nothing else. Reproduced here because
    /// <see cref="CanUseEmote(uint)"/> tests the unlock inline and so cannot answer for an unlearnt emote.
    /// </summary>
    /// <param name="chara">The character that would play it.</param>
    /// <param name="emoteRowId">The emote row, as resolved for the target.</param>
    /// <returns>True when nothing about the character's footing stands in the way.</returns>
    public static bool MeetsEnvironmentFor(ICharacter chara, uint emoteRowId)
    {
        if (chara == null)
            return true;

        // /splash and its out-of-range twin; the game's gate consults the primary water flag alone, so this does too.
        if (emoteRowId is 178u or 179u)
            return CharacterHelper.IsStandingInWater(chara);

        return true;
    }

    /// <summary>
    /// What <see cref="MeetsEnvironmentFor(ICharacter, uint)"/> would be waiting for, phrased for a message.
    /// </summary>
    /// <param name="emoteRowId">The emote row, as resolved for the target.</param>
    /// <returns>The requirement, or null when the emote has none.</returns>
    public static string? EnvironmentRequirementFor(uint emoteRowId)
        => emoteRowId is 178u or 179u ? "water underfoot" : null;

    /// <summary>
    /// Plays an emote as the local player through the game's own emote agent, so its targeting, history and unlock
    /// handling all apply. A targeted emote lands on someone only when the option carries their id.
    /// </summary>
    /// <param name="emoteRowId">The emote row to play.</param>
    /// <param name="targetId">The target's game object id, or <see cref="NoEmoteTargetId"/> for none.</param>
    /// <param name="addToHistory">Whether the emote joins the player's recent-emote history.</param>
    /// <param name="liveUpdateHistory">Whether an open emote window updates its history as this plays.</param>
    /// <returns>True when the agent accepted the call.</returns>
    public static unsafe bool ExecuteEmote(
        uint emoteRowId, ulong targetId = NoEmoteTargetId, bool addToHistory = true, bool liveUpdateHistory = true)
    {
        if (emoteRowId > ushort.MaxValue)
            return false;

        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentEmote.Instance();

        if (agent == null)
        {
            NoireLogger.LogError("AgentEmote is unavailable; the emote cannot be executed.", "[EmoteHelper] ");
            return false;
        }

        var option = EmoteOptionFor(targetId);
        agent->ExecuteEmote((ushort)emoteRowId, &option, addToHistory, liveUpdateHistory);

        return true;
    }

    /// <summary>
    /// Plays an emote as the local player at whatever they are currently targeting, soft target first.
    /// </summary>
    /// <param name="emoteRowId">The emote row to play.</param>
    /// <param name="addToHistory">Whether the emote joins the player's recent-emote history.</param>
    /// <param name="liveUpdateHistory">Whether an open emote window updates its history as this plays.</param>
    /// <returns>True when the agent accepted the call.</returns>
    public static bool ExecuteEmoteAtCurrentTarget(
        uint emoteRowId, bool addToHistory = true, bool liveUpdateHistory = true)
    {
        var targetId = NoireService.ObjectTable.LocalPlayer is { } player
            ? GameObjectHelper.GetTargetId(player)
            : NoEmoteTargetId;

        return ExecuteEmote(emoteRowId, targetId, addToHistory, liveUpdateHistory);
    }

    /// <summary>
    /// The state a character is in, in the vocabulary <see cref="GetEmoteConditions(Emote)"/> and
    /// <see cref="CanUseEmoteWhile(Emote, Enums.EmoteCondition)"/> use to say where an emote may play.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The state, or <see cref="EmoteCondition.None"/> when the character is in none of them.</returns>
    public static unsafe EmoteCondition ConditionOf(ICharacter character)
    {
        if (character == null || character.Address == 0)
            return EmoteCondition.None;

        var native = CharacterHelper.GetCharacterAddress(character);

        return ConditionFrom(
            native->Mode,
            native->ModeParam,
            native->EmoteController.CurrentPoseType,
            NoireService.Condition[ConditionFlag.Diving],
            NoireService.Condition[ConditionFlag.Swimming],
            NoireService.Condition[ConditionFlag.Fishing],
            OrnamentHelper.GetOrnamentKind(character));
    }

    /// <summary>
    /// The state a set of readings adds up to, tested in order because the states overlap: a mounted character can be
    /// swimming, and a diving character also reads as swimming.
    /// </summary>
    /// <param name="mode">The character's Mode reading.</param>
    /// <param name="modeParam">The character's ModeParam reading: 1 ground sit, 2 chair sit, 3 doze.</param>
    /// <param name="poseType">The stance the EmoteController reports.</param>
    /// <param name="isDiving">Whether the diving condition flag is set.</param>
    /// <param name="isSwimming">Whether the swimming condition flag is set.</param>
    /// <param name="isFishing">Whether the fishing condition flag is set.</param>
    /// <param name="ornamentKind">The <see cref="OrnamentHelper.GetOrnamentKind"/> reading, or null for none.</param>
    /// <returns>The state, <see cref="EmoteCondition.None"/> for dozing, which the sheet has no vocabulary for.</returns>
    public static EmoteCondition ConditionFrom(
        CharacterModes mode,
        byte modeParam,
        EmoteController.PoseType poseType,
        bool isDiving,
        bool isSwimming,
        bool isFishing,
        byte? ornamentKind)
    {
        if (mode is CharacterModes.Mounted or CharacterModes.RidingPillion)
            return EmoteCondition.Mounted;

        if (CharacterHelper.IsEmoteLoopMode(mode))
        {
            switch (modeParam)
            {
                case 1: return EmoteCondition.SittingOnGround;
                case 2: return EmoteCondition.SittingInChair;
                case 3: return EmoteCondition.None;
            }
        }

        if (isDiving)
            return EmoteCondition.Diving;

        if (isSwimming)
            return EmoteCondition.Swimming;

        if (isFishing)
            return EmoteCondition.Fishing;

        // The game reports one PoseType.Accessory for every accessory, so the kind carried has to decide.
        if (ornamentKind is { } kind)
            return OrnamentHelper.ConditionForOrnamentKind(kind);

        return poseType switch
        {
            EmoteController.PoseType.Umbrella => EmoteCondition.HoldingUmbrella,
            EmoteController.PoseType.Accessory => EmoteCondition.WearingFashionAccessory,
            _ => EmoteCondition.Standing,
        };
    }

    /// <summary>How long the game holds every emote on a shared cooldown after one is played.</summary>
    public const long EmoteCooldownMs = 500;

    /// <summary>
    /// The EmoteManager field the game stamps with the QPC-millisecond clock when an emote plays. ClientStructs
    /// does not declare it, so re-check it after a game patch.
    /// </summary>
    private const int EmoteManagerLastEmoteMsOffset = 0x18;

    /// <summary>
    /// Whether the game's shared emote cooldown is still running, which is the one gate
    /// <see cref="CanUseEmote(uint)"/> cannot be asked for separately. Every failure reads as no cooldown, so offset
    /// drift after a game patch lets the emote through rather than swallowing it.
    /// </summary>
    /// <returns>True while the cooldown is active.</returns>
    public static unsafe bool IsEmoteCooldownActive()
    {
        try
        {
            var manager = EmoteManager.Instance();
            if (manager == null)
                return false;

            var lastEmoteMs = *(long*)((byte*)manager + EmoteManagerLastEmoteMsOffset);
            if (lastEmoteMs <= 0)
                return false;

            var nowMs = System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;
            if (nowMs < lastEmoteMs)
                return false;

            return nowMs - lastEmoteMs < EmoteCooldownMs;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>How many ActionTimeline slots an Emote row carries.</summary>
    public const int EmoteActionTimelineSlots = 7;

    /// <summary>The emote a character is playing right now, read off their own EmoteController.</summary>
    /// <param name="chara">The character to read, not necessarily the local player.</param>
    /// <returns>The emote row id, or 0 when the character is playing none or cannot be read.</returns>
    public static unsafe ushort GetPlayingEmoteId(ICharacter chara)
    {
        if (chara == null || chara.Address == 0)
            return 0;

        var native = CharacterHelper.GetCharacterAddress(chara);

        return native == null ? (ushort)0 : native->EmoteController.EmoteId;
    }

    /// <summary>Whether a character is playing a given emote right now.</summary>
    /// <param name="chara">The character to read.</param>
    /// <param name="emoteRowId">The emote row id to look for; row 0 never matches.</param>
    /// <returns>True when the character is playing that emote.</returns>
    public static bool IsPlayingEmote(ICharacter chara, uint emoteRowId)
        => emoteRowId != 0 && GetPlayingEmoteId(chara) == emoteRowId;

    /// <summary>
    /// Every distinct non-zero ActionTimeline row id an emote declares, in slot order.
    /// </summary>
    /// <param name="emoteRowId">The emote row id.</param>
    /// <returns>The timeline ids, or an empty list when the emote cannot be resolved.</returns>
    public static IReadOnlyList<ushort> GetActionTimelineIds(uint emoteRowId)
    {
        try
        {
            if (GetEmoteById(emoteRowId) is not { } emote)
                return [];

            var ids = new List<ushort>(EmoteActionTimelineSlots);

            for (var slot = 0; slot < emote.ActionTimeline.Count; slot++)
            {
                var id = (ushort)emote.ActionTimeline[slot].RowId;

                if (id != 0 && !ids.Contains(id))
                    ids.Add(id);
            }

            return ids;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Could not read the ActionTimeline ids of emote {emoteRowId}; declaring none.");
            return [];
        }
    }

    /// <summary>Every emote the local player has not learnt; requires a loaded character.</summary>
    /// <returns>The locked emotes, in sheet order, or an empty list when the sheet cannot be read.</returns>
    public static IReadOnlyList<Emote> GetLockedEmotes() => FilterEmotesByUnlock(false);

    /// <summary>Every emote the local player has learnt; requires a loaded character.</summary>
    /// <returns>The unlocked emotes, in sheet order, or an empty list when the sheet cannot be read.</returns>
    public static IReadOnlyList<Emote> GetUnlockedEmotes() => FilterEmotesByUnlock(true);

    /// <summary>Walks the Emote sheet and keeps the rows whose unlock state matches.</summary>
    /// <param name="unlocked">The unlock state to keep.</param>
    /// <returns>The matching emotes, in sheet order.</returns>
    private static IReadOnlyList<Emote> FilterEmotesByUnlock(bool unlocked)
    {
        var sheet = ExcelSheetHelper.GetSheet<Emote>();

        if (sheet == null)
            return [];

        var matches = new List<Emote>();

        foreach (var emote in sheet)
        {
            if (IsEmoteUnlocked(emote.RowId) == unlocked)
                matches.Add(emote);
        }

        return matches;
    }
}
