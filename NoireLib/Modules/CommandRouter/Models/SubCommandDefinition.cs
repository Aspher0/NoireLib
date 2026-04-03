using System;
using System.Collections.Generic;

namespace NoireLib.CommandRouter;

/// <summary>
/// Represents the fully built definition of a subcommand, including its name, help text, aliases, arguments, handler, and availability condition.
/// </summary>
public sealed class SubCommandDefinition
{
    /// <summary>
    /// The primary name of the subcommand.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Optional help text describing what this subcommand does.
    /// </summary>
    public string? HelpText { get; }

    /// <summary>
    /// Alternative names that can be used to invoke this subcommand.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// The typed argument definitions for this subcommand.
    /// </summary>
    public IReadOnlyList<CommandArgumentDefinition> Arguments { get; }

    /// <summary>
    /// Child subcommands that can be dispatched from this subcommand.
    /// </summary>
    public IReadOnlyList<SubCommandDefinition> SubCommands { get; }

    /// <summary>
    /// Whether this subcommand should appear in generated help output.
    /// </summary>
    public bool ShowInHelp { get; }

    /// <summary>
    /// The display order for this subcommand within its parent command scope.
    /// </summary>
    public int DisplayOrder { get; }

    /// <summary>
    /// Whether optional arguments can be matched in any order after required positional arguments.
    /// </summary>
    public bool AllowUnorderedOptionalArguments { get; }

    /// <summary>
    /// Whether extra trailing arguments should cause command parsing to fail.
    /// </summary>
    public bool FailOnExtraArguments { get; }

    /// <summary>
    /// The handler delegate to invoke when this subcommand is dispatched.
    /// </summary>
    public Delegate? Handler { get; }

    /// <summary>
    /// Whether the handler is asynchronous.
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// Whether the handler accepts <see cref="ParsedCommandArguments"/>.
    /// </summary>
    public bool HasArguments { get; }

    /// <summary>
    /// An optional predicate that must return true for the subcommand to be available.
    /// </summary>
    public Func<bool>? Condition { get; }

    /// <summary>
    /// Creates a new subcommand definition.
    /// </summary>
    /// <param name="name">The primary name.</param>
    /// <param name="helpText">Optional help text.</param>
    /// <param name="aliases">Alternative names.</param>
    /// <param name="arguments">Typed argument definitions.</param>
    /// <param name="subCommands">Nested child subcommands.</param>
    /// <param name="handler">The handler delegate.</param>
    /// <param name="isAsync">Whether the handler is async.</param>
    /// <param name="hasArguments">Whether the handler expects parsed arguments.</param>
    /// <param name="condition">An optional availability predicate.</param>
    /// <param name="showInHelp">Whether this subcommand should appear in generated help output.</param>
    /// <param name="displayOrder">The display order within the parent command scope.</param>
    /// <param name="allowUnorderedOptionalArguments">Whether optional arguments can be matched in any order after required positional arguments.</param>
    /// <param name="failOnExtraArguments">Whether extra trailing arguments should cause command parsing to fail.</param>
    internal SubCommandDefinition(
        string name,
        string? helpText,
        List<string> aliases,
        List<CommandArgumentDefinition> arguments,
        List<SubCommandDefinition> subCommands,
        Delegate? handler,
        bool isAsync,
        bool hasArguments,
        Func<bool>? condition,
        bool showInHelp,
        int displayOrder,
        bool allowUnorderedOptionalArguments,
        bool failOnExtraArguments)
    {
        Name = name;
        HelpText = helpText;
        Aliases = aliases.AsReadOnly();
        Arguments = arguments.AsReadOnly();
        SubCommands = subCommands.AsReadOnly();
        Handler = handler;
        IsAsync = isAsync;
        HasArguments = hasArguments;
        Condition = condition;
        ShowInHelp = showInHelp;
        DisplayOrder = displayOrder;
        AllowUnorderedOptionalArguments = allowUnorderedOptionalArguments;
        FailOnExtraArguments = failOnExtraArguments;
    }
}
