using Dalamud.Game.Command;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoireLib.CommandRouter;

/// <summary>
/// The registration state for a single root slash command: its subcommands, help text, handlers and Dalamud
/// <see cref="CommandInfo"/> reference.
/// </summary>
public sealed class RootCommandRegistration
{
    /// <summary>The root slash command string.</summary>
    public string Command { get; }

    /// <summary>Optional help text describing the root command.</summary>
    public string? HelpText { get; internal set; }

    /// <summary>Whether this command should appear in Dalamud's help listing.</summary>
    public bool ShowInHelp { get; internal set; } = true;

    /// <summary>
    /// Whether the generated Dalamud help message includes the subcommand tree and the built-in "help" line, which
    /// does not affect in-chat auto-help.
    /// </summary>
    public bool DetailedDalamudHelp { get; internal set; } = true;

    /// <summary>The display order used by Dalamud when listing this root command in help.</summary>
    public int DisplayOrder { get; internal set; }

    /// <summary>
    /// An optional predicate that must return true for the root command to be available; returning false blocks the
    /// whole command, including its subcommands and generated help.
    /// </summary>
    public Func<bool>? Condition { get; internal set; }

    // The alias slash commands mapped to this root command, each registered with Dalamud as its own command and
    // dispatching to this same registration.
    internal List<string> Aliases { get; } = [];

    // The live Dalamud CommandInfo for each registered alias, keyed by alias command string; an alias missing here is
    // not currently registered with Dalamud.
    internal Dictionary<string, CommandInfo> AliasCommandInfos { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal List<SubCommandDefinition> SubCommands { get; } = [];

    // An optional handler invoked when the root command is used without any subcommand.
    internal Action? DefaultHandler { get; set; }

    // An optional handler invoked with every token of the invocation when the first argument token matches no
    // subcommand, taking precedence over DefaultHandler for that case.
    internal Action<ParsedCommandArguments>? FallbackHandler { get; set; }

    // How the fallback is presented in help listings, or null when the fallback is undocumented and listed nowhere.
    internal FallbackCommandDefinition? FallbackDefinition { get; set; }

    // An optional handler that receives the full command and raw argument string, bypassing subcommand dispatch.
    internal Action<string, string>? RawHandler { get; set; }

    // The Dalamud CommandInfo reference for this registration, or null if not currently registered.
    internal CommandInfo? DalamudCommandInfo { get; set; }

    internal RootCommandRegistration(string command)
    {
        Command = command;
    }

    // Builds the generated help text shown by Dalamud for the root command.
    internal string BuildDalamudHelpMessage(bool includeBuiltInHelp)
    {
        var lines = new List<string>();
        lines.Add(string.IsNullOrWhiteSpace(HelpText) ? "No information." : HelpText);

        // A raw handler bypasses subcommands and "help": listing them would advertise paths that never run.
        if (RawHandler == null && DetailedDalamudHelp)
        {
            // The fallback line slots among the subcommand lines by display order, a tie listing it first.
            var fallbackEmitted = FallbackDefinition is not { ShowInHelp: true };

            foreach (var subCommand in GetVisibleSubCommands(SubCommands))
            {
                if (!fallbackEmitted && FallbackDefinition!.DisplayOrder <= subCommand.DisplayOrder)
                {
                    lines.Add(BuildFallbackHelpLabel());
                    fallbackEmitted = true;
                }

                AppendHelpLines(lines, [subCommand], 1);
            }

            if (!fallbackEmitted)
                lines.Add(BuildFallbackHelpLabel());

            if (includeBuiltInHelp)
                lines.Add(BuildTreePrefix(1) + "help - " + BuiltInHelpDescription());
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendHelpLines(List<string> lines, IReadOnlyList<SubCommandDefinition> subCommands, int depth)
    {
        foreach (var subCommand in subCommands)
        {
            lines.Add(BuildHelpLabel(subCommand, depth));

            var visibleChildren = GetVisibleSubCommands(subCommand.SubCommands);
            if (visibleChildren.Count > 0)
                AppendHelpLines(lines, visibleChildren, depth + 1);
        }
    }

    private static string BuildHelpLabel(SubCommandDefinition subCommand, int depth)
    {
        var builder = new StringBuilder();
        builder.Append(BuildTreePrefix(depth));
        builder.Append(subCommand.Name);

        if (subCommand.Aliases.Count > 0)
            builder.Append($" (aliases: {string.Join("|", subCommand.Aliases)})");

        foreach (var argument in subCommand.Arguments)
            builder.Append(argument.IsRequired ? $" <{argument.Name}>" : $" [{argument.Name}]");

        if (!string.IsNullOrWhiteSpace(subCommand.HelpText))
        {
            builder.Append($" - {subCommand.HelpText}");
        }

        var argumentDescriptions = BuildArgumentDescriptions(subCommand.Arguments);
        if (!string.IsNullOrWhiteSpace(argumentDescriptions))
            builder.Append($" ({argumentDescriptions})");

        return builder.ToString();
    }

    private static string BuildTreePrefix(int depth)
        => $"{new string(' ', Math.Max(0, depth - 1) * 2)}└ ";

    private string BuildFallbackHelpLabel()
    {
        var builder = new StringBuilder();
        builder.Append(BuildTreePrefix(1));
        builder.Append($"<{FallbackDefinition!.Name}>");

        if (!string.IsNullOrWhiteSpace(FallbackDefinition.HelpText))
            builder.Append($" - {FallbackDefinition.HelpText}");

        return builder.ToString();
    }

    // Whether the built-in "help" token dispatches for this command, and so is listed: auto-help is on and no raw
    // handler bypasses dispatch.
    internal bool ListsBuiltInHelp(bool autoHelp) => autoHelp && RawHandler == null;

    // What the built-in "help" line says, in Dalamud's listing and in the chat listing alike.
    internal string BuiltInHelpDescription()
    {
        var firstSubCommand = SubCommands
            .OrderBy(subCommand => subCommand.DisplayOrder)
            .ThenBy(subCommand => subCommand.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return firstSubCommand == null
            ? "Shows a help message"
            : $"Shows a help message. Also available for subcommands (e.g. {Command} {firstSubCommand.Name} help)";
    }

    private static string? BuildArgumentDescriptions(IReadOnlyList<CommandArgumentDefinition> arguments)
    {
        var descriptions = arguments
            .Where(argument => !string.IsNullOrWhiteSpace(argument.Description))
            .Select(argument => $"{argument.Name}: {argument.Description}")
            .ToArray();

        return descriptions.Length == 0 ? null : string.Join("; ", descriptions);
    }

    private static IReadOnlyList<SubCommandDefinition> GetVisibleSubCommands(IEnumerable<SubCommandDefinition> subCommands)
        => [.. subCommands
            .Where(subCommand => subCommand.ShowInHelp)
            .OrderBy(subCommand => subCommand.DisplayOrder)
            .ThenBy(subCommand => subCommand.Name, StringComparer.OrdinalIgnoreCase)];
}
