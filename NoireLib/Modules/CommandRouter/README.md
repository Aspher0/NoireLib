# Module Documentation : NoireCommandRouter

You are reading the documentation for the `NoireCommandRouter` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Subcommands](#subcommands)
- [Arguments](#arguments)
- [Availability Conditions](#availability-conditions)
- [Help Output](#help-output)
- [Command History](#command-history)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireCommandRouter` turns slash commands into a structured tree instead of a raw string to parse by hand. It
provides:
- **Fluent command mapping** with nested subcommands and aliases on both root commands and subcommands
- **Fallback handling** for commands whose first argument is free-form input rather than a fixed keyword
- **Typed arguments** converted and validated before your handler runs
- **Auto-generated help** for the command and every level beneath it
- **Async handlers** whose real outcome is reported once they settle
- **Availability conditions** gating a command or any part of its tree
- **Command history** of what was dispatched and whether it worked
- **EventBus integration** for reacting to command execution and failure

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Add the Module

```csharp
var commandRouter = NoireLibMain.AddModule<NoireCommandRouter>();
```

### 2. Map Your First Command

`Map` returns a builder for the root command. A leading `/` is added when missing, and the command is lower-cased, so
the spelling Dalamud shows is independent of how it was written here:

```csharp
var commandRouter = NoireLibMain.GetModule<NoireCommandRouter>();

commandRouter?.Map("/myplugin")
    .WithHelp("Opens the main window.")
    .Handle(() => mainWindow.IsOpen = true)
    .AddSubCommand("config", sub => sub
        .WithHelp("Opens the settings window.")
        .AddAlias("settings")
        .Handle(() => configWindow.IsOpen = true));
```

That gives `/myplugin`, `/myplugin config`, `/myplugin settings`, and `/myplugin help`.

The command is registered with Dalamud as soon as it is mapped if the module is active, and re-registered whenever
the module is activated. Mapping a command that is already mapped replaces the previous mapping.

---

## Configuration

### Module Parameters

The main options are set through the module's constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Commands"); // Optional

var commandRouter = NoireLibMain.AddModule(new NoireCommandRouter(
    moduleId: "MyCommands",     // Optional identifier
    active: true,               // Enable/disable the module
    enableLogging: true,        // Whether this module logs its actions
    enableAutoHelp: true,       // Whether the "help" subcommand and generated help listings are available
    maxHistorySize: 50,         // How many history entries to retain. 0 disables history recording.
    eventBus: eventBus          // Optional EventBus for publishing events
));
```

The following properties can also be modified after the module is created:

- `EnableAutoHelp`: If true, the root command with no subcommand and no default handler, or any `help` token, prints a
  generated help listing. Default: `true`.
- `SeparateDalamudHelpEntries`: If true, each command's Dalamud help message ends with a blank line separating it
  from the next entry in the listing; the last entry (by display order, then name) gets none. Default: `true`.
- `MaxHistorySize`: The maximum number of `CommandHistoryEntry` records to retain, oldest discarded first. A value of
  0 disables history recording entirely. Negative values are rejected. Default: `50`.
- `EventBus`: Optional EventBus instance for publishing command events. Default: `null`.
- `IsActive`: Whether the module is active. Commands are registered with Dalamud on activation and unregistered on
  deactivation. Default: `true`.
- `EnableLogging`: Whether this module logs its actions. Warnings and errors are logged regardless. Default: `true`.
- `ModuleId`: The optional identifier used to tell several instances apart. Default: `null`.

The setter methods below cover the same options (see [Property Configuration](#property-configuration)).

### Property Configuration

Configuring the module after creation:

```csharp
var commandRouter = NoireLibMain.GetModule<NoireCommandRouter>();

// Turn off the generated help listings
commandRouter?.SetAutoHelp(false);

// Keep a longer history
commandRouter?.SetMaxHistorySize(200);

// Attach or detach an EventBus
commandRouter?.SetEventBus(eventBus);

// Activate or deactivate the module
commandRouter?.SetActive(true);
```

These methods chain:
```csharp
var commandRouter = NoireLibMain.GetModule<NoireCommandRouter>();

commandRouter?
    .SetAutoHelp(true)
    .SetMaxHistorySize(200)
    .SetEventBus(eventBus)
    .SetActive(true);
```

---

## Subcommands

### Root Handlers

A root command is handled in one of three ways:

```csharp
var commandRouter = NoireLibMain.GetModule<NoireCommandRouter>();

// Handle: runs when the command is typed bare, with no arguments at all
commandRouter?.Map("/myplugin")
    .Handle(() => mainWindow.IsOpen = true);

// HandleFallback: runs when the first token matches no subcommand, receiving every token of the invocation.
// Subcommands, typed arguments, and the built-in "help" token keep working around it.
commandRouter?.Map("/myplugin")
    .Handle(() => mainWindow.IsOpen = true)
    .AddSubCommand("config", sub => sub.Handle(() => configWindow.IsOpen = true))
    .HandleFallback(args => PlayEmote(args.RawTokens[0]));

// HandleRaw: receives the command and the raw argument string, bypassing subcommand dispatch entirely
commandRouter?.Map("/myecho")
    .HandleRaw((command, rawArgs) => NoireLogger.PrintToChat($"{command} said: {rawArgs}"));
```

Precedence for an invocation: the raw handler, when set, takes everything. Otherwise a bare command goes to the
default handler (or prints the generated help without one, when `EnableAutoHelp` is on); a first token matching a
subcommand dispatches into the tree; and an unmatched first token goes to the fallback handler, then to the default
handler, and is an "Unknown subcommand" error when neither is set.

`HandleFallback` suits commands whose first argument is free-form user input (an emote name, a search query), while
fixed keywords stay real subcommands with help, aliases, and conditions.

`AddFallbackCommand` configures the fallback like a subcommand and documents it: the free-form argument is listed as
`<emote_name> - Plays the emote if found.` among the subcommand lines, in both the in-chat listing and Dalamud's
help. Its display order slots it among those lines; a tie lists the fallback first, and without one it is listed
last:

```csharp
commandRouter?.Map("/myplugin")
    .AddFallbackCommand("emote_name", fallback => fallback
        .WithHelp("Plays the emote if found.")
        .WithDisplayOrder(0)            // Listed before the subcommands; without it, listed after them
        .Handle(args => PlayEmote(args.RawTokens[0])))
    .AddSubCommand("config", sub => sub.Handle(() => configWindow.IsOpen = true));
```

`ShowInHelp(false)` on the fallback hides the line while keeping it dispatchable, like it does for subcommands.

### Root Aliases

A root command can be reached through alias slash commands:

```csharp
commandRouter?.Map("/myplugin")
    .AddAlias("/mp")
    .WithHelp("Opens the main window.")
    .Handle(() => mainWindow.IsOpen = true)
    .AddSubCommand("config", sub => sub.Handle(() => configWindow.IsOpen = true));
```

That registers `/mp` as its own Dalamud command, listed as "Alias of /myplugin.", and everything works through it:
`/mp config`, `/mp help`, arguments, conditions. History records the command the user actually typed, so `/mp` shows
up as `/mp`. Like `Map`, an alias gains a leading `/` if missing and is lower-cased.

Aliases share their root command's `WithDisplayOrder` and `ShowInDalamudHelp` settings, and are unregistered with it
on `Unmap`, deactivation, and disposal. An alias colliding with an existing command or alias is logged and ignored;
mapping a new root command over an existing alias's name takes the name for the root.

### Nesting

Subcommands nest to any depth, each level configured by its own builder:

```csharp
commandRouter?.Map("/myplugin")
    .WithHelp("My plugin.")
    .AddSubCommand("profile", sub => sub
        .WithHelp("Profile management.")
        .AddAlias("p")
        .AddSubCommand("save", nested => nested
            .WithHelp("Saves the current profile.")
            .AddArgument<string>("name", required: true, description: "The profile name")
            .Handle(args => SaveProfile(args.Get<string>("name"))))
        .AddSubCommand("load", nested => nested
            .WithHelp("Loads a profile.")
            .AddArgument<string>("name", required: true)
            .Handle(args => LoadProfile(args.Get<string>("name")))));
```

A subcommand with children but no handler of its own prints its help listing when typed alone.

Names and aliases are matched case-insensitively.

### Handler Shapes

Each subcommand takes exactly one handler, in one of four shapes:

```csharp
// Synchronous, no arguments
sub.Handle(() => DoSomething());

// Synchronous, with parsed arguments
sub.Handle(args => DoSomething(args.Get<int>("count")));

// Asynchronous, no arguments
sub.Handle(async () => await DoSomethingAsync());

// Asynchronous, with parsed arguments
sub.Handle(async args => await DoSomethingAsync(args.Get<int>("count")));
```

An async handler is not awaited on the framework thread; its outcome is reported once the task settles, so history
and events reflect what actually happened.

### Visibility and Ordering

```csharp
commandRouter?.Map("/myplugin")
    .WithDisplayOrder(10)               // Order in Dalamud's help listing
    .ShowInDalamudHelp(true)            // Whether Dalamud lists this command at all
    .ShowDetailedDalamudHelp(false)     // Dalamud lists only the help text; "/myplugin help" stays fully detailed
    .AddSubCommand("debug", sub => sub
        .ShowInHelp(false)              // Hidden from the generated listing, still callable
        .Handle(() => DumpState()))
    .AddSubCommand("config", sub => sub
        .WithDisplayOrder(1)            // Order within this scope
        .Handle(() => configWindow.IsOpen = true));
```

Subcommands are listed by `DisplayOrder`, then alphabetically.

---

## Arguments

### Declaring Them

Arguments are declared on the subcommand and are positional by default:

```csharp
commandRouter?.Map("/myplugin")
    .AddSubCommand("wait", sub => sub
        .WithHelp("Waits, then reports.")
        .AddArgument<int>("seconds", required: true, description: "How long to wait")
        .AddArgument<string>("message", required: false, defaultValue: "Done", description: "What to say")
        .Handle(args =>
        {
            var seconds = args.Get<int>("seconds");
            var message = args.Get<string>("message");
        }));
```

A default that has to be computed each time takes a factory instead of a value:

```csharp
sub.AddArgument<string>("name", required: false, defaultValueFactory: () => GetCurrentPlayerName());
```

### Supported Types

`string`, `int`, `long`, `float`, `double`, `bool`, any `enum`, and the nullable form of each. Anything else is
attempted through `Convert.ChangeType`.

Booleans accept `true`/`1`/`yes`/`on` and `false`/`0`/`no`/`off`. Enums are matched case-insensitively by name.
Numbers are parsed with the invariant culture, so a decimal point is a `.` whatever the user's locale.

Quoted tokens are kept together, so `/myplugin say "hello there"` is one argument.

### Reading Them

```csharp
sub.Handle(args =>
{
    var count = args.Get<int>("count");                      // Throws KeyNotFoundException if not declared
    var name = args.GetOrDefault<string>("name", "nobody");  // Falls back if absent or null
    var provided = args.Has("name");                         // Whether the argument was parsed

    var raw = args.RawArgs;                                  // The raw argument string from Dalamud
    var tokens = args.RawTokens;                             // The tokens left after the subcommand name
});
```

An argument the user gets wrong, or a required one they leave out, is reported to them and the handler does not run.

### Parsing Options

```csharp
commandRouter?.Map("/myplugin")
    .AddSubCommand("run", sub => sub
        // Optional arguments may appear in any order after the required ones, matched by the type they convert into
        .WithUnorderedOptionalArguments()
        // Extra trailing arguments are an error rather than ignored
        .FailOnExtraArguments()
        .AddArgument<int>("count", required: false, defaultValue: 0)
        .AddArgument<bool>("verbose", required: false, defaultValue: false)
        .Handle(args => { }));
```

With `WithUnorderedOptionalArguments`, `/myplugin run true 7` and `/myplugin run 7 true` both give `count = 7` and
`verbose = true`.

---

## Availability Conditions

A condition is a caller-supplied predicate, asked on every invocation. While it returns false, the command reports
that it is unavailable and the handler does not run.

Conditions can be set on a root command and on any subcommand, with the same rule at both levels: **the condition
gates everything inside the scope it is declared on.**

```csharp
commandRouter?.Map("/myplugin")
    // While logged out, nothing under /myplugin runs: not the default handler, not "gearset", not even the help
    .WithCondition(() => NoireService.ClientState.IsLoggedIn)
    .Handle(() => mainWindow.IsOpen = true)
    .AddSubCommand("gearset", sub => sub
        // And this one additionally needs a target
        .WithCondition(() => NoireService.TargetManager.Target != null)
        .Handle(() => InspectTarget()));
```

- A false **root** condition blocks the whole command: the raw handler, the default handler, every subcommand
  whatever its own condition says, and the generated help.
- A false **subcommand** condition blocks that subcommand, everything nested beneath it, and its own help. The rest
  of the command is unaffected.

A blocked command is recorded in the history as an unsuccessful entry. No `CommandFailedEvent` is published; that
event is reserved for a handler that threw.

A condition hides nothing. A blocked command still appears in Dalamud's help listing, and a blocked subcommand still
appears in the generated listing. `ShowInDalamudHelp(false)` and `ShowInHelp(false)` hide them.

---

## Help Output

With `EnableAutoHelp` on (the default), help is generated from the declarations.

- `/myplugin help` lists the root command and its subcommands.
- `/myplugin profile help` lists that scope instead.
- `/myplugin` with no default handler prints the root listing.
- A subcommand with children but no handler prints its listing when typed alone.

The listing shows each command with its aliases as `(aliases: a|b)`, its arguments as `<required>` and `[optional]`,
its help text, and any argument descriptions, preceded by a legend. A root command's aliases appear the same way next
to it in the header. Dalamud's `/xlhelp` listing is generated from the same declarations.

A command with a raw handler bypasses subcommand dispatch entirely, so neither subcommands nor the `help` token can
run for it; its Dalamud listing therefore shows only its own help text. Turning `EnableAutoHelp` off likewise removes
the advertised built-in `help` line from Dalamud's listing, since the token no longer dispatches. Toggling it
refreshes every live registration.

Two more presentation controls. `ShowDetailedDalamudHelp(false)` keeps a command's Dalamud entry to its own help
text while the in-chat `/command help` stays fully detailed. With `SeparateDalamudHelpEntries` on (the default), each
command's Dalamud entry ends with a blank line separating it from the next, except the last; distinct display orders
make that last entry unambiguous.

Turning auto-help off makes `help` an ordinary token, leaving room for a custom `help` subcommand:

```csharp
commandRouter?.SetAutoHelp(false);

commandRouter?.Map("/myplugin")
    .AddSubCommand("help", sub => sub.Handle(() => PrintMyOwnHelp()));
```

---

## Command History

Every dispatched command is recorded with its outcome:

```csharp
var commandRouter = NoireLibMain.GetModule<NoireCommandRouter>();

// A snapshot, ordered oldest first. It stays safe to iterate while further commands execute.
IReadOnlyList<CommandHistoryEntry> history = commandRouter!.GetHistory();

foreach (var entry in history)
{
    var command = entry.Command;                // The root slash command
    var rawArgs = entry.RawArgs;                // The raw argument string
    var subCommand = entry.SubCommandName;      // The resolved subcommand path, or null if none matched
    var timestamp = entry.Timestamp;            // The UTC time it was dispatched
    var succeeded = entry.WasSuccessful;        // Whether it executed without errors
}

commandRouter.ClearHistory();
```

Once `MaxHistorySize` is reached the oldest entries are discarded first. Setting it to 0 disables recording entirely
and leaves `GetHistory()` permanently empty.

---

## EventBus Integration

The `NoireCommandRouter` can publish events to a `NoireEventBus` for command execution and failure.

### Quick Example

```csharp
// Create EventBus
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Commands");

// Subscribe to command events
eventBus?.Subscribe<CommandExecutedEvent>(evt =>
{
    NoireLogger.LogInfo($"{evt.Command} {evt.RawArgs} ran ({evt.SubCommandName ?? "default handler"}).");
}, owner: this);

eventBus?.Subscribe<CommandFailedEvent>(evt =>
{
    NoireLogger.LogError(evt.Exception, $"{evt.Command} {evt.RawArgs} failed.");
}, owner: this);

// Create the CommandRouter with the EventBus
var commandRouter = NoireLibMain.AddModule(new NoireCommandRouter(
    active: true,
    eventBus: eventBus
));
```

### Available Events

- `CommandExecutedEvent` - A command handler ran to completion
- `CommandFailedEvent` - A command handler threw, or its task faulted

Both are published on the framework thread, so a handler can touch game state directly.

---

## Advanced Features

### Managing Mappings

```csharp
var commandRouter = NoireLibMain.GetModule<NoireCommandRouter>();

// Whether a command is mapped
var mapped = commandRouter?.IsCommandRegistered("/myplugin");

// Every mapped command string
var commands = commandRouter?.GetRegisteredCommands();

// Remove a mapping and unregister it from Dalamud
var removed = commandRouter?.Unmap("/myplugin");
```

Disposing the module unmaps and unregisters everything it owns.

### Reconfiguring a Live Command

`Map` replaces the previous mapping for a command, so re-mapping is how a command is redefined:

```csharp
var commandRouter = NoireLibMain.GetModule<NoireCommandRouter>();

commandRouter?.Map("/myplugin")
    .WithHelp("The new definition.")
    .Handle(() => { });
```

`WithHelp`, `WithDisplayOrder`, `ShowInDalamudHelp`, and `AddSubCommand` refresh what Dalamud shows immediately when
the command is already registered.

### Several Routers

Several routers coexist when each instance has its own module ID:

```csharp
var pluginCommands = NoireLibMain.AddModule<NoireCommandRouter>("PluginCommands");
var debugCommands = NoireLibMain.AddModule<NoireCommandRouter>("DebugCommands");

pluginCommands?.Map("/myplugin").Handle(() => { });
debugCommands?.Map("/myplugindebug").Handle(() => { });

// Retrieve them the same way
var commands = NoireLibMain.GetModule<NoireCommandRouter>("PluginCommands");
```

---

## Troubleshooting

### The command does nothing
- Ensure NoireLib is initialized before adding the module.
- Confirm the module is active (`IsActive == true`); an inactive router unregisters its commands from Dalamud.
- Check that a handler is set: a subcommand with no handler and no children reports that it has none.
- Check any `WithCondition` predicate on the command or its ancestors; a false condition anywhere in the chain
  blocks everything inside it.
- Verify no other plugin already owns the same slash command; Dalamud refuses the second registration and the failure
  is logged.
- Check the dalamud logs with `/xllog`.
- If it still does not work, please report it.

### A subcommand is not matched
- Names and aliases match case-insensitively, but they must match in full.
- Confirm the subcommand is declared in the scope it is typed in, since nesting is literal.
- For a custom `help` subcommand, turn `EnableAutoHelp` off; auto-help claims that token first.

### An argument is not parsed
- Check the declared type; a value that fails to convert is reported to the user and the handler does not run.
- Arguments are positional unless `WithUnorderedOptionalArguments` is set.
- Use quotes for a value containing spaces.
- Numbers use the invariant culture, so `1.5` parses and `1,5` does not.

### History is empty
- Confirm `MaxHistorySize` is not 0, which disables recording entirely.
- An async handler records nothing until its task settles.

### EventBus events not firing
- Ensure an `EventBus` is provided to the CommandRouter (either in constructor or via `SetEventBus`).
- Check that the EventBus is active and has subscribers.
- A blocked or unknown command publishes nothing; only a handler that ran or threw does.
- Enable EventBus logging with `enableLogging: true` for debugging.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
