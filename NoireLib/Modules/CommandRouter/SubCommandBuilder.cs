using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.CommandRouter;

/// <summary>
/// Fluent builder for configuring a single subcommand, including help text, aliases, typed arguments, handler, and availability condition.
/// </summary>
public sealed class SubCommandBuilder
{
    private readonly string name;
    private string? helpText;
    private readonly List<string> aliases = [];
    private readonly List<CommandArgumentDefinition> arguments = [];
    private readonly List<SubCommandDefinition> subCommands = [];
    private Delegate? handler;
    private bool isAsync;
    private bool hasArguments;
    private Func<bool>? condition;
    private bool showInHelp = true;
    private int displayOrder;
    private bool allowUnorderedOptionalArguments;
    private bool failOnExtraArguments;

    /// <summary>
    /// Creates a new subcommand builder for the given name.
    /// </summary>
    /// <param name="name">The primary name of the subcommand.</param>
    internal SubCommandBuilder(string name)
    {
        this.name = name;
    }

    /// <summary>
    /// Sets the help text for this subcommand.
    /// </summary>
    /// <param name="helpText">A short description of what this subcommand does.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder WithHelp(string helpText)
    {
        this.helpText = helpText;
        return this;
    }

    /// <summary>
    /// Adds an alias for this subcommand so it can be invoked by an alternative name.
    /// </summary>
    /// <param name="alias">The alternative name.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder AddAlias(string alias)
    {
        aliases.Add(alias);
        return this;
    }

    /// <summary>
    /// Sets whether this subcommand should appear in generated help output.
    /// </summary>
    /// <param name="show">True to show the subcommand in generated help output; otherwise, false.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder ShowInHelp(bool show = true)
    {
        showInHelp = show;
        return this;
    }

    /// <summary>
    /// Sets the display order for this subcommand within its parent command scope.
    /// </summary>
    /// <param name="order">The display order value.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder WithDisplayOrder(int order)
    {
        displayOrder = order;
        return this;
    }

    /// <summary>
    /// Sets whether optional arguments can be matched in any order after required positional arguments.
    /// </summary>
    /// <param name="enabled">True to enable unordered optional argument parsing; otherwise, false.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder WithUnorderedOptionalArguments(bool enabled = true)
    {
        allowUnorderedOptionalArguments = enabled;
        return this;
    }

    /// <summary>
    /// Sets whether extra trailing arguments should cause command parsing to fail.
    /// </summary>
    /// <param name="enabled">True to fail when extra arguments are provided; otherwise, false to ignore them.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder FailOnExtraArguments(bool enabled = true)
    {
        failOnExtraArguments = enabled;
        return this;
    }

    /// <summary>
    /// Defines a typed argument for this subcommand.
    /// </summary>
    /// <typeparam name="T">The expected type of the argument.</typeparam>
    /// <param name="name">The argument name used for retrieval from <see cref="ParsedCommandArguments"/>.</param>
    /// <param name="required">Whether this argument must be provided by the user.</param>
    /// <param name="defaultValue">The default value when the argument is optional and not provided.</param>
    /// <param name="description">An optional description shown in help output.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder AddArgument<T>(string name, bool required = true, T? defaultValue = default, string? description = null)
    {
        arguments.Add(new CommandArgumentDefinition(name, typeof(T), required, defaultValue, description));
        return this;
    }

    /// <summary>
    /// Defines a typed argument for this subcommand using a dynamically evaluated default value.
    /// </summary>
    /// <typeparam name="T">The expected type of the argument.</typeparam>
    /// <param name="name">The argument name used for retrieval from <see cref="ParsedCommandArguments"/>.</param>
    /// <param name="required">Whether this argument must be provided by the user.</param>
    /// <param name="defaultValueFactory">The factory used to produce the default value when the argument is optional and not provided.</param>
    /// <param name="description">An optional description shown in help output.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder AddArgument<T>(string name, bool required, Func<T?> defaultValueFactory, string? description = null)
    {
        arguments.Add(new CommandArgumentDefinition(name, typeof(T), required, () => defaultValueFactory(), description));
        return this;
    }

    /// <summary>
    /// Adds a nested subcommand to this subcommand using a configuration callback.
    /// </summary>
    /// <param name="name">The primary name of the nested subcommand.</param>
    /// <param name="configure">A callback that configures the nested subcommand via a <see cref="SubCommandBuilder"/>.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder AddSubCommand(string name, Action<SubCommandBuilder> configure)
    {
        var builder = new SubCommandBuilder(name);
        configure(builder);
        subCommands.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Sets a synchronous handler with no arguments for this subcommand.
    /// </summary>
    /// <param name="handler">The handler action.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder Handle(Action handler)
    {
        this.handler = handler;
        isAsync = false;
        hasArguments = false;
        return this;
    }

    /// <summary>
    /// Sets a synchronous handler that receives parsed arguments for this subcommand.
    /// </summary>
    /// <param name="handler">The handler action receiving <see cref="ParsedCommandArguments"/>.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder Handle(Action<ParsedCommandArguments> handler)
    {
        this.handler = handler;
        isAsync = false;
        hasArguments = true;
        return this;
    }

    /// <summary>
    /// Sets an asynchronous handler with no arguments for this subcommand.
    /// </summary>
    /// <param name="handler">The async handler function.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder Handle(Func<Task> handler)
    {
        this.handler = handler;
        isAsync = true;
        hasArguments = false;
        return this;
    }

    /// <summary>
    /// Sets an asynchronous handler that receives parsed arguments for this subcommand.
    /// </summary>
    /// <param name="handler">The async handler function receiving <see cref="ParsedCommandArguments"/>.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder Handle(Func<ParsedCommandArguments, Task> handler)
    {
        this.handler = handler;
        isAsync = true;
        hasArguments = true;
        return this;
    }

    /// <summary>
    /// Sets an availability predicate. The subcommand will only execute when this predicate returns true.
    /// </summary>
    /// <param name="condition">The availability predicate.</param>
    /// <returns>The builder instance for chaining.</returns>
    public SubCommandBuilder WithCondition(Func<bool> condition)
    {
        this.condition = condition;
        return this;
    }

    /// <summary>
    /// Builds the final <see cref="SubCommandDefinition"/> from the current builder state.
    /// </summary>
    /// <returns>The built subcommand definition.</returns>
    internal SubCommandDefinition Build() =>
        new(name, helpText, aliases, arguments, subCommands, handler, isAsync, hasArguments, condition, showInHelp, displayOrder, allowUnorderedOptionalArguments, failOnExtraArguments);
}
