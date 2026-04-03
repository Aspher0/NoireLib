using Dalamud.Game.Command;
using Dalamud.Game.Text;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace NoireLib.CommandRouter;

/// <summary>
/// A module providing structured slash-command registration and dispatch.<br/>
/// Supports subcommands, aliases, typed arguments, auto-generated help text,
/// async command execution, availability predicates, command history,
/// and optional <see cref="NoireEventBus"/> integration.
/// </summary>
public class NoireCommandRouter : NoireModuleBase<NoireCommandRouter>
{
    #region Private Properties/Fields

    private readonly Dictionary<string, RootCommandRegistration> registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CommandHistoryEntry> history = [];
    private readonly object registrationLock = new();
    private static readonly Vector3 HelpCommandColor = new(0.94f, 0.86f, 0.50f);
    private static readonly Vector3 HelpAliasColor = new(0.78f, 0.74f, 0.95f);
    private static readonly Vector3 HelpArgumentColor = new(1.00f, 0.68f, 0.36f);
    private static readonly Vector3 HelpDescriptionColor = new(0.82f, 0.82f, 0.82f);
    private static readonly Vector3 HelpMetaColor = new(0.66f, 0.66f, 0.66f);
    private static readonly Vector3 ErrorTokenColor = new(0.96f, 0.42f, 0.42f);

    #endregion

    #region Constructors & Event Bus

    /// <summary>
    /// The associated <see cref="NoireEventBus"/> instance for publishing command events.<br/>
    /// When set, <see cref="CommandExecutedEvent"/> and <see cref="CommandFailedEvent"/> are published automatically.
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
    /// The default constructor needed for internal purposes.
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
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
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

    /// <summary>
    /// Gets or sets whether auto-generated help output is enabled.<br/>
    /// When true, typing the root command with no subcommand (and no default handler) or with the "help" subcommand
    /// will print an auto-generated help listing to chat.
    /// </summary>
    public bool EnableAutoHelp { get; set; } = true;

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

    /// <summary>
    /// Gets or sets the maximum number of <see cref="CommandHistoryEntry"/> records to retain.
    /// </summary>
    public int MaxHistorySize { get; set; } = 50;

    /// <summary>
    /// Sets the maximum number of command history entries to retain.
    /// </summary>
    /// <param name="maxSize">The maximum history size.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireCommandRouter SetMaxHistorySize(int maxSize)
    {
        MaxHistorySize = maxSize;
        return this;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Maps a root slash command and returns a <see cref="RootCommandBuilder"/> for configuring subcommands and behavior.<br/>
    /// If the module is currently active, the command is registered with Dalamud immediately.<br/>
    /// If a command with the same name was already mapped, the previous mapping is replaced.
    /// </summary>
    /// <param name="command">The root slash command string (e.g. "/somecommand"). A leading '/' is added automatically if missing.</param>
    /// <returns>A <see cref="RootCommandBuilder"/> for fluently configuring the command.</returns>
    public RootCommandBuilder Map(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));

        if (!command.StartsWith('/'))
            command = "/" + command;

        command = command.ToLowerInvariant();

        var registration = new RootCommandRegistration(command);

        lock (registrationLock)
        {
            if (registrations.TryGetValue(command, out var existing))
            {
                UnregisterFromDalamud(existing);

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Replacing existing command mapping for '{command}'.");
            }

            registrations[command] = registration;
        }

        if (IsActive)
            RegisterWithDalamud(registration);

        return new RootCommandBuilder(registration);
    }

    /// <summary>
    /// Removes a mapped command and unregisters it from Dalamud.
    /// </summary>
    /// <param name="command">The slash command string to remove.</param>
    /// <returns>True if the command was found and removed; otherwise, false.</returns>
    public bool Unmap(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        if (!command.StartsWith('/'))
            command = "/" + command;

        command = command.ToLowerInvariant();

        lock (registrationLock)
        {
            if (!registrations.TryGetValue(command, out var registration))
                return false;

            UnregisterFromDalamud(registration);
            registrations.Remove(command);

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Unmapped command '{command}'.");

            return true;
        }
    }

    /// <summary>
    /// Gets whether a command is currently mapped.
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
            return registrations.ContainsKey(command);
    }

    /// <summary>
    /// Gets a read-only list of all currently mapped command strings.
    /// </summary>
    /// <returns>A list of mapped command strings.</returns>
    public IReadOnlyList<string> GetRegisteredCommands()
    {
        lock (registrationLock)
            return registrations.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets a read-only snapshot of the command history.
    /// </summary>
    /// <returns>A list of <see cref="CommandHistoryEntry"/> records.</returns>
    public IReadOnlyList<CommandHistoryEntry> GetHistory() => history.AsReadOnly();

    /// <summary>
    /// Clears the command history.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireCommandRouter ClearHistory()
    {
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
        }

        history.Clear();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "CommandRouter module disposed.");
    }

    #region Private/Internal Methods

    private void RegisterWithDalamud(RootCommandRegistration registration)
    {
        var commandInfo = new CommandInfo(OnCommandDispatched)
        {
            HelpMessage = registration.BuildDalamudHelpMessage(),
            ShowInHelp = registration.ShowInHelp,
            DisplayOrder = registration.DisplayOrder,
        };

        registration.DalamudCommandInfo = commandInfo;

        try
        {
            NoireService.CommandManager.AddHandler(registration.Command, commandInfo);

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Registered command '{registration.Command}' with Dalamud.");
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Failed to register command '{registration.Command}' with Dalamud.");
        }
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
    }

    private void OnCommandDispatched(string command, string rawArgs)
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
            if (!registrations.TryGetValue(command, out registration))
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
            var trimmedArgs = rawArgs.Trim();
            var tokens = Tokenize(trimmedArgs);

            // Raw handler bypasses all subcommand logic
            if (registration.RawHandler != null)
            {
                registration.RawHandler(command, rawArgs);
                AddHistoryEntry(command, rawArgs, null, true);
                PublishExecutedEvent(command, rawArgs, null);
                return;
            }

            // No tokens will use default handler or help
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

                    NoireLogger.PrintToChat(XivChatType.Debug, $"Command '{matchedPath}' is not available right now.");
                    AddHistoryEntry(command, rawArgs, matchedPath, false);
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

                if (registration.DefaultHandler != null)
                {
                    registration.DefaultHandler();
                    AddHistoryEntry(command, rawArgs, null, true);
                    PublishExecutedEvent(command, rawArgs, null);
                }
                else
                {
                    var message = NoireLogger.CreateChatMessageBuilder()
                        .AddText("Unknown subcommand: ")
                        .AddText(unknownSubCommandName, ErrorTokenColor)
                        .AddText(". Use ")
                        .AddText($"{command} help", HelpArgumentColor)
                        .AddText(" for available commands.");

                    NoireLogger.PrintToChat(XivChatType.Debug, message);
                    AddHistoryEntry(command, rawArgs, unknownSubCommandName, false);
                }

                return;
            }

            var subCommandPath = BuildSubCommandPath(resolvedPath.Select(subCommand => subCommand.Name));
            var remainingTokens = tokens.Skip(consumedTokens).ToArray();

            if (currentSubCommand.Handler == null)
            {
                if (currentSubCommand.SubCommands.Count > 0)
                {
                    if (remainingTokens.Length == 0 && EnableAutoHelp)
                    {
                        PrintHelp(registration, currentSubCommand, resolvedPath);
                        return;
                    }

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

                AddHistoryEntry(command, rawArgs, subCommandPath, false);
                return;
            }

            var parsedArgs = ParseArguments(currentSubCommand, remainingTokens, trimmedArgs, BuildQualifiedCommandPath(command, resolvedPath.Select(subCommand => subCommand.Name)));

            if (parsedArgs == null)
            {
                AddHistoryEntry(command, rawArgs, subCommandPath, false);
                return;
            }

            ExecuteHandler(currentSubCommand, parsedArgs, command, rawArgs, subCommandPath);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Error dispatching command '{command} {rawArgs}'.");
            AddHistoryEntry(command, rawArgs, null, false);
            PublishFailedEvent(command, rawArgs, null, ex);
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

    private ParsedCommandArguments? ParseArguments(SubCommandDefinition subCommand, string[] argTokens, string rawArgs, string qualifiedCommandPath)
    {
        var parsed = new ParsedCommandArguments(rawArgs, argTokens);
        var arguments = subCommand.Arguments;
        var effectiveArgTokens = subCommand.FailOnExtraArguments
            ? argTokens
            : argTokens.Take(arguments.Count).ToArray();

        if (subCommand.FailOnExtraArguments && argTokens.Length > arguments.Count)
        {
            var message = NoireLogger.CreateChatMessageBuilder()
                .AddText("Too many arguments for command ")
                .AddText(subCommand.Name, HelpCommandColor)
                .AddText($": expected {arguments.Count}, got {argTokens.Length}.");

            NoireLogger.PrintToChat(XivChatType.Debug, message);
            return null;
        }

        if (subCommand.AllowUnorderedOptionalArguments)
            return ParseArgumentsWithUnorderedOptionals(subCommand, parsed, effectiveArgTokens, qualifiedCommandPath);

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
                    var message = NoireLogger.CreateChatMessageBuilder()
                        .AddText("Invalid value for argument ")
                        .AddText(argDef.Name, HelpArgumentColor)
                        .AddText($": expected {GetFriendlyTypeName(argDef.Type)}, got ")
                        .AddText(effectiveArgTokens[i], ErrorTokenColor)
                        .AddText(".");

                    NoireLogger.PrintToChat(XivChatType.Debug, message);
                    return null;
                }
            }
            else if (argDef.IsRequired)
            {
                var message = NoireLogger.CreateChatMessageBuilder()
                    .AddText("Missing required argument: ")
                    .AddText(argDef.Name, HelpArgumentColor)
                    .AddText($" ({GetFriendlyTypeName(argDef.Type)}).");

                NoireLogger.PrintToChat(XivChatType.Debug, message);
                return null;
            }
            else
            {
                parsed.Set(argDef.Name, argDef.GetDefaultValue());
            }
        }

        return parsed;
    }

    private ParsedCommandArguments? ParseArgumentsWithUnorderedOptionals(SubCommandDefinition subCommand, ParsedCommandArguments parsed, string[] argTokens, string qualifiedCommandPath)
    {
        var requiredArguments = subCommand.Arguments.Where(argument => argument.IsRequired).ToArray();
        var optionalArguments = subCommand.Arguments.Where(argument => !argument.IsRequired).ToList();

        if (argTokens.Length < requiredArguments.Length)
        {
            var missingArgument = requiredArguments[argTokens.Length];
            var message = NoireLogger.CreateChatMessageBuilder()
                .AddText("Missing required argument: ")
                .AddText(missingArgument.Name, HelpArgumentColor)
                .AddText($" ({GetFriendlyTypeName(missingArgument.Type)}).");

            NoireLogger.PrintToChat(XivChatType.Debug, message);
            return null;
        }

        for (var i = 0; i < requiredArguments.Length; i++)
        {
            var requiredArgument = requiredArguments[i];
            if (!TryConvertArgument(argTokens[i], requiredArgument.Type, out var converted))
            {
                var message = NoireLogger.CreateChatMessageBuilder()
                    .AddText("Invalid value for argument ")
                    .AddText(requiredArgument.Name, HelpArgumentColor)
                    .AddText($": expected {GetFriendlyTypeName(requiredArgument.Type)}, got ")
                    .AddText(argTokens[i], ErrorTokenColor)
                    .AddText(".");

                NoireLogger.PrintToChat(XivChatType.Debug, message);
                return null;
            }

            parsed.Set(requiredArgument.Name, converted);
        }

        for (var i = requiredArguments.Length; i < argTokens.Length; i++)
        {
            var token = argTokens[i];
            var matchedArgument = optionalArguments.FirstOrDefault(argument => TryConvertArgument(token, argument.Type, out _));
            if (matchedArgument == null)
            {
                var message = NoireLogger.CreateChatMessageBuilder()
                    .AddText("Invalid optional argument value ")
                    .AddText(token, ErrorTokenColor)
                    .AddText(" for command ")
                    .AddText(subCommand.Name, HelpCommandColor)
                    .AddText(". Use ")
                    .AddText($"{qualifiedCommandPath} help", HelpArgumentColor)
                    .AddText(".");

                NoireLogger.PrintToChat(XivChatType.Debug, message);
                return null;
            }

            _ = TryConvertArgument(token, matchedArgument.Type, out var converted);
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

                _ = task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception?.InnerException ?? t.Exception!;
                        NoireLogger.LogError(this, ex, $"Async command handler for '{subCommandPath}' failed.");
                        PublishFailedEvent(command, rawArgs, subCommandPath, ex);
                    }
                }, TaskScheduler.Default);
            }
            else
            {
                if (subCommand.HasArguments)
                    ((Action<ParsedCommandArguments>)subCommand.Handler)(parsedArgs);
                else
                    ((Action)subCommand.Handler)();
            }

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

    private void PrintHelp(RootCommandRegistration registration, SubCommandDefinition? scope = null, IReadOnlyList<SubCommandDefinition>? scopePath = null)
    {
        scopePath ??= [];

        PrintHelpLegend();

        var header = NoireLogger.CreateChatMessageBuilder();
        AppendCommandPath(header, registration.Command, scopePath);

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

        foreach (var sub in GetVisibleSubCommands(scope?.SubCommands ?? registration.SubCommands))
            PrintHelpLine(sub, 1);
    }

    private void PrintHelpLegend()
    {
        var legend = NoireLogger.CreateChatMessageBuilder()
            .AddText(" 》 Legend: ")
            .AddText("command", HelpCommandColor)
            .AddText(", ")
            .AddText("(alias)", HelpAliasColor)
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
            line.AddText($"({string.Join(", ", subCommand.Aliases)})", HelpAliasColor);
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
        history.Add(new CommandHistoryEntry(command, rawArgs, subCommandName, DateTimeOffset.UtcNow, wasSuccessful));

        while (history.Count > MaxHistorySize)
            history.RemoveAt(0);
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
