using Dalamud.Game.Command;
using Dalamud.Game.Text;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace NoireLib.CommandRouter;

/// <summary>
/// A module providing structured slash-command registration and dispatch: subcommands, aliases, typed arguments,
/// auto-generated help, async handlers, availability predicates, command history, and optional
/// <see cref="NoireEventBus"/> integration.
/// </summary>
public class NoireCommandRouter : NoireModuleBase<NoireCommandRouter>
{
    #region Private Properties/Fields

    private readonly Dictionary<string, RootCommandRegistration> registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RootCommandRegistration> aliasRegistrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CommandHistoryEntry> history = [];
    private readonly object registrationLock = new();
    private readonly object historyLock = new();
    private int maxHistorySize = 50;
    private static readonly Vector3 HelpCommandColor = new(0.94f, 0.86f, 0.50f);
    private static readonly Vector3 HelpAliasColor = new(0.78f, 0.74f, 0.95f);
    private static readonly Vector3 HelpArgumentColor = new(1.00f, 0.68f, 0.36f);
    private static readonly Vector3 HelpDescriptionColor = new(0.82f, 0.82f, 0.82f);
    private static readonly Vector3 HelpMetaColor = new(0.66f, 0.66f, 0.66f);
    private static readonly Vector3 ErrorTokenColor = new(0.96f, 0.42f, 0.42f);

    #endregion

    #region Constructors & Event Bus

    /// <summary>
    /// The associated <see cref="NoireEventBus"/> instance for publishing command events; when set,
    /// <see cref="CommandExecutedEvent"/> and <see cref="CommandFailedEvent"/> are published automatically.
    /// </summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>
    /// Sets the <see cref="NoireEventBus"/> instance for publishing command events.
    /// </summary>
    /// <param name="eventBus">The EventBus instance, or null to disable event publishing.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireCommandRouter SetEventBus(NoireEventBus? eventBus)
    {
        EventBus = eventBus;
        return this;
    }

    /// <summary>
    /// Creates an unconfigured instance, for internal module management only.
    /// </summary>
    public NoireCommandRouter() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireCommandRouter"/> module.
    /// </summary>
    /// <param name="moduleId">Optional module ID for multiple router instances.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="enableAutoHelp">Whether to enable auto-generated help output.</param>
    /// <param name="maxHistorySize">The maximum number of command history entries to retain.</param>
    /// <param name="eventBus">Optional <see cref="NoireEventBus"/> instance for publishing command events.</param>
    public NoireCommandRouter(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        bool enableAutoHelp = true,
        int maxHistorySize = 50,
        NoireEventBus? eventBus = null)
        : base(moduleId, active, enableLogging, enableAutoHelp, maxHistorySize, eventBus) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>,
    /// for internal module management only.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireCommandRouter(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    #endregion

    #region Module Lifecycle

    /// <inheritdoc/>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is bool enableAutoHelp)
            EnableAutoHelp = enableAutoHelp;

        if (args.Length > 1 && args[1] is int maxHistorySize)
            MaxHistorySize = maxHistorySize;

        if (args.Length > 2 && args[2] is NoireEventBus eventBus)
            EventBus = eventBus;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "CommandRouter module initialized.");
    }

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        lock (registrationLock)
        {
            foreach (var registration in registrations.Values)
                RegisterWithDalamud(registration);
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, "CommandRouter module activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        lock (registrationLock)
        {
            foreach (var registration in registrations.Values)
                UnregisterFromDalamud(registration);
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, "CommandRouter module deactivated.");
    }

    #endregion

    #region Module Configuration

    private bool enableAutoHelp = true;

    /// <summary>
    /// Whether the root command with no subcommand and no default handler, or a "help" token, prints a generated
    /// listing to chat. Setting it refreshes every live Dalamud registration.
    /// </summary>
    public bool EnableAutoHelp
    {
        get => enableAutoHelp;
        set
        {
            enableAutoHelp = value;
            RefreshAllRegistrations();
        }
    }

    /// <summary>
    /// Sets whether auto-generated help output is enabled.
    /// </summary>
    /// <param name="enable">True to enable auto-help; false to disable.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireCommandRouter SetAutoHelp(bool enable)
    {
        EnableAutoHelp = enable;
        return this;
    }

    private bool separateDalamudHelpEntries = true;

    /// <summary>
    /// Whether each command's Dalamud help message ends with a blank line separating it from the next entry, which
    /// the last entry by display order then name never carries. Setting it refreshes every live registration.
    /// </summary>
    public bool SeparateDalamudHelpEntries
    {
        get => separateDalamudHelpEntries;
        set
        {
            separateDalamudHelpEntries = value;
            RefreshAllRegistrations();
        }
    }

    /// <summary>
    /// Sets whether Dalamud help entries are separated by a blank line.
    /// </summary>
    /// <param name="enable">True to separate entries; false to list them back to back.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireCommandRouter SetSeparateDalamudHelpEntries(bool enable)
    {
        SeparateDalamudHelpEntries = enable;
        return this;
    }

    /// <summary>
    /// The maximum number of <see cref="CommandHistoryEntry"/> records to retain, oldest discarded first; 0 disables
    /// recording entirely, leaving <see cref="GetHistory"/> permanently empty.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is negative.</exception>
    public int MaxHistorySize
    {
        get => maxHistorySize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            maxHistorySize = value;
        }
    }

    /// <summary>
    /// Sets the maximum number of command history entries to retain; 0 disables recording entirely.
    /// </summary>
    /// <param name="maxSize">The maximum history size. Must not be negative.</param>
    /// <returns>The module instance for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxSize"/> is negative.</exception>
    public NoireCommandRouter SetMaxHistorySize(int maxSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSize);
        MaxHistorySize = maxSize;
        return this;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Maps a root slash command and returns a <see cref="RootCommandBuilder"/> for configuring it; registers with
    /// Dalamud immediately if the module is active, and replaces any existing mapping of the same name.
    /// </summary>
    /// <param name="command">The root slash command string (e.g. "/somecommand"), with a leading '/' added automatically if missing.</param>
    /// <returns>A <see cref="RootCommandBuilder"/> for fluently configuring the command.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="command"/> is null or whitespace.</exception>
    public RootCommandBuilder Map(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));

        if (!command.StartsWith('/'))
            command = "/" + command;

        // Lower-cased so Dalamud's canonical spelling and help listing do not depend on caller capitalization;
        // lookups here are case-insensitive regardless.
        command = command.ToLowerInvariant();

        var registration = new RootCommandRegistration(command);

        lock (registrationLock)
        {
            if (registrations.TryGetValue(command, out var existing))
            {
                UnregisterFromDalamud(existing);
                RemoveAliasEntries(existing);

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Replacing existing command mapping for '{command}'.");
            }

            // A root command cannot share its name with an alias of another command, so the alias gives way.
            if (aliasRegistrations.TryGetValue(command, out var aliasOwner))
                RemoveAliasFromRegistration(aliasOwner, command);

            registrations[command] = registration;
        }

        if (IsActive)
            RegisterWithDalamud(registration);

        // The new entry may have demoted a previously last entry, whose blank-line separator needs re-adding.
        RefreshAllRegistrations();

        return new RootCommandBuilder(this, registration);
    }

    /// <summary>
    /// Removes a mapped root command, its aliases, and unregisters them from Dalamud.
    /// </summary>
    /// <param name="command">The root slash command string to remove.</param>
    /// <returns>True if the command was found and removed; otherwise, false.</returns>
    public bool Unmap(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        if (!command.StartsWith('/'))
            command = "/" + command;

        lock (registrationLock)
        {
            if (!registrations.TryGetValue(command, out var registration))
                return false;

            UnregisterFromDalamud(registration);
            RemoveAliasEntries(registration);
            registrations.Remove(command);

            // The removed entry may have been the last one; its predecessor's separator goes away.
            RefreshAllRegistrations();

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Unmapped command '{command}'.");

            return true;
        }
    }

    /// <summary>
    /// Gets whether a command is currently mapped, as a root command or as an alias.
    /// </summary>
    /// <param name="command">The slash command string to check.</param>
    /// <returns>True if the command is registered; otherwise, false.</returns>
    public bool IsCommandRegistered(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        if (!command.StartsWith('/'))
            command = "/" + command;

        lock (registrationLock)
            return registrations.ContainsKey(command) || aliasRegistrations.ContainsKey(command);
    }

    /// <summary>
    /// Gets a read-only list of all currently mapped command strings, root commands first, then aliases.
    /// </summary>
    /// <returns>A list of mapped command strings.</returns>
    public IReadOnlyList<string> GetRegisteredCommands()
    {
        lock (registrationLock)
            return registrations.Keys.Concat(aliasRegistrations.Keys).ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets a read-only snapshot of the command history, ordered oldest first, as a copy that stays safe to iterate
    /// while further commands execute.
    /// </summary>
    /// <returns>A list of <see cref="CommandHistoryEntry"/> records.</returns>
    public IReadOnlyList<CommandHistoryEntry> GetHistory()
    {
        lock (historyLock)
            return history.ToList().AsReadOnly();
    }

    /// <summary>
    /// Clears the command history.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireCommandRouter ClearHistory()
    {
        lock (historyLock)
            history.Clear();

        return this;
    }

    #endregion

    /// <inheritdoc/>
    protected override void DisposeInternal()
    {
        lock (registrationLock)
        {
            foreach (var registration in registrations.Values)
                UnregisterFromDalamud(registration);

            registrations.Clear();
            aliasRegistrations.Clear();
        }

        lock (historyLock)
            history.Clear();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "CommandRouter module disposed.");
    }

    #region Private/Internal Methods

    private void RegisterWithDalamud(RootCommandRegistration registration)
    {
        var commandInfo = new CommandInfo(OnCommandDispatched)
        {
            HelpMessage = BuildRootHelpMessage(registration),
            ShowInHelp = registration.ShowInHelp,
            DisplayOrder = registration.DisplayOrder,
        };

        try
        {
            NoireService.CommandManager.AddHandler(registration.Command, commandInfo);

            // Set only once Dalamud owns the handler, so a failed registration does not look live to
            // RefreshRegistration or UnregisterFromDalamud.
            registration.DalamudCommandInfo = commandInfo;

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Registered command '{registration.Command}' with Dalamud.");
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Failed to register command '{registration.Command}' with Dalamud.");
        }

        foreach (var alias in registration.Aliases)
            RegisterAliasWithDalamud(registration, alias);
    }

    private void UnregisterFromDalamud(RootCommandRegistration registration)
    {
        try
        {
            NoireService.CommandManager.RemoveHandler(registration.Command);
            registration.DalamudCommandInfo = null;

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Unregistered command '{registration.Command}' from Dalamud.");
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Failed to unregister command '{registration.Command}' from Dalamud.");
        }

        foreach (var alias in registration.Aliases)
            UnregisterAliasFromDalamud(registration, alias);
    }

    private void RegisterAliasWithDalamud(RootCommandRegistration registration, string alias)
    {
        var aliasInfo = new CommandInfo(OnCommandDispatched)
        {
            HelpMessage = BuildAliasHelpMessage(registration, alias),
            ShowInHelp = registration.ShowInHelp,
            DisplayOrder = registration.DisplayOrder,
        };

        try
        {
            NoireService.CommandManager.AddHandler(alias, aliasInfo);
            registration.AliasCommandInfos[alias] = aliasInfo;

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Registered alias '{alias}' of command '{registration.Command}' with Dalamud.");
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Failed to register alias '{alias}' of command '{registration.Command}' with Dalamud.");
        }
    }

    private void UnregisterAliasFromDalamud(RootCommandRegistration registration, string alias)
    {
        try
        {
            NoireService.CommandManager.RemoveHandler(alias);
            registration.AliasCommandInfos.Remove(alias);

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Unregistered alias '{alias}' of command '{registration.Command}' from Dalamud.");
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Failed to unregister alias '{alias}' of command '{registration.Command}' from Dalamud.");
        }
    }

    /// <summary>
    /// Adds a normalized alias to a registration and registers it with Dalamud if the module is active; called by
    /// <see cref="RootCommandBuilder.AddAlias(string)"/>. A collision with an existing command or alias is logged
    /// and ignored.
    /// </summary>
    /// <param name="registration">The registration the alias dispatches to.</param>
    /// <param name="alias">The alias slash command.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias"/> is null or whitespace.</exception>
    internal void AddAliasToRegistration(RootCommandRegistration registration, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias cannot be null or empty.", nameof(alias));

        if (!alias.StartsWith('/'))
            alias = "/" + alias;

        alias = alias.ToLowerInvariant();

        lock (registrationLock)
        {
            if (alias.Equals(registration.Command, StringComparison.OrdinalIgnoreCase))
            {
                NoireLogger.LogWarning(this, $"Alias '{alias}' matches its own root command and was ignored.");
                return;
            }

            if (registrations.ContainsKey(alias) || aliasRegistrations.ContainsKey(alias))
            {
                NoireLogger.LogWarning(this, $"Alias '{alias}' is already in use by another command and was ignored.");
                return;
            }

            registration.Aliases.Add(alias);
            aliasRegistrations[alias] = registration;
        }

        if (IsActive)
            RegisterAliasWithDalamud(registration, alias);

        RefreshAllRegistrations();
    }

    /// <summary>
    /// Drops a registration's aliases from the alias lookup, called under <see cref="registrationLock"/> when the
    /// registration is being removed or replaced.
    /// </summary>
    private void RemoveAliasEntries(RootCommandRegistration registration)
    {
        foreach (var alias in registration.Aliases)
            aliasRegistrations.Remove(alias);
    }

    /// <summary>
    /// Removes a single alias from its owning registration and unregisters it from Dalamud, called under
    /// <see cref="registrationLock"/> when a new root command claims the alias's name.
    /// </summary>
    private void RemoveAliasFromRegistration(RootCommandRegistration registration, string alias)
    {
        UnregisterAliasFromDalamud(registration, alias);
        registration.Aliases.Remove(alias);
        aliasRegistrations.Remove(alias);

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Removed alias '{alias}' from command '{registration.Command}'; the name was claimed by a new root command.");
    }

    /// <summary>
    /// Applies a registration's current metadata to its live Dalamud command info and alias infos, if registered.
    /// </summary>
    /// <param name="registration">The registration to refresh.</param>
    internal void RefreshRegistration(RootCommandRegistration registration)
    {
        if (registration.DalamudCommandInfo != null)
        {
            registration.DalamudCommandInfo.HelpMessage = BuildRootHelpMessage(registration);
            registration.DalamudCommandInfo.ShowInHelp = registration.ShowInHelp;
            registration.DalamudCommandInfo.DisplayOrder = registration.DisplayOrder;
        }

        foreach (var (alias, aliasInfo) in registration.AliasCommandInfos)
        {
            aliasInfo.HelpMessage = BuildAliasHelpMessage(registration, alias);
            aliasInfo.ShowInHelp = registration.ShowInHelp;
            aliasInfo.DisplayOrder = registration.DisplayOrder;
        }
    }

    /// <summary>
    /// Refreshes every live registration, needed whenever the command set, a display order or a visibility changes,
    /// since the blank-line separation depends on which visible entry sorts last.
    /// </summary>
    internal void RefreshAllRegistrations()
    {
        lock (registrationLock)
        {
            foreach (var registration in registrations.Values)
                RefreshRegistration(registration);
        }
    }

    private string BuildRootHelpMessage(RootCommandRegistration registration)
        => AppendEntrySeparator(registration.BuildDalamudHelpMessage(EnableAutoHelp), registration.Command, registration.DisplayOrder);

    private string BuildAliasHelpMessage(RootCommandRegistration registration, string alias)
        => AppendEntrySeparator($"Alias of {registration.Command}.", alias, registration.DisplayOrder);

    private string AppendEntrySeparator(string message, string command, int displayOrder)
        => SeparateDalamudHelpEntries && !IsLastDalamudHelpEntry(command, displayOrder)
            ? message + Environment.NewLine
            : message;

    /// <summary>
    /// Whether the given entry sorts last among all visible entries, roots and aliases, by display order then
    /// command name.
    /// </summary>
    /// <param name="command">The command or alias being tested.</param>
    /// <param name="displayOrder">The entry's display order.</param>
    /// <returns>True when no visible entry sorts after it.</returns>
    internal bool IsLastDalamudHelpEntry(string command, int displayOrder)
    {
        lock (registrationLock)
        {
            foreach (var registration in registrations.Values)
            {
                if (!registration.ShowInHelp)
                    continue;

                if (ComparesAfter(registration.Command, registration.DisplayOrder))
                    return false;

                foreach (var alias in registration.Aliases)
                {
                    if (ComparesAfter(alias, registration.DisplayOrder))
                        return false;
                }
            }
        }

        return true;

        bool ComparesAfter(string otherCommand, int otherOrder)
            => otherOrder > displayOrder ||
               (otherOrder == displayOrder && string.Compare(otherCommand, command, StringComparison.OrdinalIgnoreCase) > 0);
    }

    /// <summary>
    /// The entry point Dalamud invokes for every mapped command, on the framework thread; resolves the registration
    /// for <paramref name="command"/> and dispatches it through the router.
    /// </summary>
    /// <param name="command">The root slash command that was typed.</param>
    /// <param name="rawArgs">The raw argument string as received from Dalamud.</param>
    internal void OnCommandDispatched(string command, string rawArgs)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Command '{command}' received but CommandRouter is not active.");
            return;
        }

        RootCommandRegistration? registration;

        lock (registrationLock)
        {
            if (!registrations.TryGetValue(command, out registration) &&
                !aliasRegistrations.TryGetValue(command, out registration))
            {
                if (EnableLogging)
                    NoireLogger.LogWarning(this, $"No registration found for command '{command}'.");
                return;
            }
        }

        DispatchCommand(registration, command, rawArgs);
    }

    private void DispatchCommand(RootCommandRegistration registration, string command, string rawArgs)
    {
        try
        {
            // The root condition gates everything below it: the raw handler, the default handler, every
            // subcommand, and the command's own help.
            if (registration.Condition != null && !registration.Condition())
            {
                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Command '{command}' condition returned false.");

                // Recorded before printing: if PrintToChat throws, the outer catch below must not overwrite this
                // outcome with the chat failure instead of the actual refusal.
                AddHistoryEntry(command, rawArgs, null, false);
                NoireLogger.PrintToChat(XivChatType.Debug, $"Command '{command}' is not available right now.");
                return;
            }

            var trimmedArgs = rawArgs.Trim();
            var tokens = Tokenize(trimmedArgs);

            if (registration.RawHandler != null)
            {
                registration.RawHandler(command, rawArgs);
                AddHistoryEntry(command, rawArgs, null, true);
                PublishExecutedEvent(command, rawArgs, null);
                return;
            }

            if (tokens.Length == 0)
            {
                if (registration.DefaultHandler != null)
                {
                    registration.DefaultHandler();
                    AddHistoryEntry(command, rawArgs, null, true);
                    PublishExecutedEvent(command, rawArgs, null);
                }
                else if (EnableAutoHelp)
                {
                    PrintHelp(registration);
                }

                return;
            }

            SubCommandDefinition? currentSubCommand = null;
            IReadOnlyList<SubCommandDefinition> currentScope = registration.SubCommands;
            var resolvedPath = new List<SubCommandDefinition>();
            var consumedTokens = 0;

            while (consumedTokens < tokens.Length)
            {
                var token = tokens[consumedTokens];

                if (EnableAutoHelp && token.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp(registration, currentSubCommand, resolvedPath);
                    return;
                }

                var matchedSubCommand = FindSubCommand(currentScope, token);
                if (matchedSubCommand == null)
                    break;

                var matchedPath = BuildSubCommandPath(resolvedPath.Select(subCommand => subCommand.Name).Append(matchedSubCommand.Name));
                if (matchedSubCommand.Condition != null && !matchedSubCommand.Condition())
                {
                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Subcommand '{matchedPath}' condition returned false.");

                    AddHistoryEntry(command, rawArgs, matchedPath, false);
                    NoireLogger.PrintToChat(XivChatType.Debug, $"Command '{matchedPath}' is not available right now.");
                    return;
                }

                currentSubCommand = matchedSubCommand;
                resolvedPath.Add(matchedSubCommand);
                currentScope = matchedSubCommand.SubCommands;
                consumedTokens++;
            }

            if (currentSubCommand == null)
            {
                var unknownSubCommandName = tokens[0];

                // The fallback handler claims unmatched tokens ahead of the default handler, which stays bound to
                // the bare command; without either, an unmatched token is an error.
                if (registration.FallbackHandler != null)
                {
                    registration.FallbackHandler(new ParsedCommandArguments(trimmedArgs, tokens));
                    AddHistoryEntry(command, rawArgs, null, true);
                    PublishExecutedEvent(command, rawArgs, null);
                }
                else if (registration.DefaultHandler != null)
                {
                    registration.DefaultHandler();
                    AddHistoryEntry(command, rawArgs, null, true);
                    PublishExecutedEvent(command, rawArgs, null);
                }
                else
                {
                    AddHistoryEntry(command, rawArgs, unknownSubCommandName, false);

                    var message = NoireLogger.CreateChatMessageBuilder()
                        .AddText("Unknown subcommand: ")
                        .AddText(unknownSubCommandName, ErrorTokenColor)
                        .AddText(". Use ")
                        .AddText($"{command} help", HelpArgumentColor)
                        .AddText(" for available commands.");

                    NoireLogger.PrintToChat(XivChatType.Debug, message);
                }

                return;
            }

            var subCommandPath = BuildSubCommandPath(resolvedPath.Select(subCommand => subCommand.Name));
            var remainingTokens = tokens.Skip(consumedTokens).ToArray();

            if (currentSubCommand.Handler == null)
            {
                // Printing help here is not a failure: unlike the paths below, it records no history entry.
                if (currentSubCommand.SubCommands.Count > 0 && remainingTokens.Length == 0 && EnableAutoHelp)
                {
                    PrintHelp(registration, currentSubCommand, resolvedPath);
                    return;
                }

                AddHistoryEntry(command, rawArgs, subCommandPath, false);

                if (currentSubCommand.SubCommands.Count > 0)
                {
                    if (remainingTokens.Length == 0)
                    {
                        var message = NoireLogger.CreateChatMessageBuilder()
                            .AddText("Command ")
                            .AddText(subCommandPath, HelpCommandColor)
                            .AddText(" requires a subcommand.");

                        NoireLogger.PrintToChat(XivChatType.Debug, message);
                    }
                    else
                    {
                        var currentCommandPath = BuildQualifiedCommandPath(command, resolvedPath.Select(subCommand => subCommand.Name));
                        var message = NoireLogger.CreateChatMessageBuilder()
                            .AddText("Unknown subcommand: ")
                            .AddText(remainingTokens[0], ErrorTokenColor)
                            .AddText(". Use ")
                            .AddText($"{currentCommandPath} help", HelpArgumentColor)
                            .AddText(" for available commands.");

                        NoireLogger.PrintToChat(XivChatType.Debug, message);
                    }
                }
                else
                {
                    var message = NoireLogger.CreateChatMessageBuilder()
                        .AddText("Command ")
                        .AddText(subCommandPath, HelpCommandColor)
                        .AddText(" has no executable handler.");

                    NoireLogger.PrintToChat(XivChatType.Debug, message);
                }

                return;
            }

            var parsedArgs = ParseArguments(currentSubCommand, remainingTokens, trimmedArgs,
                BuildQualifiedCommandPath(command, resolvedPath.Select(subCommand => subCommand.Name)), out var parseError);

            if (parsedArgs == null)
            {
                AddHistoryEntry(command, rawArgs, subCommandPath, false);

                if (parseError != null)
                    NoireLogger.PrintToChat(XivChatType.Debug, parseError);

                return;
            }

            ExecuteHandler(currentSubCommand, parsedArgs, command, rawArgs, subCommandPath);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Error dispatching command '{command} {rawArgs}'.");

            // Dalamud invokes this on the framework thread, so an escaping exception here crashes the game.
            // Reporting is wrapped in SafeExecutor since a consumer's event handler can itself throw.
            SafeExecutor.ExecuteSafely(() =>
            {
                AddHistoryEntry(command, rawArgs, null, false);
                PublishFailedEvent(command, rawArgs, null, ex);
            });
        }
    }

    private static SubCommandDefinition? FindSubCommand(IReadOnlyList<SubCommandDefinition> subCommands, string name)
    {
        foreach (var sub in subCommands)
        {
            if (sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return sub;

            foreach (var alias in sub.Aliases)
            {
                if (alias.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return sub;
            }
        }

        return null;
    }

    /// <summary>
    /// Converts the tokens of an invocation into the arguments its handler expects, returning a rejection through
    /// <paramref name="error"/> rather than printing it.
    /// </summary>
    /// <param name="subCommand">The subcommand whose arguments are being filled.</param>
    /// <param name="argTokens">The tokens left over once the subcommand path was consumed.</param>
    /// <param name="rawArgs">The raw argument string, carried through to the parsed result.</param>
    /// <param name="qualifiedCommandPath">The full command path, used to point the user at its help.</param>
    /// <param name="error">The message explaining the rejection, or <see langword="null"/> when parsing succeeded.</param>
    /// <returns>The parsed arguments, or <see langword="null"/> when the invocation was rejected.</returns>
    private ParsedCommandArguments? ParseArguments(SubCommandDefinition subCommand, string[] argTokens, string rawArgs, string qualifiedCommandPath, out NoireLogger.ChatMessageBuilder? error)
    {
        error = null;

        var parsed = new ParsedCommandArguments(rawArgs, argTokens);
        var arguments = subCommand.Arguments;
        var effectiveArgTokens = subCommand.FailOnExtraArguments
            ? argTokens
            : argTokens.Take(arguments.Count).ToArray();

        if (subCommand.FailOnExtraArguments && argTokens.Length > arguments.Count)
        {
            error = NoireLogger.CreateChatMessageBuilder()
                .AddText("Too many arguments for command ")
                .AddText(subCommand.Name, HelpCommandColor)
                .AddText($": expected {arguments.Count}, got {argTokens.Length}.");

            return null;
        }

        if (subCommand.AllowUnorderedOptionalArguments)
            return ParseArgumentsWithUnorderedOptionals(subCommand, parsed, effectiveArgTokens, qualifiedCommandPath, out error);

        for (var i = 0; i < arguments.Count; i++)
        {
            var argDef = arguments[i];

            if (i < effectiveArgTokens.Length)
            {
                if (TryConvertArgument(effectiveArgTokens[i], argDef.Type, out var converted))
                {
                    parsed.Set(argDef.Name, converted);
                }
                else
                {
                    error = NoireLogger.CreateChatMessageBuilder()
                        .AddText("Invalid value for argument ")
                        .AddText(argDef.Name, HelpArgumentColor)
                        .AddText($": expected {GetFriendlyTypeName(argDef.Type)}, got ")
                        .AddText(effectiveArgTokens[i], ErrorTokenColor)
                        .AddText(".");

                    return null;
                }
            }
            else if (argDef.IsRequired)
            {
                error = NoireLogger.CreateChatMessageBuilder()
                    .AddText("Missing required argument: ")
                    .AddText(argDef.Name, HelpArgumentColor)
                    .AddText($" ({GetFriendlyTypeName(argDef.Type)}).");

                return null;
            }
            else
            {
                parsed.Set(argDef.Name, argDef.GetDefaultValue());
            }
        }

        return parsed;
    }

    /// <summary>
    /// Fills a subcommand's arguments when its optional ones may arrive in any order, matching each surplus token to
    /// the first optional argument whose type accepts it.
    /// </summary>
    /// <param name="subCommand">The subcommand whose arguments are being filled.</param>
    /// <param name="parsed">The result being filled in.</param>
    /// <param name="argTokens">The tokens left over once the subcommand path was consumed.</param>
    /// <param name="qualifiedCommandPath">The full command path, used to point the user at its help.</param>
    /// <param name="error">The message explaining the rejection, or <see langword="null"/> when parsing succeeded.</param>
    /// <returns>The parsed arguments, or <see langword="null"/> when the invocation was rejected.</returns>
    private ParsedCommandArguments? ParseArgumentsWithUnorderedOptionals(SubCommandDefinition subCommand, ParsedCommandArguments parsed, string[] argTokens, string qualifiedCommandPath, out NoireLogger.ChatMessageBuilder? error)
    {
        error = null;

        var requiredArguments = subCommand.Arguments.Where(argument => argument.IsRequired).ToArray();
        var optionalArguments = subCommand.Arguments.Where(argument => !argument.IsRequired).ToList();

        if (argTokens.Length < requiredArguments.Length)
        {
            var missingArgument = requiredArguments[argTokens.Length];
            error = NoireLogger.CreateChatMessageBuilder()
                .AddText("Missing required argument: ")
                .AddText(missingArgument.Name, HelpArgumentColor)
                .AddText($" ({GetFriendlyTypeName(missingArgument.Type)}).");

            return null;
        }

        for (var i = 0; i < requiredArguments.Length; i++)
        {
            var requiredArgument = requiredArguments[i];
            if (!TryConvertArgument(argTokens[i], requiredArgument.Type, out var converted))
            {
                error = NoireLogger.CreateChatMessageBuilder()
                    .AddText("Invalid value for argument ")
                    .AddText(requiredArgument.Name, HelpArgumentColor)
                    .AddText($": expected {GetFriendlyTypeName(requiredArgument.Type)}, got ")
                    .AddText(argTokens[i], ErrorTokenColor)
                    .AddText(".");

                return null;
            }

            parsed.Set(requiredArgument.Name, converted);
        }

        for (var i = requiredArguments.Length; i < argTokens.Length; i++)
        {
            var token = argTokens[i];

            // The first optional argument the token converts into claims it; the converted value from that attempt
            // is reused, not reconverted.
            CommandArgumentDefinition? matchedArgument = null;
            object? converted = null;

            foreach (var optionalArgument in optionalArguments)
            {
                if (!TryConvertArgument(token, optionalArgument.Type, out converted))
                    continue;

                matchedArgument = optionalArgument;
                break;
            }

            if (matchedArgument == null)
            {
                error = NoireLogger.CreateChatMessageBuilder()
                    .AddText("Invalid optional argument value ")
                    .AddText(token, ErrorTokenColor)
                    .AddText(" for command ")
                    .AddText(subCommand.Name, HelpCommandColor)
                    .AddText(". Use ")
                    .AddText($"{qualifiedCommandPath} help", HelpArgumentColor)
                    .AddText(".");

                return null;
            }

            parsed.Set(matchedArgument.Name, converted);
            optionalArguments.Remove(matchedArgument);
        }

        foreach (var optionalArgument in optionalArguments)
            parsed.Set(optionalArgument.Name, optionalArgument.GetDefaultValue());

        return parsed;
    }

    private void ExecuteHandler(SubCommandDefinition subCommand, ParsedCommandArguments parsedArgs, string command, string rawArgs, string subCommandPath)
    {
        try
        {
            if (subCommand.Handler == null)
            {
                if (EnableLogging)
                    NoireLogger.LogWarning(this, $"Subcommand '{subCommandPath}' has no handler.");
                return;
            }

            if (subCommand.IsAsync)
            {
                Task task;

                if (subCommand.HasArguments)
                    task = ((Func<ParsedCommandArguments, Task>)subCommand.Handler)(parsedArgs);
                else
                    task = ((Func<Task>)subCommand.Handler)();

                // Reporting is deferred to the continuation since the outcome is unknown until the task settles;
                // awaiting here would stall the framework thread for the handler's duration.
                _ = task.ContinueWith(completedTask => ReportAsyncOutcome(completedTask, command, rawArgs, subCommandPath), TaskScheduler.Default);
                return;
            }

            if (subCommand.HasArguments)
                ((Action<ParsedCommandArguments>)subCommand.Handler)(parsedArgs);
            else
                ((Action)subCommand.Handler)();

            AddHistoryEntry(command, rawArgs, subCommandPath, true);
            PublishExecutedEvent(command, rawArgs, subCommandPath);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Command handler for '{subCommandPath}' threw an exception.");
            AddHistoryEntry(command, rawArgs, subCommandPath, false);
            PublishFailedEvent(command, rawArgs, subCommandPath, ex);
        }
    }

    /// <summary>
    /// Records exactly one outcome for a settled async handler task: success when the task ran to completion,
    /// failure when it faulted or was cancelled.
    /// </summary>
    private void ReportAsyncOutcome(Task completedTask, string command, string rawArgs, string subCommandPath)
    {
        if (completedTask.IsCompletedSuccessfully)
        {
            ReportOnFrameworkThread(() =>
            {
                AddHistoryEntry(command, rawArgs, subCommandPath, true);
                PublishExecutedEvent(command, rawArgs, subCommandPath);
            });

            return;
        }

        var exception = completedTask.Exception?.InnerException
            ?? (Exception?)completedTask.Exception
            ?? new TaskCanceledException(completedTask);

        NoireLogger.LogError(this, exception, $"Async command handler for '{subCommandPath}' failed.");

        ReportOnFrameworkThread(() =>
        {
            AddHistoryEntry(command, rawArgs, subCommandPath, false);
            PublishFailedEvent(command, rawArgs, subCommandPath, exception);
        });
    }

    /// <summary>
    /// Runs outcome reporting on the framework thread, since publishing invokes consumer handlers inline and those
    /// routinely touch game state; runs inline instead when NoireLib is not initialized.
    /// </summary>
    private static void ReportOnFrameworkThread(Action report)
    {
        if (NoireService.IsInitialized() && !NoireService.Framework.IsInFrameworkUpdateThread)
        {
            NoireService.Framework.RunOnFrameworkThread(report);
            return;
        }

        report();
    }

    private void PrintHelp(RootCommandRegistration registration, SubCommandDefinition? scope = null, IReadOnlyList<SubCommandDefinition>? scopePath = null)
    {
        scopePath ??= [];

        PrintHelpLegend();

        var header = NoireLogger.CreateChatMessageBuilder();
        AppendCommandPath(header, registration.Command, scopePath);

        if (scope == null && registration.Aliases.Count > 0)
            header.AddText($" (aliases: {string.Join("|", registration.Aliases)})", HelpAliasColor);

        if (scope != null)
            AppendArguments(header, scope.Arguments);

        var helpText = scope?.HelpText ?? registration.HelpText;
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            header.AddText(" - ");
            header.AddText(helpText!, HelpDescriptionColor);
        }

        var argumentDescriptions = BuildArgumentDescriptions(scope?.Arguments ?? []);
        if (!string.IsNullOrWhiteSpace(argumentDescriptions))
        {
            header.AddText(" ");
            header.AddText($"({argumentDescriptions})", HelpMetaColor);
        }

        NoireLogger.PrintToChat(XivChatType.Debug, header);

        // The documented fallback exists in the root scope only, slotted among the subcommand lines by display
        // order, with a tie listing it first.
        var fallback = scope == null ? registration.FallbackDefinition : null;
        var fallbackPrinted = fallback is not { ShowInHelp: true };

        foreach (var sub in GetVisibleSubCommands(scope?.SubCommands ?? registration.SubCommands))
        {
            if (!fallbackPrinted && fallback!.DisplayOrder <= sub.DisplayOrder)
            {
                PrintFallbackHelpLine(fallback);
                fallbackPrinted = true;
            }

            PrintHelpLine(sub, 1);
        }

        if (!fallbackPrinted)
            PrintFallbackHelpLine(fallback!);
    }

    private static void PrintFallbackHelpLine(FallbackCommandDefinition fallback)
    {
        var line = NoireLogger.CreateChatMessageBuilder();
        line.AddText(BuildTreePrefix(1), HelpMetaColor);
        line.AddText($"<{fallback.Name}>", HelpArgumentColor);

        if (!string.IsNullOrWhiteSpace(fallback.HelpText))
        {
            line.AddText(" - ");
            line.AddText(fallback.HelpText!, HelpDescriptionColor);
        }

        NoireLogger.PrintToChat(XivChatType.Debug, line);
    }

    private void PrintHelpLegend()
    {
        var legend = NoireLogger.CreateChatMessageBuilder()
            .AddText(" 》 Legend: ")
            .AddText("command", HelpCommandColor)
            .AddText(", ")
            .AddText("(aliases: ...)", HelpAliasColor)
            .AddText(", ")
            .AddText("[optional argument]", HelpArgumentColor)
            .AddText(", ")
            .AddText("<required argument>", HelpArgumentColor);

        NoireLogger.PrintToChat(XivChatType.Debug, legend);

        NoireLogger.PrintToChat(XivChatType.Debug, "");
    }

    private void PrintHelpLine(SubCommandDefinition subCommand, int depth)
    {
        var line = NoireLogger.CreateChatMessageBuilder();
        line.AddText(BuildTreePrefix(depth), HelpMetaColor);
        line.AddText(subCommand.Name, HelpCommandColor);

        if (subCommand.Aliases.Count > 0)
        {
            line.AddText(" ");
            line.AddText($"(aliases: {string.Join("|", subCommand.Aliases)})", HelpAliasColor);
        }

        AppendArguments(line, subCommand.Arguments);

        if (!string.IsNullOrWhiteSpace(subCommand.HelpText))
        {
            line.AddText(" - ");
            line.AddText(subCommand.HelpText!, HelpDescriptionColor);
        }

        var argumentDescriptions = BuildArgumentDescriptions(subCommand.Arguments);
        if (!string.IsNullOrWhiteSpace(argumentDescriptions))
        {
            line.AddText(" ");
            line.AddText($"({argumentDescriptions})", HelpMetaColor);
        }

        NoireLogger.PrintToChat(XivChatType.Debug, line);

        foreach (var childSubCommand in GetVisibleSubCommands(subCommand.SubCommands))
            PrintHelpLine(childSubCommand, depth + 1);
    }

    private static void AppendCommandPath(NoireLogger.ChatMessageBuilder builder, string rootCommand, IReadOnlyList<SubCommandDefinition> scopePath)
    {
        builder.AddText(rootCommand, HelpCommandColor);

        foreach (var subCommand in scopePath)
        {
            builder.AddText(" ");
            builder.AddText(subCommand.Name, HelpCommandColor);
        }
    }

    private static void AppendArguments(NoireLogger.ChatMessageBuilder builder, IReadOnlyList<CommandArgumentDefinition> arguments)
    {
        foreach (var argument in arguments)
            builder.AddText(argument.IsRequired ? $" <{argument.Name}>" : $" [{argument.Name}]", HelpArgumentColor);
    }

    private static IReadOnlyList<SubCommandDefinition> GetVisibleSubCommands(IEnumerable<SubCommandDefinition> subCommands)
        => [.. subCommands
            .Where(subCommand => subCommand.ShowInHelp)
            .OrderBy(subCommand => subCommand.DisplayOrder)
            .ThenBy(subCommand => subCommand.Name, StringComparer.OrdinalIgnoreCase)];

    private static string BuildTreePrefix(int depth)
        => $"{new string(' ', Math.Max(0, depth - 1) * 2)}└ ";

    private static string? BuildArgumentDescriptions(IReadOnlyList<CommandArgumentDefinition> arguments)
    {
        var descriptions = arguments
            .Where(argument => !string.IsNullOrWhiteSpace(argument.Description))
            .Select(argument => $"{argument.Name}: {argument.Description}")
            .ToArray();

        return descriptions.Length == 0 ? null : string.Join("; ", descriptions);
    }

    private static string BuildSubCommandPath(IEnumerable<string> subCommandNames)
        => string.Join(" ", subCommandNames);

    private static string BuildQualifiedCommandPath(string command, IEnumerable<string> subCommandNames)
    {
        var subCommandPath = BuildSubCommandPath(subCommandNames);
        return string.IsNullOrWhiteSpace(subCommandPath) ? command : $"{command} {subCommandPath}";
    }

    /// <summary>
    /// Splits a raw argument string into tokens, respecting quoted strings.
    /// </summary>
    /// <param name="input">The raw argument string.</param>
    /// <returns>The tokens, with the quotes stripped.</returns>
    internal static string[] Tokenize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        var quoteChar = '\0';

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (inQuote)
            {
                if (c == quoteChar)
                    inQuote = false;
                else
                    current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return [.. tokens];
    }

    /// <summary>
    /// Attempts to convert a string token to the specified target type.
    /// </summary>
    /// <param name="token">The token to convert.</param>
    /// <param name="targetType">The type to convert into, nullable types included.</param>
    /// <param name="result">The converted value, or <see langword="null"/> when conversion failed.</param>
    /// <returns>True when the token converted.</returns>
    internal static bool TryConvertArgument(string token, Type targetType, out object? result)
    {
        result = null;

        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType != null)
        {
            if (string.IsNullOrWhiteSpace(token))
                return true;

            return TryConvertArgument(token, underlyingType, out result);
        }

        if (targetType == typeof(string))
        {
            result = token;
            return true;
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
            { result = val; return true; }
            return false;
        }

        if (targetType == typeof(long))
        {
            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
            { result = val; return true; }
            return false;
        }

        if (targetType == typeof(float))
        {
            if (float.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var val))
            { result = val; return true; }
            return false;
        }

        if (targetType == typeof(double))
        {
            if (double.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var val))
            { result = val; return true; }
            return false;
        }

        if (targetType == typeof(bool))
        {
            var lower = token.ToLowerInvariant();
            if (lower is "true" or "1" or "yes" or "on") { result = true; return true; }
            if (lower is "false" or "0" or "no" or "off") { result = false; return true; }
            return false;
        }

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, token, ignoreCase: true, out var val))
            { result = val; return true; }
            return false;
        }

        try
        {
            result = Convert.ChangeType(token, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
            return GetFriendlyTypeName(underlyingType);

        if (type == typeof(string)) return "text";
        if (type == typeof(int) || type == typeof(long)) return "number";
        if (type == typeof(float) || type == typeof(double)) return "decimal";
        if (type == typeof(bool)) return "true/false";
        if (type.IsEnum) return string.Join("|", Enum.GetNames(type));
        return type.Name;
    }

    private void AddHistoryEntry(string command, string rawArgs, string? subCommandName, bool wasSuccessful)
    {
        var limit = MaxHistorySize;

        if (limit == 0)
            return;

        lock (historyLock)
        {
            history.Add(new CommandHistoryEntry(command, rawArgs, subCommandName, DateTimeOffset.UtcNow, wasSuccessful));

            while (history.Count > limit)
                history.RemoveAt(0);
        }
    }

    private void PublishExecutedEvent(string command, string rawArgs, string? subCommandName)
    {
        EventBus?.Publish(new CommandExecutedEvent(command, rawArgs, subCommandName));
    }

    private void PublishFailedEvent(string command, string rawArgs, string? subCommandName, Exception exception)
    {
        EventBus?.Publish(new CommandFailedEvent(command, rawArgs, subCommandName, exception));
    }

    #endregion
}
