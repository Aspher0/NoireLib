using System;

namespace NoireLib.CommandRouter;

/// <summary>
/// Fluent builder for a root command's fallback, configured through
/// <see cref="RootCommandBuilder.AddFallbackCommand(string, Action{FallbackCommandBuilder})"/>, covering the handler
/// for invocations whose first token matches no subcommand and how its free-form argument is listed in help.
/// </summary>
public sealed class FallbackCommandBuilder
{
    private readonly string name;
    private string? helpText;
    private int displayOrder = int.MaxValue;
    private bool showInHelp = true;
    private Action<ParsedCommandArguments>? handler;

    /// <summary>
    /// Creates a new fallback builder for the given argument name.
    /// </summary>
    /// <param name="name">The display name of the free-form argument, rendered as &lt;name&gt; in help listings.</param>
    internal FallbackCommandBuilder(string name)
    {
        this.name = name;
    }

    /// <summary>
    /// Sets the help text describing what the fallback does with its argument.
    /// </summary>
    /// <param name="helpText">A short description shown next to the argument in help listings.</param>
    /// <returns>The builder instance for chaining.</returns>
    public FallbackCommandBuilder WithHelp(string helpText)
    {
        this.helpText = helpText;
        return this;
    }

    /// <summary>
    /// Sets the display order among the subcommand lines in help listings, where a tie lists the fallback first and
    /// the default lists it after every subcommand.
    /// </summary>
    /// <param name="order">The display order value.</param>
    /// <returns>The builder instance for chaining.</returns>
    public FallbackCommandBuilder WithDisplayOrder(int order)
    {
        displayOrder = order;
        return this;
    }

    /// <summary>
    /// Sets whether the fallback appears in help listings, which does not affect whether it dispatches.
    /// </summary>
    /// <param name="show">True to show the fallback in help listings; otherwise, false.</param>
    /// <returns>The builder instance for chaining.</returns>
    public FallbackCommandBuilder ShowInHelp(bool show = true)
    {
        showInHelp = show;
        return this;
    }

    /// <summary>
    /// Sets the handler receiving the tokenized invocation through <see cref="ParsedCommandArguments.RawTokens"/>.
    /// </summary>
    /// <param name="handler">The fallback handler.</param>
    /// <returns>The builder instance for chaining.</returns>
    public FallbackCommandBuilder Handle(Action<ParsedCommandArguments> handler)
    {
        this.handler = handler;
        return this;
    }

    /// <summary>
    /// Builds the presentation definition from the current builder state.
    /// </summary>
    /// <returns>The fallback's help presentation definition.</returns>
    internal FallbackCommandDefinition BuildDefinition() => new(name, helpText, displayOrder, showInHelp);

    /// <summary>
    /// Returns the configured handler.
    /// </summary>
    /// <returns>The handler, or null when none was set.</returns>
    internal Action<ParsedCommandArguments>? BuildHandler() => handler;
}
