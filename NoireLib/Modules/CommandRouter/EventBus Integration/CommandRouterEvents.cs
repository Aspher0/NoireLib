using System;

namespace NoireLib.CommandRouter;

/// <summary>
/// Event published to the <see cref="EventBus.NoireEventBus"/> when a command is successfully executed.
/// </summary>
/// <param name="Command">The root slash command string.</param>
/// <param name="RawArgs">The raw argument string as received from Dalamud.</param>
/// <param name="SubCommandName">The resolved subcommand path, or null if the default handler was used.</param>
public record CommandExecutedEvent(string Command, string RawArgs, string? SubCommandName);

/// <summary>
/// Event published to the <see cref="EventBus.NoireEventBus"/> when a command fails during execution.
/// </summary>
/// <param name="Command">The root slash command string.</param>
/// <param name="RawArgs">The raw argument string as received from Dalamud.</param>
/// <param name="SubCommandName">The resolved subcommand path, or null if no subcommand was matched.</param>
/// <param name="Exception">The exception that caused the failure.</param>
public record CommandFailedEvent(string Command, string RawArgs, string? SubCommandName, Exception Exception);
