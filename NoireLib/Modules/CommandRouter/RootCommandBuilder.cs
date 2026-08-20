using System;

namespace NoireLib.CommandRouter;

/// <summary>
/// Fluent builder, returned by <see cref="NoireCommandRouter.Map(string)"/>, for configuring a root slash command's
/// help text, aliases, subcommands, and default handlers.
/// </summary>
public sealed class RootCommandBuilder
{
    private readonly NoireCommandRouter router;
    private readonly RootCommandRegistration registration;

    /// <summary>Creates a root command builder for a registration.</summary>
    /// <param name="router">The router owning the registration.</param>
    /// <param name="registration">The underlying registration to configure.</param>
    internal RootCommandBuilder(NoireCommandRouter router, RootCommandRegistration registration)
    {
        this.router = router;
        this.registration = registration;
    }

    /// <summary>Sets the help text for this root command, updating a live Dalamud registration.</summary>
    /// <param name="helpText">A short description of what this command does.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder WithHelp(string helpText)
    {
        registration.HelpText = helpText;
        router.RefreshRegistration(registration);

        return this;
    }

    /// <summary>
    /// Adds an alias slash command, registered with Dalamud as its own command and dispatching into this command's
    /// tree. A leading '/' is added if missing and the alias is lower-cased; an alias colliding with an existing
    /// command or alias is logged and ignored.
    /// </summary>
    /// <param name="alias">The alias slash command (e.g. "/mp").</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder AddAlias(string alias)
    {
        router.AddAliasToRegistration(registration, alias);
        return this;
    }

    /// <summary>
    /// Sets the display order for this root command in Dalamud's help listing, shared by its aliases.
    /// </summary>
    /// <param name="order">The display order value.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder WithDisplayOrder(int order)
    {
        registration.DisplayOrder = order;

        // Ordering shapes which entry is last, and with it every entry's blank-line separator.
        router.RefreshAllRegistrations();
        return this;
    }

    /// <summary>Sets whether this command appears in the help output, its aliases following it.</summary>
    /// <param name="show">True to show in help output; false to hide.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder ShowInDalamudHelp(bool show)
    {
        registration.ShowInHelp = show;

        // Visibility shapes which entry is last, and with it every entry's blank-line separator.
        router.RefreshAllRegistrations();

        return this;
    }

    /// <summary>
    /// Sets whether the generated Dalamud help message includes the subcommand tree and the built-in "help" line.
    /// When false, Dalamud lists only this command's own help text, while everything stays dispatchable and the
    /// in-chat "/command help" listing stays fully detailed.
    /// </summary>
    /// <param name="show">True to list the full tree in Dalamud's help; false to list only the help text.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder ShowDetailedDalamudHelp(bool show)
    {
        registration.DetailedDalamudHelp = show;
        router.RefreshRegistration(registration);

        return this;
    }

    /// <summary>Adds a subcommand to this root command.</summary>
    /// <param name="name">The primary name of the subcommand.</param>
    /// <param name="configure">A callback that configures the subcommand via a <see cref="SubCommandBuilder"/>.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder AddSubCommand(string name, Action<SubCommandBuilder> configure)
    {
        var builder = new SubCommandBuilder(name);
        configure(builder);
        registration.SubCommands.Add(builder.Build());
        router.RefreshRegistration(registration);
        return this;
    }

    /// <summary>Sets the handler invoked when the root command is used without any subcommand.</summary>
    /// <param name="handler">The default handler action.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder Handle(Action handler)
    {
        registration.DefaultHandler = handler;
        return this;
    }

    /// <summary>
    /// Sets the handler invoked when the first argument token matches no subcommand, receiving every token of the
    /// invocation through <see cref="ParsedCommandArguments.RawTokens"/>. Subcommands, the built-in "help" token and
    /// typed arguments keep working, and the default handler keeps the bare-command case.
    /// </summary>
    /// <param name="handler">The fallback handler receiving the tokenized invocation.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder HandleFallback(Action<ParsedCommandArguments> handler)
    {
        registration.FallbackHandler = handler;
        return this;
    }

    /// <summary>
    /// Configures a documented fallback: dispatch matches
    /// <see cref="HandleFallback(Action{ParsedCommandArguments})"/>, and the free-form argument also appears in help
    /// listings as "&lt;argumentName&gt; - help text" among the subcommand lines, at its display order.
    /// </summary>
    /// <param name="argumentName">The display name of the free-form argument, rendered as &lt;argumentName&gt;.</param>
    /// <param name="configure">A callback that configures the fallback via a <see cref="FallbackCommandBuilder"/>.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="argumentName"/> is null or whitespace.</exception>
    public RootCommandBuilder AddFallbackCommand(string argumentName, Action<FallbackCommandBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(argumentName))
            throw new ArgumentException("Argument name cannot be null or empty.", nameof(argumentName));

        var builder = new FallbackCommandBuilder(argumentName);
        configure(builder);

        registration.FallbackDefinition = builder.BuildDefinition();

        if (builder.BuildHandler() is { } handler)
            registration.FallbackHandler = handler;

        router.RefreshRegistration(registration);

        return this;
    }

    /// <summary>
    /// Sets a handler receiving the full command string and raw arguments, bypassing subcommand dispatch entirely,
    /// so Dalamud's help listing shows only this command's own help text.
    /// </summary>
    /// <param name="handler">The raw handler action receiving (command, rawArgs).</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder HandleRaw(Action<string, string> handler)
    {
        registration.RawHandler = handler;
        router.RefreshRegistration(registration);
        return this;
    }

    /// <summary>
    /// Sets an availability predicate, evaluated on the framework thread on every invocation; while it returns
    /// false, nothing under the command runs, aliases included, and a blocked invocation is recorded in
    /// <see cref="NoireCommandRouter.GetHistory"/> as unsuccessful without publishing a
    /// <see cref="CommandFailedEvent"/>. The command still appears in Dalamud's help listing; hide it with
    /// <see cref="ShowInDalamudHelp(bool)"/>.
    /// </summary>
    /// <param name="condition">The availability predicate.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder WithCondition(Func<bool> condition)
    {
        registration.Condition = condition;
        return this;
    }
}
