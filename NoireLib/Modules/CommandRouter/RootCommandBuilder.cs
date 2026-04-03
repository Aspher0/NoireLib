using System;

namespace NoireLib.CommandRouter;

// TODO: Add availability condition to root commands as well

/// <summary>
/// Fluent builder for configuring a root slash command, including help text, subcommands, and default handlers.<br/>
/// Returned by <see cref="NoireCommandRouter.Map(string)"/>.
/// </summary>
public sealed class RootCommandBuilder
{
    private readonly RootCommandRegistration registration;

    /// <summary>
    /// Creates a new root command builder for the given registration.
    /// </summary>
    /// <param name="registration">The underlying registration to configure.</param>
    internal RootCommandBuilder(RootCommandRegistration registration)
    {
        this.registration = registration;
    }

    /// <summary>
    /// Sets the help text for this root command.<br/>
    /// Also updates the Dalamud help message if the command is already registered.
    /// </summary>
    /// <param name="helpText">A short description of what this command does.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder WithHelp(string helpText)
    {
        registration.HelpText = helpText;
        registration.RefreshDalamudCommandInfo();

        return this;
    }

    /// <summary>
    /// Sets the display order for this root command in Dalamud's help listing.<br/>
    /// Also updates the Dalamud registration if the command is already registered.
    /// </summary>
    /// <param name="order">The display order value.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder WithDisplayOrder(int order)
    {
        registration.DisplayOrder = order;
        registration.RefreshDalamudCommandInfo();
        return this;
    }

    /// <summary>
    /// Sets whether this command should appear in the help output.<br/>
    /// Also updates the Dalamud registration if the command is already registered.
    /// </summary>
    /// <param name="show">True to show in help output; false to hide.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder ShowInDalamudHelp(bool show)
    {
        registration.ShowInHelp = show;
        registration.RefreshDalamudCommandInfo();

        return this;
    }

    /// <summary>
    /// Adds a subcommand to this root command using a configuration callback.
    /// </summary>
    /// <param name="name">The primary name of the subcommand.</param>
    /// <param name="configure">A callback that configures the subcommand via a <see cref="SubCommandBuilder"/>.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder AddSubCommand(string name, Action<SubCommandBuilder> configure)
    {
        var builder = new SubCommandBuilder(name);
        configure(builder);
        registration.SubCommands.Add(builder.Build());
        registration.RefreshDalamudCommandInfo();
        return this;
    }

    /// <summary>
    /// Sets a default handler invoked when the root command is used without any subcommand.
    /// </summary>
    /// <param name="handler">The default handler action.</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder Handle(Action handler)
    {
        registration.DefaultHandler = handler;
        return this;
    }

    /// <summary>
    /// Sets a raw handler that receives the full command string and raw arguments directly, bypassing subcommand dispatch entirely.
    /// </summary>
    /// <param name="handler">The raw handler action receiving (command, rawArgs).</param>
    /// <returns>The builder instance for chaining.</returns>
    public RootCommandBuilder HandleRaw(Action<string, string> handler)
    {
        registration.RawHandler = handler;
        return this;
    }
}
