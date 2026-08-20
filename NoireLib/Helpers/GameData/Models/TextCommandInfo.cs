using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One text command as the client accepts it. Every command has up to four spellings, and the client accepts all of
/// them, so anything matching what a user typed has to consider all four.
/// </summary>
/// <param name="RowId">The TextCommand row id.</param>
/// <param name="Command">The full command, including its leading slash.</param>
/// <param name="ShortCommand">The abbreviated command, or an empty string when it has none.</param>
/// <param name="Alias">An alternative full spelling, or an empty string when it has none.</param>
/// <param name="ShortAlias">An alternative abbreviated spelling, or an empty string when it has none.</param>
/// <param name="Description">The help text the client shows for the command.</param>
public sealed record TextCommandInfo(
    uint RowId,
    string Command,
    string ShortCommand,
    string Alias,
    string ShortAlias,
    string Description)
{
    /// <summary>Every spelling the client accepts for this command, skipping the ones it does not have.</summary>
    /// <returns>The spellings, longest form first.</returns>
    public IReadOnlyList<string> Spellings()
    {
        var spellings = new List<string>(4);

        foreach (var spelling in new[] { Command, Alias, ShortCommand, ShortAlias })
        {
            if (!string.IsNullOrEmpty(spelling))
                spellings.Add(spelling);
        }

        return spellings;
    }
}
