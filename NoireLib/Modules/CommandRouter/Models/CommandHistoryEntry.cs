using System;

namespace NoireLib.CommandRouter;

/// <summary>
/// Represents a single entry in the command history log.
/// </summary>
/// <param name="Command">The root slash command string.</param>
/// <param name="RawArgs">The raw argument string as received from Dalamud.</param>
/// <param name="SubCommandName">The resolved subcommand path, or null if none matched.</param>
/// <param name="Timestamp">The UTC time at which the command was dispatched.</param>
/// <param name="WasSuccessful">Whether the command executed without errors.</param>
public sealed record CommandHistoryEntry(
    string Command,
    string RawArgs,
    string? SubCommandName,
    DateTimeOffset Timestamp,
    bool WasSuccessful);
