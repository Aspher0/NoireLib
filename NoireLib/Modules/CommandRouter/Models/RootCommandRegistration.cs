using Dalamud.Game.Command;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoireLib.CommandRouter;

/// <summary>
/// Represents the internal registration state for a single root slash command,
/// including its subcommands, help text, handlers, and the Dalamud <see cref="CommandInfo"/> reference.
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
    /// The display order used by Dalamud when listing this root command in help.
    /// </summary>
    public int DisplayOrder { get; internal set; }

    /// <summary>
    /// The registered subcommands for this root command.
    /// </summary>
    internal List<SubCommandDefinition> SubCommands { get; } = [];

    /// <summary>
    /// An optional handler invoked when the root command is used without any subcommand.
    /// </summary>
    internal Action? DefaultHandler { get; set; }

    /// <summary>
    /// An optional raw handler that receives the full command and raw argument string directly,
    /// bypassing subcommand dispatch entirely.
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
    /// Applies the current registration metadata to the active Dalamud command info, if one exists.
    /// </summary>
    internal void RefreshDalamudCommandInfo()
    {
        if (DalamudCommandInfo == null)
            return;

        DalamudCommandInfo.HelpMessage = BuildDalamudHelpMessage();
        DalamudCommandInfo.ShowInHelp = ShowInHelp;
        DalamudCommandInfo.DisplayOrder = DisplayOrder;
    }

    /// <summary>
    /// Builds the generated help text shown by Dalamud for the root command.
    /// </summary>
    /// <returns>The generated help text.</returns>
    internal string BuildDalamudHelpMessage()
    {
        var lines = new List<string>();
        lines.Add(string.IsNullOrWhiteSpace(HelpText) ? "No information." : HelpText);

        var visibleSubCommands = GetVisibleSubCommands(SubCommands);

        if (visibleSubCommands.Count > 0)
            AppendHelpLines(lines, visibleSubCommands, 1);

        lines.Add(BuildBuiltInHelpLabel(Command, SubCommands));

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
            builder.Append($" ({string.Join(", ", subCommand.Aliases)})");

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
