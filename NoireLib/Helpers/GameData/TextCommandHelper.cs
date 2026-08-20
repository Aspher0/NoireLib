using Dalamud.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the client's text commands, so a command can be sent on a client of any language.<br/>
/// <c>/dance</c> is only <c>/dance</c> on an English client: the same row reads <c>/danse</c> in French and
/// <c>/tanz</c> in German. Use <see cref="Localize"/>: name the command in English, send what comes back.
/// </summary>
public static class TextCommandHelper
{
    /// <summary>Reads one text command.</summary>
    /// <param name="rowId">The TextCommand row id.</param>
    /// <param name="language">The language to read in, or null for the client's own.</param>
    /// <returns>The command, or null when the id names none.</returns>
    public static TextCommandInfo? Read(uint rowId, ClientLanguage? language = null)
    {
        if (rowId == 0)
            return null;

        return SafeExecutor.ExecuteSafely<TextCommandInfo?>(
            () => ExcelSheetHelper.TryGetRow<TextCommand>(rowId, out var row, language) && row.HasValue
                ? Describe(row.Value)
                : null,
            null);
    }

    /// <summary>Every text command the client knows.</summary>
    /// <param name="language">The language to read in, or null for the client's own.</param>
    /// <returns>The commands, in ascending row order.</returns>
    public static IReadOnlyList<TextCommandInfo> ReadAll(ClientLanguage? language = null)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var commands = new List<TextCommandInfo>();
            var sheet = ExcelSheetHelper.GetSheet<TextCommand>(language);
            if (sheet == null)
                return commands;

            foreach (var row in sheet)
            {
                if (row.RowId != 0 && row.Command.ByteLength > 0)
                    commands.Add(Describe(row));
            }

            return commands;
        }, []) ?? [];
    }

    /// <summary>
    /// Finds the command a piece of text names, matching every spelling the client accepts. The leading slash is
    /// optional, and anything after the command itself is ignored, so a whole line as typed can be handed in.
    /// </summary>
    /// <param name="text">The text to match.</param>
    /// <param name="language">The language the text is written in, or null for the client's own.</param>
    /// <returns>The command, or null when nothing matches.</returns>
    public static TextCommandInfo? Find(string text, ClientLanguage? language = null)
    {
        var wanted = Normalize(text);

        if (wanted.Length == 0)
            return null;

        foreach (var command in ReadAll(language))
        {
            if (Matches(command, wanted))
                return command;
        }

        return null;
    }

    /// <summary>
    /// Rewrites a command from one client language into another.
    /// </summary>
    /// <param name="command">The command as spelled in <paramref name="sourceLanguage"/>, with or without its slash.</param>
    /// <param name="targetLanguage">The language to rewrite into, or null for the client's own.</param>
    /// <param name="sourceLanguage">The language the command is written in.</param>
    /// <returns>The command in the target language, or null when the source language knows no such command.</returns>
    public static string? Localize(
        string command,
        ClientLanguage? targetLanguage = null,
        ClientLanguage sourceLanguage = ClientLanguage.English)
    {
        var found = Find(command, sourceLanguage);

        if (found == null)
            return null;

        var localized = Read(found.RowId, targetLanguage);

        return string.IsNullOrEmpty(localized?.Command) ? null : localized.Command;
    }

    /// <summary>
    /// Whether a command is spelled a given way, comparing against all four of its spellings. The needle is expected to
    /// be normalized already, which <see cref="Normalize"/> does.
    /// </summary>
    /// <param name="command">The command to test.</param>
    /// <param name="normalizedText">The normalized text to match.</param>
    /// <returns>True when the text names this command.</returns>
    public static bool Matches(TextCommandInfo command, string normalizedText)
    {
        foreach (var spelling in command.Spellings())
        {
            if (string.Equals(Normalize(spelling), normalizedText, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reduces a line of text to the command word alone: no leading slash, no arguments, no surrounding whitespace.
    /// <c>/dance</c>, <c>dance</c> and <c>/dance motion</c> all reduce to the same word.
    /// </summary>
    /// <param name="text">The text to reduce.</param>
    /// <returns>The command word, lowercased, or an empty string when there is none.</returns>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();

        if (trimmed[0] is '/' or '／')
            trimmed = trimmed[1..];

        var space = trimmed.IndexOf(' ');
        if (space >= 0)
            trimmed = trimmed[..space];

        return trimmed.ToLowerInvariant();
    }

    private static TextCommandInfo Describe(TextCommand row) => new(
        row.RowId,
        row.Command.ExtractText(),
        row.ShortCommand.ExtractText(),
        row.Alias.ExtractText(),
        row.ShortAlias.ExtractText(),
        row.Description.ExtractText());
}
