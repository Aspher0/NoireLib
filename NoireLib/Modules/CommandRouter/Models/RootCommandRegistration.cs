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
    /// <summary>
    /// The root slash command string.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Optional help text describing the root command.
    /// </summary>
    public string? HelpText { get; internal set; }

    /// <summary>
    /// Whether this command should appear in Dalamud's help listing.
    /// </summary>
    public bool ShowInHelp { get; internal set; } = true;

    /// <summary>
    /// Whether the generated Dalamud help message includes the subcommand tree and the built-in "help" line, which
    /// does not affect in-chat auto-help.
    /// </summary>
    public bool DetailedDalamudHelp { get; internal set; } = true;

    /// <summary>
    /// The display order used by Dalamud when listing this root command in help.
    /// </summary>
    public int DisplayOrder { get; internal set; }

    /// <summary>
    /// An optional predicate that must return true for the root command to be available; returning false blocks the
    /// whole command, including its subcommands and generated help.
    /// </summary>
    public Func<bool>? Condition { get; internal set; }

    /// <summary>
    /// The alias slash commands mapped to this root command, each registered with Dalamud as its own command and
    /// dispatching to this same registration.
    /// </summary>
    internal List<string> Aliases { get; } = [];

    /// <summary>
    /// The live Dalamud <see cref="CommandInfo"/> for each registered alias, keyed by alias command string; an alias
    /// missing here is not currently registered with Dalamud.
    /// </summary>
    internal Dictionary<string, CommandInfo> AliasCommandInfos { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The registered subcommands for this root command.
    /// </summary>
    internal List<SubCommandDefinition> SubCommands { get; } = [];

    /// <summary>
    /// An optional handler invoked when the root command is used without any subcommand.
    /// </summary>
    internal Action? DefaultHandler { get; set; }

    /// <summary>
    /// An optional handler invoked with every token of the invocation when the first argument token matches no
    /// subcommand, taking precedence over <see cref="DefaultHandler"/> for that case.
    /// </summary>
    internal Action<ParsedCommandArguments>? FallbackHandler { get; set; }

    /// <summary>
    /// How the fallback is presented in help listings, or null when the fallback is undocumented and listed nowhere.
    /// </summary>
    internal FallbackCommandDefinition? FallbackDefinition { get; set; }

    /// <summary>
    /// An optional handler that receives the full command and raw argument string, bypassing subcommand dispatch.
    /// </summary>
    internal Action<string, string>? RawHandler { get; set; }

    /// <summary>
    /// The Dalamud <see cref="CommandInfo"/> reference for this registration, or null if not currently registered.
    /// </summary>
    internal CommandInfo? DalamudCommandInfo { get; set; }

    /// <summary>
    /// Creates a new root command registration.
    /// </summary>
    /// <param name="command">The root slash command string.</param>
    internal RootCommandRegistration(string command)
    {
        Command = command;
    }

    /// <summary>
    /// Builds the generated help text shown by Dalamud for the root command.
    /// </summary>
    /// <param name="includeBuiltInHelp">Whether the router's auto-help is enabled, which decides if the built-in
    /// "help" line is advertised.</param>
    /// <returns>The generated help text.</returns>
    internal string BuildDalamudHelpMessage(bool includeBuiltInHelp)
    {
        var lines = new List<string>();
        lines.Add(string.IsNullOrWhiteSpace(HelpText) ? "No information." : HelpText);

        // A raw handler bypasses subcommand dispatch and the built-in "help" token, so listing either would
        // advertise paths that can never run.
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
                lines.Add(BuildBuiltInHelpLabel(Command, SubCommands));
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

    private static string BuildBuiltInHelpLabel(string rootCommand, IReadOnlyList<SubCommandDefinition> subCommands)
    {
        var builder = new StringBuilder();
        builder.Append(BuildTreePrefix(1));
        builder.Append("help - Shows a help message");

        var firstSubCommand = subCommands
            .OrderBy(subCommand => subCommand.DisplayOrder)
            .ThenBy(subCommand => subCommand.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (firstSubCommand != null)
            builder.Append($". Also available for subcommands (e.g. {rootCommand} {firstSubCommand.Name} help)");

        return builder.ToString();
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
